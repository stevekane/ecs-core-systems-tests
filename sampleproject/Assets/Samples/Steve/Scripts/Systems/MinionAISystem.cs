using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using static Unity.Mathematics.math;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
[UpdateAfter(typeof(PathFindingTestSystem))]
public class MinionAISystem : SystemBase {
  /*
  The initial version of the minion AI is only concerned with moving the minion based on multiple
  considerations:

  In particular, the minion has a current target destination and an "ideal path" queried from 
  the navigation mesh systems.

  Additionally, each minion has a local pressure which is a measure of how many minions are nearby it
  and where the minion would need to move to reduce that instantaneous pressure.

  We are going to solve this system in a very stupid way by first pushing the unit along its path
  and then pushing around based on its local pressure. 

  We could augment our notion of local pressure to include a scale factor for each contributing elements'
  velocity. This would mean that a fast-moving minion pushing "into" you would be weighed more heavily
  in the calculation of instantaneous pressure. 

  Furthermore, we could also restrict a unit's ability to react to local pressure based on its inertia
  which is simply its velocity when all units share the same mass. 

  Finally, we could give priority to units that are currently closer to the goal such that they are less likely
  to move in response to pressure. As a result, they will continue to try to get to the goal but be less
  willing to move in response to the presence of other minions.

  The algorithm thus will look as follows:

  Push unit along path based on maximum speed
  Compute local pressure
  Push unit along negative gradient of the local pressure
  */
  static float3 TravelAlongPath(in DynamicBuffer<NavigationPathPoint> path, in float maxDistance) {
    float remainingTravelableDistance = maxDistance;
    float3 position = path[0].Value;

    for (int i = 1; i < path.Length; i++) {
      float3 segmentStart = path[i-1].Value;
      float3 segmentEnd = path[i].Value;
      float3 delta = segmentEnd - segmentStart;
      float segmentDistance = length(delta);

      if (segmentDistance < remainingTravelableDistance) {
        position = segmentEnd;
        remainingTravelableDistance -= segmentDistance;
      } else {
        position += remainingTravelableDistance / segmentDistance * delta; 
        break;
      }
    }
    return position;
  }

  static int2 BucketIndex(in float3 position) {
    return int2((int)position.x, (int)position.z);
  }

  static float3 ComputeLocalPressure(in NativeMultiHashMap<int2, float3> spatialIndex, in int2 bucketIndex, in float3 position) {
    float3 pressure = float3(0,0,0);
    int count = 0;

    for (int i = bucketIndex.x - 1; i <= bucketIndex.x + 1; i++) {
      for (int j = bucketIndex.y - 1; j <= bucketIndex.y + 1; j++) {
        int2 bi = int2(i,j);
        if (spatialIndex.TryGetFirstValue(bi, out float3 p, out NativeMultiHashMapIterator<int2> iterator)) {
          do {
            if (p.x == position.x && p.z == position.z) {
              // TODO: this is horrible... may need to check for actual identity to not "interact" with itself...
              // This would require adding the entity id to this nativemultihashmap 
            }
            else {
              float3 v = p - position;
              v.y = 0; // TODO: a hack to figure out where y displacement is coming from
              float d = length(v);
              float d2 = d * d;

              if (d <= .5f) {
                pressure += v / d2;
                count += 1;
              }
            }
          } while (spatialIndex.TryGetNextValue(out p, ref iterator));
        }
      }
    }
    pressure /= math.max(count, 1);
    return pressure;
  }

  protected override void OnUpdate() {
    const int MAX_QUERYABLE_MINIONS = 1024;
    const float PRESSURE_SCALE_FACTOR = .01f;
    const int SUBSTEP_COUNT = 12;
    float dt = Time.DeltaTime;
    BufferFromEntity<NavigationPathPoint> navBuffers = GetBufferFromEntity<NavigationPathPoint>(true);

    Entities
    .WithBurst()
    .WithReadOnly(navBuffers)
    .ForEach((Entity e, ref Translation translation, in Traveler traveler) => {
      DynamicBuffer<NavigationPathPoint> path = navBuffers[e];
      float maxPathDistance = traveler.speed * dt;
      
      translation.Value = (path.Length > 1) ? TravelAlongPath(path, maxPathDistance) : translation.Value;
    }).ScheduleParallel();

    // Step the pressure iteratively to try to let it converge
    for (int i = 0; i < SUBSTEP_COUNT; i++) {
      NativeMultiHashMap<int2, float3> spatialIndex = new NativeMultiHashMap<int2, float3>(MAX_QUERYABLE_MINIONS, Allocator.TempJob);

      // TODO: Could try a .Concurrent NativeMultiHashMap and ScheduleParallel. Not sure if it will actually be faster though w/ write-locking
      Entities
      .WithBurst()
      .WithAll<Traveler>()
      .ForEach((in Translation translation) => {
        float3 position = translation.Value;
        int2 bucketIndex = BucketIndex(position);

        spatialIndex.Add(bucketIndex, position);
      }).Schedule();

      Entities
      .WithBurst()
      .WithReadOnly(spatialIndex)
      .WithDisposeOnCompletion(spatialIndex)
      .WithAll<Traveler>()
      .ForEach((ref Translation translation) => {
        int2 bucketIndex = BucketIndex(translation.Value);
        float3 localPressure = ComputeLocalPressure(spatialIndex, bucketIndex, translation.Value);
        float3 adjustedPosition = localPressure * PRESSURE_SCALE_FACTOR;

        translation.Value -= adjustedPosition;
      }).ScheduleParallel();
    }
  }
}