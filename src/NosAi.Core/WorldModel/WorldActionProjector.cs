using NosAi.Core.Memory;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;

namespace NosAi.Core.WorldModel;

/// <summary>
/// Bridges the two execution-evidence shapes this project already produces
/// for real -- AP-05's <see cref="CombatExecutionEvidence"/> and AP-04's
/// <see cref="MovementExecutionEvidence"/> -- into a real
/// <see cref="WorldAction"/>. This is the producer AP-09's action-outcome
/// ledger needed and did not have: before this file,
/// <c>new WorldAction(</c> appeared nowhere in <c>src/</c> (see
/// docs/agents/phases/AP-09/AP-09_A1_STATUS.md "AP-09/A2+A4 -- indagine
/// mirata"), so <see cref="ActionOutcomeLedgerEntry.ActionId"/> had no real
/// action to refer to. Pure and stateless: no I/O, no clock reads beyond
/// what the evidence already carries, mirroring every other projector in
/// this project (<c>CombatVerificationProjector</c>,
/// <c>MapGridObservationProjector</c>).
/// </summary>
public static class WorldActionProjector
{
    /// <summary>
    /// One attempted <see cref="CombatActionCandidate"/> as a
    /// <see cref="WorldAction"/>. <see cref="WorldAction.Kind"/> is exactly
    /// <paramref name="candidate"/>'s own <see cref="CombatActionKind"/>
    /// name -- never fabricated, since it is exactly what was actually
    /// attempted (or refused before attempt).
    /// </summary>
    public static WorldAction FromCombat(ActionId id, CombatActionCandidate candidate, DateTime issuedAtUtc, CombatExecutionEvidence evidence) =>
        new(
            id,
            WorldFact<string>.Live(candidate.Kind.ToString(), 1d, issuedAtUtc),
            WorldFact<DateTime>.Live(issuedAtUtc, 1d, issuedAtUtc),
            ProjectCombatOutcome(evidence.Result, evidence.Detail, evidence.ObservedAtUtc));

    /// <summary>
    /// One attempted movement step as a <see cref="WorldAction"/>.
    /// <paramref name="kind"/> is caller-supplied (e.g. "scout-step",
    /// "collect-step") since <see cref="MovementExecutionEvidence"/> itself
    /// carries no named act, only a requested/observed tile.
    /// </summary>
    public static WorldAction FromMovement(ActionId id, string kind, DateTime issuedAtUtc, MovementExecutionEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return new(
            id,
            WorldFact<string>.Live(kind, 1d, issuedAtUtc),
            WorldFact<DateTime>.Live(issuedAtUtc, 1d, issuedAtUtc),
            ProjectMovementOutcome(evidence.Result, evidence.Detail, evidence.ObservedAtUtc));
    }

    /// <summary>
    /// Records an already-projected <see cref="WorldAction"/> into the
    /// ledger for <paramref name="category"/>/<paramref name="context"/>.
    /// <see cref="ActionOutcomeLedgerEntry.Outcome"/> is exactly
    /// <paramref name="action"/>'s own <see cref="WorldAction.Outcome"/>,
    /// never re-derived -- the ledger entry is a record of the action, not a
    /// second, independent judgment of it.
    /// </summary>
    public static ActionOutcomeLedgerEntry ToLedgerEntry(
        WorldAction action,
        MemoryType category,
        string context,
        DateTime recordedAtUtc,
        Guid? entryId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return new ActionOutcomeLedgerEntry(
            entryId ?? Guid.NewGuid(),
            action.Id,
            category,
            action.Outcome,
            context,
            recordedAtUtc);
    }

    /// <summary>
    /// Maps a combat act's own execution result onto whether the act itself
    /// executed -- never a claim about a target being hit, mirroring
    /// <see cref="CombatExecutionEvidence"/>'s own documented boundary.
    /// <see cref="CombatExecutionResult.ResourceCostConfirmed"/>/<see cref="CombatExecutionResult.ResourceGainConfirmed"/>
    /// (a real resource change was observed) become
    /// <see cref="ActionOutcome.Succeeded"/>;
    /// <see cref="CombatExecutionResult.NoResourceChangeObserved"/> (both
    /// readings arrived, the act evidently did not execute) becomes
    /// <see cref="ActionOutcome.Failed"/>;
    /// <see cref="CombatExecutionResult.Unobserved"/>/<see cref="CombatExecutionResult.Aborted"/>
    /// (no verification reading arrived, or nothing was ever emitted) become
    /// <see cref="WorldFact{T}.Unknown"/> -- never guessed into either
    /// settled outcome.
    /// </summary>
    private static WorldFact<ActionOutcome> ProjectCombatOutcome(CombatExecutionResult result, string? detail, DateTime observedAtUtc) =>
        result switch
        {
            CombatExecutionResult.ResourceCostConfirmed or CombatExecutionResult.ResourceGainConfirmed =>
                WorldFact<ActionOutcome>.Live(ActionOutcome.Succeeded, 1d, observedAtUtc),
            CombatExecutionResult.NoResourceChangeObserved =>
                WorldFact<ActionOutcome>.Live(ActionOutcome.Failed, 1d, observedAtUtc),
            CombatExecutionResult.Unobserved or CombatExecutionResult.Aborted =>
                WorldFact<ActionOutcome>.Unknown(detail ?? "combat_execution_result_not_observed", observedAtUtc),
            _ => WorldFact<ActionOutcome>.Unknown("unrecognized_combat_execution_result", observedAtUtc)
        };

    /// <summary>
    /// Maps a movement step's own execution result onto whether the
    /// specific requested tile was reached.
    /// <see cref="MovementExecutionResult.Succeeded"/> becomes
    /// <see cref="ActionOutcome.Succeeded"/>;
    /// <see cref="MovementExecutionResult.Stalled"/>/<see cref="MovementExecutionResult.Displaced"/>
    /// (the character was observed and confirmed not on the requested tile)
    /// become <see cref="ActionOutcome.Failed"/> -- that specific step did
    /// not reach where it asked to, even if <c>Displaced</c> means some
    /// other movement happened; <see cref="MovementExecutionResult.Unobserved"/>/<see cref="MovementExecutionResult.Aborted"/>
    /// become <see cref="WorldFact{T}.Unknown"/>.
    /// </summary>
    private static WorldFact<ActionOutcome> ProjectMovementOutcome(MovementExecutionResult result, string? detail, DateTime observedAtUtc) =>
        result switch
        {
            MovementExecutionResult.Succeeded =>
                WorldFact<ActionOutcome>.Live(ActionOutcome.Succeeded, 1d, observedAtUtc),
            MovementExecutionResult.Stalled or MovementExecutionResult.Displaced =>
                WorldFact<ActionOutcome>.Live(ActionOutcome.Failed, 1d, observedAtUtc),
            MovementExecutionResult.Unobserved or MovementExecutionResult.Aborted =>
                WorldFact<ActionOutcome>.Unknown(detail ?? "movement_execution_result_not_observed", observedAtUtc),
            _ => WorldFact<ActionOutcome>.Unknown("unrecognized_movement_execution_result", observedAtUtc)
        };
}
