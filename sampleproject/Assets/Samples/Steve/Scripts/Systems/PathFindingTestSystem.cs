using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Experimental.AI;
using static Unity.Mathematics.math;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class PathFindingTestSystem : SystemBase {
  protected override void OnUpdate() {
    NavMeshWorld navMeshWorld = NavMeshWorld.GetDefaultWorld();

    // This version runs on the main thread without burst because NavMeshWorld uses unsafe pointers
    // which means you cannot pass it to a burst job
    // You could possibly get around this by following some of this blackmagicfuckery
    // https://reeseschultz.com/dots-navigation-with-auto-jumping-agents-and-movable-surfaces/
    Entities
    .WithoutBurst()
    .ForEach((ref Translation translation, in Traveler traveler) => {
      const int MAX_PATH_LENGTH = 128;
      const int MAX_NODES_TRAVERSED_PER_UPDATE = 32;
      Vector3 SEARCH_EXTENTS = new Vector3(2, 2, 2);

      // TODO: This has no error-handling but it may be fairly common that a path cannot be found
      //       or that some inputs to the query such as start and end locations are not sufficiently-close
      //       to the navmesh. Something should happen... not sure what... 

      using (var polygonIds = new NativeArray<PolygonId>(MAX_PATH_LENGTH, Allocator.Temp))
      using (var navMeshQuery = new NavMeshQuery(navMeshWorld, Allocator.Temp, MAX_PATH_LENGTH))
      {
        Vector3 worldStart = (Vector3)translation.Value;
        Vector3 worldEnd = (Vector3)traveler.targetPosition;
        NavMeshLocation navMeshStart = navMeshQuery.MapLocation(worldStart, SEARCH_EXTENTS, 0);
        NavMeshLocation navMeshEnd = navMeshQuery.MapLocation(worldEnd, SEARCH_EXTENTS, 0);
        PathQueryStatus beginPathStatus = navMeshQuery.BeginFindPath(navMeshStart, navMeshEnd);
        PathQueryStatus updateStatus;
        do {
          updateStatus = navMeshQuery.UpdateFindPath(MAX_NODES_TRAVERSED_PER_UPDATE, out int iterationsPerformed);
        } while (updateStatus == PathQueryStatus.InProgress);
        PathQueryStatus endPathStatus = navMeshQuery.EndFindPath(out int pathLength);
        int length = navMeshQuery.GetPathResult(polygonIds);

        // TODO: This appears to work but needs much more rigorous testing with non-trivial navmeshes, obstacles, etc
        // TODO: This MAY not be working when the start location is in the same polygon as the end location. 
        //       I think this is relatively easy to handle though as you just draw a path straight from start to end..
        //       This should probably be an early-out optimization.... 

        // This code is copied from a goddamn forum and some stupid library made by Unity found here
        // https://forum.unity.com/threads/how-to-use-navmeshquery-get-path-points.646861/
        // https://github.com/Unity-Technologies/UniteAustinTechnicalPresentation/blob/master/StressTesting/Assets/Scripts/Utils/PathUtils.cs
        // I am not proud of this and will someday amend my wickedness but for now...it's 82 degrees in this office and 11pm
        NativeArray<NavMeshLocation> straightPath = new NativeArray<NavMeshLocation>(pathLength, Allocator.Temp);
        NativeArray<StraightPathFlags> straightPathFlags = new NativeArray<StraightPathFlags>(pathLength, Allocator.Temp);
        NativeArray<float> vertexSide = new NativeArray<float>(pathLength, Allocator.Temp);
        int straightPathCount = 0;
        PathQueryStatus straightPathStatus = PathUtils.FindStraightPath(
          navMeshQuery, 
          worldStart, 
          worldEnd, 
          polygonIds, 
          pathLength, 
          ref straightPath, 
          ref straightPathFlags, 
          ref vertexSide, 
          ref straightPathCount,
          MAX_PATH_LENGTH);
        string path = "";
        for (int i = 0; i < straightPathCount; i++) {
          path += straightPath[i].position.ToString();
        }
        for (int i = 1; i < straightPathCount; i++) {
          Debug.DrawLine(straightPath[i-1].position, straightPath[i].position, Color.red);
        }
        Debug.Log(path);
      }
    }).Run();
  }
}