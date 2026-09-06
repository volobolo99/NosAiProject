using CoreHardware = NosAi.Core.Hardware;
using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.Runtime.Tests.Hardware.Gate;

/// <summary>
/// Covers <see cref="HardwareInferenceCapabilityGate"/>: it must call, and never
/// re-implement, <see cref="CoreHardware.InferenceTierFeasibility.CanRun"/>; it must
/// fail closed (refuse Tier 1-3) whenever the underlying
/// <see cref="CoreHardware.IHardwareCapabilityProvider"/> throws, returns null, or
/// reports a fully-Unknown snapshot; and Tier 0 must remain authorized in every one
/// of those cases, because that guarantee belongs to
/// <see cref="CoreHardware.InferenceTierFeasibility"/> itself (Safety/Guard/recovery
/// must never be starved by a hardware probe failure) and this gate must not weaken
/// or special-case it.
/// </summary>
public sealed class HardwareInferenceCapabilityGateTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CoreHardware.InferenceTier.Tier0DeterministicRules)]
    [InlineData(CoreHardware.InferenceTier.Tier1LightweightLocalMl)]
    [InlineData(CoreHardware.InferenceTier.Tier2GpuAcceleratedVision)]
    [InlineData(CoreHardware.InferenceTier.Tier3ExpensiveLocalReasoning)]
    public void TryAuthorize_AgreesWithInferenceTierFeasibility_ForAFullyCapableSnapshot(CoreHardware.InferenceTier tier)
    {
        var snapshot = FullyCapableSnapshot();
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(snapshot));

        var expected = CoreHardware.InferenceTierFeasibility.CanRun(tier, snapshot);
        var authorized = gate.TryAuthorize(tier, out var refusalReason);

        Assert.Equal(expected, authorized);
        if (authorized)
            Assert.Null(refusalReason);
        else
            Assert.False(string.IsNullOrWhiteSpace(refusalReason));
    }

    [Theory]
    [InlineData(CoreHardware.InferenceTier.Tier0DeterministicRules)]
    [InlineData(CoreHardware.InferenceTier.Tier1LightweightLocalMl)]
    [InlineData(CoreHardware.InferenceTier.Tier2GpuAcceleratedVision)]
    [InlineData(CoreHardware.InferenceTier.Tier3ExpensiveLocalReasoning)]
    public void TryAuthorize_AgreesWithInferenceTierFeasibility_ForAFullyUnknownSnapshot(CoreHardware.InferenceTier tier)
    {
        var snapshot = CoreHardware.HardwareCapabilitySnapshot.Unknown("no_probe_has_run_yet", Now);
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(snapshot));

        var expected = CoreHardware.InferenceTierFeasibility.CanRun(tier, snapshot);
        var authorized = gate.TryAuthorize(tier, out var refusalReason);

        Assert.Equal(expected, authorized);
        Assert.Equal(!expected, !string.IsNullOrWhiteSpace(refusalReason));
    }

    [Fact]
    public void TryAuthorize_Tier0_IsAuthorizedEvenWithAFullyUnknownSnapshot()
    {
        var snapshot = CoreHardware.HardwareCapabilitySnapshot.Unknown("no_probe_has_run_yet", Now);
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(snapshot));

        var authorized = gate.TryAuthorize(CoreHardware.InferenceTier.Tier0DeterministicRules, out var refusalReason);

        Assert.True(authorized);
        Assert.Null(refusalReason);
    }

    [Theory]
    [InlineData(CoreHardware.InferenceTier.Tier1LightweightLocalMl)]
    [InlineData(CoreHardware.InferenceTier.Tier2GpuAcceleratedVision)]
    [InlineData(CoreHardware.InferenceTier.Tier3ExpensiveLocalReasoning)]
    public void TryAuthorize_RefusesEveryTierAboveZero_WhenTheProviderThrows(CoreHardware.InferenceTier tier)
    {
        var gate = new HardwareInferenceCapabilityGate(new ThrowingCapabilityProvider());

        var authorized = gate.TryAuthorize(tier, out var refusalReason);

        Assert.False(authorized);
        Assert.NotNull(refusalReason);
    }

    [Fact]
    public void TryAuthorize_Tier0_IsStillAuthorized_WhenTheProviderThrows()
    {
        // This is not a bypass added by the gate: HardwareCapabilitySnapshot.Unknown
        // (the fail-closed stand-in used when the provider throws) already makes
        // InferenceTierFeasibility.CanRun return true for Tier 0 unconditionally, and
        // this test exists to pin that behaviour down explicitly rather than leave it
        // implicit.
        var gate = new HardwareInferenceCapabilityGate(new ThrowingCapabilityProvider());

        var authorized = gate.TryAuthorize(CoreHardware.InferenceTier.Tier0DeterministicRules, out var refusalReason);

        Assert.True(authorized);
        Assert.Null(refusalReason);
    }

    [Fact]
    public void TryAuthorize_RefusalReason_NamesTheExceptionWhenTheProviderThrows()
    {
        var gate = new HardwareInferenceCapabilityGate(new ThrowingCapabilityProvider());

        gate.TryAuthorize(CoreHardware.InferenceTier.Tier1LightweightLocalMl, out var refusalReason);

        Assert.Contains("hardware_capability_provider_threw", refusalReason, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), refusalReason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAuthorize_RefusesEveryTierAboveZero_WhenTheProviderReturnsNull()
    {
        // A provider is not supposed to return null (GetSnapshot's return type is
        // non-nullable), but C# nullability annotations are not enforced at runtime,
        // and a defect in a future provider implementation must still fail closed
        // here rather than throwing a NullReferenceException out of the gate.
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(() => null!));

        var tier1 = gate.TryAuthorize(CoreHardware.InferenceTier.Tier1LightweightLocalMl, out var reason1);
        var tier0 = gate.TryAuthorize(CoreHardware.InferenceTier.Tier0DeterministicRules, out var reason0);

        Assert.False(tier1);
        Assert.NotNull(reason1);
        Assert.True(tier0);
        Assert.Null(reason0);
    }

    [Fact]
    public void TryAuthorize_RefusalReason_NamesTheUnknownFieldRelevantToTheTier()
    {
        // GPU unconfirmed, everything else fully known: Tier 2 must refuse and the
        // reason must point at the GPU model specifically, not a generic message,
        // without this gate re-deriving InferenceTierFeasibility's own thresholds.
        var snapshot = FullyCapableSnapshot() with
        {
            Gpu = CoreHardware.GpuCapability.Unknown("no_gpu_detected")
        };
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(snapshot));

        var authorized = gate.TryAuthorize(CoreHardware.InferenceTier.Tier2GpuAcceleratedVision, out var refusalReason);

        Assert.False(authorized);
        Assert.Contains("gpu.model", refusalReason, StringComparison.Ordinal);
        Assert.Contains("no_gpu_detected", refusalReason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAuthorize_RefusalReason_ReportsBelowThreshold_WhenEveryRelevantFieldIsKnownButInsufficient()
    {
        // Every field Tier 1 cares about is known (not Unknown) but the core count is
        // simply too low: the refusal reason must say "below threshold", not claim a
        // field is unknown when it is not.
        var baseline = FullyCapableSnapshot();
        var snapshot = baseline with { Cpu = baseline.Cpu with { LogicalCores = CoreHardware.ClassifiedValue<int>.Live(1, Now) } };
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(snapshot));

        var authorized = gate.TryAuthorize(CoreHardware.InferenceTier.Tier1LightweightLocalMl, out var refusalReason);

        Assert.False(authorized);
        Assert.Contains("below_required_hardware_threshold", refusalReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ThrowsOnNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new HardwareInferenceCapabilityGate(null!));
    }

    [Fact]
    public void TryAuthorize_ThrowsOnUnrecognizedTierValue_RefusesRatherThanPropagating()
    {
        var gate = new HardwareInferenceCapabilityGate(new FakeCapabilityProvider(FullyCapableSnapshot()));

        var authorized = gate.TryAuthorize((CoreHardware.InferenceTier)999, out var refusalReason);

        Assert.False(authorized);
        Assert.Contains("unrecognized_inference_tier", refusalReason, StringComparison.Ordinal);
    }

    private static CoreHardware.HardwareCapabilitySnapshot FullyCapableSnapshot() => new(
        Now,
        new CoreHardware.CpuCapability(
            CoreHardware.ClassifiedValue<string>.Live("AMD Ryzen 7 260", Now),
            CoreHardware.ClassifiedValue<int>.Live(16, Now),
            CoreHardware.ClassifiedValue<int>.Live(8, Now),
            CoreHardware.ClassifiedValue<double>.Live(15.0, Now),
            CoreHardware.ClassifiedValue<double>.Live(55.0, Now),
            CoreHardware.ClassifiedValue<double>.Live(3500.0, Now)),
        new CoreHardware.GpuCapability(
            CoreHardware.ClassifiedValue<string>.Live("NVIDIA GeForce RTX 5060 Laptop GPU", Now),
            CoreHardware.ClassifiedValue<string>.Live("32.0.15.6614", Now),
            CoreHardware.ClassifiedValue<long>.Live(8192, Now),
            CoreHardware.ClassifiedValue<long>.Live(1024, Now),
            CoreHardware.ClassifiedValue<double>.Live(10.0, Now),
            CoreHardware.ClassifiedValue<double>.Live(55.0, Now),
            CoreHardware.ClassifiedValue<double>.Live(60.0, Now)),
        CoreHardware.NpuCapability.Unknown("no_npu_detected"),
        new CoreHardware.MemoryCapability(
            CoreHardware.ClassifiedValue<long>.Live(16384, Now),
            CoreHardware.ClassifiedValue<long>.Live(12000, Now),
            CoreHardware.ClassifiedValue<long>.Live(256, Now)),
        CoreHardware.StorageCapability.Unknown("storage_not_relevant_to_this_test"),
        new CoreHardware.ThermalCapability(
            CoreHardware.ClassifiedValue<double>.Live(55.0, Now),
            CoreHardware.ClassifiedValue<double>.Live(60.0, Now),
            CoreHardware.ClassifiedValue<CoreHardware.HardwareThrottleState>.Live(CoreHardware.HardwareThrottleState.Nominal, Now),
            CoreHardware.ClassifiedValue<CoreHardware.PowerSourceKind>.Live(CoreHardware.PowerSourceKind.ACPower, Now)));

    private sealed class FakeCapabilityProvider : CoreHardware.IHardwareCapabilityProvider
    {
        private readonly Func<CoreHardware.HardwareCapabilitySnapshot> _factory;

        public FakeCapabilityProvider(CoreHardware.HardwareCapabilitySnapshot snapshot) : this(() => snapshot)
        {
        }

        public FakeCapabilityProvider(Func<CoreHardware.HardwareCapabilitySnapshot> factory) => _factory = factory;

        public CoreHardware.HardwareCapabilitySnapshot GetSnapshot() => _factory();
    }

    private sealed class ThrowingCapabilityProvider : CoreHardware.IHardwareCapabilityProvider
    {
        public CoreHardware.HardwareCapabilitySnapshot GetSnapshot()
            => throw new InvalidOperationException("simulated hardware capability provider failure for AP-00/A4 tests");
    }
}
