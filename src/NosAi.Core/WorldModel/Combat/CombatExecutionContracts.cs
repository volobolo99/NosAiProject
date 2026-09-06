namespace NosAi.Core.WorldModel.Combat;

/// <summary>
/// What became of one attempted combat act, observed only through the
/// player's own resources -- never a claim about the target. Mirrors
/// <see cref="Exploration.MovementExecutionResult"/>'s role for AP-04: the
/// real Windows-side chain (AP-05/A2, DeepSeek) is the bridge that produces
/// this from a real before/after <c>ClientMemorySession.TryReadPlayerVitals</c>
/// read -- the same live, already-validated chain
/// <c>NosAi.LiveIntegration.PlayerVitalsProbe</c> (<c>--player-vitals</c>)
/// already reports as <c>[LIVE]</c>
/// (docs/agents/phases/AP-05/AP-05_A1_STATUS.md "Indagine su Gate3Runtime
/// per skill/attacco -- conclusa").
/// </summary>
public enum CombatExecutionResult
{
    /// <summary>
    /// The resource this act's <see cref="CombatActionCandidate.Kind"/> was
    /// expected to spend fell by an amount consistent with the act having
    /// actually executed.
    /// </summary>
    ResourceCostConfirmed = 0,

    /// <summary>
    /// Both readings arrived, but the expected resource did not fall. This
    /// means the act likely did not execute (interrupted, refused by a
    /// cooldown after all, keybind not confirmed) -- it is never read as
    /// "missed the target": nothing about the target is observed here.
    /// </summary>
    NoResourceChangeObserved = 1,

    /// <summary>No pre- or post-act vitals reading arrived before the verification window closed.</summary>
    Unobserved = 2,

    /// <summary>Nothing was emitted: a guard refused the act, or it was abandoned before execution.</summary>
    Aborted = 3
}

/// <summary>
/// Evidence of one attempted combat act, built only from the player's own
/// resources (HP/MP), fed back from execution into the World Model.
/// </summary>
/// <remarks>
/// Deliberately partial, and deliberately says so on every member: this does
/// <b>not</b> confirm the target was hit, damaged, or affected in any way.
/// <c>Mob.Status.Resources</c> (the target's HP in the canonical World
/// Model) is not populated today -- AP-02's entity fusion is blocked on the
/// same OCR/ONNX gap named in <c>AP-02_STATUS.md</c>. What can be observed
/// honestly today is only whether <c>docs/ROADMAP_ESECUTIVA.md</c> S:AP-05's
/// own "execute -> verify" stage had a real resource-cost side effect on the
/// player -- the same "unknown is not zero" boundary
/// <see cref="Exploration.MovementExecutionEvidence"/> draws for movement,
/// applied here to combat.
/// </remarks>
/// <param name="Candidate">The candidate act this evidence is about.</param>
/// <param name="ResourceObserved">
/// Which resource this act's <see cref="CombatActionCandidate.Kind"/> was
/// expected to move (<see cref="ResourceKind.Mana"/> for
/// <see cref="CombatActionKind.UseSkill"/>). <see langword="null"/> when
/// <see cref="CombatActionCandidate.Kind"/> has no player-side resource this
/// contract can check today -- <see cref="CombatActionKind.BasicAttack"/>,
/// <see cref="CombatActionKind.Reposition"/> and
/// <see cref="CombatActionKind.Flee"/> have no known resource cost, and
/// <see cref="CombatActionKind.UseConsumable"/> spends inventory count, not
/// a <see cref="ResourceKind"/> pool -- inventing one here would misrepresent
/// what was actually observed.
/// </param>
/// <param name="Before">The resource fact read immediately before the act was executed.</param>
/// <param name="After">The resource fact read immediately after the verification window.</param>
/// <param name="Result">What was concluded from comparing <see cref="Before"/> and <see cref="After"/>.</param>
/// <param name="Detail">A named reason (a guard refusal, why the resource is unobservable for this kind), or <see langword="null"/> for a plain confirmed cost.</param>
/// <param name="ObservedAtUtc">When this evidence was produced.</param>
public sealed record CombatExecutionEvidence(
    CombatActionCandidate Candidate,
    ResourceKind? ResourceObserved,
    WorldFact<double> Before,
    WorldFact<double> After,
    CombatExecutionResult Result,
    string? Detail,
    DateTime ObservedAtUtc)
{
    /// <summary>True only when the expected resource was observed to fall.</summary>
    public bool ResourceCostConfirmed => Result == CombatExecutionResult.ResourceCostConfirmed;

    /// <summary>The act was never attempted: a guard refused it, or it was abandoned before emission.</summary>
    public static CombatExecutionEvidence NotAttempted(
        CombatActionCandidate candidate,
        string reason,
        DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new CombatExecutionEvidence(
            candidate,
            ResourceObserved: null,
            WorldFact<double>.Unknown(reason, now),
            WorldFact<double>.Unknown(reason, now),
            CombatExecutionResult.Aborted,
            reason,
            now);
    }
}
