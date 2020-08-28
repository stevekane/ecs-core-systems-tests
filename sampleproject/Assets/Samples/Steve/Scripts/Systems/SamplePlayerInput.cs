using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class SamplePlayerInput : ComponentSystem {
  protected override void OnCreate() {
    RequireSingletonForUpdate<NetworkIdComponent>();
    RequireSingletonForUpdate<EnableNetGame>();
  }

  protected override void OnUpdate() {
    var localInputEntity = GetSingleton<CommandTargetComponent>().targetEntity;

    if (localInputEntity == Entity.Null) {
      var localPlayerId = GetSingleton<NetworkIdComponent>().Value;

      Entities
      .WithAll<Player>()
      .WithNone<PlayerInput>()
      .ForEach((Entity ent, ref GhostOwnerComponent ghostOwner) => {
        if (ghostOwner.NetworkId == localPlayerId) {
          var e = GetSingletonEntity<CommandTargetComponent>();
          var ctc = new CommandTargetComponent { targetEntity = ent };

          PostUpdateCommands.AddBuffer<PlayerInput>(ent);
          PostUpdateCommands.SetComponent(e, ctc);
        }
      });
    } else {
      var input = default(PlayerInput);

      input.tick = World.GetExistingSystem<ClientSimulationSystemGroup>().ServerTick;
      input.horizontal = (Input.GetKey("a") ? -1 : 0) + (Input.GetKey("d") ? 1 : 0);
      input.vertical = (Input.GetKey("s") ? -1 : 0) + (Input.GetKey("w") ? 1 : 0);
      EntityManager.GetBuffer<PlayerInput>(localInputEntity).AddCommandData(input);
    }
  }
}