using System.Collections.Immutable;

namespace NosAi.Core.Cognitive;

public enum CognitiveNodeKind
{
    Sensors,
    TemporalFusion,
    BeliefState,
    WorldModel,
    Memory,
    Attention,
    Prediction,
    Goal,
    UtilityRisk,
    Planner,
    CandidatePlan,
    Guard,
    Safety,
    Execute,
    Verify,
    Reobserve,
    Reflection
}

public enum CognitiveNodeStatus
{
    Idle,
    Running,
    Completed,
    Rejected,
    Failed,
    Unknown
}

public sealed record CognitiveNodeState(
    CognitiveNodeKind Kind,
    CognitiveNodeStatus Status,
    string Label,
    string? Detail,
    double Confidence,
    DateTimeOffset ObservedAtUtc,
    long Sequence);

public sealed record CognitiveTraceEvent(
    string EventId,
    string CycleId,
    CognitiveNodeKind Node,
    CognitiveNodeStatus Status,
    string EventType,
    string Summary,
    string? Evidence,
    double Confidence,
    DateTimeOffset OccurredAtUtc,
    long Sequence);

public sealed record DecisionCandidateView(
    string Id,
    string Action,
    double Score,
    double Risk,
    double Confidence,
    string Status,
    ImmutableArray<string> Reasons);

public sealed record CognitiveDecisionView(
    string DecisionId,
    string CycleId,
    string Objective,
    string SelectedAction,
    double Confidence,
    double Risk,
    string Status,
    DateTimeOffset CommittedAtUtc,
    ImmutableArray<DecisionCandidateView> Candidates);

public interface ICognitiveObservabilitySink
{
    ValueTask PublishAsync(CognitiveTraceEvent traceEvent, CancellationToken cancellationToken = default);
    ValueTask PublishDecisionAsync(CognitiveDecisionView decision, CancellationToken cancellationToken = default);
}

public interface ICognitiveObservabilityReader
{
    IReadOnlyList<CognitiveNodeState> GetNodes();
    IReadOnlyList<CognitiveTraceEvent> GetRecentTrace(int maxItems = 250);
    CognitiveDecisionView? GetLatestDecision();
}

public sealed class InMemoryCognitiveObservability : ICognitiveObservabilitySink, ICognitiveObservabilityReader
{
    private readonly object _gate = new();
    private readonly List<CognitiveTraceEvent> _trace = new();
    private readonly Dictionary<CognitiveNodeKind, CognitiveNodeState> _nodes = new();
    private CognitiveDecisionView? _latestDecision;
    private long _sequence;

    public ValueTask PublishAsync(CognitiveTraceEvent traceEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        lock (_gate)
        {
            var sequence = ++_sequence;
            _trace.Add(traceEvent with { Sequence = sequence });
            if (_trace.Count > 2000)
                _trace.RemoveRange(0, _trace.Count - 2000);

            _nodes[traceEvent.Node] = new CognitiveNodeState(
                traceEvent.Node,
                traceEvent.Status,
                traceEvent.Summary,
                traceEvent.Evidence,
                Math.Clamp(traceEvent.Confidence, 0d, 1d),
                traceEvent.OccurredAtUtc,
                sequence);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishDecisionAsync(CognitiveDecisionView decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate) _latestDecision = decision;
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<CognitiveNodeState> GetNodes()
    {
        lock (_gate) return _nodes.Values.OrderBy(x => x.Kind).ToArray();
    }

    public IReadOnlyList<CognitiveTraceEvent> GetRecentTrace(int maxItems = 250)
    {
        maxItems = Math.Clamp(maxItems, 1, 2000);
        lock (_gate) return _trace.TakeLast(maxItems).ToArray();
    }

    public CognitiveDecisionView? GetLatestDecision()
    {
        lock (_gate) return _latestDecision;
    }
}
