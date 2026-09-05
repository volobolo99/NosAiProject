using System.Collections.Immutable;
using NosAi.Core.Cognitive;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;

namespace NosAi.ControlPanel;

/// <summary>
/// Adapts real Gate 3 stage notifications to the read-only cognitive observability
/// contract consumed by the Control Panel. It never participates in execution.
/// </summary>
public sealed class CognitiveRuntimeTraceBridge : IDisposable
{
    private readonly Gate3DecisionLoop _loop;
    private readonly ICognitiveObservabilitySink _sink;
    private readonly object _gate = new();
    private string? _cycleId;
    private long _sequence;
    private bool _disposed;

    public CognitiveRuntimeTraceBridge(Gate3DecisionLoop loop, ICognitiveObservabilitySink sink)
    {
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _loop.Orchestrator.StageBoard.StageRecorded += OnStageRecorded;
        _loop.CycleCompleted += OnCycleCompleted;
    }

    private void OnStageRecorded(StageOutcomeDump stage)
    {
        string cycleId;
        long sequence;
        lock (_gate)
        {
            if (stage.Stage == "Observe" || _cycleId is null)
                _cycleId = Guid.NewGuid().ToString("N");
            cycleId = _cycleId;
            sequence = ++_sequence;
        }

        CognitiveNodeKind node = MapNode(stage.Stage);
        CognitiveNodeStatus status = stage.Ok switch
        {
            true => CognitiveNodeStatus.Completed,
            false => CognitiveNodeStatus.Rejected,
            null => CognitiveNodeStatus.Unknown
        };

        string detail = stage.Ok == true
            ? "Stage runtime completato."
            : stage.Fault ?? "Stage non confermato dal runtime.";

        _ = PublishTraceAsync(new CognitiveTraceEvent(
            Guid.NewGuid().ToString("N"),
            cycleId,
            node,
            status,
            "runtime.stage",
            $"{stage.Stage}: {status}",
            detail,
            status == CognitiveNodeStatus.Completed ? 1.0 : 0.0,
            DateTimeOffset.UtcNow,
            sequence));
    }

    private void OnCycleCompleted(Gate3LoopCycle cycle)
    {
        string cycleId;
        long sequence;
        lock (_gate)
        {
            cycleId = _cycleId ??= Guid.NewGuid().ToString("N");
            sequence = ++_sequence;
        }

        _ = PublishDecisionAsync(new CognitiveDecisionView(
            Guid.NewGuid().ToString("N"),
            cycleId,
            "Gate3 cycle",
            cycle.SelectedAction.ToString(),
            ConfidenceFor(cycle.Outcome),
            RiskFor(cycle.Outcome),
            cycle.Outcome.ToString(),
            DateTimeOffset.UtcNow,
            ImmutableArray<DecisionCandidateView>.Empty));

        _ = PublishTraceAsync(new CognitiveTraceEvent(
            Guid.NewGuid().ToString("N"),
            cycleId,
            CognitiveNodeKind.Reobserve,
            cycle.Outcome == CycleOutcome.Confirmed
                ? CognitiveNodeStatus.Completed
                : CognitiveNodeStatus.Unknown,
            "runtime.cycle",
            cycle.Summary,
            $"Outcome={cycle.Outcome}; observationAge={cycle.ObservationAge?.TotalMilliseconds:F0}ms",
            ConfidenceFor(cycle.Outcome),
            DateTimeOffset.UtcNow,
            sequence));
    }

    private async Task PublishTraceAsync(CognitiveTraceEvent trace)
    {
        if (_disposed) return;
        try { await _sink.PublishAsync(trace).ConfigureAwait(false); }
        catch { }
    }

    private async Task PublishDecisionAsync(CognitiveDecisionView decision)
    {
        if (_disposed) return;
        try { await _sink.PublishDecisionAsync(decision).ConfigureAwait(false); }
        catch { }
    }

    private static CognitiveNodeKind MapNode(string stage) => stage switch
    {
        "Observe" => CognitiveNodeKind.Sensors,
        "WorldState" => CognitiveNodeKind.WorldModel,
        "Simulation" => CognitiveNodeKind.Prediction,
        "Ranking" => CognitiveNodeKind.UtilityRisk,
        "Orchestrator" => CognitiveNodeKind.Planner,
        "Planner" => CognitiveNodeKind.Goal,
        "Guard" => CognitiveNodeKind.Guard,
        "Trust" => CognitiveNodeKind.Guard,
        "Safety" => CognitiveNodeKind.Safety,
        "Execute" => CognitiveNodeKind.Execute,
        "Verify" => CognitiveNodeKind.Verify,
        _ => CognitiveNodeKind.WorldModel
    };

    private static double ConfidenceFor(CycleOutcome outcome) => outcome switch
    {
        CycleOutcome.Confirmed => 1.0,
        CycleOutcome.Unverified => 0.5,
        CycleOutcome.NoCandidate => 0.8,
        _ => 0.0
    };

    private static double RiskFor(CycleOutcome outcome) => outcome switch
    {
        CycleOutcome.Confirmed => 0.0,
        CycleOutcome.NoCandidate => 0.1,
        CycleOutcome.Unverified => 0.5,
        CycleOutcome.Failed => 1.0,
        CycleOutcome.Blocked or CycleOutcome.ExecutionDisabled => 0.0,
        _ => 0.5
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loop.Orchestrator.StageBoard.StageRecorded -= OnStageRecorded;
        _loop.CycleCompleted -= OnCycleCompleted;
    }
}
