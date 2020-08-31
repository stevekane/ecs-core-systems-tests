using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public class MovePlayer : ComponentSystem {
  protected override void OnUpdate() {
    var group = World.GetExistingSystem<GhostPredictionSystemGroup>();
    var tick = group.PredictingTick;
    var dt = group.Time.DeltaTime;

    Entities
    .ForEach((DynamicBuffer<PlayerInput> inputBuffer, ref Player player, ref Translation translation, ref Rotation rotation, ref PredictedGhostComponent prediction) => {
      if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
        return;

      inputBuffer.GetDataAtTick(tick, out PlayerInput input);
      
      if (input.horizontal == 0 && input.vertical == 0)
        return;

      float3 direction = normalize(float3(input.horizontal, 0, input.vertical));
      float3 velocity = direction * dt * player.speed;

      translation.Value += velocity;
      rotation.Value = Quaternion.LookRotation(direction, float3(0,1,0));
    });
  }
}