using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class JoinGameClient : ComponentSystem {
  protected override void OnCreate() {
    RequireSingletonForUpdate<EnableNetGame>();
  }

  protected override void OnUpdate() {
    Entities
    .WithNone<NetworkStreamInGame>()
    .ForEach((Entity entity, ref NetworkIdComponent id) => {
      var requestEntity = PostUpdateCommands.CreateEntity();
      var rpcCommand = new SendRpcCommandRequestComponent { TargetConnection = entity };

      PostUpdateCommands.AddComponent<NetworkStreamInGame>(entity);
      PostUpdateCommands.AddComponent<JoinGameRequest>(requestEntity);
      PostUpdateCommands.AddComponent(requestEntity, rpcCommand);
    });
  }
}