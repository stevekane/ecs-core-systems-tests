using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.AI;

[Serializable]
[GenerateAuthoringComponent]
public struct Traveler : IComponentData {
  public float3 targetPosition;
}