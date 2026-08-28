namespace NosAi.Runtime.Security;

/// <summary>
/// Safe network boundary for telemetry/diagnostics. It does not inject or alter
/// game traffic; live packet manipulation remains disabled in 1.0 Beta.
/// </summary>
public interface IPacketManipulator
{
    bool InjectPacket(ReadOnlySpan<byte> packetData);
    byte[] InterceptAndModify(byte[] rawPacket);
    bool ValidateConnectionState();
}

public sealed class DefaultPacketManipulator : IPacketManipulator
{
    public bool InjectPacket(ReadOnlySpan<byte> packetData)
        => false;

    public byte[] InterceptAndModify(byte[] rawPacket)
        => rawPacket is { Length: > 0 } ? rawPacket.ToArray() : Array.Empty<byte>();

    public bool ValidateConnectionState()
        => false;
}
