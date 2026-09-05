using NosAi.Core.Cognitive;
using NosAi.Runtime.Gate3;

namespace NosAi.Runtime.Observability;

public sealed class CognitiveObservabilityBridge : IDisposable
{
    private readonly PipelineStageBoard _board;
    private readonly Gate3DecisionLoop? _loop;
    private readonly ICognitiveObservabilitySink _sink;
    private long _sequence;
    private bool _disposed;

    public CognitiveObservabilityBridge(PipelineStageBoard board, ICognitiveObservabilitySink sink, Gate3DecisionLoop? loop = null)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _loop = loop;
        _board.StageRecorded += OnStageRecorded;
        if (_loop is not null) _loop.CycleCompleted += OnCycleCompleted;
    }

    private void OnStageRecorded(StageOutcomeDump stage)
    {
        if (_disposed) return;
        var status = stage.Ok switch
        {
            true => CognitiveNodeStatus.Completed,
            false => CognitiveNodeStatus.Rejected,
            null => CognitiveNodeStatus.Unknown
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long sequence = Interlocked.Increment(ref _sequence);
        _ = PublishAsync(new CognitiveTraceEvent(
            Guid.NewGuid().ToString("N"),
            CurrentCycleId,
            Map(stage.Stage),
            status,
            stage.Ok == true ? "stage_completed" : "stage_refused",
            stage.Ok == true ? $"{stage.Stage}: completato" : $"{stage.Stage}: {stage.Fault ?? "non confermato"}",
            stage.Fault ?? "runtime_pipeline_stage",
            stage.Ok == true ? 1.0 : 0.0,
            now,
            sequence));
    }

    private string CurrentCycleId => _loop?.Last is { } last
        ? $"runtime-cycle-{last.AtUtc.Ticks}"
        : "runtime-cycle-pending";

    private void OnCycleCompleted(Gate3LoopCycle cycle)
    {
        if (_disposed) return;
        _ = PublishDecisionAsync(cycle);
    }

    private async Task PublishDecisionAsync(Gate3LoopCycle cycle)
    {
        try
        {
            string status = cycle.Outcome switch
            {
                CycleOutcome.Confirmed => "Committed",
                CycleOutcome.ExecutionDisabled => "ExecutionDisabled",
                CycleOutcome.NoCandidate => "NoCandidate",
                CycleOutcome.NoWorldState => "NoWorldState",
                CycleOutcome.RefusedStaleInput => "RefusedStaleInput",
                CycleOutcome.Blocked => "Blocked",
                CycleOutcome.Failed => "Failed",
                CycleOutcome.Unverified => "Unverified",
                _ => cycle.Outcome.ToString()
            };
            await _sink.PublishDecisionAsync(new CognitiveDecisionView(
                Guid.NewGuid().ToString("N"),
                $"runtime-cycle-{cycle.AtUtc.Ticks}",
                "runtime decision cycle",
                cycle.SelectedAction.ToString(),
                cycle.Hp.HasValue && cycle.MaxHp.HasValue ? 1.0 : 0.0,
                0.0,
                status,
                new DateTimeOffset(cycle.AtUtc, TimeSpan.Zero),
                [])).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task PublishAsync(CognitiveTraceEvent trace)
    {
        try { await _sink.PublishAsync(trace).ConfigureAwait(false); }
        catch { }
    }

    private static CognitiveNodeKind Map(string stage) => stage switch
    {
        "Observe" => CognitiveNodeKind.Sensors,
        "WorldState" => CognitiveNodeKind.WorldModel,
        "Simulation" => CognitiveNodeKind.Prediction,
        "Ranking" => CognitiveNodeKind.UtilityRisk,
        "Planner" => CognitiveNodeKind.Planner,
        "Guard" => CognitiveNodeKind.Guard,
        "Trust" => CognitiveNodeKind.Guard,
        "Safety" => CognitiveNodeKind.Safety,
        "Execute" => CognitiveNodeKind.Execute,
        "Verify" => CognitiveNodeKind.Verify,
        "Orchestrator" => CognitiveNodeKind.CandidatePlan,
        _ => CognitiveNodeKind.Reflection
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _board.StageRecorded -= OnStageRecorded;
        if (_loop is not null) _loop.CycleCompleted -= OnCycleCompleted;
    }
}
