namespace NosAi.Core.Progression;

public sealed record MissionOutcome(
    string OutcomeId,
    string ObjectiveId,
    string StrategyId,
    bool Success,
    double DurationSeconds,
    double RecoverySeconds,
    double ResourceCost,
    DateTime ObservedAtUtc,
    string? RulesetVersion = null,
    string? FailureReason = null);

public sealed record StrategyOutcomeSummary(
    string StrategyId,
    int Samples,
    int Successes,
    double SuccessRate,
    double MeanDurationSeconds,
    double MeanResourceCost,
    DateTime LastObservedAtUtc);

public interface IOutcomeLedger
{
    ValueTask RecordAsync(MissionOutcome outcome, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MissionOutcome>> QueryAsync(string objectiveId, string strategyId, CancellationToken cancellationToken = default);
    ValueTask<StrategyOutcomeSummary?> SummarizeAsync(string objectiveId, string strategyId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryOutcomeLedger : IOutcomeLedger
{
    private readonly List<MissionOutcome> _outcomes = [];
    private readonly object _sync = new();

    public ValueTask RecordAsync(MissionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(outcome);

        lock (_sync)
        {
            _outcomes.RemoveAll(x => x.OutcomeId == outcome.OutcomeId);
            _outcomes.Add(outcome);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<MissionOutcome>> QueryAsync(string objectiveId, string strategyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return ValueTask.FromResult<IReadOnlyList<MissionOutcome>>(
                _outcomes.Where(x => x.ObjectiveId == objectiveId && x.StrategyId == strategyId).OrderBy(x => x.ObservedAtUtc).ToArray());
        }
    }

    public ValueTask<StrategyOutcomeSummary?> SummarizeAsync(string objectiveId, string strategyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var items = _outcomes.Where(x => x.ObjectiveId == objectiveId && x.StrategyId == strategyId).ToArray();
            if (items.Length == 0) return ValueTask.FromResult<StrategyOutcomeSummary?>(null);
            return ValueTask.FromResult<StrategyOutcomeSummary?>(new StrategyOutcomeSummary(
                strategyId,
                items.Length,
                items.Count(x => x.Success),
                items.Count(x => x.Success) / (double)items.Length,
                items.Average(x => x.DurationSeconds),
                items.Average(x => x.ResourceCost),
                items.Max(x => x.ObservedAtUtc)));
        }
    }

    private static void Validate(MissionOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.OutcomeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.ObjectiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.StrategyId);
        if (double.IsNaN(outcome.DurationSeconds) || double.IsInfinity(outcome.DurationSeconds) || outcome.DurationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome.DurationSeconds), "DurationSeconds must be finite and non-negative.");
        if (double.IsNaN(outcome.RecoverySeconds) || double.IsInfinity(outcome.RecoverySeconds) || outcome.RecoverySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome.RecoverySeconds), "RecoverySeconds must be finite and non-negative.");
        if (double.IsNaN(outcome.ResourceCost) || double.IsInfinity(outcome.ResourceCost) || outcome.ResourceCost < 0)
            throw new ArgumentOutOfRangeException(nameof(outcome.ResourceCost), "ResourceCost must be finite and non-negative.");
        if (outcome.ObservedAtUtc == default)
            throw new ArgumentException("ObservedAtUtc must be populated.", nameof(outcome.ObservedAtUtc));
    }
}
