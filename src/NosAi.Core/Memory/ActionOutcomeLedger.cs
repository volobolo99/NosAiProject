using NosAi.Core.WorldModel;

namespace NosAi.Core.Memory;

/// <summary>
/// One recorded (action, outcome) pairing (docs/ROADMAP_ESECUTIVA.md
/// S:AP-09: "Action-outcome ledger"), the fact this project's local
/// deterministic simulation is built from. Bridges <see cref="WorldAction"/>
/// (AP-01, the World Model's own record of "what happened") into memory,
/// tagged with which <see cref="MemoryType"/> category it belongs to and
/// a free-text <see cref="Context"/> key -- the same key
/// <see cref="LocalOutcomeSimulator.Predict"/> groups entries by.
/// </summary>
/// <param name="EntryId">Identity of this ledger entry, distinct from <see cref="ActionId"/> -- the same action could in principle be recorded more than once (a correction, a re-derivation).</param>
/// <param name="ActionId">Which <see cref="WorldAction"/> this entry is about.</param>
/// <param name="Category">Which memory category this entry belongs to.</param>
/// <param name="Outcome">What became of the action.</param>
/// <param name="Context">
/// A stable key identifying "this kind of situation" (e.g. a skill id, a
/// quest objective kind, a mob species) -- deliberately a caller-chosen
/// string rather than a typed union, since the ledger is meant to record
/// history for every domain's action, not just one. Grouping entries by
/// an identical <see cref="Context"/> is what makes a prediction about
/// them meaningful.
/// </param>
/// <param name="RecordedAtUtc">When this entry was written to the ledger.</param>
public sealed record ActionOutcomeLedgerEntry(
    Guid EntryId,
    ActionId ActionId,
    MemoryType Category,
    ActionOutcome Outcome,
    string Context,
    DateTime RecordedAtUtc);

/// <summary>
/// A purely empirical prediction for one <see cref="ActionOutcomeLedgerEntry.Context"/>:
/// how the same context resolved every other time it was tried. Not a
/// probability model or a Bayesian estimate -- a plain frequency count
/// over real recorded history, which is what "deterministic" means here
/// (docs/ROADMAP_ESECUTIVA.md S:AP-09: "Simulazione deterministica locale
/// per conseguenze a breve termine"): the same ledger always yields the
/// same prediction, and an empty ledger honestly yields no rate to trust
/// rather than a guessed one.
/// </summary>
/// <param name="Context">The context this prediction is about.</param>
/// <param name="SucceededCount">How many recorded entries for this context resolved <see cref="ActionOutcome.Succeeded"/>.</param>
/// <param name="FailedCount">How many resolved <see cref="ActionOutcome.Failed"/>.</param>
/// <param name="InProgressCount">How many are still <see cref="ActionOutcome.InProgress"/> -- neither a success nor a failure yet, never counted as either.</param>
/// <param name="SuccessRate">
/// <see cref="SucceededCount"/> divided by the settled total
/// (<see cref="SucceededCount"/> + <see cref="FailedCount"/>), or
/// <see langword="null"/> when that total is zero -- "no settled history
/// yet" is a different fact from "a 0% success rate" and this contract
/// keeps them distinguishable, the same discipline every
/// <see cref="WorldFact{T}"/> in this project already applies to observed
/// facts.
/// </param>
public sealed record LocalOutcomePrediction(
    string Context,
    int SucceededCount,
    int FailedCount,
    int InProgressCount,
    double? SuccessRate);

/// <summary>
/// Turns a ledger of already-recorded <see cref="ActionOutcomeLedgerEntry"/>
/// into a <see cref="LocalOutcomePrediction"/> for one context. Pure and
/// stateless, mirroring every other phase's planner in this project: no
/// I/O, no clock reads, safe to call once per decision cycle. Owns no
/// storage itself -- persisting the ledger across cycles/sessions is a
/// future runtime-wiring concern (the same split <c>MapReconstructionSource</c>
/// draws between AP-03's pure merge algorithm and its own SQLite-backed
/// caching), not this type's job.
/// </summary>
public static class LocalOutcomeSimulator
{
    /// <summary>
    /// Counts <paramref name="ledger"/>'s entries matching <paramref name="context"/>
    /// (ordinal comparison) by outcome and derives a settled-history success
    /// rate. An empty match set is a valid, honest result: every count is
    /// zero and <see cref="LocalOutcomePrediction.SuccessRate"/> is
    /// <see langword="null"/>, not fabricated as 0% or 100%.
    /// </summary>
    public static LocalOutcomePrediction Predict(string context, EquatableArray<ActionOutcomeLedgerEntry> ledger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        int succeeded = 0, failed = 0, inProgress = 0;
        foreach (ActionOutcomeLedgerEntry entry in ledger)
        {
            if (!string.Equals(entry.Context, context, StringComparison.Ordinal))
                continue;

            switch (entry.Outcome)
            {
                case ActionOutcome.Succeeded: succeeded++; break;
                case ActionOutcome.Failed: failed++; break;
                case ActionOutcome.InProgress: inProgress++; break;
            }
        }

        int settled = succeeded + failed;
        double? successRate = settled > 0 ? (double)succeeded / settled : null;

        return new LocalOutcomePrediction(context, succeeded, failed, inProgress, successRate);
    }
}
