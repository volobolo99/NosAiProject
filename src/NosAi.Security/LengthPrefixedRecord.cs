using System.Buffers.Binary;

namespace NosAi.Security;

/// <summary>
/// 2-byte big-endian length prefix used to delimit Noise records on a
/// byte stream (TCP has none of its own). Independent of
/// <see cref="NosFrameHeader"/>: the prefix frames the Noise ciphertext,
/// and a decoded transport plaintext then contains one Nos frame.
/// </summary>
public static class LengthPrefixedRecord
{
    public const int PrefixSize = 2;
    public const int MaxLength = 65535;

    public static async ValueTask WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (payload.Length > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, $"Record exceeds the {MaxLength}-byte Noise message limit.");

        byte[] prefix = new byte[PrefixSize];
        BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)payload.Length);
        await stream.WriteAsync(prefix, ct).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one length-prefixed record into <paramref name="destination"/>.
    /// </summary>
    /// <returns>
    /// Bytes written into <paramref name="destination"/>, or -1 when the peer
    /// closed the stream before a new record began (a clean disconnect). A
    /// truncated record throws <see cref="EndOfStreamException"/> instead of
    /// returning a partial buffer. An empty record (declared length 0) returns 0.
    /// </returns>
    public static async ValueTask<int> ReadAsync(Stream stream, Memory<byte> destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] prefix = new byte[PrefixSize];
        int prefixRead = await ReadAtLeastAsync(stream, prefix, PrefixSize, ct, allowZero: true).ConfigureAwait(false);
        if (prefixRead == 0)
            return -1;

        ushort length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
        if (length > MaxLength || length > destination.Length)
            throw new InvalidOperationException($"Declared record length {length} exceeds the destination or the {MaxLength}-byte limit.");

        if (length == 0)
            return 0;

        await ReadAtLeastAsync(stream, destination[..length], length, ct, allowZero: false).ConfigureAwait(false);
        return length;
    }

    private static async ValueTask<int> ReadAtLeastAsync(Stream stream, Memory<byte> destination, int required, CancellationToken ct, bool allowZero)
    {
        int total = 0;
        while (total < required)
        {
            int read = await stream.ReadAsync(destination[total..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowZero && total == 0)
                    return 0;

                throw new EndOfStreamException("Peer closed the stream in the middle of a length-prefixed record.");
            }

            total += read;
        }

        return total;
    }
}
