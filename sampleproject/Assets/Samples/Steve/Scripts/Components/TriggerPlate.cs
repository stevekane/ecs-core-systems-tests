using System;
using Unity.NetCode;
using Unity.Entities;

[Serializable]
[GenerateAuthoringComponent]
public struct TriggerPlate : IComponentData {
  public enum TriggerState { JustTriggered, Triggered, JustUnTriggered, UnTriggered }

  [GhostField] public bool Active;
  [GhostField] public TriggerState State;
  [GhostField] public float TriggerDuration;
  [GhostField] public float TriggerTimer;
  [GhostField] public int TriggerCost;
}