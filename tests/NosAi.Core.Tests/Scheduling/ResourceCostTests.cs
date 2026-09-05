using NosAi.Core.Scheduling;
using Xunit;

namespace NosAi.Core.Tests.Scheduling;

public sealed class ResourceCostTests
{
    [Fact]
    public void IsWithinIsComponentWiseInclusive()
    {
        var cost = new ResourceCost(CpuMillis: 10, GpuMillis: 5, RamBytes: 100, VramBytes: 0);
        var budgetExact = new ResourceCost(10, 5, 100, 0);
        var budgetLarger = new ResourceCost(11, 6, 200, 1);
        var budgetTooSmall = new ResourceCost(9, 5, 100, 0);

        Assert.True(cost.IsWithin(budgetExact));
        Assert.True(cost.IsWithin(budgetLarger));
        Assert.False(cost.IsWithin(budgetTooSmall));
    }

    [Fact]
    public void AddAndSubtractAreComponentWise()
    {
        var a = new ResourceCost(10, 20, 30, 40);
        var b = new ResourceCost(1, 2, 3, 4);

        Assert.Equal(new ResourceCost(11, 22, 33, 44), a.Add(b));
        Assert.Equal(new ResourceCost(9, 18, 27, 36), a.Subtract(b));
    }

    [Fact]
    public void SubtractCanGoNegativeToRepresentOverBudgetHeadroom()
    {
        var capacity = new ResourceCost(10, 10, 10, 10);
        var committed = new ResourceCost(15, 5, 0, 0);

        ResourceCost headroom = capacity.Subtract(committed);

        Assert.Equal(-5, headroom.CpuMillis);
        Assert.False(headroom.IsNonNegative);
    }

    [Fact]
    public void ZeroIsTheAdditiveIdentity()
    {
        var cost = new ResourceCost(1, 2, 3, 4);
        Assert.Equal(cost, cost.Add(ResourceCost.Zero));
        Assert.Equal(cost, cost.Subtract(ResourceCost.Zero));
    }
}
