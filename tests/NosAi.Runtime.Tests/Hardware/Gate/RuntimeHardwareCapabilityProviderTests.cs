using CoreHardware = NosAi.Core.Hardware;
using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.Runtime.Tests.Hardware.Gate;

/// <summary>
/// Covers <see cref="RuntimeHardwareCapabilityProvider"/>: correct mapping of the
/// existing, already-verified Gate 1 hardware view
/// (<see cref="LiveHardwareTelemetry"/> / <see cref="IHardwareProbe"/>) into
/// <c>NosAi.Core.Hardware.HardwareCapabilitySnapshot</c>, explicit Unknown fallback
/// for every field the existing probe does not expose, defensive fail-closed
/// behaviour, and consistency with the existing
/// <see cref="CoreHardware.InferenceTierFeasibility"/> gate it feeds.
///
/// No real hardware/WMI access happens in this test class: every probe used here is
/// a fake or the existing non-throwing <see cref="FallbackHardwareProbe"/>, exactly
/// like the pattern already used by <c>Gate1TestRunner</c> for the same telemetry
/// path.
/// </summary>
public sealed class RuntimeHardwareCapabilityProviderTests
{
    private static readonly HardwareFingerprint FullFingerprint = new(
        Platform: "Win32NT",
        Cpu: "AMD Ryzen 7 260",
        LogicalCores: 16,
        RamMb: 16384,
        Gpu: "NVIDIA GeForce RTX 5060 Laptop GPU",
        GpuMemoryMb: 8192,
        DisplayRefreshHz: 165,
        OsVersion: "Microsoft Windows NT 10.0.26100.0");

    [Fact]
    public void GetSnapshot_WithFullFingerprint_MapsEveryProbedFieldAsLive()
    {
        var provider = new RuntimeHardwareCapabilityProvider(new LiveHardwareTelemetry(new FakeHardwareProbe(FullFingerprint)));

        var snapshot = provider.GetSnapshot();

        Assert.True(snapshot.Cpu.Model.HasValue);
        Assert.Equal("AMD Ryzen 7 260", snapshot.Cpu.Model.Value);
        Assert.Equal(CoreHardware.DataSourceKind.Live, snapshot.Cpu.Model.Source);

        Assert.True(snapshot.Cpu.LogicalCores.HasValue);
        Assert.Equal(16, snapshot.Cpu.LogicalCores.Value);

        Assert.True(snapshot.Memory.TotalRamMb.HasValue);
        Assert.Equal(16384L, snapshot.Memory.TotalRamMb.Value);

        Assert.True(snapshot.Memory.ProcessWorkingSetMb.HasValue);
        Assert.Equal(CoreHardware.DataSourceKind.Live, snapshot.Memory.ProcessWorkingSetMb.Source);

        Assert.True(snapshot.Gpu.Model.HasValue);
        Assert.Equal("NVIDIA GeForce RTX 5060 Laptop GPU", snapshot.Gpu.Model.Value);

        Assert.True(snapshot.Gpu.TotalVramMb.HasValue);
        Assert.Equal(8192L, snapshot.Gpu.TotalVramMb.Value);
    }

    [Fact]
    public void GetSnapshot_NeverFabricatesFieldsTheExistingProbeDoesNotExpose()
    {
        var provider = new RuntimeHardwareCapabilityProvider(new LiveHardwareTelemetry(new FakeHardwareProbe(FullFingerprint)));

        var snapshot = provider.GetSnapshot();

        Assert.False(snapshot.Cpu.PhysicalCores.HasValue);
        Assert.Equal("cpu_physical_cores_not_exposed_by_existing_probe", snapshot.Cpu.PhysicalCores.FailureReason);
        Assert.False(snapshot.Cpu.UtilizationPercent.HasValue);
        Assert.False(snapshot.Cpu.TemperatureCelsius.HasValue);
        Assert.False(snapshot.Cpu.CurrentClockMhz.HasValue);

        Assert.False(snapshot.Gpu.DriverVersion.HasValue);
        Assert.False(snapshot.Gpu.UsedVramMb.HasValue);
        Assert.False(snapshot.Gpu.UtilizationPercent.HasValue);
        Assert.False(snapshot.Gpu.TemperatureCelsius.HasValue);
        Assert.False(snapshot.Gpu.PowerDrawWatts.HasValue);
        // Derived from Total/Used VRAM: Used is Unknown, so Free must stay Unknown
        // too rather than being guessed from Total alone.
        Assert.False(snapshot.Gpu.FreeVramMb.HasValue);

        Assert.False(snapshot.Npu.IsPresent.HasValue);
        Assert.False(snapshot.Npu.Model.HasValue);
        Assert.False(snapshot.Npu.UtilizationPercent.HasValue);

        Assert.False(snapshot.Memory.AvailableRamMb.HasValue);

        Assert.False(snapshot.Storage.VolumeIdentity.HasValue);
        Assert.False(snapshot.Storage.TotalCapacityMb.HasValue);
        Assert.False(snapshot.Storage.FreeSpaceMb.HasValue);
        Assert.False(snapshot.Storage.ReadThroughputMbPerSecond.HasValue);
        Assert.False(snapshot.Storage.WriteThroughputMbPerSecond.HasValue);
        Assert.False(snapshot.Storage.ConnectionMode.HasValue);

        Assert.False(snapshot.Thermal.CpuTemperatureCelsius.HasValue);
        Assert.False(snapshot.Thermal.GpuTemperatureCelsius.HasValue);
        Assert.False(snapshot.Thermal.ThrottleState.HasValue);
        Assert.False(snapshot.Thermal.PowerSource.HasValue);

        // Every Unknown field must carry a distinct, diagnosable reason - never a
        // shared generic string that hides which field actually failed.
        var reasons = new[]
        {
            snapshot.Cpu.PhysicalCores.FailureReason,
            snapshot.Cpu.UtilizationPercent.FailureReason,
            snapshot.Cpu.TemperatureCelsius.FailureReason,
            snapshot.Cpu.CurrentClockMhz.FailureReason,
            snapshot.Gpu.DriverVersion.FailureReason,
            snapshot.Gpu.UsedVramMb.FailureReason,
            snapshot.Npu.IsPresent.FailureReason,
            snapshot.Memory.AvailableRamMb.FailureReason,
            snapshot.Storage.VolumeIdentity.FailureReason,
            snapshot.Thermal.ThrottleState.FailureReason
        };
        Assert.Equal(reasons.Length, reasons.Distinct().Count());
        Assert.All(reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }

    [Fact]
    public void GetSnapshot_WithNoUsableProbeData_FallsBackToUnknownForNamesAndRam()
    {
        // FallbackHardwareProbe is the existing, already-verified non-throwing probe
        // used when no real probe is available (e.g. off Windows).
        var provider = new RuntimeHardwareCapabilityProvider(new LiveHardwareTelemetry(new FallbackHardwareProbe()));

        var snapshot = provider.GetSnapshot();

        Assert.False(snapshot.Cpu.Model.HasValue);
        Assert.False(snapshot.Gpu.Model.HasValue);
        Assert.False(snapshot.Memory.TotalRamMb.HasValue);
        Assert.False(snapshot.Gpu.TotalVramMb.HasValue);

        // Environment.ProcessorCount is always available even when the OS-specific
        // probe reports nothing: LiveHardwareTelemetry falls back to it directly, and
        // this mapping must carry that reading through rather than discarding it.
        Assert.True(snapshot.Cpu.LogicalCores.HasValue);
        Assert.True(snapshot.Cpu.LogicalCores.Value > 0);
    }

    [Fact]
    public void GetSnapshot_NeverThrows_EvenWhenTheUnderlyingProbeThrows()
    {
        // ThrowingHardwareProbe is deliberately NOT wrapped in SafeHardwareProbe here,
        // so LiveHardwareTelemetry.Capture()'s own internal recovery path is what is
        // being exercised, and this test documents that the provider built on top of
        // it stays fail-closed (Unknown, not zero/guessed) rather than throwing.
        var provider = new RuntimeHardwareCapabilityProvider(new LiveHardwareTelemetry(new ThrowingHardwareProbe()));

        var snapshot = provider.GetSnapshot();

        Assert.NotNull(snapshot);
        Assert.False(snapshot.Cpu.Model.HasValue);
        Assert.False(snapshot.Memory.TotalRamMb.HasValue);
        Assert.False(snapshot.Gpu.Model.HasValue);
        Assert.NotNull(snapshot.Cpu.Model.FailureReason);
    }

    [Fact]
    public void Map_ThrowsOnNullView()
    {
        Assert.Throws<ArgumentNullException>(() => RuntimeHardwareCapabilityProvider.Map(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnNullTelemetry()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeHardwareCapabilityProvider(null!));
    }

    [Fact]
    public void DefaultConstructor_ProducesAUsableProviderOnAnyPlatform()
    {
        // Must not throw during construction on the CI/dev platform this test suite
        // actually runs on (this repo builds NosAi.Runtime as net8.0-windows with
        // EnableWindowsTargeting, but tests may run on non-Windows hosts too).
        var provider = new RuntimeHardwareCapabilityProvider();

        var snapshot = provider.GetSnapshot();

        Assert.NotNull(snapshot);
    }

    [Fact]
    public void GetSnapshot_ProducesASnapshotThatIsFailClosedForEveryTierAboveZero_UntilThermalTelemetryExists()
    {
        // Documents a real, load-bearing consequence of this adapter's honesty: the
        // existing Gate 1 probe path reports no thermal/throttle telemetry at all, so
        // Thermal.ThrottleState is always Unknown here. Every tier above Tier 0 in
        // InferenceTierFeasibility.CanRun requires a *known* throttle state, so this
        // provider can never authorize Tier 1, 2 or 3 today - a correct fail-closed
        // outcome, not a defect in the mapping. A future probe that adds real thermal
        // telemetry changes this; nothing in this gate/provider needs to change to
        // pick that up, since both call the shared CanRun logic rather than
        // re-implementing it.
        var provider = new RuntimeHardwareCapabilityProvider(new LiveHardwareTelemetry(new FakeHardwareProbe(FullFingerprint)));

        var snapshot = provider.GetSnapshot();

        Assert.True(CoreHardware.InferenceTierFeasibility.CanRun(CoreHardware.InferenceTier.Tier0DeterministicRules, snapshot));
        Assert.False(CoreHardware.InferenceTierFeasibility.CanRun(CoreHardware.InferenceTier.Tier1LightweightLocalMl, snapshot));
        Assert.False(CoreHardware.InferenceTierFeasibility.CanRun(CoreHardware.InferenceTier.Tier2GpuAcceleratedVision, snapshot));
        Assert.False(CoreHardware.InferenceTierFeasibility.CanRun(CoreHardware.InferenceTier.Tier3ExpensiveLocalReasoning, snapshot));
    }

    private sealed class FakeHardwareProbe : IHardwareProbe
    {
        private readonly HardwareFingerprint _fingerprint;
        public FakeHardwareProbe(HardwareFingerprint fingerprint) => _fingerprint = fingerprint;
        public HardwareFingerprint Detect() => _fingerprint;
    }

    private sealed class ThrowingHardwareProbe : IHardwareProbe
    {
        public HardwareFingerprint Detect() => throw new InvalidOperationException("simulated probe failure for AP-00/A4 tests");
    }
}
