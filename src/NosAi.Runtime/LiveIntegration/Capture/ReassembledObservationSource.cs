using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Feeds the perception channel with whole application messages instead of raw
/// TCP segments.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> <see cref="ScopedLiveCaptureBackend"/> hands the
/// observer <c>parsed.Payload</c> — the payload of one TCP segment, in arrival
/// order, labelled LIVE. <see cref="ConfigurableProtocolDecoder"/> then reads the
/// opcode and the mapped fields straight out of it. That works only while every
/// message happens to sit alone inside exactly one segment, and TCP guarantees
/// nothing of the kind: a message split across two segments decodes its second
/// half as though it were a message, a retransmission decodes twice, and an
/// out-of-order arrival decodes bytes from the wrong place in the stream.
/// </para>
/// <para>
/// None of those fail loudly. Each produces a field read at a wrong offset, which
/// is a number — a plausible HP, a plausible position — carrying a LIVE label into
/// the world model. That is the exact failure ADR-0012 rejected memory reads over,
/// arriving through the door ADR-0014 opened instead.
/// </para>
/// <para>
/// So this source puts the two layers back in the order they have to be in:
/// parse, reassemble the ordered stream per direction, frame it into messages
/// with the operator's map, and only then hand a message over as an
/// <see cref="ObservedPacket"/>. Every packet leaving here is one whole
/// application message.
/// </para>
/// <para>
/// <b>Provenance is the framer's, not this class's.</b> A message is no better
/// than the stream it came from and the map that cut it, and
/// <see cref="ProtocolMapFramer"/> has already reduced it to the weaker of the
/// two. This source copies that verdict rather than re-asserting one, so a replay
/// stays CACHED and a map-derived reading stays DERIVED all the way to the world
/// model.
/// </para>
/// </remarks>
public sealed class ReassembledObservationSource : INetworkObservationSource, IDisposable
{
    private readonly IPacketSource _packets;
    private readonly GameTrafficCaptureEngine _engine;
    private readonly Queue<ObservedPacket> _ready = new();
    private readonly string _remoteHost;
    private readonly int _remotePort;
    private readonly TimeSpan _readTimeout;
    private bool _disposed;

    /// <summary>
    /// Provenance of the channel as a whole.
    /// </summary>
    /// <remarks>
    /// The ceiling the caller declared for the packet source. Individual packets
    /// may come out weaker still — the map is applied on top — but never stronger.
    /// </remarks>
    public DataSourceKind Source { get; }

    /// <summary>The counts from the capture engine, for diagnosis.</summary>
    public CaptureSummary Capture => _engine.Snapshot();

    /// <summary>Messages framed and handed on so far.</summary>
    public long MessagesObserved { get; private set; }

    /// <summary>
    /// Frames the framer could not read: a desync, or an opcode field outside the
    /// message. Counted rather than dropped, so a wrong map shows up as a rising
    /// number instead of as a channel that looks quiet.
    /// </summary>
    public long UnreadableFrames { get; private set; }

    /// <param name="packets">Where raw packets come from: driver, recording, or memory.</param>
    /// <param name="map">The operator's protocol map. Supplies framing and the opcode field.</param>
    /// <param name="streamSource">
    /// What the caller knows about those packets: LIVE only for a driver capture of
    /// the running client, CACHED for a replay, SIMULATED for synthetic traffic.
    /// </param>
    /// <param name="readTimeout">How long one poll waits on a quiet wire.</param>
    public ReassembledObservationSource(
        IPacketSource packets,
        ProtocolMap map,
        DataSourceKind streamSource,
        TimeSpan? readTimeout = null)
        : this(packets, FramerFrom(map, streamSource), streamSource, readTimeout)
    {
    }

    /// <summary>
    /// Same chain, with a caller-supplied framer — the world-channel decoder
    /// rather than a reconstructed binary map.
    /// </summary>
    public ReassembledObservationSource(
        IPacketSource packets,
        Func<StreamDirection, IGameStreamFramer> framerFactory,
        DataSourceKind streamSource,
        TimeSpan? readTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentNullException.ThrowIfNull(framerFactory);

        _packets = packets;
        _remoteHost = packets.ServerAddress.ToString();
        _remotePort = packets.ServerPort;
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(250);
        Source = streamSource;

        _engine = new GameTrafficCaptureEngine(packets, framerFactory);
        _engine.FrameProduced += OnFrame;
    }

    /// <summary>
    /// Frames the world channel with <see cref="NosTaleWorldFramer"/> so
    /// inbound packets that verify their terminator can be LIVE.
    /// </summary>
    public static ReassembledObservationSource ForNosTaleWorld(
        IPacketSource packets,
        DataSourceKind streamSource,
        TimeSpan? readTimeout = null)
        => new(packets, NosTaleWorldFramer.Factory(streamSource), streamSource, readTimeout);

    private static Func<StreamDirection, IGameStreamFramer> FramerFrom(
        ProtocolMap map, DataSourceKind streamSource)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Validate();
        return direction => new ProtocolMapFramer(map, direction, streamSource);
    }

    private void OnFrame(CaptureFrame frame)
    {
        if (frame.Frame.Source == DataSourceKind.Unknown)
        {
            // A desync or an unreadable opcode. It is not an observation, and it is
            // not nothing either: the operator needs to see the map failing.
            UnreadableFrames++;
            return;
        }

        MessagesObserved++;
        _ready.Enqueue(new ObservedPacket(
            frame.TimestampUtc,
            frame.Direction == StreamDirection.Inbound ? NetworkDirection.Inbound : NetworkDirection.Outbound,
            _remoteHost,
            _remotePort,
            frame.Frame.Body,
            frame.Frame.Source));
    }

    /// <inheritdoc />
    /// <remarks>
    /// One packet can complete several messages and several packets can complete
    /// none, so a queue sits between the pump and the caller. False means "nothing
    /// ready", never "the wire is idle" — the caller drives the pace.
    /// </remarks>
    public bool TryObserve(out ObservedPacket packet)
    {
        packet = null!;
        if (_disposed)
            return false;

        while (_ready.Count == 0)
        {
            if (!_packets.TryRead(_readTimeout, out CapturedPacket captured))
                return false;   // timeout on a live source, or end of a recording

            _engine.Pump(captured);
        }

        packet = _ready.Dequeue();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _engine.FrameProduced -= OnFrame;
        _packets.Dispose();
    }
}
