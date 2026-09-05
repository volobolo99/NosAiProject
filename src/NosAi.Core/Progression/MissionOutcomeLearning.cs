namespace NosAi.Core.Progression;

public sealed record MissionOutcome(
    string Id,
    string ObjectiveId,
    string StrategyId,
    bool Succeeded,
    double DurationSeconds,
    double ResourceCost,
    int Retries,
    DateTime ObservedAtUtc);

public sealed record MissionOutcomeSummary(
    string ObjectiveId,
    string StrategyId,
    int Samples,
    int Successes,
    double SuccessRate,
    double MeanDurationSeconds,
    double MeanResourceCost,
    int TotalRetries);

public interface IOutcomeLedger
{
    ValueTask RecordAsync(MissionOutcome outcome, CancellationToken cancellationToken = default);
    ValueTask<MissionOutcomeSummary?> SummarizeAsync(string objectiveId, string strategyId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryOutcomeLedger : IOutcomeLedger
{
    private readonly Dictionary<string, MissionOutcome> _outcomes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ValueTask RecordAsync(MissionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.ObjectiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.StrategyId);
        if (!double.IsFinite(outcome.DurationSeconds) || outcome.DurationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome), "Duration must be finite and non-negative.");
        if (!double.IsFinite(outcome.ResourceCost) || outcome.ResourceCost < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome), "Resource cost must be finite and non-negative.");
        if (outcome.Retries < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome), "Retries must be non-negative.");

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _outcomes[outcome.Id] = outcome;
        return ValueTask.CompletedTask;
    }

    public ValueTask<MissionOutcomeSummary?> SummarizeAsync(
        string objectiveId,
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        cancellationToken.ThrowIfCancellationRequested();

        MissionOutcome[] matches;
        lock (_gate)
        {
            matches = _outcomes.Values
                .Where(x => string.Equals(x.ObjectiveId, objectiveId, StringComparison.Ordinal)
                    && string.Equals(x.StrategyId, strategyId, StringComparison.Ordinal))
                .ToArray();
        }

        if (matches.Length == 0)
            return ValueTask.FromResult<MissionOutcomeSummary?>(null);

        var successes = matches.Count(x => x.Succeeded);
        return ValueTask.FromResult<MissionOutcomeSummary?>(new MissionOutcomeSummary(
            objectiveId,
            strategyId,
            matches.Length,
            successes,
            (double)successes / matches.Length,
            matches.Average(x => x.DurationSeconds),
            matches.Average(x => x.ResourceCost),
            matches.Sum(x => x.Retries)));
    }
}

public sealed class OutcomeAwareMissionStrategyRanker
{
    private readonly IMissionStrategyOptimizer _optimizer;
    private readonly IOutcomeLedger _ledger;

    public OutcomeAwareMissionStrategyRanker(IMissionStrategyOptimizer optimizer, IOutcomeLedger ledger)
    {
        _optimizer = optimizer;
        _ledger = ledger;
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
            var blendedSuccess = (candidate.SuccessProbability + observedSuccess) * 0.5;
            var blendedConfidence = Math.Clamp(Math.Max(candidate.Confidence, Math.Min(1d, 0.5d + summary.Samples / 20d)), 0, 1);

            adjusted.Add(candidate with
            {
                EstimatedExecutionSeconds = observedDuration,
                ResourceCost = observedResource,
                SuccessProbability = blendedSuccess,
                Confidence = blendedConfidence,
                ExpectedRetryCount = summary.TotalRetries / (double)summary.Samples
            });
        }

        return _optimizer.SelectBest(objective, adjusted);
    }
}
