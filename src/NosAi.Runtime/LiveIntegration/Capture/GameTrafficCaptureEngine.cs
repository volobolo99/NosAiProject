using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration.Capture;

/// <summary>One frame the engine produced, with its direction and time.</summary>
public sealed record CaptureFrame(DateTime TimestampUtc, StreamDirection Direction, GameFrame Frame);

/// <summary>What a capture run added up to.</summary>
/// <remarks>
/// Counts, not a verdict. The engine reassembles and frames; whether the frames
/// mean anything is the decoder's question, and until a real decoder exists every
/// frame is <see cref="DataSourceKind.Unknown"/> and this says so honestly.
/// </remarks>
public sealed record CaptureSummary(
    long PacketsRead,
    long PacketsParsed,
    long PacketsRejected,
    long OutboundBytes,
    long InboundBytes,
    long FramesProduced,
    long UnknownFrames)
{
    /// <summary>Whether any byte was decoded to something other than UNKNOWN.</summary>
    public bool AnyDecoded => FramesProduced > UnknownFrames;
}

/// <summary>
/// Drives a packet source through parsing, reassembly and framing.
/// </summary>
/// <remarks>
/// <para>
/// The one place the capture pieces are wired into a whole: a source hands over
/// raw packets, the parser turns each into a direction-labelled segment, the
/// per-direction reassembler rebuilds the ordered stream, and a framer per
/// direction turns that into frames. Everything below the source has already been
/// tested in isolation; this composes them and is tested against a synthetic
/// session.
/// </para>
/// <para>
/// A packet the parser refuses is counted, not dropped silently: a rising reject
/// count is how the operator sees that the endpoint filter is wrong or the source
/// is delivering something unexpected, rather than a capture that looks empty.
/// </para>
/// </remarks>
public sealed class GameTrafficCaptureEngine
{
    private readonly IPacketSource _source;
    private readonly TcpConversation _conversation = new();
    private readonly IGameStreamFramer _outboundFramer;
    private readonly IGameStreamFramer _inboundFramer;

    private long _packetsRead, _packetsParsed, _packetsRejected;
    private long _outboundBytes, _inboundBytes;
    private long _framesProduced, _unknownFrames;

    /// <param name="framerFactory">
    /// Builds the framer for a direction. Defaults to the honest UNKNOWN framer;
    /// a real NosTale decoder is dropped in here without touching the engine.
    /// </param>
    public GameTrafficCaptureEngine(
        IPacketSource source,
        Func<StreamDirection, IGameStreamFramer>? framerFactory = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        framerFactory ??= direction => new UnknownGameStreamFramer(direction);
        _outboundFramer = framerFactory(StreamDirection.Outbound);
        _inboundFramer = framerFactory(StreamDirection.Inbound);
    }

    /// <summary>Frames as they are produced. Fires on the pumping thread.</summary>
    public event Action<CaptureFrame>? FrameProduced;

    /// <summary>
    /// Pumps packets until the source is exhausted or the token is cancelled.
    /// </summary>
    /// <remarks>
    /// A recorded source ends on its own; a live source runs until cancelled. The
    /// per-read timeout keeps a live source responsive to cancellation rather than
    /// blocked in the driver.
    /// </remarks>
    public CaptureSummary Run(CancellationToken cancellationToken = default, TimeSpan? readTimeout = null)
    {
        var timeout = readTimeout ?? TimeSpan.FromMilliseconds(500);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_source.TryRead(timeout, out var packet))
            {
                // Timeout on a live source, or the end of a recorded one. A live
                // capture keeps waiting; a finished replay stops. The source itself
                // knows which — a recorded file returns false only at true EOF.
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (IsExhausted)
                    break;
                continue;
            }

            Pump(packet);
        }

        return Snapshot();
    }

    /// <summary>Whether the source has permanently ended.</summary>
    /// <remarks>
    /// A recorded source sets this at EOF; a live source never does, so a live run
    /// only ends on cancellation. Kept distinct from a timeout so the loop does not
    /// mistake a quiet moment for the end.
    /// </remarks>
    public bool IsExhausted => _source is IFinitePacketSource finite && finite.Ended;

    /// <summary>Feeds one packet through the whole chain. Public so a test can step it.</summary>
    public void Pump(CapturedPacket packet)
    {
        _packetsRead++;

        if (!Ipv4TcpParser.TryParseSegment(packet.Raw.Span, _source.ServerAddress, _source.ServerPort, out var segment, out _))
        {
            _packetsRejected++;
            return;
        }

        _packetsParsed++;
        byte[] delivered = _conversation.Accept(segment);
        if (delivered.Length == 0)
            return;

        if (segment.Direction == StreamDirection.Outbound)
        {
            _outboundBytes += delivered.Length;
            Emit(packet.TimestampUtc, _outboundFramer, delivered);
        }
        else
        {
            _inboundBytes += delivered.Length;
            Emit(packet.TimestampUtc, _inboundFramer, delivered);
        }
    }

    private void Emit(DateTime timestamp, IGameStreamFramer framer, byte[] delivered)
    {
        foreach (var frame in framer.Consume(delivered))
        {
            _framesProduced++;
            if (frame.Source == DataSourceKind.Unknown)
                _unknownFrames++;
            FrameProduced?.Invoke(new CaptureFrame(timestamp, framer.Direction, frame));
        }
    }

    /// <summary>The counts so far, safe to read at any point.</summary>
    public CaptureSummary Snapshot() => new(
        _packetsRead, _packetsParsed, _packetsRejected,
        _outboundBytes, _inboundBytes, _framesProduced, _unknownFrames);
}

/// <summary>A source that ends, as a recorded one does. Live sources do not implement it.</summary>
public interface IFinitePacketSource
{
    /// <summary>True once every recorded packet has been handed over.</summary>
    bool Ended { get; }
}
