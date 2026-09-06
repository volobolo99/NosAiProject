using NosAi.Core.Hardware;
using Xunit;

namespace NosAi.Core.Tests.Hardware;

/// <summary>
/// AP-00 / A5 audit coverage: additional <see cref="InferenceTierFeasibility"/>
/// boundary values not already exercised by <see cref="InferenceTierFeasibilityTests"/>
/// (Tier 2's free-VRAM and total-RAM floors; every Tier 3 floor; negative/absurd
/// hardware readings), plus a real integer-overflow defect this audit found in
/// <see cref="GpuCapability.FreeVramMb"/> while constructing those absurd-value
/// cases.
/// </summary>
public sealed class InferenceTierFeasibilityAdditionalBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 10, 30, 0, DateTimeKind.Utc);

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

    // ---- Tier 2 floors not yet boundary-tested elsewhere ----------------

    [Theory]
    [InlineData(InferenceTierThresholds.Tier2MinFreeVramMb, true)]
    [InlineData(InferenceTierThresholds.Tier2MinFreeVramMb - 1, false)]
    public void Tier2_FreeVramBoundary(long freeVramMb, bool expected)
    {
        long totalVramMb = InferenceTierThresholds.Tier2MinTotalVramMb + freeVramMb;
        var snapshot = Capable(
            cores: 8,
            totalRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier2MinTotalRamMb,
            gpuKnown: true,
            totalVramMb: totalVramMb,
            usedVramMb: totalVramMb - freeVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier2MinTotalRamMb, true)]
    [InlineData(InferenceTierThresholds.Tier2MinTotalRamMb - 1, false)]
    public void Tier2_TotalRamBoundary(long totalRamMb, bool expected)
    {
        var snapshot = Capable(
            cores: 8,
            totalRamMb: totalRamMb,
            availableRamMb: totalRamMb,
            gpuKnown: true,
            totalVramMb: InferenceTierThresholds.Tier2MinTotalVramMb,
            usedVramMb: InferenceTierThresholds.Tier2MinTotalVramMb - InferenceTierThresholds.Tier2MinFreeVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
    }

    // ---- Tier 3 floors: none of these six were boundary-tested elsewhere -

    [Theory]
    [InlineData(InferenceTierThresholds.Tier3MinTotalRamMb, true)]
    [InlineData(InferenceTierThresholds.Tier3MinTotalRamMb - 1, false)]
    public void Tier3_MinTotalRamBoundary_OnTheCpuFallbackPath(long totalRamMb, bool expected)
    {
        var snapshot = Capable(
            cores: InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores,
            totalRamMb: totalRamMb,
            availableRamMb: InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores, true)]
    [InlineData(InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores - 1, false)]
    public void Tier3_CpuFallbackLogicalCoresBoundary(int cores, bool expected)
    {
        var snapshot = Capable(
            cores: cores,
            totalRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb, true)]
    [InlineData(InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb - 1, false)]
    public void Tier3_CpuFallbackAvailableRamBoundary(long availableRamMb, bool expected)
    {
        var snapshot = Capable(
            cores: InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores,
            totalRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            availableRamMb: availableRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier3GpuPathMinTotalVramMb, InferenceTierThresholds.Tier3GpuPathMinFreeVramMb, true)]
    [InlineData(InferenceTierThresholds.Tier3GpuPathMinTotalVramMb - 1, InferenceTierThresholds.Tier3GpuPathMinFreeVramMb, false)]
    public void Tier3_GpuPathTotalVramBoundary(long totalVramMb, long freeVramMb, bool expected)
    {
        var snapshot = Capable(
            cores: 4, // deliberately below the CPU-fallback floor so only the GPU path can qualify
            totalRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            gpuKnown: true,
            totalVramMb: totalVramMb,
            usedVramMb: totalVramMb - freeVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    [Theory]
    [InlineData(InferenceTierThresholds.Tier3GpuPathMinFreeVramMb, true)]
    [InlineData(InferenceTierThresholds.Tier3GpuPathMinFreeVramMb - 1, false)]
    public void Tier3_GpuPathFreeVramBoundary(long freeVramMb, bool expected)
    {
        long totalVramMb = InferenceTierThresholds.Tier3GpuPathMinTotalVramMb + freeVramMb;
        var snapshot = Capable(
            cores: 4, // deliberately below the CPU-fallback floor so only the GPU path can qualify
            totalRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier3MinTotalRamMb,
            gpuKnown: true,
            totalVramMb: totalVramMb,
            usedVramMb: totalVramMb - freeVramMb,
            throttle: HardwareThrottleState.Nominal);

        Assert.Equal(expected, InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    // ---- Negative / absurd hardware readings -----------------------------
    // ClassifiedValue<T> performs no range validation (by design -- it is a
    // provenance wrapper, not a domain-value validator), so nothing today
    // stops a broken/corrupted probe from reporting a "Live" negative core
    // count, negative RAM, etc. These tests pin down that
    // InferenceTierFeasibility.CanRun still fails closed (never authorizes)
    // for the ordinary comparison-based thresholds when fed such values --
    // AtLeast<T> is a plain CompareTo, so a negative value simply compares
    // below any positive threshold and correctly disqualifies the tier.

    [Fact]
    public void NegativeLogicalCores_NeverSatisfiesTier1()
    {
        var snapshot = Capable(
            cores: -4,
            totalRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            availableRamMb: InferenceTierThresholds.Tier1MinTotalRamMb,
            gpuKnown: false,
            totalVramMb: 0,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));
    }

    [Fact]
    public void NegativeTotalRam_NeverSatisfiesAnyTierAboveZero()
    {
        var snapshot = Capable(
            cores: 16,
            totalRamMb: -1,
            availableRamMb: -1,
            gpuKnown: true,
            totalVramMb: InferenceTierThresholds.Tier3GpuPathMinTotalVramMb,
            usedVramMb: 0,
            throttle: HardwareThrottleState.Nominal);

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier1LightweightLocalMl, snapshot));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    /// <summary>
    /// REAL DEFECT found during this AP-00/A5 audit (not introduced by this
    /// test): <see cref="GpuCapability.FreeVramMb"/> computes
    /// <c>Math.Max(0, TotalVramMb.Value - UsedVramMb.Value)</c> using plain,
    /// unchecked <c>long</c> arithmetic. Nothing in <see cref="ClassifiedValue{T}"/>
    /// or <see cref="GpuCapability"/> prevents a corrupted/nonsensical probe
    /// reading from reporting a negative <c>TotalVramMb</c> as "Live", and
    /// when it does, the subtraction can silently overflow the signed 64-bit
    /// range and wrap around to a large POSITIVE number instead of throwing,
    /// saturating, or staying non-positive -- e.g. <c>long.MinValue - 1</c>
    /// wraps to <c>long.MaxValue</c> in C#'s default unchecked arithmetic.
    ///
    /// This assertion documents the CORRECT expectation for
    /// <see cref="GpuCapability.FreeVramMb"/> in isolation (a non-positive
    /// <c>TotalVramMb</c> can never derive a usable positive "free VRAM"
    /// reading) and is therefore expected to FAIL until that computation is
    /// changed to reject/clamp an already-negative <c>TotalVramMb</c> (or use
    /// checked arithmetic and treat an overflow as Unknown) rather than
    /// deriving a value from operands that are already impossible.
    ///
    /// See <see cref="CanRun_IsNotFooledByThatOverflow_OnlyBecauseOfTheIndependentTotalVramFloorCheck"/>
    /// immediately below for the important nuance this audit also verified:
    /// this raw overflow does NOT currently let
    /// <see cref="InferenceTierFeasibility.CanRun"/> wrongly authorize a GPU
    /// tier, purely because <c>CanRunTier2</c>/<c>CanRunTier3</c> separately
    /// require <c>TotalVramMb</c> itself to be at least the tier's own
    /// floor -- and no floor is satisfied by a negative number. That
    /// protection is incidental (a side effect of an unrelated check), not a
    /// deliberate guard against this overflow, so it must not be read as
    /// "this defect is harmless": any other/future consumer that reads
    /// <see cref="GpuCapability.FreeVramMb"/> directly without independently
    /// re-checking <c>TotalVramMb</c>'s sign would be exposed to it.
    /// Fixing <c>src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs</c> is
    /// out of A5's file ownership (it belongs to A1); this is handed to A6.
    /// </summary>
    [Fact]
    public void GpuCapability_FreeVramMb_OverflowsToALargePositiveValue_ForACorruptedNegativeTotalVram_KnownA1RobustnessGap()
    {
        var gpu = new GpuCapability(
            ClassifiedValue<string>.Live("GPU", Now),
            ClassifiedValue<string>.Live("driver", Now),
            ClassifiedValue<long>.Live(long.MinValue, Now), // corrupted/nonsensical negative total VRAM
            ClassifiedValue<long>.Live(1, Now),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"));

        ClassifiedValue<long> free = gpu.FreeVramMb;

        Assert.False(
            free.HasValue && free.Value > 0,
            $"GpuCapability.FreeVramMb derived a positive 'free VRAM' value ({(free.HasValue ? free.Value.ToString() : "n/a")}) " +
            "from a TotalVramMb that was itself negative (long.MinValue) and therefore already nonsensical. " +
            "Actual observed value confirms the unchecked long subtraction overflowed and wrapped around " +
            "(TotalVramMb - UsedVramMb == long.MinValue - 1, which wraps to long.MaxValue) instead of the " +
            "computation staying non-positive/Unknown for impossible input. This is a real, pre-existing " +
            "robustness gap in src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs found during the AP-00/A5 " +
            "test audit -- NOT introduced by this test and NOT fixable from within A5's file ownership " +
            "(src/NosAi.Core/Hardware/ belongs to A1). Hand off to A6.");
    }

    /// <summary>
    /// Companion to the overflow defect documented immediately above.
    /// <b>Updated by A6 after fixing that defect</b> (see
    /// <c>src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs</c>,
    /// <see cref="GpuCapability.FreeVramMb"/> now rejects a negative
    /// <c>TotalVramMb</c>/<c>UsedVramMb</c> before subtracting, instead of
    /// deriving a value from operands that are already impossible): this test
    /// used to pin down that the overflow, while real, happened not to be
    /// reachable through <see cref="InferenceTierFeasibility.CanRun"/> only
    /// because of an unrelated, independent floor check on
    /// <c>TotalVramMb</c> itself. Now that the overflow can no longer happen
    /// at all, <see cref="GpuCapability.FreeVramMb"/> for this corrupted
    /// input is <see cref="DataSourceKind.Unknown"/> rather than a wrapped
    /// <see cref="long.MaxValue"/>, and <c>CanRun</c> still correctly refuses
    /// Tier2/Tier3 -- now straightforwardly because a corrupted/negative
    /// total VRAM reading fails the ordinary floor check, with no reliance on
    /// an unrelated coincidence. Kept as a regression test for both facts.
    /// </summary>
    [Fact]
    public void CanRun_IsNotFooledByThatOverflow_OnlyBecauseOfTheIndependentTotalVramFloorCheck()
    {
        var gpu = new GpuCapability(
            ClassifiedValue<string>.Live("GPU", Now),
            ClassifiedValue<string>.Live("driver", Now),
            ClassifiedValue<long>.Live(long.MinValue, Now), // same corrupted reading as the test above
            ClassifiedValue<long>.Live(1, Now),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"));

        // The overflow this test used to pin down is fixed: a negative
        // TotalVramMb now makes FreeVramMb explicitly Unknown, never a
        // fabricated large positive number.
        Assert.False(gpu.FreeVramMb.HasValue);

        var snapshot = new HardwareCapabilitySnapshot(
            Now,
            new CpuCapability(
                ClassifiedValue<string>.Live("AMD Ryzen (detected)", Now),
                ClassifiedValue<int>.Live(4, Now),
                ClassifiedValue<int>.Live(2, Now),
                ClassifiedValue<double>.Live(15.0, Now),
                ClassifiedValue<double>.Live(55.0, Now),
                ClassifiedValue<double>.Live(3500.0, Now)),
            gpu,
            NpuCapability.Unknown("no_npu_detected"),
            new MemoryCapability(
                ClassifiedValue<long>.Live(InferenceTierThresholds.Tier2MinTotalRamMb, Now),
                ClassifiedValue<long>.Live(InferenceTierThresholds.Tier2MinTotalRamMb, Now),
                ClassifiedValue<long>.Live(256, Now)),
            StorageCapability.Unknown("storage_not_relevant_to_this_test"),
            new ThermalCapability(
                ClassifiedValue<double>.Live(55.0, Now),
                ClassifiedValue<double>.Live(60.0, Now),
                ClassifiedValue<HardwareThrottleState>.Live(HardwareThrottleState.Nominal, Now),
                ClassifiedValue<PowerSourceKind>.Live(PowerSourceKind.ACPower, Now)));

        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier2GpuAcceleratedVision, snapshot));
        Assert.False(InferenceTierFeasibility.CanRun(InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }
}
