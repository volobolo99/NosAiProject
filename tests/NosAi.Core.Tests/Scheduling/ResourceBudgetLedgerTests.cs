using NosAi.Core.Scheduling;
using Xunit;

namespace NosAi.Core.Tests.Scheduling;

public sealed class ResourceBudgetLedgerTests
{
    [Fact]
    public void AvailableEqualsCapacityBeforeAnyReservation()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        Assert.Equal(new ResourceCost(10, 10, 10, 10), ledger.Available);
    }

    [Fact]
    public void TryReserveWithinHeadroomSucceedsAndReducesAvailable()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        bool ok = ledger.TryReserve(new ResourceCost(3, 0, 0, 0));

        Assert.True(ok);
        Assert.Equal(new ResourceCost(7, 10, 10, 10), ledger.Available);
    }

    [Fact]
    public void TryReserveBeyondHeadroomFailsWithoutMutatingState()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(5, 5, 5, 5));
        bool ok = ledger.TryReserve(new ResourceCost(6, 0, 0, 0));

        Assert.False(ok);
        Assert.Equal(new ResourceCost(5, 5, 5, 5), ledger.Available);
    }

    [Fact]
    public void ReleaseRestoresHeadroom()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        ledger.TryReserve(new ResourceCost(4, 0, 0, 0));
        ledger.Release(new ResourceCost(4, 0, 0, 0));

        Assert.Equal(new ResourceCost(10, 10, 10, 10), ledger.Available);
    }

    [Fact]
    public void UnbalancedReleaseThrowsInsteadOfSilentlyClamping()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        ledger.TryReserve(new ResourceCost(2, 0, 0, 0));

        Assert.Throws<InvalidOperationException>(() => ledger.Release(new ResourceCost(3, 0, 0, 0)));
    }

    [Fact]
    public void NegativeCapacityAtConstructionThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceBudgetLedger(new ResourceCost(-1, 0, 0, 0)));
    }

    [Fact]
    public void NegativeReserveAmountThrows()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.TryReserve(new ResourceCost(-1, 0, 0, 0)));
    }

    [Fact]
    public void CapacityShrinkingBelowCommittedProducesNegativeAvailableRatherThanThrowing()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(10, 10, 10, 10));
        ledger.TryReserve(new ResourceCost(8, 0, 0, 0));

        // Simulate a thermal/power throttle event shrinking total capacity
        // below what is already committed (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:9).
        ledger.UpdateCapacity(new ResourceCost(5, 10, 10, 10));

        Assert.Equal(-3, ledger.Available.CpuMillis);
        Assert.False(ledger.TryReserve(new ResourceCost(1, 0, 0, 0)));
    }

    private sealed class RecordingBudgetSource : IResourceBudgetSource
    {
        public ResourceCost CurrentCapacity { get; set; }
    }

    [Fact]
    public void RefreshCapacityFromSourcePullsLatestReading()
    {
        var source = new RecordingBudgetSource { CurrentCapacity = new ResourceCost(1, 1, 1, 1) };
        var ledger = new ResourceBudgetLedger(new ResourceCost(0, 0, 0, 0), source);

        source.CurrentCapacity = new ResourceCost(20, 20, 20, 20);
        ledger.RefreshCapacityFromSource();

        Assert.Equal(new ResourceCost(20, 20, 20, 20), ledger.Capacity);
    }

    [Fact]
    public void RefreshCapacityFromSourceIsNoOpWithoutASource()
    {
        var ledger = new ResourceBudgetLedger(new ResourceCost(1, 1, 1, 1));
        ledger.RefreshCapacityFromSource();
        Assert.Equal(new ResourceCost(1, 1, 1, 1), ledger.Capacity);
    }
}
