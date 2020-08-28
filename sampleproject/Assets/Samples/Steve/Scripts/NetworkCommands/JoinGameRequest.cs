using System;
using Unity.Burst;
using Unity.NetCode;

[Serializable]
[BurstCompile]
public struct JoinGameRequest : IRpcCommand {}