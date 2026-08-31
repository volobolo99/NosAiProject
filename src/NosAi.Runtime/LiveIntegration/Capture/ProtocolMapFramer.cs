using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Frames a captured stream using the operator's protocol map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists.</b> The capture stack declared
/// <see cref="IGameStreamFramer"/> and shipped no implementation of it except
/// <see cref="UnknownGameStreamFramer"/>; the perception stack shipped a real
/// length-prefixed framer (<see cref="MessageFramer"/>) and had nothing to feed
/// it. Two halves of one path, written either side of a seam neither crossed —
/// so the capture engine's only production framer reported every byte UNKNOWN
/// while a working framer sat one namespace away, exercised by its own suite and
/// by nothing else.
/// </para>
/// <para>
/// <b>Provenance is the weaker of two claims.</b> A frame is no better than the
/// stream it came from <i>and</i> no better than the map used to cut it. The
/// framing is structural, but a wrong <see cref="FramingSpec"/> puts every
/// message boundary in the wrong place, so framing inherits the map's confidence
/// exactly as decoding does. Since <see cref="ProtocolMap"/> refuses to be LIVE,
/// a frame produced here can never be LIVE either — which is the right answer for
/// a map an operator derived by correlating captures with remembered ground
/// truth.
/// </para>
/// <para>
/// <b>Desynchronisation is reported once, then the stream stops.</b> A declared
/// length past <see cref="FramingSpec.MaxMessageLength"/> means the boundaries
/// are wrong — the wrong map, or a capture started mid-message. Guessing a
/// resynchronisation point would resume emitting confident nonsense, so the
/// framer emits one UNKNOWN frame naming the reason and then emits nothing.
/// </para>
/// </remarks>
public sealed class ProtocolMapFramer : IGameStreamFramer
{
    private readonly ProtocolMap _map;
    private readonly MessageFramer _framer;
    private readonly DataSourceKind _frameSource;
    private bool _desyncReported;

    /// <inheritdoc />
    public StreamDirection Direction { get; }

    /// <summary>Messages framed so far.</summary>
    public long FramedMessages { get; private set; }

    /// <summary>Messages whose opcode field fell outside the message.</summary>
    public long UnreadableOpcodes { get; private set; }

    /// <summary>Whether the stream stopped making sense against this map.</summary>
    public bool IsDesynchronised => _framer.IsDesynchronised;

    /// <param name="map">The operator's map. Supplies both the framing and the opcode field.</param>
    /// <param name="direction">Which half of the conversation this instance frames.</param>
    /// <param name="streamSource">
    /// What the caller knows about the bytes: LIVE for a driver capture of the
    /// running client, CACHED for a replayed <c>.noscap</c>, SIMULATED for
    /// synthetic traffic. It is a ceiling, not a label to be taken at face value —
    /// the map's own confidence still applies on top of it.
    /// </param>
    public ProtocolMapFramer(ProtocolMap map, StreamDirection direction, DataSourceKind streamSource)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Validate();
        _map = map;
        Direction = direction;
        _framer = new MessageFramer(map.Framing);
        _frameSource = Weaker(streamSource, map.Confidence);
    }

    /// <inheritdoc />
    public IReadOnlyList<GameFrame> Consume(ReadOnlySpan<byte> delivered)
    {
        if (_framer.IsDesynchronised)
            return ReportDesyncOnce();

        IReadOnlyList<byte[]> messages = _framer.Push(delivered);

        // The desync may have been raised by this very push, after it had already
        // emitted whole messages. Those messages are good — the boundary broke
        // after them — so they are returned, and the desync is appended.
        var frames = new List<GameFrame>(messages.Count + 1);
        foreach (byte[] message in messages)
        {
            if (!_map.OpcodeField.TryRead(message, out double opcode))
            {
                // The message framed but the opcode field is not inside it. That is
                // a map that disagrees with itself; the bytes are handed over as
                // UNKNOWN rather than assigned opcode 0.
                UnreadableOpcodes++;
                frames.Add(GameFrame.Unframed(message, "opcode_field_outside_message"));
                continue;
            }

            FramedMessages++;
            frames.Add(new GameFrame(_frameSource, (int)opcode, message, null));
        }

        if (_framer.IsDesynchronised)
            frames.AddRange(ReportDesyncOnce());

        return frames;
    }

    private IReadOnlyList<GameFrame> ReportDesyncOnce()
    {
        if (_desyncReported) return Array.Empty<GameFrame>();
        _desyncReported = true;
        return new[]
        {
            GameFrame.Unframed(
                ReadOnlyMemory<byte>.Empty,
                _framer.DesyncReason ?? "stream_desynchronised")
        };
    }

    /// <summary>
    /// Clears the buffered bytes and the desync flag.
    /// </summary>
    /// <remarks>
    /// For a reconnect: the new connection starts at a message boundary, so the
    /// tail of the old one must not be prefixed onto it.
    /// </remarks>
    public void Reset()
    {
        _framer.Reset();
        _desyncReported = false;
    }

    // The same rule ConfigurableProtocolDecoder applies: an observation is never
    // more trusted than the weakest link that produced it.
    private static DataSourceKind Weaker(DataSourceKind a, DataSourceKind b)
    {
        static int Rank(DataSourceKind kind) => kind switch
        {
            DataSourceKind.Live => 4,
            DataSourceKind.Derived => 3,
            DataSourceKind.Cached => 2,
            DataSourceKind.Simulated => 1,
            _ => 0,
        };
        return Rank(a) <= Rank(b) ? a : b;
    }
}
