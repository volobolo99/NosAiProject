using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The gameplay provider: the source the world model has never had.
/// </summary>
/// <remarks>
/// Every test here is about the same question — can this thing report a number it
/// did not read? The chain runs end to end from raw IPv4 packets, because the
/// interesting failures (a partially mapped protocol, a map that reads a stray
/// offset as HP) only appear once the real layers are in it.
/// </remarks>
public sealed class GameplayProviderTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4006);

    // [len:2 BE, body only][opcode:1][...]
    private static readonly FramingSpec Framing = new(
        LengthOffset: 0, LengthSize: 2, BigEndian: true, HeaderSize: 3, LengthIncludesHeader: false);

    private const byte OpSighting = 3;
    private const byte OpVitals = 9;

    private static ProtocolMap Map(PlayerVitalsSpec? vitals) => new(
        Name: "test",
        Framing: Framing,
        OpcodeField: new FieldSpec(2, 1),
        Messages: ImmutableArray.Create(new MessageSpec(
            Opcode: OpSighting,
            Kind: GameEventKind.EntitySighting,
            EntityId: new FieldSpec(3, 2),
            X: new FieldSpec(5, 2),
            Y: new FieldSpec(7, 2),
            HpRatio: null)),
        Confidence: DataSourceKind.Derived,
        PlayerVitals: vitals);

    /// <summary>[len:2][op:9][hp:2][maxHp:2][mp:2][hasTarget:1][inCombat:1].</summary>
    private static PlayerVitalsSpec FullVitals => new(
        Opcode: OpVitals,
        Hp: new FieldSpec(3, 2),
        MaxHp: new FieldSpec(5, 2),
        Mp: new FieldSpec(7, 2),
        HasTarget: new FieldSpec(9, 1),
        InCombat: new FieldSpec(10, 1));

    /// <summary>The same message, but the operator has not found the flags yet.</summary>
    private static PlayerVitalsSpec VitalsWithoutFlags => new(
        Opcode: OpVitals,
        Hp: new FieldSpec(3, 2),
        MaxHp: new FieldSpec(5, 2),
        Mp: new FieldSpec(7, 2));

    private static byte[] VitalsMessage(ushort hp, ushort maxHp, ushort mp, byte hasTarget = 0, byte inCombat = 0)
    {
        var message = new byte[11];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), 8);
        message[2] = OpVitals;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(3, 2), hp);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(5, 2), maxHp);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(7, 2), mp);
        message[9] = hasTarget;
        message[10] = inCombat;
        return message;
    }

    private static byte[] SightingMessage(ushort entityId, ushort x, ushort y)
    {
        var message = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), 6);
        message[2] = OpSighting;
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

    /// <summary>Builds the whole chain: packets → reassembly → framing → decode → feed → provider.</summary>
    private static NetworkGameplayProvider Provider(
        PlayerVitalsSpec? vitals, DataSourceKind streamSource, params byte[][] messages)
    {
        ProtocolMap map = Map(vitals);
        uint seq = 1000;
        var packets = new List<CapturedPacket>();
        foreach (byte[] message in messages)
        {
            packets.Add(new CapturedPacket(
                new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc), Packet(seq, message)));
            seq += (uint)message.Length;
        }

        var source = new ReassembledObservationSource(
            new InMemoryPacketSource(Server, ServerPort, packets), map, streamSource);
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new ConfigurableProtocolDecoder(map));
        return new NetworkGameplayProvider(new NetworkWorldFeed(observer));
    }

    [Fact]
    public void With_no_provider_attached_nothing_is_claimed()
    {
        GameplayObservation observation = UnavailableGameplayProvider.Instance.Observe();

        Assert.False(observation.HasVitals);
        Assert.Equal("gameplay_provider_not_available", observation.Hp.FailureReason);
        Assert.Equal("gameplay_provider_not_available", observation.UnusableReason);
    }

    [Fact]
    public void A_fully_mapped_vitals_message_is_read()
    {
        NetworkGameplayProvider provider = Provider(
            FullVitals, DataSourceKind.Live, VitalsMessage(hp: 4200, maxHp: 5000, mp: 900, hasTarget: 1, inCombat: 1));

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(4200, observation.Hp.Value);
        Assert.Equal(5000, observation.MaxHp.Value);
        Assert.Equal(900, observation.Mp.Value);
        Assert.True(observation.HasTarget.Value);
        Assert.True(observation.InCombat.Value);
    }

    /// <summary>
    /// A capture of the running client is LIVE bytes, but the map is the
    /// operator's reconstruction. The reading is DERIVED all the way through, so a
    /// policy that requires LIVE can still refuse it.
    /// </summary>
    [Fact]
    public void A_reading_is_never_more_trusted_than_the_map_that_produced_it()
    {
        GameplayObservation observation = Provider(
            FullVitals, DataSourceKind.Live, VitalsMessage(4200, 5000, 900)).Observe();

        Assert.Equal(DataSourceKind.Derived, observation.Hp.Source);
        Assert.Equal(DataSourceKind.Derived, observation.MaxHp.Source);
    }

    [Fact]
    public void A_replayed_capture_is_cached_not_derived()
    {
        GameplayObservation observation = Provider(
            FullVitals, DataSourceKind.Cached, VitalsMessage(4200, 5000, 900)).Observe();

        Assert.Equal(DataSourceKind.Cached, observation.Hp.Source);
    }

    /// <summary>
    /// The case that decides whether this design is honest. The operator has pinned
    /// the vitals but not the combat flags. Three fields carry values and two say
    /// why they do not — rather than two falses that read exactly like observations.
    /// </summary>
    [Fact]
    public void A_partially_mapped_protocol_reports_what_it_has_and_names_what_it_lacks()
    {
        GameplayObservation observation = Provider(
            VitalsWithoutFlags, DataSourceKind.Live, VitalsMessage(4200, 5000, 900)).Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(4200, observation.Hp.Value);
        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal("target_flag_not_mapped", observation.HasTarget.FailureReason);
        Assert.Equal("combat_flag_not_mapped", observation.InCombat.FailureReason);
    }

    [Fact]
    public void A_map_without_vitals_reads_entities_but_not_the_player()
    {
        GameplayObservation observation = Provider(
            null, DataSourceKind.Live, SightingMessage(101, 30, 40), SightingMessage(102, 50, 60)).Observe();

        Assert.False(observation.HasVitals);
        Assert.Equal("player_vitals_not_mapped", observation.UnusableReason);
        Assert.True(observation.EntitiesInView.HasValue);
        Assert.Equal(2, observation.EntitiesInView.Value);
    }

    /// <summary>
    /// A max HP of zero is the signature of a wrong offset, and it would also make
    /// every ratio computed from it a division by zero. It is refused, not clamped:
    /// clamping is how a wrong map goes on producing plausible readings.
    /// </summary>
    [Fact]
    public void A_zero_max_hp_is_refused_rather_than_clamped()
    {
        GameplayObservation observation = Provider(
            FullVitals, DataSourceKind.Live, VitalsMessage(hp: 0, maxHp: 0, mp: 0)).Observe();

        Assert.False(observation.HasVitals);
    }

    /// <summary>HP above max HP is the same evidence of a misplaced field.</summary>
    [Fact]
    public void Hp_above_max_hp_is_refused()
    {
        GameplayObservation observation = Provider(
            FullVitals, DataSourceKind.Live, VitalsMessage(hp: 9000, maxHp: 5000, mp: 100)).Observe();

        Assert.False(observation.HasVitals);
    }

    /// <summary>A dead character is a real reading, not a broken one.</summary>
    [Fact]
    public void Zero_hp_with_a_real_max_is_read_not_refused()
    {
        GameplayObservation observation = Provider(
            FullVitals, DataSourceKind.Live, VitalsMessage(hp: 0, maxHp: 5000, mp: 100)).Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(0, observation.Hp.Value);
    }

    /// <summary>
    /// Within one batch the later message is the current state. Keeping the first
    /// would report an HP the character no longer has as though it were now.
    /// </summary>
    [Fact]
    public void The_most_recent_vitals_in_a_batch_win()
    {
        GameplayObservation observation = Provider(
            FullVitals,
            DataSourceKind.Live,
            VitalsMessage(4200, 5000, 900),
            VitalsMessage(3100, 5000, 850)).Observe();

        Assert.Equal(3100, observation.Hp.Value);
    }

    [Fact]
    public void A_map_that_maps_one_opcode_twice_is_refused_at_validation()
    {
        var clash = Map(FullVitals with { Opcode = OpSighting });

        InvalidDataException error = Assert.Throws<InvalidDataException>(clash.Validate);
        Assert.Contains("both as a message and as the player vitals", error.Message, StringComparison.Ordinal);
    }
}
