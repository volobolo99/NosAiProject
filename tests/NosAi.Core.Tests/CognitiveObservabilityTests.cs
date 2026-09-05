using NosAi.Core.Cognitive;

namespace NosAi.Core.Tests;

public sealed class CognitiveObservabilityTests
{
    [Fact]
    public async Task SinkKeepsLatestStateAndBoundedTrace()
    {
        var sink = new InMemoryCognitiveObservability();
        for (var i = 0; i < 2_100; i++)
        {
            await sink.PublishAsync(new CognitiveTraceEvent(
                $"e{i}", "cycle", CognitiveNodeKind.WorldModel, CognitiveNodeStatus.Completed,
                "stage", $"event-{i}", null, 1, DateTimeOffset.UtcNow, 0));
        }

        Assert.Equal(2_000, sink.GetRecentTrace(2_000).Count);
        Assert.Equal("event-2099", sink.GetRecentTrace(1)[0].Summary);
        Assert.Equal("event-2099", sink.GetNodes().Single(x => x.Kind == CognitiveNodeKind.WorldModel).Label);
    }

    [Fact]
    public async Task DecisionIsReadOnlyProjection()
    {
        var sink = new InMemoryCognitiveObservability();
        var decision = new CognitiveDecisionView(
            "d1", "c1", "test", "move", .9, .1, "Committed", DateTimeOffset.UtcNow,
            System.Collections.Immutable.ImmutableArray<DecisionCandidateView>.Empty);

        await sink.PublishDecisionAsync(decision);

        Assert.Same(decision, sink.GetLatestDecision());
    }
}
