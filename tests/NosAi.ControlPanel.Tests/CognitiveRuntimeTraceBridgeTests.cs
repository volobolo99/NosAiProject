using NosAi.Core.Cognitive;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class CognitiveRuntimeTraceBridgeTests
{
    [Fact]
    public void Real_stage_events_are_projected_without_becoming_execution_authority()
    {
        var sink = new InMemoryCognitiveObservability();
        var board = new PipelineStageBoard();
        var orchestrator = new Gate3ExecutionOrchestrator(stageBoard: board);
        var loop = new Gate3DecisionLoop(
            new UnavailableWorldStateSource(),
            orchestrator,
            new NullRuntimeLogger());
        using var bridge = new CognitiveRuntimeTraceBridge(loop, sink);

        board.Record("Observe", true);
        board.Record("Planner", true);
        board.Record("Safety", false, "policy_refused");

        var trace = sink.GetRecentTrace(10);

        Assert.Contains(trace, x => x.Node == CognitiveNodeKind.Sensors && x.Status == CognitiveNodeStatus.Completed);
        Assert.Contains(trace, x => x.Node == CognitiveNodeKind.Goal && x.Status == CognitiveNodeStatus.Completed);
        Assert.Contains(trace, x => x.Node == CognitiveNodeKind.Safety && x.Status == CognitiveNodeStatus.Rejected && x.Evidence == "policy_refused");
    }

    private sealed class UnavailableWorldStateSource : IWorldStateSource
    {
        public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Gate3WorldState.Unobserved("test_unavailable"));
    }
}
