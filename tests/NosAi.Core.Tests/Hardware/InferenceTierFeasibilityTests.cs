using NosAi.Core.Hardware;
using Xunit;

namespace NosAi.Core.Tests.Hardware;

public sealed class InferenceTierFeasibilityTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Tier0_AlwaysRuns_EvenWithFullyUnknownHardware()
    {
        var unknown = HardwareCapabilitySnapshot.Unknown("no_probe_has_run_yet", Now);

        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier0DeterministicRules, unknown));
    }

    [Fact]
    public void FullyUnknownHardware_CannotRunAnyTierAboveZero()
    {
        var unknown = HardwareCapabilitySnapshot.Unknown("no_probe_has_run_yet", Now);

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, unknown));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, unknown));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, unknown));
    }

    [Fact]
    public void FullyCapableHardware_CanRunEveryTier()
    {
        var snapshot = Capable(
            cores: 16,
            totalRamMb: 16384,
            availableRamMb: 12000,
            gpuKnown: true,
            totalVramMb: 8192,
            usedVramMb: 1024,
            throttle: HardwareThrottleState.Nominal);

        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier0DeterministicRules, snapshot));
        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));
        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Fact]
    public void HigherTiers_RequireStrictlyMoreResourcesThanLowerTiers()
    {
        // A machine with a modest CPU-only profile (no GPU, few cores) can
        // run Tier 0/1 but never the GPU-bound Tier 2 or the heavier Tier 3.
        var cpuOnlyModest = Capable(
            cores: 4,
            totalRamMb: 8192,
            availableRamMb: 5000,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier0DeterministicRules, cpuOnlyModest));
        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, cpuOnlyModest));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, cpuOnlyModest));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, cpuOnlyModest));

        // A GPU-capable machine that satisfies Tier 2's floor exactly but not
        // Tier 3's stricter VRAM floor, and whose core count also falls short
        // of Tier 3's CPU-fallback floor (so that path cannot rescue it
        // either): Tier 2 runs, Tier 3 does not.
        var tier2Only = Capable(
            cores: InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores - 1,
            totalRamMb: 16384,
            availableRamMb: 12000,
            gpuKnown: true,
            totalVramMb: InferenceTierThresholds.Tier2MinTotalVramMb,
            usedVramMb: InferenceTierThresholds.Tier2MinTotalVramMb - InferenceTierThresholds.Tier2MinFreeVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, tier2Only));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, tier2Only));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier1MinLogicalCores, true)]
    [InlineData(InferenceTierThresholds.Tier1MinLogicalCores - 1, false)]
    public void Tier1_LogicalCoresBoundary(int cores, bool expected)
    {
        var snapshot = Capable(
            cores: cores,
            totalRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier1MinTotalRamMb, true)]
    [InlineData(InferenceTierThresholds.Tier1MinTotalRamMb - 1, false)]
    public void Tier1_TotalRamBoundary(long totalRamMb, bool expected)
    {
        var snapshot = Capable(
            cores: InferenceTierThresholds.Tier1MinLogicalCores,
            totalRamMb: totalRamMb,
            availableRamMb: totalRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));
    }

    [Theory]
    [InlineData(HardwareThrottleState.Nominal, true)]
    [InlineData(HardwareThrottleState.Elevated, true)]
    [InlineData(HardwareThrottleState.Throttling, false)]
    [InlineData(HardwareThrottleState.Critical, false)]
    public void Tier1_ThrottleStateBoundary(HardwareThrottleState throttle, bool expected)
    {
        var snapshot = Capable(
            cores: InferenceTierThresholds.Tier1MinLogicalCores,
            totalRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: throttle);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));

        // Tier 0 must be entirely unaffected by thermal state.
        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier0DeterministicRules, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier2MinTotalVramMb, InferenceTierThresholds.Tier2MinFreeVramMb, true)]
    [InlineData(InferenceTierThresholds.Tier2MinTotalVramMb - 1, InferenceTierThresholds.Tier2MinFreeVramMb, false)]
    public void Tier2_TotalVramBoundary(long totalVramMb, long freeVramMb, bool expected)
    {
        var usedVramMb = totalVramMb - freeVramMb;
        var snapshot = Capable(
            cores: 8,
            totalRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            gpuKnown: true,
            totalVramMb: totalVramMb,
            usedVramMb: usedVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
    }

    [Fact]
    public void Tier2_UnconfirmedGpu_NeverSatisfiesTheGpuRequirement()
    {
        // Even if VRAM numbers happen to be populated, an unconfirmed GPU
        // model (Unknown) must not be treated as "a GPU is present".
        var snapshot = Capable(
            cores: 8,
            totalRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            gpuKnown: false,
            totalVramMb: InferenceTierThresholds.Tier2MinTotalVramMb,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
    }

    [Fact]
    public void Tier3_CpuFallbackPath_RunsWithoutAGpuWhenCoresAndRamAreSufficient()
    {
        var snapshot = Capable(
            cores: InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores,
            totalRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.True(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Fact]
    public void Tier3_RequiresFullyNominalThermalState_ElevatedIsNotEnough()
    {
        var snapshot = Capable(
            cores: 16,
            totalRamMb: 16384,
            availableRamMb: 12000,
            gpuKnown: true,
            totalVramMb: InferenceTierThresholds.Tier3GpuPathMinTotalVramMb,
            usedVramMb: InferenceTierThresholds.Tier3GpuPathMinTotalVramMb - InferenceTierThresholds.Tier3GpuPathMinFreeVramMb,
            throttle: HardwareThrottleState.Elevated);

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Fact]
    public void CanRun_ThrowsOnNullSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InferenceTierFeasibility.CanRun(InferenceTier.Tier0DeterministicRules, null!));
    }

    private static HardwareCapabilitySnapshot Capable(
        int cores,
        long totalRamMb,
        long availableRamMb,
        bool gpuKnown,
        long totalVramMb,
        long usedVramMb,
        HardwareThrottleState throttle)
    {
        var gpu = gpuKnown
            ? new GpuCapability(
                ClassifiedValue<string>.Live("NVIDIA GeForce RTX 5060 Laptop GPU", Now),
                ClassifiedValue<string>.Live("32.0.15.6614", Now),
                ClassifiedValue<long>.Live(totalVramMb, Now),
                ClassifiedValue<long>.Live(usedVramMb, Now),
                ClassifiedValue<double>.Live(10.0, Now),
                ClassifiedValue<double>.Live(55.0, Now),
                ClassifiedValue<double>.Live(60.0, Now))
            : GpuCapability.Unknown("no_gpu_detected");

        return new HardwareCapabilitySnapshot(
            Now,
            new CpuCapability(
                ClassifiedValue<string>.Live("AMD Ryzen (detected)", Now),
                ClassifiedValue<int>.Live(cores, Now),
                ClassifiedValue<int>.Live(Math.Max(1, cores / 2), Now),
                ClassifiedValue<double>.Live(15.0, Now),
                ClassifiedValue<double>.Live(55.0, Now),
                ClassifiedValue<double>.Live(3500.0, Now)),
            gpu,
            NpuCapability.Unknown("no_npu_detected"),
            new MemoryCapability(
                ClassifiedValue<long>.Live(totalRamMb, Now),
                ClassifiedValue<long>.Live(availableRamMb, Now),
                ClassifiedValue<long>.Live(256, Now)),
            StorageCapability.Unknown("storage_not_relevant_to_this_test"),
            new ThermalCapability(
                ClassifiedValue<double>.Live(55.0, Now),
                ClassifiedValue<double>.Live(60.0, Now),
                ClassifiedValue<HardwareThrottleState>.Live(throttle, Now),
                ClassifiedValue<PowerSourceKind>.Live(PowerSourceKind.ACPower, Now)));
    }
}
