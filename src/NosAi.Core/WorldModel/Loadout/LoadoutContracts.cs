namespace NosAi.Core.WorldModel.Loadout;

/// <summary>
/// What a <see cref="LoadoutActionCandidate"/> does to a <see cref="Player"/>'s
/// equipment (docs/ROADMAP_ESECUTIVA.md S:AP-07: "equip/upgrade/inventory
/// actions sono motivate, autorizzate e verificate").
/// </summary>
public enum LoadoutActionKind
{
    /// <summary>Move an <see cref="InventoryItem"/> into an <see cref="EquipmentSlot"/>. Needs <see cref="LoadoutActionCandidate.Item"/> and <see cref="LoadoutActionCandidate.Slot"/>.</summary>
    Equip = 0,

    /// <summary>Remove whatever occupies an <see cref="EquipmentSlot"/>. Needs <see cref="LoadoutActionCandidate.Slot"/> only.</summary>
    Unequip = 1,

    /// <summary>Improve a currently equipped item in place (enchant/refine/upgrade). Needs <see cref="LoadoutActionCandidate.Item"/> only.</summary>
    Upgrade = 2
}

/// <summary>
/// One candidate loadout act, before hard constraints have judged it. Which
/// fields are required depends on <see cref="Kind"/>; the constructor
/// enforces exactly the combination that kind needs, the same discipline
/// <see cref="Combat.CombatActionCandidate"/> already applies for
/// <see cref="Combat.CombatActionKind"/>.
/// </summary>
public sealed record LoadoutActionCandidate
{
    public LoadoutActionKind Kind { get; init; }
    public ItemId? Item { get; init; }
    public EquipmentSlot? Slot { get; init; }

    public LoadoutActionCandidate(LoadoutActionKind kind, ItemId? item = null, EquipmentSlot? slot = null)
    {
        switch (kind)
        {
            case LoadoutActionKind.Equip:
                RequirePresent(item, nameof(item), kind);
                RequirePresent(slot, nameof(slot), kind);
                break;

            case LoadoutActionKind.Unequip:
                RequireAbsent(item, nameof(item), kind);
                RequirePresent(slot, nameof(slot), kind);
                break;

            case LoadoutActionKind.Upgrade:
                RequirePresent(item, nameof(item), kind);
                RequireAbsent(slot, nameof(slot), kind);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown LoadoutActionKind.");
        }

        Kind = kind;
        Item = item;
        Slot = slot;
    }

    private static void RequirePresent<T>(T? value, string parameterName, LoadoutActionKind kind) where T : struct
    {
        if (value is null)
            throw new ArgumentException($"{parameterName} is required for {kind} candidates.", parameterName);
    }

    private static void RequireAbsent<T>(T? value, string parameterName, LoadoutActionKind kind) where T : struct
    {
        if (value is not null)
            throw new ArgumentException($"{parameterName} must not be set for {kind} candidates.", parameterName);
    }
}

/// <summary>
/// Whether one <see cref="LoadoutActionCandidate"/> survives the hard
/// constraints stage: item availability, slot occupancy. A tactical
/// pre-filter, distinct from any downstream Guard/Trust/Safety
/// authorization the act still needs before execution -- same principle
/// as <see cref="Combat.CombatConstraintCheck"/>.
/// </summary>
public sealed record LoadoutConstraintCheck(
    LoadoutActionCandidate Candidate,
    bool IsAllowed,
    EquatableArray<string> ViolatedConstraints)
{
    /// <summary>Every hard constraint passed.</summary>
    public static LoadoutConstraintCheck Allowed(LoadoutActionCandidate candidate) =>
        new(candidate, true, EquatableArray<string>.Empty);

    /// <summary>At least one hard constraint failed; <paramref name="reasons"/> must not be empty.</summary>
    public static LoadoutConstraintCheck Violated(LoadoutActionCandidate candidate, EquatableArray<string> reasons)
    {
        if (reasons.Count == 0)
        {
            throw new ArgumentException(
                "A violated constraint check must name at least one reason.", nameof(reasons));
        }

        return new LoadoutConstraintCheck(candidate, false, reasons);
    }
}

/// <summary>
/// The raw build-evaluation inputs for one already-constraint-passed
/// <see cref="LoadoutActionCandidate"/> (docs/ROADMAP_ESECUTIVA.md S:AP-07:
/// "valuta statistiche, DPS, survivability, resource efficiency, sinergie,
/// enemy-specific performance, movement/utility, costo upgrade, opportunity
/// cost e quest relevance"). Every field here is a raw proxy, not a
/// normalized score -- same "raw inputs only" contract as
/// <see cref="Combat.CombatSimulationResult"/> and
/// <see cref="Exploration.FrontierCandidate"/>; scoring/ranking candidates
/// against each other is a future AP-07/A3 algorithm's job, not this
/// type's.
/// </summary>
/// <remarks>
/// AP-07/A1 defines this shape without populating most of it: real
/// per-item stats (attack/defense/element, upgrade material cost) exist
/// nowhere in this repository today -- <see cref="InventoryItem"/>/
/// <see cref="EquipmentItem"/> (AP-01) carry only identity/quantity/slot,
/// no combat statistics, and the one real game-data catalogue
/// (<c>GameReferenceDatabase</c>) explicitly declines to semantically
/// decode the fields that would provide them (same finding already made
/// for skill data in AP-05's Gate3Runtime investigation,
/// docs/agents/phases/AP-05/AP-05_A1_STATUS.md). Fabricating those numbers
/// here would be exactly the kind of simulated-data-as-real this project
/// forbids. <see cref="QuestRelevance"/> is the one dimension this phase's
/// A3 <i>does</i> compute honestly, by cross-referencing AP-06's Quest
/// Graph (<c>LoadoutPlanner.CountActiveQuestNeedsFor</c>) rather than
/// estimating it.
/// </remarks>
public sealed record LoadoutEvaluation(
    LoadoutActionCandidate Candidate,
    double EstimatedDps,
    double EstimatedSurvivability,
    double ResourceEfficiency,
    double SynergyScore,
    double EnemySpecificPerformance,
    double MovementUtility,
    double UpgradeCost,
    double OpportunityCost,
    double QuestRelevance);
