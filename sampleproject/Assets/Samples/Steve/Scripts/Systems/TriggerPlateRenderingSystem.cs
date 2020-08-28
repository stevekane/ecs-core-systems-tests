using Unity.Entities;
using Unity.Rendering;
using Unity.NetCode;

[UpdateInGroup(typeof(ClientPresentationSystemGroup))]
public class TriggerPlateRenderingSystem : SystemBase {
  protected override void OnUpdate() {
    Entities
    .WithoutBurst()
    .WithStructuralChanges()
    .ForEach((Entity entity, in TriggerPlateRenderer triggerPlateRenderer) => {
      var triggerPlate = EntityManager.GetComponentData<TriggerPlate>(triggerPlateRenderer.TriggerPlate);
      var renderMesh = EntityManager.GetSharedComponentData<RenderMesh>(entity);
      var baseColor = UnityEngine.Color.white;
      var activeColor = UnityEngine.Color.blue;
      var color = UnityEngine.Color.Lerp(baseColor, activeColor, triggerPlate.TriggerTimer / triggerPlate.TriggerDuration);

      renderMesh.material.SetColor("_Color", color);
      EntityManager.SetSharedComponentData<RenderMesh>(entity, renderMesh);
    }).Run();
  }
}