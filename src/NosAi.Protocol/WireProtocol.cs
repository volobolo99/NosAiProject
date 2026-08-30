using System.Buffers.Binary;

namespace NosAi.Runtime.Gate1;

// The canonical PC<->phone wire primitives, split out of Gate1Runtime.cs so the
// Guard AI client can compile the very same source instead of restating the
// format. ADR-0006 makes this the only canonical channel, and a client that
// re-derives the layout is a drift waiting to happen: the phone would keep
// talking a protocol the runtime stopped speaking, with no build error to say so.
//
// Shared by <Compile Include="..." Link="..."/> from src/NosAi.GuardClient.
// Keep this file free of server-side and Windows-only dependencies.

public enum WireMessageType : byte
{
    SessionHello = 0x01,
    Capabilities = 0x02,
    AuthChallenge = 0x03,
    AuthResponse = 0x04,
    AuthResult = 0x05,
    Heartbeat = 0x06,
    HeartbeatAck = 0x07,
    WorldStateDelta = 0x10,
    TelemetrySnapshot = 0x11,
    CommandRequest = 0x20,
    CommandAck = 0x21,
    Disconnect = 0xFF
}

public readonly record struct WireHeader(WireMessageType MessageType, ushort PayloadLength, uint SequenceNumber)
{
    public const uint ExpectedMagic = 0x4E4F5341; // NOSA
    public const byte CurrentVersion = 1;
    public const int HeaderSize = 12;
    public const int MaxPayloadLength = ushort.MaxValue;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination buffer is smaller than the 12-byte header.", nameof(destination));
        BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], ExpectedMagic);
        destination[4] = CurrentVersion;
        destination[5] = (byte)MessageType;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], PayloadLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], SequenceNumber);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out WireHeader header, out string? error)
    {
        header = default;
        error = null;
        if (source.Length < HeaderSize) { error = "incomplete_header"; return false; }
        if (BinaryPrimitives.ReadUInt32BigEndian(source[0..4]) != ExpectedMagic) { error = "invalid_magic"; return false; }
        if (source[4] != CurrentVersion) { error = "unsupported_version"; return false; }
        header = new WireHeader((WireMessageType)source[5], BinaryPrimitives.ReadUInt16BigEndian(source[6..8]), BinaryPrimitives.ReadUInt32BigEndian(source[8..12]));
        return true;
    }
}

public sealed class SequenceGuard
{
    private readonly object _sync = new();
    private uint _expected;

    public SequenceGuard(uint expected = 1) => _expected = expected;

    public bool ValidateAndAdvance(uint received, out string? reason)
    {
        lock (_sync)
        {
            if (received == _expected)
            {
                _expected = _expected == uint.MaxValue ? 1 : _expected + 1;
                reason = null;
                return true;
            }
            reason = received < _expected ? "replay_or_duplicate" : "sequence_gap";
            return false;
        }
    }

    public uint Next { get { lock (_sync) return _expected; } }
    public void Reset(uint expected = 1) { lock (_sync) _expected = expected; }
}
