using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;

[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public class MovePlayer : ComponentSystem {
  protected override void OnUpdate() {
    var group = World.GetExistingSystem<GhostPredictionSystemGroup>();
    var tick = group.PredictingTick;
    var dt = Time.DeltaTime;

    Entities
    .ForEach((DynamicBuffer<PlayerInput> inputBuffer, ref Translation translation, ref PredictedGhostComponent prediction) => {
      if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
        return;

      inputBuffer.GetDataAtTick(tick, out PlayerInput input);
      translation.Value.x += dt * input.horizontal;
      translation.Value.z += dt * input.vertical;
    });
  }
}