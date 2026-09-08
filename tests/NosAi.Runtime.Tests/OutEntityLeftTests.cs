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
/// AP-05/A2+A4: <c>out</c> means the entity left the view, not that it died, and
/// <c>su</c> (field 5) / <c>ct</c> (field 7) carry the skill vnum the wire states.
/// </summary>
/// <remarks>
/// <para>
/// The hand-built packets prove the decoder reads each shape the catalogue
/// describes. A <c>0</c> skill is a basic action, not a skill: it is published as
/// <i>absent</i>, never as "skill 0", for the same reason the target's vitals are
/// only read when their presence flag is set — a zero that means "not reported"
/// must not become a number someone looks up.
/// </para>
/// </remarks>
public sealed class OutEntityLeftTests
{
    private static readonly DateTime At = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);
    private const string Cond = "cond 1 3443217 0 0 11";

    private static ObservedPacket Ascii(string packet, DataSourceKind source = DataSourceKind.Live)
        => new(At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(packet), source);

    private static NosTaleWorldProtocolDecoder Identified()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(Cond));
        return decoder;
    }

    // ------------------------------------------------------------------ out

    [Fact]
    public void Out_produces_one_entity_left_and_no_death()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Ascii("out 3 3013"));

        GameEvent left = Assert.Single(decoded.Events);
        Assert.Equal(GameEventKind.EntityLeft, left.Kind);
        Assert.Equal(3013, left.EntityId);
        Assert.Equal("out", left.Descriptor);
        Assert.Empty(decoded.Sightings);
        Assert.DoesNotContain(decoded.Events, e => e.Kind == GameEventKind.EntityDeath);
    }

    [Fact]
    public void An_entity_that_leaves_stops_producing_sightings()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii("in 3 36 3013 110 63 2 100 100"));

        GameEvent left = Assert.Single(decoder.Decode(Ascii("out 3 3013")).Events);
        Assert.Equal(GameEventKind.EntityLeft, left.Kind);

        // A later move under the same id is a brand-new entity with no remembered
        // health or species, not the departed entity's last state.
        EntitySighting after = Assert.Single(decoder.Decode(Ascii("mv 3 3013 111 64 5")).Sightings);
        Assert.Null(after.HpRatio);
        Assert.Null(after.Vnum);
    }

    [Fact]
    public void The_vnum_survives_the_exit()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii("in 3 45 3205 110 62 2 100 100"));

        GameEvent left = Assert.Single(decoder.Decode(Ascii("out 3 3205")).Events);

        Assert.Equal(GameEventKind.EntityLeft, left.Kind);
        // Read before the removal, so "a mob of vnum 45 left" stays confirmable.
        Assert.Equal(45, left.Vnum);
    }

    [Theory]
    [InlineData("out 3")]           // no id
    [InlineData("out")]             // no type, no id
    [InlineData("out 3 notanid")]   // the id is not a number
    public void A_malformed_out_produces_nothing(string line)
    {
        Assert.True(new NosTaleWorldProtocolDecoder().Decode(Ascii(line)).IsEmpty);
    }

    [Theory]
    [InlineData("out 1 8309204")]
    [InlineData("out 2 2460985")]
    [InlineData("out 3 3013")]
    public void All_three_entity_types_decode(string line)
    {
        GameEvent left = Assert.Single(new NosTaleWorldProtocolDecoder().Decode(Ascii(line)).Events);
        Assert.Equal(GameEventKind.EntityLeft, left.Kind);
    }

    // --------------------------------------------------------------- provider

    [Fact]
    public void An_entity_that_leaves_is_not_selectable_before_retention_expires()
    {
        // The departure empties the table immediately: no time has passed, so the
        // removal is the `out` event, not the 60-second retention the provider
        // still applies to everything the server does not announce.
        NetworkGameplayProvider withDeparture = Provider("in 3 36 3013 110 63 2 100 100", "out 3 3013");
        GameplayObservation afterExit = withDeparture.Observe();

        // Nothing is left to select: the one entity seen has left.
        Assert.False(afterExit.Entities.HasValue);
        Assert.Equal("no_entity_retained", afterExit.Entities.FailureReason);

        // The same entity without a departure stays selectable.
        NetworkGameplayProvider withoutDeparture = Provider("in 3 36 3013 110 63 2 100 100");
        GameplayObservation afterEnter = withoutDeparture.Observe();

        Assert.True(afterEnter.Entities.HasValue);
        Assert.Contains(afterEnter.Entities.Value, e => e.EntityId == 3013);
    }

    // ----------------------------------------------------------- su field 5

    [Fact]
    public void A_player_attack_carries_the_skill_vnum()
    {
        DecodedObservations decoded = Identified().Decode(Ascii(
            "su 1 3443217 3 313816 226 250 12 522 0 0 1 0 698 5 0 200 310"));

        GameEvent hit = Assert.Single(decoded.Events);
        Assert.Equal(GameEventKind.CombatHit, hit.Kind);
        Assert.Equal(226, hit.SkillVnum);
    }

    [Fact]
    public void A_monster_basic_attack_carries_no_skill_vnum()
    {
        DecodedObservations decoded = Identified().Decode(Ascii(
            "su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305"));

        GameEvent hit = Assert.Single(decoded.Events);
        // 0 is a basic attack, not a skill: absent, never "skill 0".
        Assert.Null(hit.SkillVnum);
    }

    // ------------------------------------------------------------ ct field 7

    [Fact]
    public void A_cast_carries_the_skill_vnum_in_field_seven()
    {
        DecodedObservations decoded = Identified().Decode(Ascii("ct 1 3443217 3 3205 -1 -1 220"));

        Assert.NotNull(decoded.PlayerTarget);
        Assert.Equal(220, decoded.PlayerTarget!.SkillVnum);
    }

    [Fact]
    public void A_cast_with_a_zero_skill_carries_no_skill_vnum()
    {
        DecodedObservations decoded = Identified().Decode(Ascii("ct 1 3443217 3 3205 -1 -1 0"));

        Assert.NotNull(decoded.PlayerTarget);
        Assert.Null(decoded.PlayerTarget!.SkillVnum);
    }

    // ---------------------------------------------------------------- helpers

    private static NetworkGameplayProvider Provider(params string[] lines)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (string line in lines)
        {
            byte[] body = Encoded(line);
            packets.Add(new CapturedPacket(At, TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }

        var source = ReassembledObservationSource.ForNosTaleWorld(
            new InMemoryPacketSource(IPAddress.Parse(Endpoint.Host), Endpoint.Port, packets),
            DataSourceKind.Live);
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());
        return new NetworkGameplayProvider(new NetworkWorldFeed(observer));
    }

    /// <summary>Encodes a NosTale world line the way the server does.</summary>
    private static byte[] Encoded(string line)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < line.Length; i += 0x7F)
        {
            string chunk = line.Substring(i, Math.Min(0x7F, line.Length - i));
            bytes.Add((byte)chunk.Length);
            foreach (char c in chunk)
                bytes.Add((byte)(c ^ 0xFF));
        }
        bytes.Add(NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        IPAddress.Parse(Endpoint.Host).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse("192.168.0.4").GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], (ushort)Endpoint.Port);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), 56027);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }
}
