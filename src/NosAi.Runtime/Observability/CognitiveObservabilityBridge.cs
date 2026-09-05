using NosAi.Core.Cognitive;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Bridges the runtime's real pipeline-stage events into the Core cognitive
/// observability stream. It contains no execution authority and emits only facts
/// produced by PipelineStageBoard.
/// </summary>
public sealed class CognitiveObservabilityBridge : IDisposable
{
    private readonly PipelineStageBoard _board;
    private readonly ICognitiveObservabilitySink _sink;
    private readonly string _cycleId;
    private long _sequence;
    private bool _disposed;

    public CognitiveObservabilityBridge(
        PipelineStageBoard board,
        ICognitiveObservabilitySink sink,
        string? cycleId = null)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _cycleId = string.IsNullOrWhiteSpace(cycleId) ? Guid.NewGuid().ToString("N") : cycleId;
        _board.StageRecorded += OnStageRecorded;
    }

    private void OnStageRecorded(StageOutcomeDump stage)
    {
        if (_disposed) return;

        CognitiveNodeKind kind = Map(stage.Stage);
        CognitiveNodeStatus status = stage.Ok switch
        {
            true => CognitiveNodeStatus.Completed,
            false => CognitiveNodeStatus.Rejected,
            null => CognitiveNodeStatus.Unknown
        };
        string detail = stage.Ok == true
            ? "Stage completato dal runtime."
            : stage.Fault ?? "Stage non confermato dal runtime.";

        long sequence = Interlocked.Increment(ref _sequence);
        DateTime now = DateTime.UtcNow;
        var node = new CognitiveNodeState(
            kind,
            status,
            stage.Stage,
            detail,
            stage.Ok == true ? 1.0 : 0.0,
            now,
            sequence);

        var trace = new CognitiveTraceEvent(
            Guid.NewGuid().ToString("N"),
            _cycleId,
            kind,
            status,
            stage.Ok == true ? "stage_completed" : "stage_refused",
            stage.Ok == true ? $"{stage.Stage}: completato" : $"{stage.Stage}: {detail}",
            stage.Fault ?? "runtime_pipeline_stage",
            stage.Ok == true ? 1.0 : 0.0,
            now,
            sequence);

        _ = PublishAsync(node, trace);
    }

    private async Task PublishAsync(CognitiveNodeState node, CognitiveTraceEvent trace)
    {
        try
        {
            await _sink.PublishAsync(node).ConfigureAwait(false);
            await _sink.PublishAsync(trace).ConfigureAwait(false);
        }
        catch
        {
            // Observability must never stop the decision loop. The runtime remains
            // authoritative; a telemetry sink failure is not a decision failure.
        }
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
    }
}
