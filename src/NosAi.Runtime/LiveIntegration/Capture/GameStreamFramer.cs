using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration.Capture;

/// <summary>One decoded application frame, classified by how it was obtained.</summary>
/// <remarks>
/// A frame the framer parsed cleanly is <see cref="DataSourceKind.Live"/>. A run
/// of bytes it could not frame is surfaced as <see cref="DataSourceKind.Unknown"/>
/// with the bytes attached for diagnosis — never dropped, and never guessed into
/// a plausible message.
/// </remarks>
public sealed record GameFrame(
    DataSourceKind Source,
    int Opcode,
    ReadOnlyMemory<byte> Body,
    string? Reason)
{
    public static GameFrame Live(int opcode, ReadOnlyMemory<byte> body) =>
        new(DataSourceKind.Live, opcode, body, null);

    public static GameFrame Unframed(ReadOnlyMemory<byte> raw, string reason) =>
        new(DataSourceKind.Unknown, -1, raw, reason);
}

/// <summary>
/// Turns a reassembled byte stream into application frames.
/// </summary>
/// <remarks>
/// This is the seam where knowledge of the NosTale protocol goes, and the point
/// past which honesty gets hard. A decoder that falls out of sync with a
/// proprietary, often obfuscated protocol will keep producing byte runs that
/// <i>look</i> like messages, and the classification discipline exists precisely
/// so those are labelled <see cref="DataSourceKind.Unknown"/> rather than fed to
/// the world model as facts.
/// </remarks>
public interface IGameStreamFramer
{
    /// <summary>The direction this framer decodes.</summary>
    StreamDirection Direction { get; }

    /// <summary>
    /// Consumes newly delivered bytes and returns whatever frames completed.
    /// </summary>
    /// <remarks>
    /// Bytes that do not complete a frame are retained for the next call, so a
    /// message split across TCP segments is assembled rather than lost.
    /// </remarks>
    IReadOnlyList<GameFrame> Consume(ReadOnlySpan<byte> delivered);
}

/// <summary>
/// The framer in use until a real NosTale decoder is written against captured
/// traffic.
/// </summary>
/// <remarks>
/// <para>
/// It is not a stub that pretends. It accumulates the stream faithfully and
/// reports every byte as <see cref="DataSourceKind.Unknown"/>, because that is
/// the truthful classification of bytes from a protocol this code does not yet
/// know how to read. ADR-0014 lifted the prohibition on reading the traffic; it
/// did not grant the ability to interpret it, and inventing a decoder would be
/// the "plausible number" the same decision forbids.
/// </para>
/// <para>
/// Swapping in a real framer is the whole point of the interface: the capture
/// path, the reassembler and the classification are all real and tested now, so
/// the decoder is the only piece that has to wait for data.
/// </para>
/// </remarks>
public sealed class UnknownGameStreamFramer : IGameStreamFramer
{
    private long _totalBytes;

    public UnknownGameStreamFramer(StreamDirection direction) => Direction = direction;

    public StreamDirection Direction { get; }

    /// <summary>Bytes seen so far, so a caller can confirm the stream is flowing.</summary>
    public long TotalBytes => _totalBytes;

    public IReadOnlyList<GameFrame> Consume(ReadOnlySpan<byte> delivered)
    {
        if (delivered.Length == 0)
            return Array.Empty<GameFrame>();

        _totalBytes += delivered.Length;
        // One UNKNOWN frame carrying the raw bytes: the stream is real and flowing,
        // and nothing about its meaning is claimed.
        return new[] { GameFrame.Unframed(delivered.ToArray(), "no_nostale_decoder") };
    }
}
