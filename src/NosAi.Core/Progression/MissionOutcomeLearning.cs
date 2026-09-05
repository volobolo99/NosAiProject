namespace NosAi.Core.Progression;

/// <summary>
/// Applies real observed outcomes to mission-strategy estimates before deterministic ranking.
/// This component changes ranking only; it never grants execution authority.
/// </summary>
public sealed class OutcomeAwareMissionStrategyRanker
{
    private readonly IMissionStrategyOptimizer _optimizer;
    private readonly IOutcomeLedger _ledger;

    public OutcomeAwareMissionStrategyRanker(IMissionStrategyOptimizer optimizer, IOutcomeLedger ledger)
    {
        _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public async ValueTask<MissionStrategyScore?> SelectBestAsync(
        MissionObjective objective,
        IReadOnlyList<MissionStrategyCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(candidates);

        var adjusted = new List<MissionStrategyCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = await _ledger.SummarizeAsync(objective.Id, candidate.Id, cancellationToken);
            if (summary is null || summary.Samples == 0)
            {
                adjusted.Add(candidate);
                continue;
            }

            var observedSuccess = Math.Clamp(summary.SuccessRate, 0.01, 1d);
            var observedDuration = Math.Max(0, summary.MeanDurationSeconds);
            var observedResource = Math.Max(0, summary.MeanResourceCost);
            var blendedSuccess = Math.Clamp((candidate.SuccessProbability + observedSuccess) * 0.5, 0.01, 1d);
            var sampleConfidence = Math.Min(1d, 0.5d + summary.Samples / 20d);

            adjusted.Add(candidate with
            {
                EstimatedExecutionSeconds = observedDuration,
                ResourceCost = observedResource,
                SuccessProbability = blendedSuccess,
                Confidence = Math.Clamp(Math.Max(candidate.Confidence, sampleConfidence), 0, 1)
            });
        }

        return _optimizer.SelectBest(objective, adjusted);
    }
}
