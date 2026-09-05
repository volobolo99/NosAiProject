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
                items.Average(x => Math.Max(0, x.DurationSeconds)),
                items.Average(x => Math.Max(0, x.ResourceCost)),
                items.Max(x => x.ObservedAtUtc)));
        }
    }
}
