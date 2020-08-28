using Unity.Entities;
using Unity.Jobs;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.NetCode;
using Unity.Burst;
using Unity.Collections;
using Unity.Rendering;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class TriggerPlateSystem : JobComponentSystem {
  [BurstCompile]
  public struct TriggerJob : ITriggerEventsJob {
    [ReadOnly] public float dt;
    [ReadOnly] public ComponentDataFromEntity<Player> players;
    public ComponentDataFromEntity<TriggerPlate> triggerPlates;

    static TriggerPlate UpdateTriggerPlate(TriggerPlate plate, float dt) {
      // Kind of a lame way to detect if this plate has already been interacted with this frame...
      if (plate.Active) return plate;

      switch (plate.State) {
      case TriggerPlate.TriggerState.JustTriggered:
      case TriggerPlate.TriggerState.Triggered:
        plate.Active = true;
        plate.State = TriggerPlate.TriggerState.Triggered;
        plate.TriggerTimer += dt;
      break;
      case TriggerPlate.TriggerState.UnTriggered:
        plate.Active = true;
        plate.State = TriggerPlate.TriggerState.JustTriggered;
      break;
      }
      return plate;
    }

    public void Execute(TriggerEvent triggerEvent) {
      if (triggerPlates.HasComponent(triggerEvent.EntityA) && players.HasComponent(triggerEvent.EntityB)) {
        triggerPlates[triggerEvent.EntityA] = UpdateTriggerPlate(triggerPlates[triggerEvent.EntityA], dt);
      }
      if (triggerPlates.HasComponent(triggerEvent.EntityB) && players.HasComponent(triggerEvent.EntityA)) {
        triggerPlates[triggerEvent.EntityB] = UpdateTriggerPlate(triggerPlates[triggerEvent.EntityB], dt);
      }
    }
  }

  BuildPhysicsWorld buildPhysicsWorld;
  StepPhysicsWorld stepPhysicsWorld;

  protected override void OnCreate() {
    buildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
    stepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
  }

  protected override JobHandle OnUpdate(JobHandle inputDeps) {
    var prePass = Entities
    .ForEach((Entity e, ref TriggerPlate triggerPlate) => {
      triggerPlate.Active = false;
    }).Schedule(inputDeps);

    var triggerPass = new TriggerJob {
      dt = Time.DeltaTime,
      players = GetComponentDataFromEntity<Player>(true),
      triggerPlates = GetComponentDataFromEntity<TriggerPlate>()
    }.Schedule(stepPhysicsWorld.Simulation, ref buildPhysicsWorld.PhysicsWorld, prePass);

    var postPass = Entities
    .ForEach((Entity e, ref TriggerPlate triggerPlate) => {
      if (triggerPlate.Active)
        return;

      triggerPlate.TriggerTimer = 0;
      switch (triggerPlate.State) {
      case TriggerPlate.TriggerState.Triggered:
      case TriggerPlate.TriggerState.JustTriggered:
        triggerPlate.State = TriggerPlate.TriggerState.JustUnTriggered;
      break;
      case TriggerPlate.TriggerState.UnTriggered:
      case TriggerPlate.TriggerState.JustUnTriggered:
        triggerPlate.State = TriggerPlate.TriggerState.UnTriggered;
      break;
      }
    }).Schedule(triggerPass);

    return postPass;
  }
}
