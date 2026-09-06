using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Pure, mechanical bridge from a real player-vitals reading
/// (<c>NosAi.LiveIntegration.PlayerVitalsReading</c>, produced by the live
/// memory chain <c>ClientMemorySession.TryReadPlayerVitals</c> -- the same
/// chain <c>--player-vitals</c> already reports as <c>[LIVE]</c>) into the
/// canonical combat-evidence contract
/// (<c>NosAi.Core.WorldModel.Combat.CombatExecutionEvidence</c>, AP-05/A1) --
/// the same role AP-04/A2's <see cref="MovementVerificationProjector"/> plays
/// for movement, and the reason <c>NosAi.Core</c> never references
/// <c>NosAi.Runtime</c>: this projection is where the two meet.
/// </summary>
/// <remarks>
/// <b>Deliberately partial, and the contract says so on every member.</b>
/// This projector can only answer whether the expected resource (mana for
/// <see cref="CombatActionKind.UseSkill"/>) fell on the player between a
/// before- and after-read. It never confirms the target was hit, damaged or
/// affected: <c>Mob.Status.Resources</c> is not populated anywhere today
/// (AP-02's entity fusion is blocked on the same OCR/ONNX gap). A kind with
/// no observable player-side resource pool
/// (<see cref="CombatActionKind.BasicAttack"/>/<see cref="CombatActionKind.Reposition"/>/
/// <see cref="CombatActionKind.Flee"/>, and inventory-spending
/// <see cref="CombatActionKind.UseConsumable"/>) projects to
/// <see cref="CombatExecutionResult.Unobserved"/> with a named reason --
/// never an invented cost.
/// </remarks>
public static class CombatVerificationProjector
{
    /// <summary>Recorded when the candidate's kind has no observable player-side resource pool.</summary>
    public const string ResourceNotObservableReason = "resource_kind_not_observable_for_kind";

    /// <summary>Recorded when either side of the before/after pair is missing.</summary>
    public const string VitalsNotObservedReason = "vitals_not_observed";

    /// <summary>
    /// Which <see cref="ResourceKind"/> a <see cref="CombatActionKind"/> is
    /// expected to spend. <see cref="ResourceKind.Mana"/> for
    /// <see cref="CombatActionKind.UseSkill"/>; <see langword="null"/> for every
    /// other kind -- see AP-05/A2+A4's scope: BasicAttack/Reposition/Flee have
    /// no known resource cost and UseConsumable spends inventory count, not a
    /// <see cref="ResourceKind"/> pool.
    /// </summary>
    public static ResourceKind? ExpectedResource(CombatActionKind kind) => kind switch
    {
        CombatActionKind.UseSkill => ResourceKind.Mana,
        _ => null
    };

    /// <summary>
    /// Projects one act's before/after vitals into canonical combat evidence.
    /// </summary>
    /// <param name="candidate">The act that was attempted.</param>
    /// <param name="before">Vitals read immediately before the act, or <see langword="null"/> when none arrived.</param>
    /// <param name="after">Vitals read after the verification window, or <see langword="null"/> when none arrived.</param>
    /// <param name="observedAtUtc">The instant this evidence is produced, stamped on every fact it creates.</param>
    public static CombatExecutionEvidence Project(
        CombatActionCandidate candidate,
        PlayerVitalsReading? before,
        PlayerVitalsReading? after,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        ResourceKind? resource = ExpectedResource(candidate.Kind);
        if (resource is null)
        {
            return new CombatExecutionEvidence(
                candidate, null,
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                CombatExecutionResult.Unobserved, ResourceNotObservableReason, observedAtUtc);
        }

        if (before is not { } b || after is not { } a)
        {
            return new CombatExecutionEvidence(
                candidate, resource,
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                CombatExecutionResult.Unobserved, VitalsNotObservedReason, observedAtUtc);
        }

        double beforeValue = ResourceValue(b, resource.Value);
        double afterValue = ResourceValue(a, resource.Value);
        WorldFact<double> beforeFact = WorldFact<double>.Live(beforeValue, confidence: 1d, observedAtUtc);
        WorldFact<double> afterFact = WorldFact<double>.Live(afterValue, confidence: 1d, observedAtUtc);

        CombatExecutionResult result = afterValue < beforeValue
            ? CombatExecutionResult.ResourceCostConfirmed
            : CombatExecutionResult.NoResourceChangeObserved;

        return new CombatExecutionEvidence(candidate, resource, beforeFact, afterFact, result, null, observedAtUtc);
    }

    /// <summary>
    /// Projects one <see cref="CombatActionKind.UseConsumable"/> act's
    /// before/after Health into recovery evidence (AP-08's Survival response
    /// executed as the operator command <c>--recover &lt;slot&gt;</c>). Unlike
    /// <see cref="Project"/>, the tracked resource is not inferred from
    /// <paramref name="candidate"/>'s Kind (<see cref="CombatActionKind.UseConsumable"/>
    /// has no general answer -- see this type's own remarks): it is always
    /// <see cref="ResourceKind.Health"/>, because the caller (an operator naming
    /// a slot as a recovery item) is the one asserting that. A slot that does
    /// not actually heal reports
    /// <see cref="CombatExecutionResult.NoResourceChangeObserved"/> honestly,
    /// never a fabricated gain.
    /// </summary>
    /// <param name="candidate">
    /// Must be <see cref="CombatActionKind.UseConsumable"/>; any other Kind
    /// returns <see cref="CombatExecutionResult.Unobserved"/> with
    /// <see cref="ResourceNotObservableReason"/>, the same sweep <see cref="Project"/>
    /// applies to the kinds it cannot track.
    /// </param>
    /// <param name="before">Vitals read immediately before the press, or <see langword="null"/> when none arrived.</param>
    /// <param name="after">Vitals read after the verification window, or <see langword="null"/> when none arrived.</param>
    /// <param name="observedAtUtc">The instant this evidence is produced, stamped on every fact it creates.</param>
    public static CombatExecutionEvidence ProjectRecovery(
        CombatActionCandidate candidate,
        PlayerVitalsReading? before,
        PlayerVitalsReading? after,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Kind != CombatActionKind.UseConsumable)
        {
            return new CombatExecutionEvidence(
                candidate, null,
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                CombatExecutionResult.Unobserved, ResourceNotObservableReason, observedAtUtc);
        }

        if (before is not { } b || after is not { } a)
        {
            return new CombatExecutionEvidence(
                candidate, ResourceKind.Health,
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                CombatExecutionResult.Unobserved, VitalsNotObservedReason, observedAtUtc);
        }

        WorldFact<double> beforeFact = WorldFact<double>.Live(b.Hp, confidence: 1d, observedAtUtc);
        WorldFact<double> afterFact = WorldFact<double>.Live(a.Hp, confidence: 1d, observedAtUtc);

        // A rise confirms the recovery act; an equal or lower Health is no gain.
        // Equal counts as no gain on purpose, the same "fails toward
        // under-claiming" discipline Project's cost check uses.
        CombatExecutionResult result = a.Hp > b.Hp
            ? CombatExecutionResult.ResourceGainConfirmed
            : CombatExecutionResult.NoResourceChangeObserved;

        return new CombatExecutionEvidence(candidate, ResourceKind.Health, beforeFact, afterFact, result, null, observedAtUtc);
    }

    private static double ResourceValue(PlayerVitalsReading reading, ResourceKind kind) => kind switch
    {
        ResourceKind.Mana => reading.Mp,
        ResourceKind.Health => reading.Hp,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            "CombatVerificationProjector only reads Health/Mana off PlayerVitalsReading; extend ResourceValue before using another ResourceKind.")
    };
}
