using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class PlanningContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UnverifiedAction_OutcomeIsExplicitlyUnknown_NotInProgressByDefault()
    {
        var action = new WorldAction(new ActionId("a1"), WorldFact<string>.Live("MoveTo", 1.0, Now), WorldFact<DateTime>.Live(Now, 1.0, Now), WorldFact<ActionOutcome>.Unknown("verification_pending"));

        Assert.False(action.Outcome.HasValue);
    }

    [Fact]
    public void VerifiedAction_CarriesTheObservedOutcome()
    {
        var action = new WorldAction(new ActionId("a1"), WorldFact<string>.Live("MoveTo", 1.0, Now), WorldFact<DateTime>.Live(Now, 1.0, Now), WorldFact<ActionOutcome>.Derived(ActionOutcome.Succeeded, 0.95, Now));

        Assert.True(action.Outcome.HasValue);
        Assert.Equal(ActionOutcome.Succeeded, action.Outcome.Value);
    }

    [Fact]
    public void Goal_UnrankedPriority_IsExplicitlyUnknown()
    {
        var goal = new Goal(new GoalId("g1"), WorldFact<string>.Live("Survive", 1.0, Now), WorldFact<int>.Unknown("orchestrator_not_yet_ranked"));

        Assert.False(goal.Priority.HasValue);
    }
}
