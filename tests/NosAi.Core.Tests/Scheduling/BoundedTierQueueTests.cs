using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

public sealed class BoundedTierQueueTests
{
    private static InferenceJob Job(string id, InferenceTier tier) =>
        new(id, tier, JobPriority.Normal, DeadlineUnixMillis: 1_000, EstimatedDurationMs: 1,
            EstimatedCost: new ResourceCost(1, 1, 1, 1), MinimumConfidence: 0.1, Fallback: FallbackStrategy.Reject());

    private static BoundedTierQueue NewQueue(int tier0 = 2, int tier1 = 2, int tier2 = 1, int tier3 = 1) => new(
        new Dictionary<InferenceTier, int>
        {
            [InferenceTier.Tier0DeterministicRules] = tier0,
            [InferenceTier.Tier1LightweightLocalMl] = tier1,
            [InferenceTier.Tier2GpuAcceleratedVision] = tier2,
            [InferenceTier.Tier3ExpensiveLocalReasoning] = tier3
        });

    [Fact]
    public void EnqueueWithinCapacitySucceeds()
    {
        var queue = NewQueue();
        QueueAdmissionResult result = queue.TryEnqueue(Job("a", InferenceTier.Tier1LightweightLocalMl));

        Assert.True(result.Accepted);
        Assert.Equal(QueueAdmission.Enqueued, result.Outcome);
        Assert.Equal(1, queue.Count(InferenceTier.Tier1LightweightLocalMl));
    }

    [Fact]
    public void JobBeyondCapacityIsRejectedExplicitlyNotSilentlyDropped()
    {
        var queue = NewQueue(tier2: 1);
        QueueAdmissionResult first = queue.TryEnqueue(Job("a", InferenceTier.Tier2GpuAcceleratedVision));
        QueueAdmissionResult second = queue.TryEnqueue(Job("b", InferenceTier.Tier2GpuAcceleratedVision));

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Equal(QueueAdmission.Rejected, second.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(second.Explanation));
        Assert.Contains("full", second.Explanation, StringComparison.OrdinalIgnoreCase);
        // The queue must not have silently accepted it under the surface.
        Assert.Equal(1, queue.Count(InferenceTier.Tier2GpuAcceleratedVision));
    }

    [Fact]
    public void TierCapacitiesAreIsolatedFromEachOther()
    {
        var queue = NewQueue(tier2: 1, tier3: 1);
        queue.TryEnqueue(Job("a", InferenceTier.Tier2GpuAcceleratedVision));
        QueueAdmissionResult tier2Overflow = queue.TryEnqueue(Job("b", InferenceTier.Tier2GpuAcceleratedVision));
        QueueAdmissionResult tier3Admission = queue.TryEnqueue(Job("c", InferenceTier.Tier3ExpensiveLocalReasoning));

        Assert.False(tier2Overflow.Accepted);
        Assert.True(tier3Admission.Accepted);
    }

    [Fact]
    public void TryEnqueueNeverThrowsWhenFullItReturnsAStructuredRejection()
    {
        var queue = NewQueue(tier0: 0);
        QueueAdmissionResult result = queue.TryEnqueue(Job("a", InferenceTier.Tier0DeterministicRules));

        Assert.False(result.Accepted);
    }

    [Fact]
    public void DequeueIsFifoPerTier()
    {
        var queue = NewQueue(tier1: 5);
        queue.TryEnqueue(Job("first", InferenceTier.Tier1LightweightLocalMl));
        queue.TryEnqueue(Job("second", InferenceTier.Tier1LightweightLocalMl));

        Assert.True(queue.TryDequeue(InferenceTier.Tier1LightweightLocalMl, out InferenceJob? a));
        Assert.True(queue.TryDequeue(InferenceTier.Tier1LightweightLocalMl, out InferenceJob? b));
        Assert.False(queue.TryDequeue(InferenceTier.Tier1LightweightLocalMl, out InferenceJob? c));

        Assert.Equal("first", a!.Id);
        Assert.Equal("second", b!.Id);
        Assert.Null(c);
    }

    [Fact]
    public void ConstructorRejectsNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundedTierQueue(new Dictionary<InferenceTier, int> { [InferenceTier.Tier0DeterministicRules] = -1 }));
    }

    [Fact]
    public void ConstructorRejectsEmptyCapacityMap()
    {
        Assert.Throws<ArgumentException>(() => new BoundedTierQueue(new Dictionary<InferenceTier, int>()));
    }
}
