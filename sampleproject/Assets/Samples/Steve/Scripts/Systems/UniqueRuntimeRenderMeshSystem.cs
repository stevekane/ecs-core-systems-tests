using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(ClientInitializationSystemGroup))]
public class UniqueRuntimeRenderMeshSystem : SystemBase {
    protected override void OnUpdate() {
    Entities
    .WithoutBurst()
    .WithStructuralChanges()
    .ForEach((Entity entity, ref UniqueRuntimeRenderMesh uniqueRuntimeRenderMesh) => {
      var renderMesh = EntityManager.GetSharedComponentData<RenderMesh>(entity);

      renderMesh.material = new Material(renderMesh.material);
      EntityManager.RemoveComponent<UniqueRuntimeRenderMesh>(entity);
      EntityManager.SetSharedComponentData<RenderMesh>(entity, renderMesh);
    }).Run();
  }
}