using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

public sealed class InferenceJobValidationTests
{
    private static InferenceJob ValidJob(
        string id = "job-1",
        InferenceTier tier = InferenceTier.Tier2GpuAcceleratedVision,
        long deadline = 1_000,
        long durationMs = 10,
        double minConfidence = 0.5,
        FallbackStrategy? fallback = null) =>
        new(id, tier, JobPriority.Normal, deadline, durationMs, new ResourceCost(1, 1, 1, 1), minConfidence, fallback ?? FallbackStrategy.Reject());

    [Fact]
    public void ValidJobConstructsSuccessfully()
    {
        InferenceJob job = ValidJob();
        Assert.Equal("job-1", job.Id);
        Assert.Equal(InferenceTier.Tier2GpuAcceleratedVision, job.Tier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceIdThrows(string id) =>
        Assert.Throws<ArgumentException>(() => ValidJob(id: id));

    [Fact]
    public void NonPositiveDeadlineThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidJob(deadline: 0));

    [Fact]
    public void NegativeEstimatedDurationThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidJob(durationMs: -1));

    [Fact]
    public void NegativeEstimatedCostComponentThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceJob(
            "job-1", InferenceTier.Tier1LightweightLocalMl, JobPriority.Normal, 1_000, 10,
            new ResourceCost(-1, 0, 0, 0), 0.5, FallbackStrategy.Reject()));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ConfidenceOutsideUnitIntervalThrows(double confidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidJob(minConfidence: confidence));

    [Fact]
    public void NullFallbackThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new InferenceJob(
            "job-1", InferenceTier.Tier2GpuAcceleratedVision, JobPriority.Normal, 1_000, 10,
            new ResourceCost(1, 1, 1, 1), 0.5, null!));
    }

    [Fact]
    public void DegradeFallbackWithoutPlanThrows()
    {
        var fallback = new FallbackStrategy { Kind = FallbackKind.Degrade, Degradation = null };
        Assert.Throws<ArgumentException>(() => ValidJob(fallback: fallback));
    }

    [Fact]
    public void DegradeFallbackTargetingSameOrHigherTierThrows()
    {
        var plan = new DegradationPlan(InferenceTier.Tier3ExpensiveLocalReasoning, new ResourceCost(1, 1, 1, 1), 5, 0.1);
        var fallback = FallbackStrategy.DegradeTo(plan);

        Assert.Throws<ArgumentException>(() => ValidJob(tier: InferenceTier.Tier2GpuAcceleratedVision, fallback: fallback));
    }

    [Fact]
    public void DegradeFallbackTargetingStrictlyCheaperTierIsAccepted()
    {
        var plan = new DegradationPlan(InferenceTier.Tier0DeterministicRules, new ResourceCost(0, 0, 0, 0), 1, 0.1);
        var fallback = FallbackStrategy.DegradeTo(plan);

        InferenceJob job = ValidJob(tier: InferenceTier.Tier2GpuAcceleratedVision, fallback: fallback);
        Assert.Equal(FallbackKind.Degrade, job.Fallback.Kind);
    }

    [Fact]
    public void TryDegradeProducesSingleHopCandidateWithRejectFallback()
    {
        var plan = new DegradationPlan(InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 1, 0), 2, 0.2);
        InferenceJob original = ValidJob(tier: InferenceTier.Tier3ExpensiveLocalReasoning, fallback: FallbackStrategy.DegradeTo(plan));

        bool degradedOk = original.Fallback.TryDegrade(original, out InferenceJob degraded);

        Assert.True(degradedOk);
        Assert.Equal(InferenceTier.Tier1LightweightLocalMl, degraded.Tier);
        Assert.Equal(FallbackKind.Reject, degraded.Fallback.Kind);
    }

    [Fact]
    public void TryDegradeOnNonDegradeFallbackReturnsFalseAndOriginal()
    {
        InferenceJob original = ValidJob(fallback: FallbackStrategy.UseCachedResult("no fresher data"));

        bool degradedOk = original.Fallback.TryDegrade(original, out InferenceJob degraded);

        Assert.False(degradedOk);
        Assert.Same(original, degraded);
    }
}
