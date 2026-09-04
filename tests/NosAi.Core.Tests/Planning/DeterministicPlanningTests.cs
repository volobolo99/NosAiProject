using NosAi.Core.Planning;
using Xunit;

namespace NosAi.Core.Tests.Planning;

public sealed class DeterministicPlanningTests
{
    [Fact]
    public void PlanStepIsStableValue()
    {
        var a = new PlanStep(1, 42, 10, 100, 7);
        var b = new PlanStep(1, 42, 10, 100, 7);
        Assert.Equal(a.ActionId, b.ActionId);
        Assert.Equal(a.TargetEntityId, b.TargetEntityId);
        Assert.Equal(a.RequiredScope, b.RequiredScope);
    }
}
