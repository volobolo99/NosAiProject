using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

public sealed class InferenceBudgetSchedulerTests
{
    private static InferenceBudgetScheduler NewScheduler(
        long nowUnixMillis = 1_000_000,
        ResourceCost? capacity = null,
        int tier0 = 4, int tier1 = 4, int tier2 = 2, int tier3 = 1)
    {
        var clock = new ManualMonotonicClock(nowUnixMillis);
        var ledger = new ResourceBudgetLedger(capacity ?? new ResourceCost(100, 100, 100, 100));
        var queue = new BoundedTierQueue(new Dictionary<InferenceTier, int>
        {
            [InferenceTier.Tier0DeterministicRules] = tier0,
            [InferenceTier.Tier1LightweightLocalMl] = tier1,
            [InferenceTier.Tier2GpuAcceleratedVision] = tier2,
            [InferenceTier.Tier3ExpensiveLocalReasoning] = tier3
        });
        return new InferenceBudgetScheduler(queue, ledger, clock);
    }

    private static InferenceJob Job(
        string id,
        InferenceTier tier,
        long now,
        ResourceCost cost,
        long durationMs = 10,
        FallbackStrategy? fallback = null) =>
        new(id, tier, JobPriority.Normal, now + 1_000, durationMs, cost, 0.1, fallback ?? FallbackStrategy.Reject());

    [Fact]
    public void JobWithinBudgetAndCapacityIsAccepted()
    {
        var scheduler = NewScheduler();
        AdmissionResult result = scheduler.TryAdmit(Job("a", InferenceTier.Tier1LightweightLocalMl, 1_000_000, new ResourceCost(1, 1, 1, 1)));

        Assert.Equal(AdmissionOutcome.Accepted, result.Outcome);
        Assert.True(result.Accepted);
    }

    [Fact]
    public void JobIsRejectedExplicitlyWhenQueueIsFullAndFallbackIsReject()
    {
        var scheduler = NewScheduler(tier2: 1);
        long now = 1_000_000;
        scheduler.TryAdmit(Job("first", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1)));

        AdmissionResult second = scheduler.TryAdmit(Job("second", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1)));

        Assert.Equal(AdmissionOutcome.Rejected, second.Outcome);
        Assert.Equal(RejectionReason.QueueFull, second.Reason);
        Assert.False(string.IsNullOrWhiteSpace(second.Explanation));
    }

    [Fact]
    public void JobIsDegradedExplicitlyWhenQueueIsFullAndFallbackDegrades()
    {
        var scheduler = NewScheduler(tier2: 1, tier1: 4);
        long now = 1_000_000;
        scheduler.TryAdmit(Job("first", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1)));

        var plan = new DegradationPlan(InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 1, 0), 5, 0.1);
        InferenceJob second = Job("second", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1), fallback: FallbackStrategy.DegradeTo(plan));

        AdmissionResult result = scheduler.TryAdmit(second);

        Assert.Equal(AdmissionOutcome.AcceptedDegraded, result.Outcome);
        Assert.Equal(InferenceTier.Tier1LightweightLocalMl, result.Job.Tier);
        Assert.Equal(1, scheduler.Queue.Count(InferenceTier.Tier1LightweightLocalMl));
        Assert.Equal(1, scheduler.Queue.Count(InferenceTier.Tier2GpuAcceleratedVision)); // only "first" remains there
    }

    [Fact]
    public void DegradationIsSingleHopSoASecondFailureIsAHardReject()
    {
        var scheduler = NewScheduler(tier2: 1, tier1: 0); // tier1 has no room either
        long now = 1_000_000;
        scheduler.TryAdmit(Job("first", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1)));

        var plan = new DegradationPlan(InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 1, 0), 5, 0.1);
        InferenceJob second = Job("second", InferenceTier.Tier2GpuAcceleratedVision, now, new ResourceCost(1, 1, 1, 1), fallback: FallbackStrategy.DegradeTo(plan));

        AdmissionResult result = scheduler.TryAdmit(second);

        Assert.Equal(AdmissionOutcome.Rejected, result.Outcome);
        Assert.Equal("second", result.Job.Id); // reports against the ORIGINAL job, not a further-degraded ghost
    }

    [Fact]
    public void InsufficientLiveBudgetIsRejectedExplicitly()
    {
        var scheduler = NewScheduler(capacity: new ResourceCost(2, 2, 2, 2));
        AdmissionResult result = scheduler.TryAdmit(Job("too-big", InferenceTier.Tier1LightweightLocalMl, 1_000_000, new ResourceCost(100, 0, 0, 0)));

        Assert.Equal(AdmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(RejectionReason.InsufficientBudget, result.Reason);
    }

    [Fact]
    public void DeadlineAlreadyUnreachableIsRejectedExplicitly()
    {
        var scheduler = NewScheduler();
        long now = 1_000_000;
        var job = new InferenceJob("late", InferenceTier.Tier1LightweightLocalMl, JobPriority.Normal,
            now + 5, 50, new ResourceCost(1, 1, 1, 1), 0.1, FallbackStrategy.Reject());

        AdmissionResult result = scheduler.TryAdmit(job);

        Assert.Equal(AdmissionOutcome.Rejected, result.Outcome);
        Assert.Equal(RejectionReason.DeadlineUnreachable, result.Reason);
    }

    [Fact]
    public void CompleteReleasesTheReservedCost()
    {
        var scheduler = NewScheduler(capacity: new ResourceCost(10, 10, 10, 10));
        InferenceJob job = Job("a", InferenceTier.Tier1LightweightLocalMl, 1_000_000, new ResourceCost(4, 0, 0, 0));
        scheduler.TryAdmit(job);

        Assert.Equal(6, scheduler.Ledger.Available.CpuMillis);
        scheduler.Complete(job);
        Assert.Equal(10, scheduler.Ledger.Available.CpuMillis);
    }

    [Fact]
    public void NullJobThrows()
    {
        var scheduler = NewScheduler();
        Assert.Throws<ArgumentNullException>(() => scheduler.TryAdmit(null!));
    }
}
