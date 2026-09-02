using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// C1-3 and A4: the five opcodes the catalogue marks <i>probable</i> and the
/// decoder now reads — <c>sr</c>, <c>ivn</c>, <c>get</c>, <c>drop</c>, <c>ct</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every shape below is the one <c>docs/PROTOCOLLO_NOSTALE.md</c> recorded from
/// the two real captures, character for character. A <i>probable</i> reading is
/// published with the packet's own provenance — the framing was verified, which
/// is what provenance asserts (ADR-0014) — and the guard against a misread is the
/// shape check, on the same principle by which a <c>stat</c> whose HP exceeds its
/// maximum is refused rather than clamped.
/// </para>
/// <para>
/// So each opcode gets a pair: one packet exactly as the wire sent it, and one
/// malformed packet that must produce nothing at all. What "malformed" means is
/// the interesting half — a field that is not a number, a field outside what that
/// field can hold, or a packet shorter than the observed shape. Not one of them
/// may yield a partial reading, because a partial reading of a positional
/// protocol is a value taken out of a field that is not that field.
/// </para>
/// </remarks>
public sealed class CataloguedOpcodeTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private const long OwnId = 3443217;
    private const long StrangerId = 9999999;
    private const long MonsterId = 313816;

    /// <summary>The <c>cond</c> that names this session's character (confirmed).</summary>
    private const string Cond = "cond 1 3443217 0 0 11";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    /// <summary>A decoder that has already seen the character's own id.</summary>
    private static NosTaleWorldProtocolDecoder Identified()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));
        return decoder;
    }

    // ------------------------------------------------------------------- sr

    /// <summary>
    /// <c>sr 2</c> — one of the three slots the capture showed (0, 2, 6). It is
    /// the second half of <c>UseSkill</c>'s post-condition, and what it states is
    /// the instant the slot became usable again.
    /// </summary>
    [Fact]
    public void A_skill_ready_names_its_slot_and_the_instant_the_packet_crossed_the_wire()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet("sr 2"));

        Assert.NotNull(decoded.SkillReady);
        SkillReady ready = decoded.SkillReady!;
        Assert.Equal(2, ready.Slot);
        Assert.Equal(At, ready.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, ready.Source);
        // A cooldown ending is not a sighting and not a hit.
        Assert.Empty(decoded.Sightings);
        Assert.Empty(decoded.Events);
        Assert.Null(decoded.Vitals);
    }

    /// <summary>Slot 0 is a real slot the capture showed; it is not "no slot".</summary>
    [Fact]
    public void Slot_zero_is_a_slot_and_not_an_absence()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet("sr 0"));

        Assert.NotNull(decoded.SkillReady);
        Assert.Equal(0, decoded.SkillReady!.Slot);
    }

    /// <summary>
    /// A negative slot, a slot beyond any skill bar, a slot that is not a number,
    /// and a packet with no slot at all. Each is a field that is not a slot, and
    /// what is not read produces nothing rather than slot zero.
    /// </summary>
    [Theory]
    [InlineData("sr")]                  // no slot at all
    [InlineData("sr -1")]               // negative is not an index
    [InlineData("sr x")]                // not a number
    [InlineData("sr 100000")]           // past MaxPlausibleSkillSlot: not a slot
    [InlineData("sr 2.5")]              // not an integer
    public void A_malformed_skill_ready_is_refused_whole(string line)
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.SkillReady);
    }

    /// <summary>The bound is loose on purpose: it rejects garbage, not a big skill bar.</summary>
    [Fact]
    public void The_slot_bound_rejects_a_field_that_is_not_a_slot_and_admits_the_largest_that_is()
    {
        Assert.NotNull(new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"sr {NosTaleWorldProtocolDecoder.MaxPlausibleSkillSlot}")).SkillReady);
        Assert.Null(new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"sr {NosTaleWorldProtocolDecoder.MaxPlausibleSkillSlot + 1}")).SkillReady);
    }

    // ------------------------------------------------------------------ ivn

    /// <summary>
    /// <c>ivn 2 34.2006.1.0</c> — the capture's own line, whose vnum 2006 matches
    /// the <c>drop</c> that preceded it.
    /// </summary>
    [Fact]
    public void An_inventory_slot_reads_the_four_dotted_parts_the_capture_showed()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet("ivn 2 34.2006.1.0"));

        Assert.NotNull(decoded.InventorySlot);
        InventorySlotReading slot = decoded.InventorySlot!;
        Assert.Equal(2, slot.InventoryKind);
        Assert.Equal(34, slot.Slot);
        Assert.Equal(2006, slot.Vnum);
        Assert.Equal(1, slot.Amount);
        Assert.Equal(0, slot.Rarity);
        Assert.Equal(At, slot.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, slot.Source);
    }

    /// <summary>
    /// Three parts is a truncated packet; five is a shape nobody observed, in
    /// which the third part may not be an amount at all. Both are refused rather
    /// than read at positions the capture never established.
    /// </summary>
    [Theory]
    [InlineData("ivn 2")]                   // no dotted field
    [InlineData("ivn 2 34.2006.1")]         // three parts: not the observed shape
    [InlineData("ivn 2 34.2006.1.0.7")]     // five parts: not the observed shape
    [InlineData("ivn 2 34.2006.0.0")]       // amount 0: an empty slot was never observed
    [InlineData("ivn 2 34.-1.1.0")]         // vnum -1: never observed
    [InlineData("ivn 2 -3.2006.1.0")]       // negative slot
    [InlineData("ivn 2 a.2006.1.0")]        // slot is not a number
    [InlineData("ivn -1 34.2006.1.0")]      // negative inventory kind
    public void A_malformed_inventory_slot_is_refused_whole(string line)
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.InventorySlot);
    }

    /// <summary>
    /// The kind travels with the slot. The catalogue does not say what it means,
    /// and the reading is keyed on the pair so one bag's slot 34 cannot overwrite
    /// another's.
    /// </summary>
    [Fact]
    public void The_inventory_kind_is_carried_without_a_meaning_being_attached_to_it()
    {
        InventorySlotReading? first =
            new NosTaleWorldProtocolDecoder().Decode(Packet("ivn 0 34.2006.1.0")).InventorySlot;
        InventorySlotReading? second =
            new NosTaleWorldProtocolDecoder().Decode(Packet("ivn 2 34.2006.1.0")).InventorySlot;

        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.Equal(0, first!.InventoryKind);
        Assert.Equal(2, second!.InventoryKind);
        Assert.Equal(first.Slot, second.Slot);
    }

    // ------------------------------------------------------------------ get

    /// <summary>
    /// <c>get 1 3443217 1092257 0</c> — the capture's own line. With the own id
    /// known the taker resolves to this character.
    /// </summary>
    [Fact]
    public void A_pickup_by_this_character_is_named_once_the_own_id_is_known()
    {
        DecodedObservations decoded = Identified().Decode(Packet($"get 1 {OwnId} 1092257 0"));

        Assert.NotNull(decoded.Pickup);
        ItemPickup pickup = decoded.Pickup!;
        Assert.Equal(1, pickup.TakerType);
        Assert.Equal(OwnId, pickup.TakerId);
        Assert.Equal(1092257, pickup.DropId);
        Assert.True(pickup.ByPlayer);
        Assert.Equal(At, pickup.ObservedAtUtc);
    }

    /// <summary>Another player taking an item is a pickup, and not this character's.</summary>
    [Fact]
    public void A_pickup_by_another_player_is_read_and_is_not_this_characters()
    {
        ItemPickup? pickup = Identified().Decode(Packet($"get 1 {StrangerId} 1092257 0")).Pickup;

        Assert.NotNull(pickup);
        Assert.Equal(StrangerId, pickup!.TakerId);
        Assert.False(pickup.ByPlayer);
    }

    /// <summary>
    /// Before <c>cond</c> has named the character, taker type 1 says "a player
    /// picked it up" and not which one. The answer is null, and null is carried
    /// rather than resolved to false — the same asymmetry as the aggressor's.
    /// </summary>
    [Fact]
    public void Before_the_own_id_is_known_whether_it_was_me_is_null_and_not_false()
    {
        ItemPickup? pickup = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"get 1 {OwnId} 1092257 0")).Pickup;

        Assert.NotNull(pickup);
        Assert.Null(pickup!.ByPlayer);
        // The pickup itself is still a real observation: the drop id is what the
        // inventory post-condition matches against.
        Assert.Equal(1092257, pickup.DropId);
    }

    [Theory]
    [InlineData("get 1 3443217")]           // no drop id
    [InlineData("get 1 3443217 0 0")]       // drop id 0: never observed
    [InlineData("get 1 0 1092257 0")]       // taker id 0 is the channel's player convention
    [InlineData("get -1 3443217 1092257 0")]// negative taker type
    [InlineData("get 1 x 1092257 0")]       // taker id is not a number
    [InlineData("get 1 3443217 y 0")]       // drop id is not a number
    public void A_malformed_pickup_is_refused_whole(string line)
    {
        DecodedObservations decoded = Identified().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Pickup);
    }

    // ----------------------------------------------------------------- drop

    /// <summary>
    /// <c>drop 2006 1092257 110 63 1 0 3443217</c> — the capture's own line. The
    /// position is what makes a ground item collectable at all.
    /// </summary>
    [Fact]
    public void A_ground_item_carries_its_vnum_its_square_and_its_owner()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"drop 2006 1092257 110 63 1 0 {OwnId}"));

        Assert.NotNull(decoded.GroundItem);
        GroundItem item = decoded.GroundItem!;
        Assert.Equal(2006, item.Vnum);
        Assert.Equal(1092257, item.DropId);
        Assert.Equal(110, item.X);
        Assert.Equal(63, item.Y);
        Assert.Equal(1, item.Amount);
        Assert.Equal(OwnId, item.OwnerId);
        Assert.Equal(At, item.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, item.Source);
    }

    /// <summary>
    /// Field 6 is unknown in the catalogue and is not read. Changing it changes
    /// nothing about the reading, which is what "not read" has to mean.
    /// </summary>
    [Fact]
    public void The_unknown_sixth_field_changes_nothing_because_it_is_not_read()
    {
        GroundItem? asCaptured = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"drop 2006 1092257 110 63 1 0 {OwnId}")).GroundItem;
        GroundItem? withAnother = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"drop 2006 1092257 110 63 1 77 {OwnId}")).GroundItem;

        Assert.NotNull(asCaptured);

        Assert.Equal(asCaptured, withAnother);
    }

    /// <summary>
    /// A coordinate past the map bound is not a distant square, it is a field
    /// that is not a coordinate — the same check the memory reader applies to the
    /// character's own position.
    /// </summary>
    [Theory]
    [InlineData("drop 2006 1092257 110 63 1 0")]            // no owner id
    [InlineData("drop 0 1092257 110 63 1 0 3443217")]       // vnum 0
    [InlineData("drop 2006 0 110 63 1 0 3443217")]          // drop id 0
    [InlineData("drop 2006 1092257 -1 63 1 0 3443217")]     // negative x
    [InlineData("drop 2006 1092257 110 99999 1 0 3443217")] // y past the map bound
    [InlineData("drop 2006 1092257 110 63 0 0 3443217")]    // amount 0
    [InlineData("drop 2006 1092257 x 63 1 0 3443217")]      // x is not a number
    [InlineData("drop 2006 1092257 110 63 1 0 -5")]         // negative owner id
    public void A_malformed_ground_item_is_refused_whole(string line)
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.GroundItem);
    }

    /// <summary>The coordinate bound admits the largest square it calls a square.</summary>
    [Fact]
    public void The_coordinate_bound_admits_the_edge_and_refuses_past_it()
    {
        const int max = NosTaleWorldProtocolDecoder.MaxPlausibleCoordinate;
        Assert.NotNull(new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"drop 2006 1092257 {max} {max} 1 0 {OwnId}")).GroundItem);
        Assert.Null(new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"drop 2006 1092257 {max + 1} 63 1 0 {OwnId}")).GroundItem);
    }

    // ------------------------------------------------------------------- ct

    /// <summary>
    /// <c>ct 1 3443217 3 3205 -1 -1 220</c> — the character acting on a monster.
    /// The wire answers <i>which</i>; the screen answers <i>whether</i> (ADR-0018).
    /// </summary>
    [Fact]
    public void A_cast_by_this_character_names_which_entity_it_acted_on()
    {
        DecodedObservations decoded = Identified().Decode(Packet("ct 1 3443217 3 3205 -1 -1 220"));

        Assert.NotNull(decoded.PlayerTarget);
        PlayerTargetSelection selection = decoded.PlayerTarget!;
        Assert.Equal(3205, selection.Target.EntityId);
        Assert.Equal(3, selection.Target.EntityType);
        Assert.Equal(At, selection.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, selection.Source);
    }

    /// <summary>
    /// The 108 occurrences in the capture are the other direction: a monster
    /// acting on the character. That is the monster's selection, not this
    /// character's, and it names nothing here.
    /// </summary>
    [Fact]
    public void A_monster_acting_on_the_character_selects_nothing()
    {
        DecodedObservations decoded = Identified().Decode(Packet($"ct 3 {MonsterId} 1 {OwnId} -1 -1 0"));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.PlayerTarget);
    }

    /// <summary>
    /// Before <c>cond</c>, source type 1 says "a player acted" and not which one,
    /// so a stranger's cast could name an entity this character never chose.
    /// Nothing is published until the id is known — the aggressor's rule again.
    /// </summary>
    [Fact]
    public void Before_the_own_id_is_known_no_cast_names_a_selection()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet("ct 1 3443217 3 3205 -1 -1 220"));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.PlayerTarget);
    }

    /// <summary>Another player's cast is not this character's selection.</summary>
    [Fact]
    public void A_cast_by_another_player_selects_nothing()
    {
        Assert.Null(Identified().Decode(Packet($"ct 1 {StrangerId} 3 3205 -1 -1 220")).PlayerTarget);
    }

    /// <summary>
    /// A cast aimed at the character itself is a self-buff in the captures.
    /// Selecting yourself is not selecting a target.
    /// </summary>
    [Fact]
    public void A_self_cast_selects_nothing()
    {
        Assert.Null(Identified().Decode(Packet($"ct 1 {OwnId} 1 {OwnId} -1 -1 0")).PlayerTarget);
    }

    [Theory]
    [InlineData("ct 1 3443217 3")]              // no target id
    [InlineData("ct 1 3443217 3 0 -1 -1 220")]  // target id 0
    [InlineData("ct 1 3443217 -1 3205 -1 -1 0")]// negative target type
    [InlineData("ct 1 3443217 3 x -1 -1 0")]    // target id is not a number
    [InlineData("ct x 3443217 3 3205 -1 -1 0")] // source type is not a number
    public void A_malformed_cast_is_refused_whole(string line)
    {
        DecodedObservations decoded = Identified().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.PlayerTarget);
    }

    // ------------------------------------------- out through the observer

    /// <summary>
    /// Every one of the five reaches the batch report. The lists keep wire order
    /// rather than collapsing to a last value: a post-condition window has to see
    /// each event, not only the newest.
    /// </summary>
    [Fact]
    public void All_five_reach_the_report_out_of_one_batch()
    {
        var source = new ListSource(
            Packet(Cond),
            Packet("sr 2"),
            Packet("sr 6"),
            Packet("ivn 2 34.2006.1.0"),
            Packet($"drop 2006 1092257 110 63 1 0 {OwnId}"),
            Packet($"get 1 {OwnId} 1092257 0"),
            Packet("ct 1 3443217 3 3205 -1 -1 220"));
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());

        NetworkObservationReport report = observer.ObservePending(32);

        Assert.Equal(new[] { 2, 6 }, report.SkillsReady.Select(s => s.Slot));
        Assert.Equal(2006, Assert.Single(report.InventorySlots).Vnum);
        Assert.Equal(1092257, Assert.Single(report.GroundItems).DropId);
        Assert.Equal(1092257, Assert.Single(report.Pickups).DropId);
        Assert.Equal(3205, report.LastPlayerTarget!.Target.EntityId);
    }

    /// <summary>
    /// A batch that carried none of them reports none — empty lists and nulls,
    /// never a zero-slot reading or a selection of nobody.
    /// </summary>
    [Fact]
    public void A_batch_without_them_reports_nothing_rather_than_empty_readings()
    {
        var source = new ListSource(Packet("stat 7305 7305 1420 1420 0 1184"));
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());

        NetworkObservationReport report = observer.ObservePending(16);

        Assert.Empty(report.SkillsReady);
        Assert.Empty(report.InventorySlots);
        Assert.Empty(report.Pickups);
        Assert.Empty(report.GroundItems);
        Assert.Null(report.LastPlayerTarget);
    }

    // ------------------------------------------ out through the observation

    /// <summary>
    /// The provider publishes each of them classified. A slot read this poll keeps
    /// the packet's provenance; what nothing has said is UNKNOWN with a reason,
    /// never an empty list presented as an observation of emptiness.
    /// </summary>
    [Fact]
    public void The_observation_publishes_the_four_readings_with_their_own_provenance()
    {
        (ListSource wire, NetworkGameplayProvider provider) = Chain(
            Packet(Cond),
            Packet("stat 7305 7305 1420 1420 0 1184"),
            Packet("sr 2"),
            Packet("ivn 2 34.2006.1.0"),
            Packet($"drop 2006 1092257 110 63 1 0 {OwnId}"));
        _ = wire;

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.SkillsReady.HasValue);
        Assert.Equal(2, Assert.Single(observation.SkillsReady.Value).Slot);
        Assert.Equal(DataSourceKind.Live, observation.SkillsReady.Source);

        Assert.True(observation.Inventory.HasValue);
        Assert.Equal(2006, Assert.Single(observation.Inventory.Value).Vnum);

        Assert.True(observation.GroundItems.HasValue);
        Assert.Equal(1092257, Assert.Single(observation.GroundItems.Value).DropId);

        // Nothing was picked up, and that is stated rather than shown as an empty
        // list a consumer would read as "no items exist".
        Assert.False(observation.LastPickup.HasValue);
        Assert.Equal("no_pickup_observed", observation.LastPickup.FailureReason);
    }

    /// <summary>
    /// A pickup takes the item off the ground: the catalogue matched the two ids,
    /// and an item somebody has taken is not lying there to be collected.
    /// </summary>
    [Fact]
    public void A_pickup_removes_the_item_it_names_from_the_ground()
    {
        (ListSource wire, NetworkGameplayProvider provider) = Chain(
            Packet(Cond),
            Packet("stat 7305 7305 1420 1420 0 1184"),
            Packet($"drop 2006 1092257 110 63 1 0 {OwnId}"),
            Packet($"drop 2007 1092258 111 64 1 0 {OwnId}"),
            Packet($"get 1 {OwnId} 1092257 0"));
        _ = wire;

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.GroundItems.HasValue);
        GroundItem left = Assert.Single(observation.GroundItems.Value);
        Assert.Equal(1092258, left.DropId);
        Assert.True(observation.LastPickup.HasValue);
        Assert.Equal(1092257, observation.LastPickup.Value.DropId);
        Assert.True(observation.LastPickup.Value.ByPlayer);
    }

    /// <summary>
    /// The selection is the wire's <i>which</i> and never becomes the screen's
    /// <i>whether</i>: publishing a selected entity leaves <c>HasTarget</c>
    /// exactly as unknown as it was (ADR-0018).
    /// </summary>
    [Fact]
    public void A_selection_never_establishes_that_a_target_exists()
    {
        (ListSource wire, NetworkGameplayProvider provider) = Chain(
            Packet(Cond),
            Packet("stat 7305 7305 1420 1420 0 1184"),
            Packet("ct 1 3443217 3 3205 -1 -1 220"));
        _ = wire;

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.SelectedTarget.HasValue);
        Assert.Equal(3205, observation.SelectedTarget.Value.EntityId);
        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal("target_flag_not_mapped", observation.HasTarget.FailureReason);
    }

    private static (ListSource Wire, NetworkGameplayProvider Provider) Chain(params ObservedPacket[] packets)
    {
        var wire = new ListSource(packets);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        return (wire, new NetworkGameplayProvider(feed));
    }

    private sealed class ListSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets;
        public ListSource(params ObservedPacket[] packets) => _packets = new Queue<ObservedPacket>(packets);
        public DataSourceKind Source => DataSourceKind.Live;

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }
}
