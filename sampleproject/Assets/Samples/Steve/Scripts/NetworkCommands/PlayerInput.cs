using Unity.NetCode;
using Unity.Networking.Transport;

public class NetPlayerSendCommandSystem : CommandSendSystem<PlayerInput> {}
public class NetPlayerReceiveCommandSystem : CommandReceiveSystem<PlayerInput> {}

public struct PlayerInput : ICommandData<PlayerInput> {
  public uint Tick => tick;
  public uint tick;
  public int horizontal;
  public int vertical;

  public void Deserialize(uint tick, ref DataStreamReader reader) {
    this.tick = tick;
    horizontal = reader.ReadInt();
    vertical = reader.ReadInt();
  }

  public void Serialize(ref DataStreamWriter writer) {
    writer.WriteInt(horizontal);
    writer.WriteInt(vertical);
  }

  public void Deserialize(uint tick, ref DataStreamReader reader, PlayerInput baseline, NetworkCompressionModel compressionModel) {
    Deserialize(tick, ref reader);
  }

  public void Serialize(ref DataStreamWriter writer, PlayerInput baseline, NetworkCompressionModel compressionModel) {
    Serialize(ref writer);
  }
}