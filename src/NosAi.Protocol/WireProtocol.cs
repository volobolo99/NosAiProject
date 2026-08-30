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

    /// <summary>
    /// The runtime proving itself to the phone, before the phone signs anything.
    /// </summary>
    /// <remarks>
    /// Added in version 2. Without it the channel authenticated the phone to the
    /// PC and not the reverse, so on a network the operator does not control a
    /// hostile host could answer discovery first and act as a runtime.
    /// </remarks>
    ServerAuthProof = 0x08,
    Heartbeat = 0x06,
    HeartbeatAck = 0x07,
    WorldStateDelta = 0x10,
    TelemetrySnapshot = 0x11,
    CommandRequest = 0x20,
    CommandAck = 0x21,
    Disconnect = 0xFF
}

/// <summary>Which messages travel in clear, and which must be sealed.</summary>
public static class WireMessageTypes
{
    /// <summary>
    /// True for the messages that establish the session keys.
    /// </summary>
    /// <remarks>
    /// These travel in clear because they are what produces the keys, and they
    /// are already authenticated by RSA. Everything else is encrypted under
    /// ADR-0009 and is refused if it arrives any other way — the predicate is
    /// stated once here so the runtime and the phone cannot disagree about which
    /// frames are allowed to be readable.
    /// </remarks>
    public static bool IsHandshake(WireMessageType type) => type switch
    {
        WireMessageType.SessionHello => true,
        WireMessageType.Capabilities => true,
        WireMessageType.AuthChallenge => true,
        WireMessageType.AuthResponse => true,
        WireMessageType.AuthResult => true,
        WireMessageType.ServerAuthProof => true,
        _ => false
    };
}

public readonly record struct WireHeader(WireMessageType MessageType, ushort PayloadLength, uint SequenceNumber)
{
    public const uint ExpectedMagic = 0x4E4F5341; // NOSA
    /// <summary>
    /// Wire version. Bumped to 2 by mutual authentication, to 3 by payload
    /// encryption (ADR-0009).
    /// </summary>
    /// <remarks>
    /// An older peer is refused rather than downgraded. Version 1 cannot prove the
    /// runtime to the phone and version 2 sends the payload in clear, so accepting
    /// either would leave exactly the hole the bump exists to close. There is no
    /// negotiation: a channel that agrees to skip encryption when asked is a
    /// channel with no encryption. Both ends ship together, so there is nothing to
    /// stay compatible with.
    /// </remarks>
    public const byte CurrentVersion = 3;
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
