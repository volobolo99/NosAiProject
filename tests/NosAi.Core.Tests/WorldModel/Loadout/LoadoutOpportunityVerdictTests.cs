using System;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Loadout;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Loadout;

/// <summary>
/// No proposed loadout change can mean the build is already fine, or that nobody ever read
/// what the character wears and carries. The second is a gap in observation, and reporting it
/// as the first would let the player settle for gear it never looked at.
/// </summary>
public sealed class LoadoutOpportunityVerdictTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static EquipmentSlot? NoSlot(ItemId item) => null;

    [Fact]
    public void NeitherSourceObserved_IsNotAFinishedAssessment()
    {
        LoadoutOpportunityVerdict verdict = LoadoutPlanner.ExplainCandidates(
            BuildPlayer(equipmentRead: false, inventoryRead: false),
            NoSlot);

        Assert.Equal(LoadoutOpportunityVerdict.SourcesNeverRead, verdict);
    }

    [Fact]
    public void AnEquippedItem_OffersAChange()
    {
        LoadoutOpportunityVerdict verdict = LoadoutPlanner.ExplainCandidates(
            BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) })),
            NoSlot);

        Assert.Equal(LoadoutOpportunityVerdict.CandidatesAvailable, verdict);
    }

    [Fact]
    public void BothSourcesReadAndEmpty_IsNothingToChange()
    {
        LoadoutOpportunityVerdict verdict = LoadoutPlanner.ExplainCandidates(BuildPlayer(), NoSlot);

        Assert.Equal(LoadoutOpportunityVerdict.NothingToChange, verdict);
    }

    [Fact]
    public void UnreadEquipment_IsNamedRatherThanCalledSettled()
    {
        LoadoutOpportunityVerdict verdict = LoadoutPlanner.ExplainCandidates(
            BuildPlayer(equipmentRead: false),
            NoSlot);

        Assert.Equal(LoadoutOpportunityVerdict.EquipmentNeverRead, verdict);
    }

    [Fact]
    public void UnreadInventory_IsNamedRatherThanCalledSettled()
    {
        LoadoutOpportunityVerdict verdict = LoadoutPlanner.ExplainCandidates(
            BuildPlayer(inventoryRead: false),
            NoSlot);

        Assert.Equal(LoadoutOpportunityVerdict.InventoryNeverRead, verdict);
    }

    [Fact]
    public void NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => LoadoutPlanner.ExplainCandidates(BuildPlayer(), null!));
    }

    /// <summary>
    /// Gear nobody ever read must not score zero: zero says the build needs no attention, which
    /// is a claim about equipment that was never looked at.
    /// </summary>
    [Fact]
    public void GearNeverRead_ReportsNothingRatherThanNoUrgency()
    {
        Assert.Null(StrategyPlanner.AssessOptimizationUrgency(
            BuildPlayer(equipmentRead: false, inventoryRead: false),
            NoSlot));
    }

    [Fact]
    public void APartialReading_KeepsTheGoalAliveWithoutPretendingItIsPressing()
    {
        StrategicSignal? signal = StrategyPlanner.AssessOptimizationUrgency(
            BuildPlayer(equipmentRead: false),
            NoSlot);

        Assert.NotNull(signal);
        Assert.Equal(0.25, signal!.Urgency);
        Assert.Equal("equipment_never_read", signal.Reason);
    }

    [Fact]
    public void AvailableOptions_ScoreLowBecauseTheyAreNotRanked()
    {
        StrategicSignal? signal = StrategyPlanner.AssessOptimizationUrgency(
            BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) })),
            NoSlot);

        Assert.NotNull(signal);
        Assert.Equal(0.2, signal!.Urgency);
        Assert.Equal("loadout_options_available_unranked", signal.Reason);
    }

    /// <summary>
    /// Filling a free slot is the only loadout gain this project can prove: the item catalogue
    /// exposes price, type and icon but no attack or defence, so comparing a candidate against
    /// the piece already worn would rank on numbers nobody stated.
    /// </summary>
    [Fact]
    public void AnItemFittingAFreeSlot_IsAProvableGain()
    {
        Player player = BuildPlayer(
            equipment: EquatableArray<EquipmentItem>.Empty,
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("500", 1) }));

        Assert.Single(LoadoutPlanner.GenerateEmptySlotCandidates(player, _ => EquipmentSlot.Hat));
    }

    [Fact]
    public void AnItemFittingAnOccupiedSlot_IsNotAProvableGain()
    {
        Player player = BuildPlayer(
            equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("900", EquipmentSlot.Hat) }),
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("500", 1) }));

        Assert.Empty(LoadoutPlanner.GenerateEmptySlotCandidates(player, _ => EquipmentSlot.Hat));
    }

    /// <summary>
    /// Without knowing what is worn, "this slot is free" is a guess, so nothing is proposed.
    /// </summary>
    [Fact]
    public void UnreadEquipment_YieldsNoEmptySlotCandidate()
    {
        Player player = BuildPlayer(
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("500", 1) }),
            equipmentRead: false);

        Assert.Empty(LoadoutPlanner.GenerateEmptySlotCandidates(player, _ => EquipmentSlot.Hat));
    }

    [Fact]
    public void AFillableSlot_RaisesTheOptimizationSignal()
    {
        Player player = BuildPlayer(
            equipment: EquatableArray<EquipmentItem>.Empty,
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("500", 1) }));

        StrategicSignal? signal = StrategyPlanner.AssessOptimizationUrgency(player, _ => EquipmentSlot.Hat);

        Assert.NotNull(signal);
        Assert.Equal(0.6, signal!.Urgency);
        Assert.Equal("empty_slot_fillable", signal.Reason);
    }

    private static InventoryItem BuildStack(string id, int quantity) =>
        new(
            new ItemId(id),
            WorldFact<string>.Live(id, 1d, Now),
            WorldFact<int>.Live(quantity, 1d, Now),
            WorldFact<int>.Live(0, 1d, Now));

    private static EquipmentItem BuildEquipped(string id, EquipmentSlot slot) =>
        new(new ItemId(id), WorldFact<string>.Live(id, 1d, Now), slot, WorldFact<bool>.Live(true, 1d, Now));

    private static Player BuildPlayer(
        EquatableArray<EquipmentItem>? equipment = null,
        EquatableArray<InventoryItem>? inventory = null,
        bool equipmentRead = true,
        bool inventoryRead = true) =>
        new(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            CombatantStatus.Empty,
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, Now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, Now),
            inventoryRead
                ? WorldFact<EquatableArray<InventoryItem>>.Live(inventory ?? EquatableArray<InventoryItem>.Empty, 1d, Now)
                : WorldFact<EquatableArray<InventoryItem>>.Unknown("inventory_never_read", Now),
            equipmentRead
                ? WorldFact<EquatableArray<EquipmentItem>>.Live(equipment ?? EquatableArray<EquipmentItem>.Empty, 1d, Now)
                : WorldFact<EquatableArray<EquipmentItem>>.Unknown("equipment_never_read", Now));
}
