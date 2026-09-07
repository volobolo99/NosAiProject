using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Loadout;
using NosAi.Core.WorldModel.Quests;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Loadout;

public sealed class LoadoutPlannerTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static Player BuildPlayer(
        EquatableArray<EquipmentItem>? equipment = null,
        EquatableArray<InventoryItem>? inventory = null) =>
        new(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            CombatantStatus.Empty,
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            inventory ?? EquatableArray<InventoryItem>.Empty,
            equipment ?? EquatableArray<EquipmentItem>.Empty);

    private static EquipmentItem BuildEquipped(string id, EquipmentSlot slot, bool equipped = true) =>
        new(new ItemId(id), WorldFact<string>.Live(id, 1d, Now), slot, WorldFact<bool>.Live(equipped, 1d, Now));

    private static InventoryItem BuildStack(string id, int quantity) =>
        new(new ItemId(id), WorldFact<string>.Live(id, 1d, Now), WorldFact<int>.Live(quantity, 1d, Now), WorldFact<int>.Live(0, 1d, Now));

    // ---- GenerateUnequipCandidates ----

    [Fact]
    public void GenerateUnequipCandidates_OneEquippedItem_ProducesOneCandidate()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateUnequipCandidates(player);

        LoadoutActionCandidate candidate = Assert.Single(candidates);
        Assert.Equal(LoadoutActionKind.Unequip, candidate.Kind);
        Assert.Equal(EquipmentSlot.Weapon, candidate.Slot);
    }

    [Fact]
    public void GenerateUnequipCandidates_NotEquipped_ProducesNoCandidate()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon, equipped: false) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateUnequipCandidates(player);

        Assert.Empty(candidates);
    }

    // ---- GenerateUpgradeCandidates ----

    [Fact]
    public void GenerateUpgradeCandidates_OneEquippedItem_ProducesOneCandidate()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateUpgradeCandidates(player);

        LoadoutActionCandidate candidate = Assert.Single(candidates);
        Assert.Equal(LoadoutActionKind.Upgrade, candidate.Kind);
        Assert.Equal(new ItemId("sword"), candidate.Item);
    }

    // ---- GenerateEquipCandidates ----

    [Fact]
    public void GenerateEquipCandidates_ResolvableStack_ProducesOneCandidate()
    {
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("helmet", 1) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateEquipCandidates(
            player, resolveSlot: id => id == new ItemId("helmet") ? EquipmentSlot.Hat : null);

        LoadoutActionCandidate candidate = Assert.Single(candidates);
        Assert.Equal(LoadoutActionKind.Equip, candidate.Kind);
        Assert.Equal(new ItemId("helmet"), candidate.Item);
        Assert.Equal(EquipmentSlot.Hat, candidate.Slot);
    }

    [Fact]
    public void GenerateEquipCandidates_UnresolvableStack_ProducesNoCandidate()
    {
        // A vnum resolveSlot legitimately has no answer for (an unrecognized
        // item, a catalog miss) must never be guessed into a fabricated slot.
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("mystery-item", 1) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateEquipCandidates(
            player, resolveSlot: _ => null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateEquipCandidates_ZeroQuantityStack_ProducesNoCandidate()
    {
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("helmet", 0) }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateEquipCandidates(
            player, resolveSlot: _ => EquipmentSlot.Hat);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateEquipCandidates_UnknownQuantity_ProducesNoCandidate()
    {
        var unknownQuantityStack = new InventoryItem(
            new ItemId("helmet"),
            WorldFact<string>.Live("helmet", 1d, Now),
            WorldFact<int>.Unknown("r", Now),
            WorldFact<int>.Live(0, 1d, Now));
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { unknownQuantityStack }));

        IReadOnlyList<LoadoutActionCandidate> candidates = LoadoutPlanner.GenerateEquipCandidates(
            player, resolveSlot: _ => EquipmentSlot.Hat);

        Assert.Empty(candidates);
    }

    // ---- CheckHardConstraints: Equip ----

    [Fact]
    public void CheckHardConstraints_Equip_ItemInInventoryAndSlotFree_IsAllowed()
    {
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("helmet", 1) }));
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: new ItemId("helmet"), slot: EquipmentSlot.Hat);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void CheckHardConstraints_Equip_ItemNotInInventory_Violates()
    {
        Player player = BuildPlayer();
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: new ItemId("helmet"), slot: EquipmentSlot.Hat);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.False(check.IsAllowed);
        Assert.Contains("item_not_in_inventory", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_Equip_ZeroQuantity_Violates()
    {
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("helmet", 0) }));
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: new ItemId("helmet"), slot: EquipmentSlot.Hat);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.False(check.IsAllowed);
        Assert.Contains("item_not_in_inventory", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_Equip_SlotAlreadyOccupied_Violates()
    {
        Player player = BuildPlayer(
            equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("old-helmet", EquipmentSlot.Hat) }),
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("new-helmet", 1) }));
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: new ItemId("new-helmet"), slot: EquipmentSlot.Hat);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.False(check.IsAllowed);
        Assert.Contains("slot_already_occupied", check.ViolatedConstraints);
    }

    // ---- CheckHardConstraints: Unequip ----

    [Fact]
    public void CheckHardConstraints_Unequip_SlotOccupied_IsAllowed()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) }));
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Unequip, slot: EquipmentSlot.Weapon);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void CheckHardConstraints_Unequip_SlotAlreadyEmpty_Violates()
    {
        Player player = BuildPlayer();
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Unequip, slot: EquipmentSlot.Weapon);

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.False(check.IsAllowed);
        Assert.Contains("slot_already_empty", check.ViolatedConstraints);
    }

    // ---- CheckHardConstraints: Upgrade ----

    [Fact]
    public void CheckHardConstraints_Upgrade_ItemEquipped_IsAllowed()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) }));
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: new ItemId("sword"));

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void CheckHardConstraints_Upgrade_ItemNotEquipped_Violates()
    {
        Player player = BuildPlayer();
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: new ItemId("sword"));

        LoadoutConstraintCheck check = LoadoutPlanner.CheckHardConstraints(candidate, player);

        Assert.False(check.IsAllowed);
        Assert.Contains("item_not_equipped", check.ViolatedConstraints);
    }

    // ---- CountActiveQuestNeedsFor ----

    private static EnrichedQuestObjective BuildEnriched(QuestObjectiveKind kind, ItemId? item, int? current, int? required)
    {
        var objective = new QuestObjective(
            WorldFact<string>.Live("desc", 1d, Now),
            current is { } c ? WorldFact<int>.Live(c, 1d, Now) : WorldFact<int>.Unknown("r", Now),
            required is { } r ? WorldFact<int>.Live(r, 1d, Now) : WorldFact<int>.Unknown("r", Now),
            WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.InProgress, 1d, Now));
        var target = kind switch
        {
            QuestObjectiveKind.Collect => new QuestObjectiveTarget(kind, item: item),
            QuestObjectiveKind.Deliver => new QuestObjectiveTarget(kind, npc: new EntityId("npc-1"), item: item),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new EnrichedQuestObjective(objective, target);
    }

    [Fact]
    public void CountActiveQuestNeedsFor_CollectObjectiveUnsatisfied_CountsIt()
    {
        var item = new ItemId("wolf-pelt");
        var objectives = EquatableArray<EnrichedQuestObjective>.From(new[]
        {
            BuildEnriched(QuestObjectiveKind.Collect, item, current: 2, required: 5)
        });

        int count = LoadoutPlanner.CountActiveQuestNeedsFor(item, objectives);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountActiveQuestNeedsFor_DeliverObjectiveUnsatisfied_CountsIt()
    {
        var item = new ItemId("wolf-pelt");
        var objectives = EquatableArray<EnrichedQuestObjective>.From(new[]
        {
            BuildEnriched(QuestObjectiveKind.Deliver, item, current: 0, required: 1)
        });

        int count = LoadoutPlanner.CountActiveQuestNeedsFor(item, objectives);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountActiveQuestNeedsFor_ObjectiveAlreadySatisfied_DoesNotCount()
    {
        var item = new ItemId("wolf-pelt");
        var objectives = EquatableArray<EnrichedQuestObjective>.From(new[]
        {
            BuildEnriched(QuestObjectiveKind.Collect, item, current: 5, required: 5)
        });

        int count = LoadoutPlanner.CountActiveQuestNeedsFor(item, objectives);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountActiveQuestNeedsFor_DifferentItem_DoesNotCount()
    {
        var objectives = EquatableArray<EnrichedQuestObjective>.From(new[]
        {
            BuildEnriched(QuestObjectiveKind.Collect, new ItemId("other-item"), current: 0, required: 5)
        });

        int count = LoadoutPlanner.CountActiveQuestNeedsFor(new ItemId("wolf-pelt"), objectives);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountActiveQuestNeedsFor_MultipleMatchingObjectives_CountsAll()
    {
        var item = new ItemId("wolf-pelt");
        var objectives = EquatableArray<EnrichedQuestObjective>.From(new[]
        {
            BuildEnriched(QuestObjectiveKind.Collect, item, current: 0, required: 5),
            BuildEnriched(QuestObjectiveKind.Deliver, item, current: 0, required: 1)
        });

        int count = LoadoutPlanner.CountActiveQuestNeedsFor(item, objectives);

        Assert.Equal(2, count);
    }
}
