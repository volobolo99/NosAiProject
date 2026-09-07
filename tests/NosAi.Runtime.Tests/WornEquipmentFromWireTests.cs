using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-07/A2+A4: read what the character is wearing from <c>eq</c> and
/// <c>equip</c>, and confirm the equipment-bag channel against <c>ivn</c>.
/// </summary>
/// <remarks>
/// <para>
/// The hand-built packets prove the decoder reads the shapes the catalogue
/// wrote down. The <c>RecordedCaptureFact</c> tests prove those shapes are on
/// the wire: they replay <c>data/equip_test.noscap</c> and cross-check the
/// decoded vnums against each other, which a hand-built packet cannot do.
/// </para>
/// </remarks>
public sealed class WornEquipmentFromWireTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    /// <summary>The <c>cond</c> that names this session's character (confirmed).</summary>
    private const string Cond = "cond 1 3443217 0 0 11";

    /// <summary>The capture's own <c>eq</c> line, byte for byte.</summary>
    private const string CapturedEq = "eq 3443217 0 0 1 2 1 221.-1.262.157.224.279.-1.-1.-1.-1.-1 25 0 100";

    /// <summary>The capture's own first <c>equip</c> line, byte for byte.</summary>
    private const string CapturedEquip =
        "equip 25 0 0.262.5.2.0.0.0 2.221.0.0.0.0.0 4.715.0.2.0.0.0 5.157.0.0.0.0.0 " +
        "6.309.0.0.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0 12.902.0.5.0.0.0";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    /// <summary>A decoder that has already seen the character's own id.</summary>
    private static NosTaleWorldProtocolDecoder Identified()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));
        return decoder;
    }

    // ------------------------------------------------------------------- eq

    /// <summary>
    /// The capture's <c>eq</c> line yields the five worn slots, and the <c>-1</c>
    /// positions produce none. Five, not six: the dotted group has 11 positions,
    /// five carrying a vnum and six carrying <c>-1</c>.
    /// </summary>
    [Fact]
    public void The_eq_line_yields_the_worn_slots_and_the_minus_one_positions_produce_none()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(CapturedEq));

        Assert.NotNull(decoded.Equipment);
        WornEquipment worn = decoded.Equipment!;
        Assert.Equal(3443217, worn.EntityId);
        Assert.Equal(EquipmentWireOpcode.Eq, worn.Opcode);
        Assert.Equal(
            new[]
            {
                new WornEquipmentSlot(0, 221),
                new WornEquipmentSlot(2, 262),
                new WornEquipmentSlot(3, 157),
                new WornEquipmentSlot(4, 224),
                new WornEquipmentSlot(5, 279),
            },
            worn.Slots);
        Assert.Empty(decoded.Sightings);
        Assert.Empty(decoded.Events);
    }

    // ---------------------------------------------------------------- equip

    /// <summary>
    /// The capture's first <c>equip</c> line yields nine occupied slots, the
    /// first of which is slot 0 holding vnum 262.
    /// </summary>
    [Fact]
    public void The_first_equip_line_yields_nine_slots_with_slot_zero_first()
    {
        DecodedObservations decoded = Identified().Decode(Packet(CapturedEquip));

        Assert.NotNull(decoded.Equipment);
        WornEquipment worn = decoded.Equipment!;
        Assert.Equal(3443217, worn.EntityId);
        Assert.Equal(EquipmentWireOpcode.Equip, worn.Opcode);
        Assert.Equal(9, worn.Slots.Length);
        Assert.Equal(new WornEquipmentSlot(0, 262), worn.Slots[0]);
    }

    /// <summary>A group whose vnum is 0 is an empty slot and produces nothing.</summary>
    [Fact]
    public void An_equip_group_with_vnum_zero_produces_no_slot()
    {
        DecodedObservations decoded = Identified().Decode(Packet(
            "equip 25 0 0.262.5.2.0.0.0 2.0.0.0.0.0.0"));

        Assert.NotNull(decoded.Equipment);
        WornEquipmentSlot only = Assert.Single(decoded.Equipment!.Slots);
        Assert.Equal(new WornEquipmentSlot(0, 262), only);
    }

    /// <summary>
    /// The five fields after the vnum inside a group are not read, so changing
    /// them changes nothing about the reading.
    /// </summary>
    [Fact]
    public void Fields_after_the_vnum_are_ignored()
    {
        WornEquipment? asCaptured = Identified().Decode(Packet(CapturedEquip)).Equipment;
        WornEquipment? withOtherTail = Identified().Decode(Packet(
            "equip 25 0 0.262.9.9.9.9.9 2.221.9.9.9.9.9 4.715.9.9.9.9.9 5.157.9.9.9.9.9 " +
            "6.309.9.9.9.9.9 9.224.9.9.9.9.9 10.279.9.9.9.9.9 11.284.9.9.9.9.9 12.902.9.9.9.9.9")).Equipment;

        Assert.NotNull(asCaptured);
        Assert.NotNull(withOtherTail);
        Assert.True(asCaptured!.Slots.SequenceEqual(withOtherTail!.Slots), "slots differ after trailing fields changed");
    }

    /// <summary>
    /// A malformed packet — a non-numeric slot, a group with a single part, an
    /// <c>eq</c> with no dotted group, a negative vnum — is refused whole: no
    /// half-read equipment set may reach a planner.
    /// </summary>
    [Theory]
    [InlineData("equip 25 0 x.262.5.2.0.0.0")]                        // slot is not a number
    [InlineData("equip 25 0 262")]                                    // group with one part
    [InlineData("equip 25 0 0.262.5.2.0.0.0 2.x.0.0.0.0.0")]        // vnum is not a number
    [InlineData("equip 25 0 0.262.5.2.0.0.0 -3.221.0.0.0.0.0")]      // negative slot
    [InlineData("eq 3443217 0 0 1 2 1 25 0 100")]                     // no dotted group
    [InlineData("eq 3443217")]                                        // group field absent
    public void A_malformed_equipment_packet_is_refused_whole(string line)
    {
        DecodedObservations decoded = Identified().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Equipment);
    }

    /// <summary>
    /// <c>equip</c> carries no entity id; before <c>cond</c> has named the
    /// character it is refused rather than attributed to entity 0.
    /// </summary>
    [Fact]
    public void An_equip_before_cond_is_refused_not_attributed_to_nobody()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(CapturedEquip));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Equipment);
    }

    // --------------------------------------------- against the real recording

    /// <summary>
    /// The equip_test session yields six <c>eq</c> and six <c>equip</c> readings;
    /// every <c>eq</c> reading carries the same five slots and the session's own
    /// entity id.
    /// </summary>
    [RecordedCaptureFact("equip_test.noscap")]
    public void The_recording_yields_six_eq_and_six_equip_readings_all_named_3443217()
    {
        List<WornEquipment> readings = ReadEquipmentReadings("equip_test.noscap");

        List<WornEquipment> eq = readings.Where(r => r.Opcode == EquipmentWireOpcode.Eq).ToList();
        List<WornEquipment> equip = readings.Where(r => r.Opcode == EquipmentWireOpcode.Equip).ToList();

        Assert.Equal(6, eq.Count);
        Assert.Equal(6, equip.Count);
        Assert.All(eq, r => Assert.Equal(3443217, r.EntityId));
        Assert.All(eq, r => Assert.True(eq[0].Slots.SequenceEqual(r.Slots), "eq slots differ between readings"));
        Assert.All(eq, r => Assert.Equal(5, r.Slots.Length));
    }

    /// <summary>
    /// The correspondence that matters: three vnums each leave an <c>equip</c>
    /// slot and appear in an <c>ivn</c> slot within the same capture, and the
    /// <c>equip</c> readings containing each are not all of them. If this fails,
    /// one of the two decoders is reading the capture wrongly.
    /// </summary>
    [RecordedCaptureTheory("equip_test.noscap")]
    [InlineData(309)]
    [InlineData(518)]
    [InlineData(284)]
    public void Each_vnum_appears_in_both_equip_and_ivn_and_not_in_every_equip_reading(int vnum)
    {
        string path = RecordedCaptureFactAttribute.Resolve("equip_test.noscap")!;

        List<WornEquipment> readings = ReadEquipmentReadings(path);
        List<WornEquipment> equip = readings.Where(r => r.Opcode == EquipmentWireOpcode.Equip).ToList();
        List<InventorySlotReading> ivn = ReadInventoryReadings(path);

        Assert.Contains(equip, r => r.Slots.Any(s => s.Vnum == vnum));
        Assert.Contains(ivn, i => i.Vnum == vnum);
        Assert.Contains(equip, r => !r.Slots.Any(s => s.Vnum == vnum));
    }

    /// <summary>
    /// The combat recording carries no <c>eq</c>/<c>equip</c>, so it yields no
    /// equipment readings and its census is unchanged by this task.
    /// </summary>
    [RecordedCaptureFact("nostale_combat.noscap")]
    public void The_combat_recording_yields_no_equipment_and_its_census_is_unchanged()
    {
        string path = RecordedCaptureFactAttribute.Resolve("nostale_combat.noscap")!;

        WorldChannelReplaySummary summary = WorldChannelReplay.ReplayFile(path);

        Assert.Equal(0, summary.EqReadings);
        Assert.Equal(0, summary.EquipReadings);
        Assert.DoesNotContain(summary.Opcodes, o => o.Key is "eq" or "equip");
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// Replays one recording through the shipping chain and returns every
    /// equipment reading, in wire order. One packet per report keeps the order.
    /// </summary>
    private static List<WornEquipment> ReadEquipmentReadings(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        IPacketSource packets = CaptureFile.Open(path);
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using var observationSource = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var observer = new GameTrafficObserver(
            observationSource, new ScopedGameTrafficFilter(endpoint), new NosTaleWorldProtocolDecoder());

        var readings = new List<WornEquipment>();
        while (true)
        {
            NetworkObservationReport report = observer.ObservePending(1);
            if (report.ObservedPackets == 0)
                break;
            if (report.Equipment is { } worn)
                readings.Add(worn);
        }
        return readings;
    }

    /// <summary>Replays one recording and returns every <c>ivn</c> slot reading.</summary>
    private static List<InventorySlotReading> ReadInventoryReadings(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        IPacketSource packets = CaptureFile.Open(path);
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using var observationSource = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var observer = new GameTrafficObserver(
            observationSource, new ScopedGameTrafficFilter(endpoint), new NosTaleWorldProtocolDecoder());

        var readings = new List<InventorySlotReading>();
        while (true)
        {
            NetworkObservationReport report = observer.ObservePending(1);
            if (report.ObservedPackets == 0)
                break;
            readings.AddRange(report.InventorySlots);
        }
        return readings;
    }
}
