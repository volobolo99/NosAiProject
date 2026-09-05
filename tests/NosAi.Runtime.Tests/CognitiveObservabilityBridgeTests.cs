using NosAi.Core.Cognitive;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class CognitiveObservabilityBridgeTests
{
    [Fact]
    public async Task Real_stage_record_is_published_as_trace()
    {
        var sink = new RecordingSink();
        var board = new PipelineStageBoard();
        using var bridge = new CognitiveObservabilityBridge(board, sink);

        board.Record("Observe", true);

        await WaitUntilAsync(() => sink.Events.Count == 1);

        var trace = Assert.Single(sink.Events);
        Assert.Equal(CognitiveNodeKind.Sensors, trace.Node);
        Assert.Equal(CognitiveNodeStatus.Completed, trace.Status);
        Assert.Equal("stage_completed", trace.EventType);
        Assert.Equal("Observe: completato", trace.Summary);
    }

    [Fact]
    public void Board_subscriber_failure_does_not_break_recording()
    {
        var board = new PipelineStageBoard();
        board.StageRecorded += _ => throw new InvalidOperationException("telemetry failure");

        board.Record("Planner", true);

        var result = Assert.Single(board.Snapshot().Where(x => x.Stage == "Planner"));
        Assert.True(result.Ok);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 20 && !predicate(); i++)
            await Task.Delay(5);
        Assert.True(predicate());
    }

    private sealed class RecordingSink : ICognitiveObservabilitySink
    {
        public List<CognitiveTraceEvent> Events { get; } = [];

        public ValueTask PublishAsync(CognitiveTraceEvent traceEvent, CancellationToken cancellationToken = default)
        {
            lock (Events) Events.Add(traceEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishDecisionAsync(CognitiveDecisionView decision, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
