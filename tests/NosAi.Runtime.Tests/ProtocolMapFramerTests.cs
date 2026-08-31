using System.Collections.Immutable;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The seam between the capture stack and the perception stack.
/// </summary>
/// <remarks>
/// Before this framer existed the capture engine's only production framer was
/// <see cref="UnknownGameStreamFramer"/>, so a capture could never yield anything
/// but UNKNOWN however good the operator's map was. These tests pin the two
/// properties that make the bridge safe to cross: message boundaries come from
/// the map, and the frames it produces never claim more provenance than the
/// weaker of the stream and the map.
/// </remarks>
public sealed class ProtocolMapFramerTests
{
    // [len:2 BE, body only][opcode:1][body...]
    private static readonly FramingSpec Framing = new(
        LengthOffset: 0, LengthSize: 2, BigEndian: true, HeaderSize: 3, LengthIncludesHeader: false);

    private static ProtocolMap Map(DataSourceKind confidence = DataSourceKind.Derived) => new(
        Name: "test",
        Framing: Framing,
        OpcodeField: new FieldSpec(Offset: 2, Size: 1),
        Messages: ImmutableArray.Create(new MessageSpec(
            Opcode: 7,
            Kind: GameEventKind.EntitySighting,
            EntityId: new FieldSpec(3, 2),
            X: new FieldSpec(5, 2),
            Y: new FieldSpec(7, 2),
            HpRatio: null)),
        Confidence: confidence);

    /// <summary>
    /// [len:2][opcode:1][body...]. The header is 3 bytes and does not count itself,
    /// so the declared length is the body length exactly.
    /// </summary>
    private static byte[] Message(byte opcode, params byte[] body)
    {
        var message = new byte[3 + body.Length];
        message[0] = (byte)(body.Length >> 8);
        message[1] = (byte)body.Length;
        message[2] = opcode;
        body.CopyTo(message, 3);
        return message;
    }

    [Fact]
    public void Frames_a_whole_message_and_reads_its_opcode()
    {
        var framer = new ProtocolMapFramer(Map(), StreamDirection.Inbound, DataSourceKind.Live);

        IReadOnlyList<GameFrame> frames = framer.Consume(Message(7, 1, 2, 3, 4));

        GameFrame frame = Assert.Single(frames);
        Assert.Equal(7, frame.Opcode);
        Assert.Null(frame.Reason);
        Assert.Equal(1, framer.FramedMessages);
    }

    [Fact]
    public void A_message_split_across_deliveries_is_assembled_not_lost()
    {
        var framer = new ProtocolMapFramer(Map(), StreamDirection.Inbound, DataSourceKind.Live);
        byte[] message = Message(7, 9, 9, 9, 9);

        Assert.Empty(framer.Consume(message.AsSpan(0, 4)));
        IReadOnlyList<GameFrame> frames = framer.Consume(message.AsSpan(4));

        Assert.Equal(7, Assert.Single(frames).Opcode);
    }

    [Fact]
    public void Several_messages_in_one_delivery_all_come_out()
    {
        var framer = new ProtocolMapFramer(Map(), StreamDirection.Inbound, DataSourceKind.Live);
        byte[] a = Message(7, 1, 1), b = Message(7, 2, 2), c = Message(7, 3, 3);
        byte[] all = [.. a, .. b, .. c];

        Assert.Equal(3, framer.Consume(all).Count);
        Assert.Equal(3, framer.FramedMessages);
    }

    /// <summary>
    /// The property that makes the bridge honest. A driver capture is LIVE bytes,
    /// but the map that cuts them is the operator's reconstruction, so what comes
    /// out is DERIVED. Reporting LIVE here would let a wrong map look like ground
    /// truth to every policy downstream that checks provenance.
    /// </summary>
    [Fact]
    public void A_live_stream_read_through_a_derived_map_yields_derived_frames()
    {
        var framer = new ProtocolMapFramer(Map(DataSourceKind.Derived), StreamDirection.Inbound, DataSourceKind.Live);

        GameFrame frame = Assert.Single(framer.Consume(Message(7, 1, 2, 3, 4)));

        Assert.Equal(DataSourceKind.Derived, frame.Source);
    }

    /// <summary>And the ceiling works the other way round too.</summary>
    [Fact]
    public void A_replayed_stream_never_rises_to_the_maps_confidence()
    {
        var framer = new ProtocolMapFramer(Map(DataSourceKind.Derived), StreamDirection.Inbound, DataSourceKind.Cached);

        GameFrame frame = Assert.Single(framer.Consume(Message(7, 1, 2, 3, 4)));

        Assert.Equal(DataSourceKind.Cached, frame.Source);
    }

    [Fact]
    public void An_implausible_length_desynchronises_and_says_so_once()
    {
        var map = Map() with { Framing = Framing with { MaxMessageLength = 32 } };
        var framer = new ProtocolMapFramer(map, StreamDirection.Inbound, DataSourceKind.Live);

        IReadOnlyList<GameFrame> frames = framer.Consume([0xFF, 0xFF, 0x07, 0x00]);

        GameFrame frame = Assert.Single(frames);
        Assert.Equal(DataSourceKind.Unknown, frame.Source);
        Assert.Contains("implausible_message_length", frame.Reason);
        Assert.True(framer.IsDesynchronised);

        // Reported once: a desync repeated on every subsequent delivery would bury
        // the frames that mattered under identical noise.
        Assert.Empty(framer.Consume([0x00, 0x01, 0x02]));
    }

    /// <summary>
    /// Messages that framed cleanly before the boundary broke are still real, and
    /// dropping them would lose observations to a fault that happened after them.
    /// </summary>
    [Fact]
    public void Messages_framed_before_a_desync_survive_it()
    {
        var map = Map() with { Framing = Framing with { MaxMessageLength = 32 } };
        var framer = new ProtocolMapFramer(map, StreamDirection.Inbound, DataSourceKind.Live);

        byte[] good = Message(7, 1, 2);
        byte[] delivered = [.. good, 0xFF, 0xFF, 0x07, 0x00];

        IReadOnlyList<GameFrame> frames = framer.Consume(delivered);

        Assert.Equal(2, frames.Count);
        Assert.Equal(7, frames[0].Opcode);
        Assert.Equal(DataSourceKind.Unknown, frames[1].Source);
    }

    /// <summary>
    /// A map whose opcode field sits past the shortest message it admits is a map
    /// that disagrees with itself. The bytes are surfaced, not filed under opcode 0.
    /// </summary>
    [Fact]
    public void An_opcode_outside_the_message_is_unknown_not_zero()
    {
        var map = Map() with
        {
            Framing = Framing with { HeaderSize = 2 },
            OpcodeField = new FieldSpec(Offset: 64, Size: 1),
        };
        var framer = new ProtocolMapFramer(map, StreamDirection.Inbound, DataSourceKind.Live);

        // [len:2 = 1][one body byte] -> three bytes total, opcode field at 64.
        IReadOnlyList<GameFrame> frames = framer.Consume([0x00, 0x01, 0x41]);

        GameFrame frame = Assert.Single(frames);
        Assert.Equal(DataSourceKind.Unknown, frame.Source);
        Assert.Equal("opcode_field_outside_message", frame.Reason);
        Assert.Equal(1, framer.UnreadableOpcodes);
        Assert.Equal(0, framer.FramedMessages);
    }

    [Fact]
    public void Reset_drops_the_partial_message_a_reconnect_would_otherwise_prefix()
    {
        var framer = new ProtocolMapFramer(Map(), StreamDirection.Inbound, DataSourceKind.Live);
        byte[] message = Message(7, 4, 4, 4, 4);

        Assert.Empty(framer.Consume(message.AsSpan(0, 4)));
        framer.Reset();

        // The whole message now frames on its own; the stale half is gone.
        Assert.Equal(7, Assert.Single(framer.Consume(message)).Opcode);
    }

    /// <summary>
    /// The engine takes a framer factory, and this is the substitution the seam
    /// was written for: same engine, same capture, a real framer instead of the
    /// UNKNOWN one.
    /// </summary>
    [Fact]
    public void The_capture_engine_accepts_it_in_place_of_the_unknown_framer()
    {
        ProtocolMap map = Map();
        Func<StreamDirection, IGameStreamFramer> factory =
            direction => new ProtocolMapFramer(map, direction, DataSourceKind.Simulated);

        IGameStreamFramer inbound = factory(StreamDirection.Inbound);

        Assert.Equal(StreamDirection.Inbound, inbound.Direction);
        Assert.Equal(7, Assert.Single(inbound.Consume(Message(7, 1, 2, 3, 4))).Opcode);
    }
}
