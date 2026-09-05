namespace NosAi.Core.WorldModel.Combat;

/// <summary>
/// What kind of act a <see cref="CombatActionCandidate"/> represents
/// (docs/ROADMAP_ESECUTIVA.md S:AP-05: "Candidate generation -> hard
/// constraints -> short-horizon simulation -> utility/risk -> combo prefix
/// -> execute -> verify -> learn"). Deliberately its own enum, not a reuse
/// of <c>NosAi.Runtime.Contracts.ActionType</c> (the pre-existing Gate 1-6
/// action type, which also covers non-combat acts like
/// <c>CollectGroundItem</c> and is owned by a different, not-yet-reconciled
/// system) -- AP-05/A2 is the future bridge between the two, not this type.
/// </summary>
public enum CombatActionKind
{
    /// <summary>The default, unarmed/weapon attack. Requires <see cref="CombatActionCandidate.Target"/>.</summary>
    BasicAttack = 0,

    /// <summary>Casting one <see cref="Skill"/>. Requires <see cref="CombatActionCandidate.Skill"/>; <see cref="CombatActionCandidate.Target"/> is optional (self/AoE skills have none).</summary>
    UseSkill = 1,

    /// <summary>Consuming one item on self (a potion, a buff scroll). Requires <see cref="CombatActionCandidate.Item"/>.</summary>
    UseConsumable = 2,

    /// <summary>Moving to a better position without disengaging. Requires <see cref="CombatActionCandidate.Destination"/>.</summary>
    Reposition = 3,

    /// <summary>Disengaging from the fight. <see cref="CombatActionCandidate.Destination"/> is optional -- the exact escape route is an execution-time decision, not this candidate's.</summary>
    Flee = 4
}

/// <summary>
/// One candidate combat act, before hard constraints or simulation have
/// judged it (docs/ROADMAP_ESECUTIVA.md S:AP-05's "Candidate generation"
/// stage). Which fields are required depends on <see cref="Kind"/>; the
/// constructor enforces exactly the combination that kind needs, the same
/// discipline <see cref="Resource"/>'s constructor already applies to
/// <see cref="ResourceKind.Custom"/>.
/// </summary>
public sealed record CombatActionCandidate
{
    public CombatActionKind Kind { get; init; }
    public EntityId? Target { get; init; }
    public SkillId? Skill { get; init; }
    public ItemId? Item { get; init; }
    public WorldPosition? Destination { get; init; }

    public CombatActionCandidate(
        CombatActionKind kind,
        EntityId? target = null,
        SkillId? skill = null,
        ItemId? item = null,
        WorldPosition? destination = null)
    {
        switch (kind)
        {
            case CombatActionKind.BasicAttack:
                RequirePresent(target, nameof(target), kind);
                RequireAbsent(skill, nameof(skill), kind);
                RequireAbsent(item, nameof(item), kind);
                RequireAbsent(destination, nameof(destination), kind);
                break;

            case CombatActionKind.UseSkill:
                RequirePresent(skill, nameof(skill), kind);
                RequireAbsent(item, nameof(item), kind);
                RequireAbsent(destination, nameof(destination), kind);
                break;

            case CombatActionKind.UseConsumable:
                RequirePresent(item, nameof(item), kind);
                RequireAbsent(target, nameof(target), kind);
                RequireAbsent(skill, nameof(skill), kind);
                RequireAbsent(destination, nameof(destination), kind);
                break;

            case CombatActionKind.Reposition:
                RequirePresent(destination, nameof(destination), kind);
                RequireAbsent(target, nameof(target), kind);
                RequireAbsent(skill, nameof(skill), kind);
                RequireAbsent(item, nameof(item), kind);
                break;

            case CombatActionKind.Flee:
                RequireAbsent(target, nameof(target), kind);
                RequireAbsent(skill, nameof(skill), kind);
                RequireAbsent(item, nameof(item), kind);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown CombatActionKind.");
        }

        Kind = kind;
        Target = target;
        Skill = skill;
        Item = item;
        Destination = destination;
    }

    private static void RequirePresent<T>(T? value, string parameterName, CombatActionKind kind) where T : struct
    {
        if (value is null)
        {
            throw new ArgumentException(
                $"{parameterName} is required for {kind} candidates.", parameterName);
        }
    }

    private static void RequireAbsent<T>(T? value, string parameterName, CombatActionKind kind) where T : struct
    {
        if (value is not null)
        {
            throw new ArgumentException(
                $"{parameterName} must not be set for {kind} candidates.", parameterName);
        }
    }
}

/// <summary>
/// Whether one <see cref="CombatActionCandidate"/> survives the "hard
/// constraints" stage (docs/ROADMAP_ESECUTIVA.md S:AP-05) -- cooldown,
/// resource cost, range, target validity. Distinct from the runtime's own
/// <c>GuardPolicyEngine</c>/Safety Gate, which judges an already-selected
/// act just before execution: this is an earlier, purely tactical filter
/// that keeps obviously-unusable candidates out of the short-horizon
/// simulation stage, not a safety/authorization decision.
/// </summary>
/// <param name="Candidate">The candidate this check is about.</param>
/// <param name="IsAllowed">Whether every constraint passed.</param>
/// <param name="ViolatedConstraints">Named reasons, empty exactly when <see cref="IsAllowed"/> is <see langword="true"/>.</param>
public sealed record CombatConstraintCheck(
    CombatActionCandidate Candidate,
    bool IsAllowed,
    EquatableArray<string> ViolatedConstraints)
{
    /// <summary>Every hard constraint passed.</summary>
    public static CombatConstraintCheck Allowed(CombatActionCandidate candidate) =>
        new(candidate, true, EquatableArray<string>.Empty);

    /// <summary>At least one hard constraint failed; <paramref name="reasons"/> must not be empty.</summary>
    public static CombatConstraintCheck Violated(CombatActionCandidate candidate, EquatableArray<string> reasons)
    {
        if (reasons.Count == 0)
        {
            throw new ArgumentException(
                "A violated constraint check must name at least one reason.", nameof(reasons));
        }

        return new CombatConstraintCheck(candidate, false, reasons);
    }
}

/// <summary>
/// The short-horizon simulation's raw predicted inputs for one
/// already-constraint-passed <see cref="CombatActionCandidate"/>
/// (docs/ROADMAP_ESECUTIVA.md S:AP-05's "short-horizon simulation" stage,
/// feeding its "utility/risk" stage). Every field here is a raw proxy, not
/// a normalized score -- ranking/scoring candidates against each other is
/// AP-05/A3's algorithm, mirroring <see cref="Exploration.FrontierCandidate"/>'s
/// same "raw inputs only" contract in AP-04.
/// </summary>
/// <param name="Candidate">The candidate this prediction is about.</param>
/// <param name="PredictedDamageDealt">Non-negative. Feeds DPS/time-to-kill.</param>
/// <param name="PredictedDamageTaken">Non-negative. Feeds survivability.</param>
/// <param name="PredictedTimeToExecute">How long this act is predicted to take, cast time included.</param>
/// <param name="PredictedResourceCost">Non-negative. E.g. mana/stamina/item consumed.</param>
/// <param name="PredictedCrowdRisk">Non-negative proxy for how many additional hostiles this act is predicted to draw in or expose the player to.</param>
/// <param name="PredictedEscapeProbability">In [0, 1]: how likely disengaging remains viable after this act.</param>
/// <param name="MissionRelevance">Signed proxy for how much this act serves the current strategic goal; zero when combat has no active mission tie-in.</param>
/// <param name="PredictedPositionAfter">Where the player is predicted to be after this act, when the act changes position (<see cref="CombatActionKind.Reposition"/>/<see cref="CombatActionKind.Flee"/>); <see langword="null"/> otherwise.</param>
public sealed record CombatSimulationResult(
    CombatActionCandidate Candidate,
    double PredictedDamageDealt,
    double PredictedDamageTaken,
    TimeSpan PredictedTimeToExecute,
    double PredictedResourceCost,
    double PredictedCrowdRisk,
    double PredictedEscapeProbability,
    double MissionRelevance,
    WorldPosition? PredictedPositionAfter);

/// <summary>
/// One act within a <see cref="ComboPlan"/>: the act itself, and how long
/// to wait before the next one is due (cast time plus the animation/global
/// cooldown it is predicted to occupy).
/// </summary>
public sealed record ComboStep(
    CombatActionCandidate Action,
    TimeSpan ExpectedDelayBeforeNext);

/// <summary>
/// A short, ordered sequence of combat acts chosen as one unit
/// (docs/ROADMAP_ESECUTIVA.md S:AP-05's "combo prefix" stage) rather than
/// re-evaluating one act at a time -- e.g. a stun followed by a burst skill
/// while the stun holds. Execution still verifies and can abandon partway
/// through (that is AP-05/A4's job, bridging to the already-real
/// Guard/Trust/Safety/Execute chain); this contract only names the
/// intended order.
/// </summary>
/// <param name="Steps">The sequence, in intended execution order. Empty exactly when <see cref="IsViable"/> does not have a value or is <see langword="false"/>.</param>
/// <param name="IsViable">Whether a usable combo was actually found this cycle.</param>
/// <param name="ObservedAtUtc">When this plan was produced.</param>
public sealed record ComboPlan(
    EquatableArray<ComboStep> Steps,
    WorldFact<bool> IsViable,
    DateTime ObservedAtUtc)
{
    /// <summary>No usable combo was found, or none has been attempted yet.</summary>
    public static ComboPlan Unviable(string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new ComboPlan(
            EquatableArray<ComboStep>.Empty,
            WorldFact<bool>.Unknown(reason, now),
            now);
    }
}
