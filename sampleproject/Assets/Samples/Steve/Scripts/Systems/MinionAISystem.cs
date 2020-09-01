using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using static Unity.Mathematics.math;

public struct SpatialMember {
  public float3 position;
  public Entity entity;
}

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
[UpdateAfter(typeof(PathFindingTestSystem))]
public class MinionAISystem : SystemBase {
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

  static NativeList<SpatialMember> Neighbors(in Entity entity, in float3 position, in NativeMultiHashMap<int2, SpatialMember> spatialIndex, in Allocator allocator) {
    const int MAX_NEIGHBOR_COUNT = 64;

    int2 bucketIndex = BucketIndex(position);
    NativeList<SpatialMember> neighbors = new NativeList<SpatialMember>(MAX_NEIGHBOR_COUNT, allocator);

    for (int i = bucketIndex.x - 1; i <= bucketIndex.x + 1; i++) {
      for (int j = bucketIndex.y - 1; j <= bucketIndex.y + 1; j++) {
        int2 bi = int2(i,j);
        if (spatialIndex.TryGetFirstValue(bi, out SpatialMember n, out NativeMultiHashMapIterator<int2> iterator)) {
          do {
            if (entity != n.entity) {
              neighbors.Add(n);
            }
          } while (spatialIndex.TryGetNextValue(out n, ref iterator));
        }
      }
    }
    return neighbors;
  }

  // This is a function W that describes the weighted falloff relationship between a particle and another particle
  // Taken from https://github.com/InteractiveComputerGraphics/PositionBasedDynamics/blob/9f9edfe33bc7bb8a8624e0e5ca1a544fce05da88/PositionBasedDynamics/SPHKernels.h#L35
  static float W(in float3 v, in float maxDistanceSquared) {
    float rl = lengthsq(v);
    float q = rl / maxDistanceSquared;

    // These are two functions that have the same value at q=.5 and smoothly blend to one-another
    if (q <= .5) {
      float q2 = q * q;
      float q3 = q2 * q;

      return 6*q3 - 6*q2 + 1;
    } else if (q <= 1) {
      return 2*pow(1-q, 3);
    } else {
      return 0;
    }
  }

  // This is a function gradW that is the gradient of the function W above
  // Taken from https://github.com/InteractiveComputerGraphics/PositionBasedDynamics/blob/9f9edfe33bc7bb8a8624e0e5ca1a544fce05da88/PositionBasedDynamics/SPHKernels.h#L59
  static float3 GradW(in float3 v, in float maxDistanceSquared) {
    const float EPSILON = 1e-6f;

    float rl = lengthsq(v);
    float q = rl / maxDistanceSquared;

    if (rl <= EPSILON) 
      return float3(0,0,0);

    float3 gradq = v / (rl * maxDistanceSquared);

    // These are two functions that have the same value at q=.5 and smoothly blend to one-another
    if (q <= .5) {
      return q * (3*q - 2) * gradq;
    } else if (q <= 1) {
      float factor = 1-q;

      return -factor*factor*gradq;
    } else {
      return float3(0,0,0);
    }
  }

  // p_i in equations of position-based-fluid constraints
  static float Density(in NativeList<SpatialMember> neighbors, in float3 position) {
    const float MAX_DISTANCE_SQUARED = .5f * .5f;
    float density = 1;

    for (int i = 0; i < neighbors.Length; i++) {
      density += W(position - neighbors[i].position, MAX_DISTANCE_SQUARED);
    }
    return density;
  }

  // lambda in equations of position-based-fluid constraints
  static float LagrangeMultiplier(in NativeList<SpatialMember> neighbors, in float3 position, in float density) {
    const float MAX_DISTANCE_SQUARED = .5f * .5f;
    const float EPSILON = 1e-6f;
    float constraint = max(0, density - 1);

    if (constraint == 0)
      return 0;

    float sumGradC2 = 0;
    float3 gradCi = float3(0,0,0);

    for (int i = 0; i < neighbors.Length; i++) {
      float3 gradCj = -GradW(position - neighbors[i].position, MAX_DISTANCE_SQUARED);

      sumGradC2 += dot(gradCj, gradCj);
      gradCi -= gradCj;
    }
    sumGradC2 += dot(gradCi, gradCi);
    return -constraint / (sumGradC2 + EPSILON);
  }

  static float3 ProjectDensityConstraint(in NativeList<SpatialMember> neighbors, in ComponentDataFromEntity<FluidLike> fluidLikes, in Entity entity, in float3 position) {
    const float MAX_DISTANCE_SQUARED = .5f * .5f;

    float3 delta = float3(0,0,0);
    for (int i = 0; i < neighbors.Length; i++) {
      SpatialMember neighbor = neighbors[i];
      float3 gradC = -GradW(position - neighbor.position, MAX_DISTANCE_SQUARED);
      float lagrangeMultiplier1 = fluidLikes[entity].lagrangeMultiplier;
      float lagrangeMultiplier2 = fluidLikes[neighbor.entity].lagrangeMultiplier;

      delta -= (lagrangeMultiplier1 + lagrangeMultiplier2) * gradC;
    }
    return delta;
  }

  protected override void OnUpdate() {
    float dt = Time.DeltaTime;
    BufferFromEntity<NavigationPathPoint> navBuffers = GetBufferFromEntity<NavigationPathPoint>(true);

    // move each traveler along their path
    Entities
    .WithBurst()
    .WithReadOnly(navBuffers)
    .ForEach((Entity e, ref Translation translation, in FluidLike fluidLike, in Traveler traveler) => {
      DynamicBuffer<NavigationPathPoint> path = navBuffers[e];
      float maxPathDistance = traveler.speed * dt;

      // TODO: I think I need to add something here that modulates the urgency of path-finding by local density
      // If you're tightly surrounded you should not try very hard to move

      translation.Value = (path.Length > 1) ? TravelAlongPath(path, maxPathDistance) : translation.Value;
    }).ScheduleParallel();

    const int MAX_QUERYABLE_MINIONS = 1024;
    const int ITERATION_COUNT = 8;

    for (int i = 0; i < ITERATION_COUNT; i++) {
      NativeMultiHashMap<int2, SpatialMember> spatialIndex = new NativeMultiHashMap<int2, SpatialMember>(MAX_QUERYABLE_MINIONS, Allocator.TempJob);

      // Create spatial index to store nearest neighbors
      Entities
      .WithBurst()
      .WithAll<Traveler, FluidLike>()
      .ForEach((Entity entity, in Translation translation) => {
        float3 position = translation.Value;
        int2 bucketIndex = BucketIndex(position);

        spatialIndex.Add(bucketIndex, new SpatialMember { entity = entity, position = position });
      }).Schedule();

      // Compute each particle's density and lagrange multiplier via neighbors
      Entities
      .WithBurst()
      .WithReadOnly(spatialIndex)
      .WithAll<Traveler>()
      .ForEach((Entity e, ref FluidLike fluidLike, in Translation translation) => {
        NativeList<SpatialMember> neighbors = Neighbors(e, translation.Value, spatialIndex, Allocator.Temp);
        float density = Density(neighbors, translation.Value);
        float lagrangeMultiplier = LagrangeMultiplier(neighbors, translation.Value, density);

        fluidLike.density = density;
        fluidLike.lagrangeMultiplier = lagrangeMultiplier;
        neighbors.Dispose();
      }).ScheduleParallel();

      ComponentDataFromEntity<FluidLike> fluidLikes = GetComponentDataFromEntity<FluidLike>(false);

      // Project density constraints 
      Entities
      .WithBurst()
      .WithReadOnly(spatialIndex)
      .WithReadOnly(fluidLikes)
      .WithDisposeOnCompletion(spatialIndex) // TODO: Disposing here
      .WithAll<Traveler, FluidLike>()
      .ForEach((Entity entity, ref Translation translation) => {
        NativeList<SpatialMember> neighbors = Neighbors(entity, translation.Value, spatialIndex, Allocator.Temp);
        float3 delta = ProjectDensityConstraint(neighbors, fluidLikes, entity, translation.Value);

        translation.Value += delta;
        neighbors.Dispose();
      }).ScheduleParallel();
    }
  }
}