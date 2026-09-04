using NosAi.Core.Planning;
using NosAi.Core.Planning.Goap;
using Xunit;

namespace NosAi.Core.Tests.Planning;

public sealed class DeterministicGoapPlannerTests
{
    [Fact]
    public void ProducesSamePlanForSameInput()
    {
        var actions = new[]
        {
            new GoapAction("prepare", [new("ready", 0)], [new("ready", 1)], 1, default),
            new GoapAction("finish", [new("ready", 1)], [new("done", 1)], 1, default)
        };
        var planner = new DeterministicGoapPlanner(actions);
        Span<PlanStep> first = stackalloc PlanStep[4];
        Span<PlanStep> second = stackalloc PlanStep[4];
        var goal = new[] { new GoapFact("done", 1) };

        Assert.True(planner.TryPlan([new GoapFact("ready", 0)], goal, first, out var n1, out _));
        Assert.True(planner.TryPlan([new GoapFact("ready", 0)], goal, second, out var n2, out _));
        Assert.Equal(n1, n2);
        Assert.Equal(n1, 2);
    }
}
