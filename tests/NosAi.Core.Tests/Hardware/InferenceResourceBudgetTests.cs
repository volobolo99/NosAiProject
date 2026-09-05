using NosAi.Core.Hardware;
using Xunit;

namespace NosAi.Core.Tests.Hardware;

public sealed class InferenceResourceBudgetTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void UnknownPlan_EveryTierBudgetIsExplicitlyUnknown()
    {
        var plan = InferenceResourceBudgetPlan.Unknown("hardware_snapshot_unknown", Now);

        foreach (var tier in new[]
                 {
                     InferenceTier.Tier0DeterministicRules,
                     InferenceTier.Tier1LightweightLocalMl,
                     InferenceTier.Tier2GpuAcceleratedVision,
                     InferenceTier.Tier3ExpensiveLocalReasoning
                 })
        {
            var budget = plan.GetBudget(tier);
            Assert.Equal(tier, budget.Tier);
            Assert.False(budget.CpuBudgetPercent.HasValue);
            Assert.False(budget.GpuBudgetPercent.HasValue);
            Assert.False(budget.VramBudgetMb.HasValue);
            Assert.False(budget.RamBudgetMb.HasValue);
            Assert.False(budget.ThermalHeadroomCelsius.HasValue);
            Assert.False(budget.SsdIoBudgetMbPerSecond.HasValue);
            Assert.False(budget.LatencyBudgetMs.HasValue);
        }
    }

    [Fact]
    public void GetBudget_ReturnsTheMatchingTierField()
    {
        var tier0 = TierResourceBudget.Unknown(InferenceTier.Tier0DeterministicRules, "r");
        var tier1 = new TierResourceBudget(
            InferenceTier.Tier1LightweightLocalMl,
            ClassifiedValue<double>.Live(10, Now),
            ClassifiedValue<double>.Live(0, Now),
            ClassifiedValue<long>.Live(0, Now),
            ClassifiedValue<long>.Live(512, Now),
            ClassifiedValue<double>.Live(20, Now),
            ClassifiedValue<double>.Live(50, Now),
            ClassifiedValue<double>.Live(16, Now));
        var tier2 = TierResourceBudget.Unknown(InferenceTier.Tier2GpuAcceleratedVision, "r");
        var tier3 = TierResourceBudget.Unknown(InferenceTier.Tier3ExpensiveLocalReasoning, "r");

        var plan = new InferenceResourceBudgetPlan(Now, tier0, tier1, tier2, tier3);

        Assert.Same(tier1, plan.GetBudget(InferenceTier.Tier1LightweightLocalMl));
        Assert.Equal(512, plan.GetBudget(InferenceTier.Tier1LightweightLocalMl).RamBudgetMb.Value);
    }

    [Fact]
    public void GetBudget_ThrowsForAnUnrecognizedTierValue()
    {
        var plan = InferenceResourceBudgetPlan.Unknown("r", Now);
        var invalidTier = (InferenceTier)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.GetBudget(invalidTier));
    }

    [Fact]
    public void JobBudgetRequest_ValidRequestIsStructurallyValid()
    {
        var request = new InferenceJobBudgetRequest(
            "detector-001",
            InferenceTier.Tier2GpuAcceleratedVision,
            InferenceJobPriority.Normal,
            TimeSpan.FromMilliseconds(50),
            EstimatedCpuPercent: 5,
            EstimatedGpuPercent: 20,
            EstimatedRamMb: 128,
            EstimatedVramMb: 512,
            MinimumConfidence: 0.7,
            FallbackTier: InferenceTier.Tier1LightweightLocalMl);

        Assert.True(request.IsStructurallyValid);
    }

    [Theory]
    [InlineData("", 5.0, 5.0, 10L, 10L, 0.5, true)]
    [InlineData("job", -1.0, 5.0, 10L, 10L, 0.5, true)]
    [InlineData("job", 5.0, -1.0, 10L, 10L, 0.5, true)]
    [InlineData("job", 5.0, 5.0, -1L, 10L, 0.5, true)]
    [InlineData("job", 5.0, 5.0, 10L, -1L, 0.5, true)]
    [InlineData("job", 5.0, 5.0, 10L, 10L, 1.5, true)]
    [InlineData("job", 5.0, 5.0, 10L, 10L, -0.1, true)]
    public void JobBudgetRequest_InvalidFieldsFailStructuralValidation(
        string jobId, double cpu, double gpu, long ram, long vram, double confidence, bool expectInvalid)
    {
        var request = new InferenceJobBudgetRequest(
            jobId,
            InferenceTier.Tier1LightweightLocalMl,
            InferenceJobPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            cpu,
            gpu,
            ram,
            vram,
            confidence,
            FallbackTier: null);

        Assert.Equal(!expectInvalid, request.IsStructurallyValid);
    }

    [Fact]
    public void JobBudgetRequest_FallbackTierMustBeCheaperThanPrimaryTier()
    {
        var sameTier = new InferenceJobBudgetRequest(
            "job",
            InferenceTier.Tier2GpuAcceleratedVision,
            InferenceJobPriority.Normal,
            TimeSpan.FromMilliseconds(10),
            1, 1, 1, 1, 0.5,
            FallbackTier: InferenceTier.Tier2GpuAcceleratedVision);

        var moreExpensiveFallback = sameTier with { FallbackTier = InferenceTier.Tier3ExpensiveLocalReasoning };
        var cheaperFallback = sameTier with { FallbackTier = InferenceTier.Tier0DeterministicRules };

        Assert.False(sameTier.IsStructurallyValid);
        Assert.False(moreExpensiveFallback.IsStructurallyValid);
        Assert.True(cheaperFallback.IsStructurallyValid);
    }

    [Fact]
    public void JobBudgetRequest_NegativeDeadlineIsInvalid()
    {
        var request = new InferenceJobBudgetRequest(
            "job",
            InferenceTier.Tier0DeterministicRules,
            InferenceJobPriority.Critical,
            TimeSpan.FromMilliseconds(-1),
            1, 1, 1, 1, 0.5,
            FallbackTier: null);

        Assert.False(request.IsStructurallyValid);
    }
}
