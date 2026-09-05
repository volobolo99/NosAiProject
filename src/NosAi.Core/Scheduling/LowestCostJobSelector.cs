namespace NosAi.Core.Scheduling;

/// <summary>Outcome of a candidate-selection attempt. There is no "pick something anyway" fallback here -- a candidate that meets nothing is an explicit, reported non-selection.</summary>
public enum SelectionOutcome : byte
{
    Selected,
    NoCandidateSatisfiesRequirements
}

/// <summary>A structured selection result: the chosen job (when any qualified) plus a human-readable explanation for logging/telemetry.</summary>
public readonly record struct SelectionResult(SelectionOutcome Outcome, InferenceJob? Job, string Explanation)
{
    public bool Selected => Outcome == SelectionOutcome.Selected;
}

/// <summary>
/// The deterministic "pick the cheapest computation that meets the bar" rule
/// from docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8 ("The scheduler prefers
/// the lowest-cost computation that meets the confidence/deadline
/// requirement") and docs/ROADMAP_ESECUTIVA.md S:5 ("Il runtime sceglie il
/// tier minimo che soddisfa confidence e deadline").
///
/// This type is a pure function over its arguments: no wall-clock reads (the
/// caller supplies "now"), no randomness, no shared mutable state, no
/// allocation beyond the single returned <see cref="SelectionResult"/>. The
/// same (<paramref name="availableBudget"/>, candidates, requiredConfidence,
/// now) input always yields the same output, including tie-breaking, which
/// is what makes it independently testable and safe to call from hot paths
/// without surprising GC pressure.
///
/// This selector answers "which of several alternative ways to satisfy one
/// decision is cheapest and still good enough" -- it does not itself manage
/// queue capacity or a live resource ledger; the winning job is typically
/// handed to <see cref="InferenceBudgetScheduler.TryAdmit"/> next.
/// </summary>
public static class LowestCostJobSelector
{
    /// <summary>
    /// Selects the lowest-cost candidate whose declared confidence meets
    /// <paramref name="requiredConfidence"/>, whose estimated duration still
    /// fits before its own deadline as of <paramref name="nowUnixMillis"/>,
    /// and whose estimated cost fits within <paramref name="availableBudget"/>.
    /// Returns <see cref="SelectionOutcome.NoCandidateSatisfiesRequirements"/>
    /// (never a null/default silently treated as "fine") when no candidate
    /// satisfies all three at once.
    /// </summary>
    public static SelectionResult SelectCheapest(
        ResourceCost availableBudget,
        IReadOnlyList<InferenceJob> candidates,
        double requiredConfidence,
        long nowUnixMillis)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (requiredConfidence is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(requiredConfidence), "requiredConfidence must be in [0,1].");

        InferenceJob? best = null;
        int consideredCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            InferenceJob? candidate = candidates[i];
            if (candidate is null)
                continue;
            if (candidate.MinimumConfidence < requiredConfidence)
                continue;
            if (nowUnixMillis + candidate.EstimatedDurationMs > candidate.DeadlineUnixMillis)
                continue;
            if (!candidate.EstimatedCost.IsWithin(availableBudget))
                continue;

            consideredCount++;
            if (best is null || IsCheaperOrPreferredTie(candidate, best))
                best = candidate;
        }

        if (best is null)
        {
            return new SelectionResult(
                SelectionOutcome.NoCandidateSatisfiesRequirements,
                null,
                $"None of {candidates.Count} candidate(s) satisfy deadline, confidence ({requiredConfidence:0.###}) and budget simultaneously.");
        }

        return new SelectionResult(
            SelectionOutcome.Selected,
            best,
            $"Selected '{best.Id}' at {best.Tier} as the lowest-cost of {consideredCount} qualifying candidate(s).");
    }

    /// <summary>
    /// A deterministic total order over candidates, evaluated in this fixed
    /// sequence so the outcome never depends on input/collection order:
    ///   1. tier ascending (the roadmap's primary cost proxy: a lower tier
    ///      is always at least as cheap as a higher one by construction);
    ///   2. total estimated resource cost ascending, within the same tier
    ///      (CPU/GPU milliseconds plus RAM/VRAM converted to whole
    ///      megabytes so all four components sit on a comparable integer
    ///      scale -- deliberately simple and exact, no floating-point
    ///      drift, and good enough once tier has already done most of the
    ///      separating work);
    ///   3. priority descending, so an exact cost tie favors the more
    ///      important job;
    ///   4. Id, ordinal ascending, as the final tie-breaker so two
    ///      candidates that are identical in every scheduling-relevant
    ///      dimension still resolve to one stable winner.
    /// </summary>
    private static bool IsCheaperOrPreferredTie(InferenceJob candidate, InferenceJob current)
    {
        if (candidate.Tier != current.Tier)
            return candidate.Tier < current.Tier;

        long candidateScore = TotalCostScore(candidate.EstimatedCost);
        long currentScore = TotalCostScore(current.EstimatedCost);
        if (candidateScore != currentScore)
            return candidateScore < currentScore;

        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        return string.CompareOrdinal(candidate.Id, current.Id) < 0;
    }

    private const long BytesPerMegabyte = 1024L * 1024L;

    private static long TotalCostScore(ResourceCost cost) =>
        cost.CpuMillis + cost.GpuMillis + (cost.RamBytes / BytesPerMegabyte) + (cost.VramBytes / BytesPerMegabyte);
}
