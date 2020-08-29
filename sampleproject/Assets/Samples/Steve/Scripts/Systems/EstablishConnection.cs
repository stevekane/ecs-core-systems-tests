using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
public class EstablishConnection : ComponentSystem {
  struct InitGameComponent : IComponentData {}

  protected override void OnCreate() {
    RequireSingletonForUpdate<InitGameComponent>();
    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Steve")
        return;
    EntityManager.CreateEntity(typeof(InitGameComponent));
  }

  protected override void OnUpdate() {
    EntityManager.DestroyEntity(GetSingletonEntity<InitGameComponent>());
    foreach (var world in World.All) {
      var network = world.GetExistingSystem<NetworkStreamReceiveSystem>();
      var clientSimGroup = world.GetExistingSystem<ClientSimulationSystemGroup>();
      var serverSimGroup = world.GetExistingSystem<ServerSimulationSystemGroup>();

      if (clientSimGroup != null) {
        NetworkEndPoint endPoint = NetworkEndPoint.LoopbackIpv4;

        endPoint.Port = 7979;
        #if UNITY_EDITOR
        endPoint = NetworkEndPoint.Parse(ClientServerBootstrap.RequestedAutoConnect, 7979);
        #endif

        network.Connect(endPoint);
        world.EntityManager.CreateEntity(typeof(EnableNetGame));
      }
      #if UNITY_EDITOR || UNITY_SERVER
      else if (serverSimGroup != null) {
        NetworkEndPoint endPoint = NetworkEndPoint.LoopbackIpv4;

        endPoint.Port = 7979;
        network.Listen(endPoint);
        world.EntityManager.CreateEntity(typeof(EnableNetGame));
      }
      #endif
    }
  }
}
