using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
[GenerateAuthoringComponent]
public struct TriggerPlateRenderer : IComponentData {
  public Entity TriggerPlate;
}