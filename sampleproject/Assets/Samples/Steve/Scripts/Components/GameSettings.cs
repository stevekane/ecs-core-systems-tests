using System;
using Unity.Entities;

[Serializable]
[GenerateAuthoringComponent]
public struct GameSettings : IComponentData {
  public float PlayerSpeed;
}