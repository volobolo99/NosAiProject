using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Cuts the world channel's reassembled stream into whole NosTale packets.
/// </summary>
/// <remarks>
/// <para>
/// The framer <see cref="UnknownGameStreamFramer"/> was standing in for. It knows
/// one thing about the protocol — where a packet ends — and nothing about what any
/// packet means: that is <see cref="NosTaleWorldProtocolDecoder"/>'s job, one
/// layer up. Keeping the two apart is what stops a decoding mistake from moving
/// the message boundaries, which is the failure that makes every subsequent field
/// wrong while still yielding numbers.
/// </para>
/// <para>
/// The boundary rule comes from <see cref="NosTaleWorldDecoder.TryMeasurePacket"/>,
/// so the framer and the decoder walk the same structure rather than two
/// descriptions of it that can drift.
/// </para>
/// <para>
/// <b>Inbound only.</b> Server → client is the direction this shape was derived
/// from and the only one that decodes; client → server is separately encrypted and
/// is not framed here — see <c>docs/PROTOCOLLO_NOSTALE.md</c>. Use
/// <see cref="Factory"/> to get the right framer per direction rather than
/// constructing one for outbound.
/// </para>
/// <para>
/// <b>Provenance passes through.</b> Unlike <see cref="ProtocolMapFramer"/>, whose
/// boundaries come from a map an operator derived by correlation and which can
/// therefore never be LIVE, this rule was derived from real captures and confirmed
/// by what it produces: every packet of both recordings decodes to printable text
/// with a consistent grammar. Framing does not weaken the bytes. Confidence in the
/// meaning of individual fields is applied by the decoder, field by field.
/// </para>
/// </remarks>
public sealed class NosTaleWorldFramer : IGameStreamFramer
{
    /// <summary>
    /// The longest run of bytes that may pass without a packet ending.
    /// </summary>
    /// <remarks>
    /// A terminator occurs every ~13 bytes in both recordings, and a packet's
    /// length byte caps one field at 127 bytes. A stream that runs this far without
    /// ending a packet is not a long packet, it is a stream that stopped making
    /// sense — a capture started mid-packet, or the wrong direction.
    /// </remarks>
    public const int MaxPacketBytes = 8192;

    private readonly List<byte> _buffer = new();
    private readonly DataSourceKind _streamSource;
    private bool _desynchronised;
    private bool _desyncReported;

    /// <inheritdoc />
    public StreamDirection Direction { get; }

    /// <summary>Packets cut so far.</summary>
    public long FramedPackets { get; private set; }

    /// <summary>Bytes waiting for the rest of their packet.</summary>
    public int PendingBytes => _buffer.Count;

    /// <summary>Whether the stream stopped making sense as this protocol.</summary>
    public bool IsDesynchronised => _desynchronised;

    /// <param name="direction">Which half of the conversation. Only inbound decodes.</param>
    /// <param name="streamSource">
    /// What the caller knows about the bytes: LIVE for a driver capture of the
    /// running client, CACHED for a replayed <c>.noscap</c>, SIMULATED for
    /// synthetic traffic.
    /// </param>
    public NosTaleWorldFramer(StreamDirection direction, DataSourceKind streamSource)
    {
        Direction = direction;
        _streamSource = streamSource;
    }

    /// <summary>
    /// Builds the framer for each direction: this one inbound, the honest UNKNOWN
    /// one outbound.
    /// </summary>
    /// <remarks>
    /// Client → server uses a different, session-keyed encryption. Running this
    /// framer over it would cut it at whatever bytes happened to be 0xFF and hand
    /// the decoder runs of noise, so that direction keeps saying it cannot be read.
    /// </remarks>
    public static Func<StreamDirection, IGameStreamFramer> Factory(DataSourceKind streamSource)
        => direction => direction == StreamDirection.Inbound
            ? new NosTaleWorldFramer(direction, streamSource)
            : new UnknownGameStreamFramer(direction);

    /// <inheritdoc />
    public IReadOnlyList<GameFrame> Consume(ReadOnlySpan<byte> delivered)
    {
        if (Direction != StreamDirection.Inbound)
        {
            if (delivered.Length == 0)
                return Array.Empty<GameFrame>();
            return new[] { GameFrame.Unframed(delivered.ToArray(), "client_direction_undecoded") };
        }

        if (_desynchronised)
            return ReportDesyncOnce();

        foreach (byte b in delivered)
            _buffer.Add(b);

        var frames = new List<GameFrame>();
        byte[] pending = _buffer.ToArray();
        int consumed = 0;

        while (consumed < pending.Length)
        {
            if (!NosTaleWorldDecoder.TryMeasurePacket(pending.AsSpan(consumed), out int length))
            {
                // Still arriving — unless it has been arriving for too long, in
                // which case the boundaries are wrong and guessing a resynchro-
                // nisation point would resume emitting confident nonsense.
                if (pending.Length - consumed > MaxPacketBytes)
                {
                    _desynchronised = true;
                    frames.AddRange(ReportDesyncOnce());
                }
                break;
            }

            FramedPackets++;
            // The opcode of this protocol is a text mnemonic ("stat", "mv"), not a
            // number, and it is inside the encoded body. Claiming a numeric opcode
            // here would be inventing one, so the field stays -1 and the decoder
            // reads the real one.
            frames.Add(new GameFrame(_streamSource, -1, pending.AsMemory(consumed, length), null));
            consumed += length;
        }

        if (consumed > 0)
            _buffer.RemoveRange(0, consumed);

        return frames;
    }

    private IReadOnlyList<GameFrame> ReportDesyncOnce()
    {
        if (_desyncReported)
            return Array.Empty<GameFrame>();
        _desyncReported = true;
        return new[]
        {
            GameFrame.Unframed(
                ReadOnlyMemory<byte>.Empty,
                $"no_packet_terminator_within:{MaxPacketBytes}")
        };
    }

    /// <summary>
    /// Drops the buffered bytes and clears the desync flag, for a reconnect.
    /// </summary>
    /// <remarks>
    /// A new connection starts at a packet boundary, so the tail of the old one
    /// must not be prefixed onto it.
    /// </remarks>
    public void Reset()
    {
        _buffer.Clear();
        _desynchronised = false;
        _desyncReported = false;
    }
}
