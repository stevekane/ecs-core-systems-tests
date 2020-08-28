using System;
using Unity.Entities;
using Unity.Mathematics;

[Serializable]
[GenerateAuthoringComponent]
public struct Traveler : IComponentData {
  public float3 targetPosition;
}