using NosAi.Core.Memory;
using NosAi.Core.Navigation;
using NosAi.Core.Planning;
using NosAi.Core.Planning.Goap;
using NosAi.Core.Safety;
using Xunit;

namespace NosAi.Core.Tests.Planning;

public sealed class PlanningSafetyTests
{
    [Fact]
    public void UnknownGameplayProvenanceIsRejected()
    {
        var store = new InMemoryStore();
        var record = new MemoryRecord(Guid.NewGuid(), MemoryType.Semantic, MemoryProvenance.Unknown, 1f, 1, 1, 1, "enemy", "present", false);
        Assert.False(store.Append(record));
    }

    [Fact]
    public void RecoveryEventuallyFailsClosed()
    {
        var controller = new RecoveryController(new RecoveryPolicy(TimeSpan.FromSeconds(1), 1, TimeSpan.Zero));
        Assert.True(controller.OnTransientFailure());
        Assert.False(controller.OnTransientFailure());
        Assert.Equal(RecoveryState.SafeStop, controller.State);
    }

    [Fact]
    public void NavigationFindsDeterministicPath()
    {
        var grid = new bool[4, 4];
        for (var x = 0; x < 4; x++) for (var y = 0; y < 4; y++) grid[x, y] = true;
        var planner = new DeterministicGridNavigationPlanner(grid);
        Span<NavigationPoint> path = stackalloc NavigationPoint[16];
        Assert.True(planner.TryFindPath(new NavigationPoint(0, 0), new NavigationPoint(3, 3), path, out var count));
        Assert.Equal(7, count);
    }
}
