using NosAi.Core.WorldModel.Quests;

namespace NosAi.Core.WorldModel.Loadout;

/// <summary>
/// Turns the current <see cref="Player"/> equipment/inventory state into
/// candidate loadout acts and filters them against hard constraints. Pure
/// and stateless, mirroring
/// <c>NosAi.Core.WorldModel.Combat.CombatPlanner</c>: no I/O, no clock
/// reads, safe to call once per decision cycle.
/// </summary>
/// <remarks>
/// <b>Scope, honestly restricted.</b> <see cref="GenerateUnequipCandidates"/>
/// and <see cref="GenerateUpgradeCandidates"/> need only facts
/// <see cref="Player.Equipment"/> already carries.
/// <see cref="GenerateEquipCandidates"/> needs one more: which
/// <see cref="EquipmentSlot"/> an <see cref="InventoryItem"/> would occupy
/// if equipped, a fact <see cref="InventoryItem"/> (AP-01) itself never
/// carries. This method does not fabricate that mapping -- it takes it as
/// a caller-supplied lookup, the same pattern
/// <see cref="Strategy.StrategyPlanner.AssessRecoveryUrgency"/> uses for
/// its "currently in combat" fact: pure here, real data sourced by
/// whoever calls in. <c>NosAi.Runtime.GameData.ItemReferenceDecoder</c>
/// decodes a real item-catalog slot from <c>Item.dat</c> today (see its
/// own remarks for provenance) and is the obvious real lookup to wire in,
/// but this method takes any delegate -- it is not this method's job to
/// know where the answer came from, only to use it correctly once given.
/// </remarks>
public static class LoadoutPlanner
{
    /// <summary>One <see cref="LoadoutActionKind.Unequip"/> candidate per currently equipped slot.</summary>
    public static IReadOnlyList<LoadoutActionCandidate> GenerateUnequipCandidates(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var candidates = new List<LoadoutActionCandidate>();
        foreach (EquipmentItem item in player.Equipment)
        {
            if (item.IsEquipped is { HasValue: true, Value: true })
                candidates.Add(new LoadoutActionCandidate(LoadoutActionKind.Unequip, slot: item.Slot));
        }

        return candidates;
    }

    /// <summary>
    /// One <see cref="LoadoutActionKind.Equip"/> candidate per inventory
    /// stack <paramref name="resolveSlot"/> can place: an item this project
    /// has no real catalog answer for (an unrecognized vnum, a lookup that
    /// legitimately returns <see langword="null"/>) produces no candidate at
    /// all, never a guessed one. Whether the target slot is actually free is
    /// <see cref="CheckHardConstraints"/>'s job, not this method's -- the
    /// same division of labour <see cref="GenerateUnequipCandidates"/> and
    /// <see cref="GenerateUpgradeCandidates"/> already use.
    /// </summary>
    public static IReadOnlyList<LoadoutActionCandidate> GenerateEquipCandidates(
        Player player, Func<ItemId, EquipmentSlot?> resolveSlot)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(resolveSlot);

        var candidates = new List<LoadoutActionCandidate>();
        foreach (InventoryItem stack in player.Inventory)
        {
            if (stack.Quantity is not { HasValue: true, Value: > 0 })
                continue;

            if (resolveSlot(stack.Id) is { } slot)
                candidates.Add(new LoadoutActionCandidate(LoadoutActionKind.Equip, item: stack.Id, slot: slot));
        }

        return candidates;
    }

    /// <summary>One <see cref="LoadoutActionKind.Upgrade"/> candidate per currently equipped item.</summary>
    public static IReadOnlyList<LoadoutActionCandidate> GenerateUpgradeCandidates(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var candidates = new List<LoadoutActionCandidate>();
        foreach (EquipmentItem item in player.Equipment)
        {
            if (item.IsEquipped is { HasValue: true, Value: true })
                candidates.Add(new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: item.Id));
        }

        return candidates;
    }

    /// <summary>
    /// The hard-constraint stage: item availability (Equip), slot occupancy
    /// (Equip/Unequip), item currently equipped (Upgrade) -- all from
    /// already-known facts only. Does <b>not</b> check an upgrade's
    /// material/currency cost -- no such data exists on
    /// <see cref="EquipmentItem"/>; do not add a fabricated check here.
    /// </summary>
    public static LoadoutConstraintCheck CheckHardConstraints(LoadoutActionCandidate candidate, Player player)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);

        var violations = new List<string>();

        switch (candidate.Kind)
        {
            case LoadoutActionKind.Equip:
                CheckItemAvailable(candidate.Item!.Value, player.Inventory, violations);
                CheckSlotOccupancy(candidate.Slot!.Value, player.Equipment, violations, requireOccupied: false);
                break;

            case LoadoutActionKind.Unequip:
                CheckSlotOccupancy(candidate.Slot!.Value, player.Equipment, violations, requireOccupied: true);
                break;

            case LoadoutActionKind.Upgrade:
                CheckItemEquipped(candidate.Item!.Value, player.Equipment, violations);
                break;
        }

        return violations.Count == 0
            ? LoadoutConstraintCheck.Allowed(candidate)
            : LoadoutConstraintCheck.Violated(candidate, EquatableArray<string>.From(violations));
    }

    /// <summary>
    /// How many not-yet-satisfied objectives of known quests need
    /// <paramref name="item"/> (a <see cref="QuestObjectiveKind.Collect"/>
    /// or <see cref="QuestObjectiveKind.Deliver"/> target) -- a real,
    /// honest input for <see cref="LoadoutEvaluation.QuestRelevance"/>,
    /// computed by cross-referencing AP-06's Quest Graph rather than
    /// estimated.
    /// </summary>
    public static int CountActiveQuestNeedsFor(ItemId item, EquatableArray<EnrichedQuestObjective> objectives)
    {
        int count = 0;
        foreach (EnrichedQuestObjective enriched in objectives)
        {
            bool targetsThisItem = enriched.Target.Item is { } targetItem && targetItem.Equals(item);
            bool isCollectOrDeliver = enriched.Target.Kind is QuestObjectiveKind.Collect or QuestObjectiveKind.Deliver;

            if (targetsThisItem && isCollectOrDeliver && !QuestGraphPlanner.IsObjectiveSatisfied(enriched.Objective))
                count++;
        }

        return count;
    }

    private static void CheckItemAvailable(ItemId item, EquatableArray<InventoryItem> inventory, List<string> violations)
    {
        foreach (InventoryItem stack in inventory)
        {
            if (stack.Id.Equals(item) && stack.Quantity is { HasValue: true, Value: > 0 })
                return;
        }

        violations.Add("item_not_in_inventory");
    }

    private static void CheckSlotOccupancy(
        EquipmentSlot slot,
        EquatableArray<EquipmentItem> equipment,
        List<string> violations,
        bool requireOccupied)
    {
        bool occupied = false;
        foreach (EquipmentItem item in equipment)
        {
            if (item.Slot == slot && item.IsEquipped is { HasValue: true, Value: true })
            {
                occupied = true;
                break;
            }
        }

        if (requireOccupied && !occupied)
            violations.Add("slot_already_empty");
        else if (!requireOccupied && occupied)
            violations.Add("slot_already_occupied");
    }

    private static void CheckItemEquipped(ItemId item, EquatableArray<EquipmentItem> equipment, List<string> violations)
    {
        foreach (EquipmentItem equipped in equipment)
        {
            if (equipped.Id.Equals(item) && equipped.IsEquipped is { HasValue: true, Value: true })
                return;
        }

        violations.Add("item_not_equipped");
    }
}
