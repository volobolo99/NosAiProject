using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Strategy;

public sealed class EnrichedGoalTests
{
    [Fact]
    public void RecordEquality_ComparesGoalAndKind()
    {
        DateTime now = DateTime.UnixEpoch;
        var goal = new Goal(new GoalId("g1"), WorldFact<string>.Live("Survive", 1d, now), WorldFact<int>.Live(1, 1d, now));

        var first = new EnrichedGoal(goal, StrategicGoalKind.Survival);
        var second = new EnrichedGoal(goal, StrategicGoalKind.Survival);

        Assert.Equal(first, second);
    }
}

public sealed class StrategicPlanTests
{
    [Fact]
    public void Unselected_HasNoKind()
    {
        StrategicPlan plan = StrategicPlan.Unselected("no_signal");

        Assert.Null(plan.SelectedKind);
    }

    [Fact]
    public void Unselected_HasSelectionIsUnknown()
    {
        StrategicPlan plan = StrategicPlan.Unselected("no_signal");

        Assert.False(plan.HasSelection.HasValue);
    }

    [Fact]
    public void Unselected_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        StrategicPlan plan = StrategicPlan.Unselected("reason", fixedInstant);

        Assert.Equal(fixedInstant, plan.ObservedAtUtc);
        Assert.Equal(fixedInstant, plan.HasSelection.ObservedAtUtc);
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        var first = new StrategicPlan(StrategicGoalKind.Survival, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);
        var second = new StrategicPlan(StrategicGoalKind.Survival, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);

        Assert.Equal(first, second);
    }
}
