using System.Buffers.Binary;
using System.Net;
using System.Text;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The T-05 path: framed world-channel bytes become LIVE vitals, not a
/// reconstructed binary map's DERIVED guess.
/// </summary>
/// <remarks>
/// The golden vector is the same 35 bytes as <see cref="NosTaleWorldDecoderTests"/>:
/// two packets the server sent, one of them the <c>stat</c> whose HP/MP matched
/// the HUD. Every test below is about whether those numbers can leave the
/// decoder wearing a LIVE label, and about the ways they must not.
/// </remarks>
public sealed class NosTaleWorldObservationTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private const string GoldenHex =
        "0292899217175D81565155419EFF048C8B9E8B9C1B7491B749158641586414155C8EFF";

    private static byte[] Golden() => Convert.FromHexString(GoldenHex);

    private static ObservedPacket Ascii(
        string packet,
        DataSourceKind source = DataSourceKind.Live,
        NetworkDirection direction = NetworkDirection.Inbound)
        => new(
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            direction,
            Endpoint.Host,
            Endpoint.Port,
            Encoding.ASCII.GetBytes(packet),
            source);

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body)
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
        new(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), TcpPacket(seq, body));

    private static NetworkGameplayProvider Provider(DataSourceKind streamSource, params CapturedPacket[] packets)
    {
        var source = ReassembledObservationSource.ForNosTaleWorld(
            new InMemoryPacketSource(Server, ServerPort, packets), streamSource);
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());
        return new NetworkGameplayProvider(new NetworkWorldFeed(observer));
    }

    // ------------------------------------------------------------------ framer

    [Fact]
    public void The_recorded_bytes_frame_as_the_two_packets_the_client_read()
    {
        var framer = new NosTaleWorldFramer(StreamDirection.Inbound, DataSourceKind.Live);

        IReadOnlyList<GameFrame> frames = framer.Consume(Golden());

        Assert.Equal(2, frames.Count);
        Assert.True(NosTaleWorldDecoder.TryMeasurePacket(Golden(), out int firstLength));
        Assert.Equal(Golden()[..firstLength], frames[0].Body.ToArray());
        Assert.Equal("mv 3 3194 121 110 5", Assert.Single(NosTaleWorldDecoder.Decode(frames[0].Body.Span)));
        Assert.Equal("stat 7305 7305 1420 1420 0 1184", Assert.Single(NosTaleWorldDecoder.Decode(frames[1].Body.Span)));
        Assert.Equal(DataSourceKind.Live, frames[0].Source);
        Assert.Equal(DataSourceKind.Live, frames[1].Source);
        Assert.Equal(-1, frames[0].Opcode);
        Assert.Equal(0, framer.PendingBytes);
    }

    [Fact]
    public void A_packet_split_across_two_consumes_is_not_emitted_until_the_terminator()
    {
        byte[] whole = Golden();
        Assert.True(NosTaleWorldDecoder.TryMeasurePacket(whole, out int firstLength));
        var framer = new NosTaleWorldFramer(StreamDirection.Inbound, DataSourceKind.Live);

        Assert.Empty(framer.Consume(whole.AsSpan(0, firstLength - 1)));
        Assert.Equal(firstLength - 1, framer.PendingBytes);

        GameFrame frame = Assert.Single(framer.Consume(whole.AsSpan(firstLength - 1, 1)));
        Assert.Equal(whole[..firstLength], frame.Body.ToArray());
        Assert.Equal("mv 3 3194 121 110 5", Assert.Single(NosTaleWorldDecoder.Decode(frame.Body.Span)));
    }

    [Fact]
    public void Outbound_bytes_stay_unknown()
    {
        var framer = new NosTaleWorldFramer(StreamDirection.Outbound, DataSourceKind.Live);

        GameFrame frame = Assert.Single(framer.Consume(Golden()));

        Assert.Equal(DataSourceKind.Unknown, frame.Source);
        Assert.Equal("client_direction_undecoded", frame.Reason);
    }

    [Fact]
    public void A_replay_is_framed_cached_not_live()
    {
        var framer = new NosTaleWorldFramer(StreamDirection.Inbound, DataSourceKind.Cached);

        IReadOnlyList<GameFrame> frames = framer.Consume(Golden());
        Assert.Equal(DataSourceKind.Cached, frames[0].Source);
    }

    [Fact]
    public void Empty_input_produces_no_frames()
    {
        var framer = new NosTaleWorldFramer(StreamDirection.Inbound, DataSourceKind.Live);
        Assert.Empty(framer.Consume(ReadOnlySpan<byte>.Empty));
    }

    // ------------------------------------------------------------------ decoder

    [Fact]
    public void Stat_carries_the_vitals_the_hud_was_showing()
    {
        var decoder = new NosTaleWorldProtocolDecoder();

        DecodedObservations decoded = decoder.Decode(Ascii("stat 7305 7305 1420 1420 0 1184"));

        Assert.NotNull(decoded.Vitals);
        Assert.Equal(7305, decoded.Vitals!.Hp);
        Assert.Equal(7305, decoded.Vitals.MaxHp);
        Assert.Equal(1420, decoded.Vitals.Mp);
        Assert.Equal(DataSourceKind.Live, decoded.Vitals.Source);
        Assert.Null(decoded.Vitals.HasTarget);
        Assert.Null(decoded.Vitals.InCombat);
    }

    [Fact]
    public void Unknown_stat_fields_are_not_read_as_flags()
    {
        // Field 5 is 0 and field 6 is 1184 throughout the capture. Either would
        // look like a combat flag or an SP bar if this decoder invented a
        // meaning for them. They stay unread, so the provider publishes UNKNOWN.
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("stat 7305 7305 1420 1420 0 1184"));

        Assert.NotNull(decoded.Vitals);
        Assert.Null(decoded.Vitals!.HasTarget);
        Assert.Null(decoded.Vitals.InCombat);
    }

    [Fact]
    public void A_short_stat_is_refused_rather_than_padded()
    {
        Assert.True(new NosTaleWorldProtocolDecoder().Decode(Ascii("stat 7305 7305")).IsEmpty);
    }

    [Fact]
    public void Hp_above_max_hp_is_refused()
    {
        Assert.True(new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("stat 9000 5000 1420 1420")).IsEmpty);
    }

    [Fact]
    public void Zero_max_hp_is_refused()
    {
        Assert.True(new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("stat 0 0 0 0")).IsEmpty);
    }

    [Fact]
    public void Zero_hp_with_a_real_max_is_read()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("stat 0 7305 0 1420"));
        Assert.NotNull(decoded.Vitals);
        Assert.Equal(0, decoded.Vitals!.Hp);
    }

    [Fact]
    public void An_unmapped_opcode_produces_nothing()
    {
        Assert.True(new NosTaleWorldProtocolDecoder().Decode(Ascii("guri 2 1 3443217 0")).IsEmpty);
    }

    [Fact]
    public void Outbound_text_is_not_decoded()
    {
        var packet = Ascii("stat 7305 7305 1420 1420", direction: NetworkDirection.Outbound);
        var decoder = new NosTaleWorldProtocolDecoder();

        Assert.False(decoder.CanDecode(packet));
        Assert.True(decoder.Decode(packet).IsEmpty);
    }

    [Fact]
    public void A_framed_encoded_packet_is_decoded_then_parsed()
    {
        byte[] golden = Golden();
        Assert.True(NosTaleWorldDecoder.TryMeasurePacket(golden, out int firstLength));
        var raw = new ObservedPacket(
            DateTime.UtcNow, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            golden[firstLength..], DataSourceKind.Live);

        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(raw);

        Assert.NotNull(decoded.Vitals);
        Assert.Equal(7305, decoded.Vitals!.Hp);
        Assert.Equal(DataSourceKind.Live, decoded.Vitals.Source);
    }

    [Fact]
    public void Two_packets_in_one_payload_are_refused_rather_than_merged()
    {
        // The framer hands one packet per frame. Concatenating two would be a
        // framing bug; reading them as one observation would hide it.
        var raw = new ObservedPacket(
            DateTime.UtcNow, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            Golden(), DataSourceKind.Live);

        Assert.False(new NosTaleWorldProtocolDecoder().CanDecode(raw));
    }

    [Fact]
    public void St_uses_absolute_hp_not_the_disagreeing_percent()
    {
        // round(198/310*100) = 64; field 5 says 66. The catalogue forbids field 5.
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii("in 3 36 313816 109 63 2 100 100"));

        EntitySighting sighting = Assert.Single(
            decoder.Decode(Ascii("st 3 313816 8 0 66 100 198 52 310 52 0")).Sightings);

        Assert.Equal(313816, sighting.EntityId);
        Assert.Equal(198.0 / 310.0, sighting.HpRatio!.Value, 9);
        Assert.NotEqual(0.66, sighting.HpRatio);
    }

    /// <summary>
    /// The move says where and says nothing about health, and the sighting now
    /// says exactly that. Before, the only way to avoid inventing full health was
    /// to throw the packet away, which cost the position too.
    /// </summary>
    [Fact]
    public void A_move_without_a_prior_spawn_reports_the_position_and_no_health()
    {
        EntitySighting moved = Assert.Single(new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("mv 3 3194 121 110 5")).Sightings);

        Assert.Equal(3194, moved.EntityId);
        Assert.Equal(121, moved.X);
        Assert.Equal(110, moved.Y);
        Assert.Null(moved.HpRatio);
        Assert.Null(moved.ToDetection());
    }

    [Fact]
    public void A_spawn_then_a_move_keeps_the_hp_the_spawn_carried()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii("in 3 36 3194 120 109 2 80 100"));

        EntitySighting moved = Assert.Single(
            decoder.Decode(Ascii("mv 3 3194 121 110 5")).Sightings);

        Assert.Equal(3194, moved.EntityId);
        Assert.Equal(121, moved.X);
        Assert.Equal(110, moved.Y);
        Assert.Equal(0.80, moved.HpRatio!.Value, 9);
    }

    [Fact]
    public void Die_removes_the_entity()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii("in 3 36 313820 110 63 2 100 100"));

        GameEvent death = Assert.Single(
            decoder.Decode(Ascii("die 3 313820 3 313820")).Events);
        Assert.Equal(GameEventKind.EntityDeath, death.Kind);
        Assert.Equal(313820, death.EntityId);

        // The death forgets the health the spawn carried. A later move under the
        // same id is a position with no health, not the dead entity's last HP
        // attached to a new one.
        EntitySighting afterDeath = Assert.Single(
            decoder.Decode(Ascii("mv 3 313820 111 64 5")).Sightings);
        Assert.Null(afterDeath.HpRatio);
    }

    [Fact]
    public void Su_is_a_hit_not_a_vitals_reading()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Ascii("su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305"));

        Assert.Null(decoded.Vitals);
        GameEvent hit = Assert.Single(decoded.Events);
        Assert.Equal(GameEventKind.CombatHit, hit.Kind);
        Assert.Equal(3443217, hit.EntityId);
    }

    // ------------------------------------------------------------------ provider, end to end

    [Fact]
    public void The_recorded_session_publishes_live_vitals()
    {
        NetworkGameplayProvider provider = Provider(DataSourceKind.Live, Cap(1000, Golden()));

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(7305, observation.MaxHp.Value);
        Assert.Equal(1420, observation.Mp.Value);
        Assert.Equal(DataSourceKind.Live, observation.Hp.Source);
        Assert.Equal(DataSourceKind.Live, observation.MaxHp.Source);
        Assert.Equal(DataSourceKind.Live, observation.Mp.Source);
        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal("target_flag_not_mapped", observation.HasTarget.FailureReason);
        Assert.Equal("combat_flag_not_mapped", observation.InCombat.FailureReason);
    }

    [Fact]
    public void A_replay_of_the_same_bytes_is_cached_not_live()
    {
        GameplayObservation observation = Provider(DataSourceKind.Cached, Cap(1000, Golden())).Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(DataSourceKind.Cached, observation.Hp.Source);
    }

    [Fact]
    public void A_truncated_stat_does_not_publish_the_previous_packets_numbers()
    {
        // Half a packet parses into fields that look like values and are not.
        byte[] truncated = Golden()[..^6];

        GameplayObservation observation = Provider(DataSourceKind.Live, Cap(1000, truncated)).Observe();

        Assert.False(observation.HasVitals);
        Assert.Equal(DataSourceKind.Unknown, observation.Hp.Source);
    }

    [Fact]
    public void A_message_split_across_two_tcp_segments_still_reads_as_one_stat()
    {
        byte[] golden = Golden();
        int split = golden.Length / 2;
        NetworkGameplayProvider provider = Provider(
            DataSourceKind.Live,
            Cap(1000, golden.AsSpan(0, split)),
            Cap(1000 + (uint)split, golden.AsSpan(split)));

        GameplayObservation observation = provider.Observe();

        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(DataSourceKind.Live, observation.Hp.Source);
    }

    [Fact]
    public void The_later_stat_in_a_batch_is_the_current_hp()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        var first = decoder.Decode(Ascii("stat 7305 7305 1420 1420 0 1184"));
        var second = decoder.Decode(Ascii("stat 7218 7305 1420 1420 0 1184"));

        Assert.Equal(7305, first.Vitals!.Hp);
        Assert.Equal(7218, second.Vitals!.Hp);
    }
}
