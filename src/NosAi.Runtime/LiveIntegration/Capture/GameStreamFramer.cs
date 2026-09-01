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
/// The default framer: bytes flow, nothing is claimed about their meaning.
/// </summary>
/// <remarks>
/// <para>
/// It is not a stub that pretends. It accumulates the stream faithfully and
/// reports every byte as <see cref="DataSourceKind.Unknown"/>. The world-channel
/// decoder is <see cref="NosTaleWorldFramer"/>, opted in through the capture
/// engine's factory — this class stays the default so a capture that has not
/// chosen a decoder cannot start looking decoded.
/// </para>
/// <para>
/// Outbound traffic still belongs here even after that opt-in: client-to-server
/// uses a session-keyed encoding this runtime does not read.
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
