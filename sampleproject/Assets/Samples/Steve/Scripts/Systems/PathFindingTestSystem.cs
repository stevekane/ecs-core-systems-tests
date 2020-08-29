using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Experimental.AI;
using Unity.Mathematics;

public struct PolygonSearchConfig {
  public NavMeshLocation start;
  public NavMeshLocation end;
  public int maxNodesTraversedPerUpdate;
}

public struct StraightPathConfig {
  public NavMeshLocation start;
  public NavMeshLocation end;
  public NativeArray<PolygonId> polygonIds;
  public int polyCount;
  public int maxPathLength;
}

public struct PathConfig {
  public Allocator allocator;
  public NavMeshWorld navMeshWorld;
  public Vector3 start;
  public Vector3 end;
  public Vector3 searchExtents;
  public int maxPathLength;
  public int maxStackSize;
  public int maxNodesTraversedPerUpdate;
  public int agentTypeId;
}

public struct NavigationPathPoint : IBufferElementData {
  public static implicit operator float3(NavigationPathPoint np) => np.Value;
  public float3 Value;
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class PathFindingTestSystem : SystemBase {
  // TODO: This has no error-handling but it may be fairly common that a path cannot be found
  //       or that some inputs to the query such as start and end locations are not sufficiently-close
  //       to the navmesh. Something should happen... not sure what... 
  public int ComputeConnectedPolygons([ReadOnly] PolygonSearchConfig searchConfig, NavMeshQuery navMeshQuery, NativeArray<PolygonId> polygonIds) {
    PathQueryStatus beginPathStatus = navMeshQuery.BeginFindPath(searchConfig.start, searchConfig.end);
    PathQueryStatus updateStatus;
    do {
      updateStatus = navMeshQuery.UpdateFindPath(searchConfig.maxNodesTraversedPerUpdate, out int iterationsPerformed);
    } while (updateStatus == PathQueryStatus.InProgress);
    PathQueryStatus endPathStatus = navMeshQuery.EndFindPath(out int pathLength);

    return navMeshQuery.GetPathResult(polygonIds);
  }

  // This code is copied from a goddamn forum and some stupid library made by Unity found here
  // https://forum.unity.com/threads/how-to-use-navmeshquery-get-path-points.646861/
  // https://github.com/Unity-Technologies/UniteAustinTechnicalPresentation/blob/master/StressTesting/Assets/Scripts/Utils/PathUtils.cs
  // I am not proud of this and will someday amend my wickedness but for now...it's 82 degrees in this office and 11pm
  public int ComputeStraightPath([ReadOnly] StraightPathConfig pathConfig, NavMeshQuery navMeshQuery, NativeArray<NavMeshLocation> straightPath) {
    if (pathConfig.start.polygon == pathConfig.end.polygon) {
      straightPath[0] = pathConfig.start;
      straightPath[1] = pathConfig.end;
      return 2;
    } else {
      int straightPathCount = 0;
      NativeArray<StraightPathFlags> straightPathFlags = new NativeArray<StraightPathFlags>(pathConfig.polyCount, Allocator.Temp);
      NativeArray<float> vertexSide = new NativeArray<float>(pathConfig.polyCount, Allocator.Temp);
      PathQueryStatus straightPathStatus = PathUtils.FindStraightPath(
        navMeshQuery, 
        pathConfig.start.position, 
        pathConfig.end.position, 
        pathConfig.polygonIds, 
        pathConfig.polyCount, 
        ref straightPath, 
        ref straightPathFlags, 
        ref vertexSide, 
        ref straightPathCount,
        pathConfig.maxPathLength);
      
      straightPathFlags.Dispose();
      vertexSide.Dispose();
      return straightPathCount;
    }
  }

  public int TryComputePath([ReadOnly] PathConfig pathConfig, NativeArray<NavMeshLocation> straightPath) {
    var navMeshQuery = new NavMeshQuery(pathConfig.navMeshWorld, Allocator.TempJob, pathConfig.maxStackSize);
    var polygonIds = new NativeArray<PolygonId>(pathConfig.maxPathLength, Allocator.TempJob);
    PolygonSearchConfig searchConfig = new PolygonSearchConfig {
      start = navMeshQuery.MapLocation(pathConfig.start, pathConfig.searchExtents, pathConfig.agentTypeId),
      end = navMeshQuery.MapLocation(pathConfig.end, pathConfig.searchExtents, pathConfig.agentTypeId),
      maxNodesTraversedPerUpdate = 32,
    };
    int polyCount = ComputeConnectedPolygons(searchConfig, navMeshQuery, polygonIds);

    StraightPathConfig straightPathConfig = new StraightPathConfig {
      start = searchConfig.start,
      end = searchConfig.end,
      polyCount = polyCount,
      maxPathLength = pathConfig.maxPathLength,
      polygonIds = polygonIds
    };

    int pathLength = ComputeStraightPath(straightPathConfig, navMeshQuery, straightPath);

    navMeshQuery.Dispose();
    polygonIds.Dispose();
    return pathLength;
  }

  public void FillPathBuffer(DynamicBuffer<NavigationPathPoint> navPointBuffer, NativeArray<NavMeshLocation> navMeshLocations, int count) {
    navPointBuffer.Clear();
    for (int i = 0; i < count; i++) {
      navPointBuffer.Add(new NavigationPathPoint { Value = navMeshLocations[i].position });
    }
  }

  public void DebugDrawPathBuffer(DynamicBuffer<NavigationPathPoint> navBuffer, int length) {
    for (int i = 1; i < length; i++) {
      Debug.DrawLine(navBuffer[i-1].Value, navBuffer[i].Value, Color.red);
    }
  }

  // This version runs on the main thread without burst because NavMeshWorld uses unsafe pointers
  // which means you cannot pass it to a burst job
  // You could possibly get around this by following some of this blackmagicfuckery
  // https://reeseschultz.com/dots-navigation-with-auto-jumping-agents-and-movable-surfaces/
  protected override void OnUpdate() {
    NavMeshWorld navMeshWorld = NavMeshWorld.GetDefaultWorld();
    BufferFromEntity<NavigationPathPoint> existingPathBuffers = GetBufferFromEntity<NavigationPathPoint>();
    EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.SinglePlayback);

    Entities
    .WithAll<Traveler>()
    .ForEach((Entity e) => {
      if (!existingPathBuffers.HasComponent(e)) {
        ecb.AddBuffer<NavigationPathPoint>(e);
      }
    }).Run();

    ecb.Playback(World.EntityManager);
    ecb.Dispose();

    BufferFromEntity<NavigationPathPoint> pathBuffers = GetBufferFromEntity<NavigationPathPoint>();

    Entities
    .WithoutBurst()
    .ForEach((Entity e, ref Translation translation, in Traveler traveler) => {
      PathConfig pathConfig = new PathConfig {
        allocator = Allocator.Temp,
        navMeshWorld = navMeshWorld,
        start = translation.Value,
        end = traveler.targetPosition,
        searchExtents = new Vector3(2,2,2),
        maxPathLength = 128,
        maxStackSize = 64,
        maxNodesTraversedPerUpdate = 32,
        agentTypeId = 0
      };
      NativeArray<NavMeshLocation> path = new NativeArray<NavMeshLocation>(pathConfig.maxPathLength, Allocator.Temp);
      int pathLength = TryComputePath(pathConfig, path);
      DynamicBuffer<NavigationPathPoint> navBuffer = pathBuffers[e];
      
      FillPathBuffer(navBuffer, path, pathLength);
      DebugDrawPathBuffer(navBuffer, pathLength);
      path.Dispose();
    }).Run();
  }
}