using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class JoinGameServer : ComponentSystem {
  protected override void OnCreate() {
    RequireSingletonForUpdate<EnableNetGame>();
  }

  protected override void OnUpdate() {
    Entities
    .WithNone<SendRpcCommandRequestComponent>()
    .ForEach((Entity requestEntity, ref JoinGameRequest request, ref ReceiveRpcCommandRequestComponent reqSrc) => {
      PostUpdateCommands.AddComponent<NetworkStreamInGame>(reqSrc.SourceConnection);

      // Logging
      {
        var id = EntityManager.GetComponentData<NetworkIdComponent>(reqSrc.SourceConnection).Value;

        UnityEngine.Debug.Log($"Server setting connection {id} to in game");
      }
      var ghostCollection = GetSingleton<GhostPrefabCollectionComponent>();
      var prefab = Entity.Null;
      var serverPrefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(ghostCollection.serverPrefabs);
      for (int ghostId = 0; ghostId < serverPrefabs.Length; ++ghostId) {
        if (EntityManager.HasComponent<Player>(serverPrefabs[ghostId].Value)) {
          prefab = serverPrefabs[ghostId].Value;
        }
      }
      var player = EntityManager.Instantiate(prefab);
      var networkId = EntityManager.GetComponentData<NetworkIdComponent>(reqSrc.SourceConnection).Value;
      var ghostOwner = new GhostOwnerComponent { NetworkId = networkId };
      var commandTarget = new CommandTargetComponent { targetEntity = player };

      EntityManager.SetComponentData(player, ghostOwner);
      PostUpdateCommands.AddBuffer<PlayerInput>(player);
      PostUpdateCommands.SetComponent(reqSrc.SourceConnection, commandTarget);
      PostUpdateCommands.DestroyEntity(requestEntity);
    });
  }
}