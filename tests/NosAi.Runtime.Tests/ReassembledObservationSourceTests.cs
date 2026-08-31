using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The chain the perception channel was missing: reassemble, then frame, then
/// decode.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScopedLiveCaptureBackend"/> hands the decoder one TCP segment's raw
/// payload in arrival order. Each test here builds traffic that TCP is entitled
/// to deliver — a message split in two, a retransmission, segments out of order —
/// and asserts the whole message comes out exactly once. On the raw-payload path
/// every one of these produces a field read at the wrong offset, which is a
/// plausible number wearing a LIVE label rather than a failure.
/// </para>
/// </remarks>
public sealed class ReassembledObservationSourceTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    // [len:2 BE, body only][opcode:1][entityId:2][x:2][y:2]
    private static readonly FramingSpec Framing = new(
        LengthOffset: 0, LengthSize: 2, BigEndian: true, HeaderSize: 3, LengthIncludesHeader: false);

    private static ProtocolMap Map => new(
        Name: "test",
        Framing: Framing,
        OpcodeField: new FieldSpec(2, 1),
        Messages: ImmutableArray.Create(new MessageSpec(
            Opcode: 3,
            Kind: GameEventKind.EntitySighting,
            EntityId: new FieldSpec(3, 2),
            X: new FieldSpec(5, 2),
            Y: new FieldSpec(7, 2),
            HpRatio: null)),
        Confidence: DataSourceKind.Derived);

    /// <summary>[len:2][opcode:1][id:2][x:2][y:2] — nine bytes, body length six.</summary>
    private static byte[] Sighting(ushort entityId, ushort x, ushort y)
    {
        var message = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), 6);
        message[2] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(3, 2), entityId);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(5, 2), x);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(7, 2), y);
        return message;
    }

    private static byte[] Packet(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        Server.GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], ServerPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), ClientPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }

    private static CapturedPacket Cap(uint seq, ReadOnlySpan<byte> body) =>
        new(new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc), Packet(seq, body));

    private static ReassembledObservationSource Open(
        DataSourceKind streamSource, params CapturedPacket[] packets) =>
        new(new InMemoryPacketSource(Server, ServerPort, packets), Map, streamSource);

    private static List<ObservedPacket> Drain(ReassembledObservationSource source)
    {
        var observed = new List<ObservedPacket>();
        while (source.TryObserve(out ObservedPacket packet))
            observed.Add(packet);
        return observed;
    }

    [Fact]
    public void One_message_in_one_segment_comes_out_whole()
    {
        using var source = Open(DataSourceKind.Live, Cap(1000, Sighting(101, 30, 40)));

        ObservedPacket packet = Assert.Single(Drain(source));

        Assert.Equal(9, packet.Payload.Length);
        Assert.Equal(NetworkDirection.Inbound, packet.Direction);
        Assert.Equal(1, source.MessagesObserved);
    }

    /// <summary>
    /// The case the raw-payload path gets wrong most often. Split a nine-byte
    /// message after five bytes and the old path decodes two fragments: the first
    /// too short, the second read as though its middle were a header.
    /// </summary>
    [Fact]
    public void A_message_split_across_two_segments_yields_one_observation()
    {
        byte[] message = Sighting(101, 30, 40);
        using var source = Open(
            DataSourceKind.Live,
            Cap(1000, message.AsSpan(0, 5)),
            Cap(1005, message.AsSpan(5)));

        ObservedPacket packet = Assert.Single(Drain(source));

        Assert.Equal(message, packet.Payload.ToArray());
    }

    /// <summary>Two messages inside one segment are two observations, not one.</summary>
    [Fact]
    public void Two_messages_in_one_segment_yield_two_observations()
    {
        byte[] both = [.. Sighting(101, 30, 40), .. Sighting(102, 50, 60)];
        using var source = Open(DataSourceKind.Live, Cap(1000, both));

        Assert.Equal(2, Drain(source).Count);
    }

    /// <summary>
    /// A retransmission repeats bytes the stream already had. Decoding the raw
    /// payload would report the same entity twice; the reassembler recognises the
    /// overlap and the message is observed once.
    /// </summary>
    [Fact]
    public void A_retransmitted_segment_does_not_double_the_observation()
    {
        byte[] message = Sighting(101, 30, 40);
        using var source = Open(
            DataSourceKind.Live,
            Cap(1000, message),
            Cap(1000, message));

        Assert.Single(Drain(source));
    }

    /// <summary>
    /// Out-of-order arrival. The second half arrives first; holding it until the
    /// first half lands is what keeps the message readable at all.
    /// </summary>
    [Fact]
    public void Segments_arriving_out_of_order_still_frame_one_message()
    {
        byte[] message = Sighting(101, 30, 40);
        using var source = Open(
            DataSourceKind.Live,
            Cap(1000, message.AsSpan(0, 1)),   // anchors the stream at 1000
            Cap(1005, message.AsSpan(5)),      // arrives early, is held
            Cap(1001, message.AsSpan(1, 4)));  // fills the gap

        ObservedPacket packet = Assert.Single(Drain(source));

        Assert.Equal(message, packet.Payload.ToArray());
    }

    /// <summary>
    /// Provenance survives the whole chain at its weakest link: LIVE bytes read
    /// through a DERIVED map are DERIVED observations, and a policy that demands
    /// LIVE can still refuse them.
    /// </summary>
    [Fact]
    public void A_live_capture_through_a_derived_map_observes_derived_packets()
    {
        using var source = Open(DataSourceKind.Live, Cap(1000, Sighting(101, 30, 40)));

        Assert.Equal(DataSourceKind.Derived, Assert.Single(Drain(source)).Source);
    }

    [Fact]
    public void A_replay_never_observes_live_packets()
    {
        using var source = Open(DataSourceKind.Cached, Cap(1000, Sighting(101, 30, 40)));

        Assert.Equal(DataSourceKind.Cached, Assert.Single(Drain(source)).Source);
    }

    /// <summary>
    /// The whole point of the chain: what comes out is decodable by the map that
    /// framed it, so the observer produces a sighting rather than a count of
    /// undecodable packets.
    /// </summary>
    [Fact]
    public void The_observer_decodes_what_this_source_hands_it()
    {
        using var source = Open(DataSourceKind.Live, Cap(1000, Sighting(101, 30, 40)));
        var observer = new GameTrafficObserver(
            source,
            new ScopedGameTrafficFilter(new GameEndpoint(Server.ToString(), ServerPort)),
            new ConfigurableProtocolDecoder(Map));

        NetworkObservationReport report = observer.ObservePending();

        EntitySighting sighting = Assert.Single(report.Sightings);
        Assert.Equal(101, sighting.EntityId);
        Assert.Equal(30, sighting.X);
        Assert.Equal(40, sighting.Y);
        Assert.Equal(0, report.UndecodablePackets);
    }

    /// <summary>
    /// A map whose framing does not match the traffic desynchronises. The frames
    /// are counted as unreadable rather than handed on, so a wrong map reads as a
    /// rising failure count instead of as a quiet channel.
    /// </summary>
    [Fact]
    public void A_map_that_does_not_fit_the_traffic_observes_nothing_and_counts_it()
    {
        var wrong = Map with { Framing = Framing with { MaxMessageLength = 4 } };
        using var source = new ReassembledObservationSource(
            new InMemoryPacketSource(Server, ServerPort, new[] { Cap(1000, Sighting(101, 30, 40)) }),
            wrong,
            DataSourceKind.Live);

        Assert.Empty(Drain(source));
        Assert.Equal(0, source.MessagesObserved);
        Assert.Equal(1, source.UnreadableFrames);
    }

    [Fact]
    public void Packets_from_another_conversation_are_rejected_by_the_parser()
    {
        var stranger = new byte[20 + 20 + 4];
        stranger[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(2, 2), (ushort)stranger.Length);
        stranger[9] = 6;
        IPAddress.Parse("8.8.8.8").GetAddressBytes().CopyTo(stranger, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(stranger, 16);
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(40, 2), 53);
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(42, 2), ClientPort);
        stranger[32] = 5 << 4;

        using var source = new ReassembledObservationSource(
            new InMemoryPacketSource(Server, ServerPort,
                new[] { new CapturedPacket(DateTime.UtcNow, stranger) }),
            Map,
            DataSourceKind.Live);

        Assert.Empty(Drain(source));
        Assert.Equal(1, source.Capture.PacketsRejected);
    }
}
