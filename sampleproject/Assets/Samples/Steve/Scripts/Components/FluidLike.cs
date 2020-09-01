using System;
using Unity.Entities;

[Serializable]
[GenerateAuthoringComponent]
public struct FluidLike : IComponentData {
  public float density;
  public float lagrangeMultiplier;
}