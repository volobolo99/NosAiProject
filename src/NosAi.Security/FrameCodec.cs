using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using NosAi.Core;

namespace NosAi.Security;

/// <summary>
/// Computes the frame authentication tag for one session key, reused across
/// every frame in that session so no HMAC provider is allocated per frame
/// (INV-07). Not thread-safe: one instance per session, matching the codec's
/// single-writer/single-reader-per-direction model.
/// </summary>
public sealed class FrameTagCalculator : IDisposable
{
    private readonly IncrementalHash _hmac;

    public FrameTagCalculator(ReadOnlySpan<byte> sessionKey)
    {
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, sessionKey);
    }

    /// <summary>
    /// <c>Tag = first 4 bytes of HMAC-SHA256(K_session, Version‖OpCode‖Length‖Sequence‖Payload)</c>
    /// (docs/ROADMAP_ESECUTIVA.md S:2.3). <paramref name="headerPrefix"/> must be
    /// exactly the first 8 header bytes (Version, OpCode, Length, Sequence);
    /// the Tag field itself is never part of its own input.
    /// </summary>
    public uint ComputeTag(ReadOnlySpan<byte> headerPrefix, ReadOnlySpan<byte> payload)
    {
        Span<byte> mac = stackalloc byte[32];
        _hmac.AppendData(headerPrefix);
        _hmac.AppendData(payload);
        _hmac.GetHashAndReset(mac);
        return BinaryPrimitives.ReadUInt32BigEndian(mac[..4]);
    }

    public void Dispose() => _hmac.Dispose();
}

/// <summary>
/// Encodes and decodes Gate 1 wire frames entirely over <see cref="Span{T}"/>
/// (docs/ROADMAP_ESECUTIVA.md S:2.3-2.4): big-endian multi-byte fields, no
/// heap allocation, no exception on a malformed or corrupted frame -- decode
/// failure is a return value, not a control-flow event, because a corrupted
/// frame arriving at network speed is routine, not exceptional.
/// </summary>
[SkipLocalsInit]
public static class FrameCodec
{
    public const int HeaderSize = NosFrameHeader.Size;
    public const int MaxPayloadLength = NosFrameHeader.MaxPayloadLength;
    private const int HeaderPrefixSize = 8; // Version(1) + OpCode(1) + Length(2) + Sequence(4), i.e. header minus Tag.

    /// <summary>
    /// Writes a complete frame (header + payload) into <paramref name="destination"/>.
    /// </summary>
    /// <returns>Total bytes written (<see cref="HeaderSize"/> + payload length).</returns>
    public static int Encode(byte opCode, uint sequence, ReadOnlySpan<byte> payload, FrameTagCalculator tagCalculator, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(tagCalculator);

        if ((uint)payload.Length > MaxPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, $"Payload exceeds the {MaxPayloadLength}-byte frame limit.");

        int total = HeaderSize + payload.Length;
        if (destination.Length < total)
            throw new ArgumentException("Destination buffer is too small for this frame.", nameof(destination));

        Span<byte> header = destination[..HeaderSize];
        header[0] = NosFrameHeader.CurrentVersion;
        header[1] = opCode;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), sequence);

        Span<byte> payloadDestination = destination.Slice(HeaderSize, payload.Length);
        payload.CopyTo(payloadDestination);

        uint tag = tagCalculator.ComputeTag(header[..HeaderPrefixSize], payloadDestination);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(8, 4), tag);

        return total;
    }

    /// <summary>
    /// Validates and decodes a frame. <paramref name="payload"/> aliases
    /// <paramref name="frame"/>; it is not copied.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for any structural or authentication failure
    /// (never throws for malformed input): <paramref name="fault"/> is always
    /// set to a specific <see cref="FaultCode"/> in that case, never left at
    /// <see cref="FaultCode.None"/>.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, FrameTagCalculator tagCalculator, out NosFrameHeader header, out ReadOnlySpan<byte> payload, out FaultCode fault)
    {
        ArgumentNullException.ThrowIfNull(tagCalculator);

        header = default;
        payload = default;

        if (frame.Length < HeaderSize)
        {
            fault = FaultCode.FrameInvalid;
            return false;
        }

        byte version = frame[0];
        byte opCode = frame[1];
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(2, 2));
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(4, 4));
        uint tag = BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(8, 4));

        if (version != NosFrameHeader.CurrentVersion)
        {
            fault = FaultCode.FrameInvalid;
            return false;
        }

        // Length is checked against the hard limit, and the frame is discarded,
        // before any buffer sized from it is touched (docs/ROADMAP_ESECUTIVA.md
        // S:2.3): an oversized declared length must never itself become the
        // trigger for an allocation.
        if (length > MaxPayloadLength)
        {
            fault = FaultCode.FrameInvalid;
            return false;
        }

        if (frame.Length != HeaderSize + length)
        {
            fault = FaultCode.FrameInvalid;
            return false;
        }

        ReadOnlySpan<byte> candidatePayload = frame.Slice(HeaderSize, length);
        uint expectedTag = tagCalculator.ComputeTag(frame[..HeaderPrefixSize], candidatePayload);

        Span<byte> expectedBytes = stackalloc byte[4];
        Span<byte> actualBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(expectedBytes, expectedTag);
        BinaryPrimitives.WriteUInt32BigEndian(actualBytes, tag);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            fault = FaultCode.FrameInvalid;
            return false;
        }

        header = new NosFrameHeader(version, opCode, length, sequence, tag);
        payload = candidatePayload;
        fault = FaultCode.None;
        return true;
    }
}
