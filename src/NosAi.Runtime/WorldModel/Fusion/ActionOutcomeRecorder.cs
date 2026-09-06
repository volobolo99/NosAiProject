using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Storage;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Records one already-executed act's evidence into the durable
/// action-outcome ledger, via
/// <see cref="NosAi.Core.WorldModel.WorldActionProjector"/> and
/// <see cref="ActionOutcomeLedgerStore"/>. A <see langword="null"/>
/// <paramref name="store"/> is a documented no-op: ledger recording is
/// opportunistic history, never a reason to fail a command whose actual
/// act already ran and was already verified before this call runs.
/// </summary>
public static class ActionOutcomeRecorder
{
    /// <summary>
    /// Records one combat act (a <c>--engage</c>/<c>--recover</c> round, or
    /// the same round dispatched by <c>--autoplay</c>). Every mapping is
    /// delegated to <see cref="WorldActionProjector"/> -- this type never
    /// re-implements it.
    /// </summary>
    public static void RecordCombat(
        ActionOutcomeLedgerStore? store,
        ActionId id,
        CombatActionCandidate candidate,
        DateTime issuedAtUtc,
        CombatExecutionEvidence evidence,
        MemoryType category,
        string context,
        DateTime recordedAtUtc)
    {
        if (store is null) return;

        WorldAction action = NosAi.Core.WorldModel.WorldActionProjector.FromCombat(id, candidate, issuedAtUtc, evidence);
        store.Append(NosAi.Core.WorldModel.WorldActionProjector.ToLedgerEntry(action, category, context, recordedAtUtc));
    }

    /// <summary>
    /// Records one movement step (a <c>--scout</c> emitted step, or the same
    /// round dispatched by <c>--autoplay</c>). Every mapping is delegated to
    /// <see cref="WorldActionProjector"/> -- this type never re-implements it.
    /// </summary>
    public static void RecordMovement(
        ActionOutcomeLedgerStore? store,
        ActionId id,
        string kind,
        DateTime issuedAtUtc,
        MovementExecutionEvidence evidence,
        MemoryType category,
        string context,
        DateTime recordedAtUtc)
    {
        if (store is null) return;

        WorldAction action = NosAi.Core.WorldModel.WorldActionProjector.FromMovement(id, kind, issuedAtUtc, evidence);
        store.Append(NosAi.Core.WorldModel.WorldActionProjector.ToLedgerEntry(action, category, context, recordedAtUtc));
    }
}
