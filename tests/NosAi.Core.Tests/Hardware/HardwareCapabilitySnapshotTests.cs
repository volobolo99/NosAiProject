using System.Text.Json;
using NosAi.Core.Hardware;
using Xunit;

namespace NosAi.Core.Tests.Hardware;

public sealed class HardwareCapabilitySnapshotTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 5, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Unknown_EveryFieldIsExplicitlyUnknown_NeverAZeroOrDefault()
    {
        var snapshot = HardwareCapabilitySnapshot.Unknown("no_probe_has_run_yet", FixedNow);

        Assert.False(snapshot.Cpu.Model.HasValue);
        Assert.False(snapshot.Cpu.LogicalCores.HasValue);
        Assert.False(snapshot.Cpu.PhysicalCores.HasValue);
        Assert.False(snapshot.Cpu.UtilizationPercent.HasValue);
        Assert.False(snapshot.Cpu.TemperatureCelsius.HasValue);
        Assert.False(snapshot.Cpu.CurrentClockMhz.HasValue);

        Assert.False(snapshot.Gpu.Model.HasValue);
        Assert.False(snapshot.Gpu.DriverVersion.HasValue);
        Assert.False(snapshot.Gpu.TotalVramMb.HasValue);
        Assert.False(snapshot.Gpu.UsedVramMb.HasValue);
        Assert.False(snapshot.Gpu.FreeVramMb.HasValue);
        Assert.False(snapshot.Gpu.UtilizationPercent.HasValue);
        Assert.False(snapshot.Gpu.TemperatureCelsius.HasValue);
        Assert.False(snapshot.Gpu.PowerDrawWatts.HasValue);

        Assert.False(snapshot.Npu.IsPresent.HasValue);
        Assert.False(snapshot.Npu.Model.HasValue);

        Assert.False(snapshot.Memory.TotalRamMb.HasValue);
        Assert.False(snapshot.Memory.AvailableRamMb.HasValue);

        Assert.False(snapshot.Storage.VolumeIdentity.HasValue);
        Assert.False(snapshot.Storage.TotalCapacityMb.HasValue);
        Assert.False(snapshot.Storage.FreeSpaceMb.HasValue);

        Assert.False(snapshot.Thermal.ThrottleState.HasValue);
        Assert.False(snapshot.Thermal.PowerSource.HasValue);

        // The reason must survive onto every leaf, not just the top-level call.
        Assert.Equal("no_probe_has_run_yet", snapshot.Cpu.Model.FailureReason);
        Assert.Equal("no_probe_has_run_yet", snapshot.Gpu.TotalVramMb.FailureReason);
        Assert.Equal("no_probe_has_run_yet", snapshot.Thermal.ThrottleState.FailureReason);
    }

    [Fact]
    public void Gpu_FreeVramMb_IsDerivedWhenBothTotalAndUsedAreKnown()
    {
        var gpu = new GpuCapability(
            ClassifiedValue<string>.Live("NVIDIA GeForce RTX 5060 Laptop GPU", FixedNow),
            ClassifiedValue<string>.Live("32.0.15.6614", FixedNow),
            ClassifiedValue<long>.Live(8192, FixedNow),
            ClassifiedValue<long>.Live(2048, FixedNow),
            ClassifiedValue<double>.Live(12.5, FixedNow),
            ClassifiedValue<double>.Live(58.0, FixedNow),
            ClassifiedValue<double>.Live(65.0, FixedNow));

        var free = gpu.FreeVramMb;

        Assert.True(free.HasValue);
        Assert.Equal(6144, free.Value);
        Assert.Equal(DataSourceKind.Derived, free.Source);
    }

    [Fact]
    public void Gpu_FreeVramMb_IsUnknownWhenEitherInputIsUnknown()
    {
        var totalUnknown = new GpuCapability(
            ClassifiedValue<string>.Live("GPU", FixedNow),
            ClassifiedValue<string>.Unknown("driver_not_reported"),
            ClassifiedValue<long>.Unknown("vram_total_not_reported"),
            ClassifiedValue<long>.Live(1024, FixedNow),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"));

        Assert.False(totalUnknown.FreeVramMb.HasValue);
        Assert.Equal(DataSourceKind.Unknown, totalUnknown.FreeVramMb.Source);
    }

    [Fact]
    public void Gpu_FreeVramMb_NeverGoesNegativeWhenUsedExceedsTotal()
    {
        // A transient/inconsistent driver reading (used > total) must not
        // produce a negative "free" value that a budget check could
        // misinterpret; it clamps to zero instead of going negative.
        var gpu = new GpuCapability(
            ClassifiedValue<string>.Live("GPU", FixedNow),
            ClassifiedValue<string>.Live("driver", FixedNow),
            ClassifiedValue<long>.Live(4096, FixedNow),
            ClassifiedValue<long>.Live(5000, FixedNow),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"),
            ClassifiedValue<double>.Unknown("x"));

        Assert.True(gpu.FreeVramMb.HasValue);
        Assert.Equal(0, gpu.FreeVramMb.Value);
    }

    [Fact]
    public void TwoSnapshotsBuiltFromTheSameInputs_AreEqual_Deterministic()
    {
        var a = FullyKnownSnapshot();
        var b = FullyKnownSnapshot();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ChangingOneLeafValue_BreaksEquality()
    {
        var a = FullyKnownSnapshot();
        var b = a with { Cpu = a.Cpu with { LogicalCores = ClassifiedValue<int>.Live(1, FixedNow) } };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Snapshot_RoundTripsThroughJsonWithoutCustomConverters()
    {
        var original = FullyKnownSnapshot();

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<HardwareCapabilitySnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void UnknownSnapshot_RoundTripsThroughJson()
    {
        var original = HardwareCapabilitySnapshot.Unknown("hardware_probe_unavailable", FixedNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<HardwareCapabilitySnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
        Assert.False(restored!.Gpu.TotalVramMb.HasValue);
    }

    internal static HardwareCapabilitySnapshot FullyKnownSnapshot() => new(
        FixedNow,
        new CpuCapability(
            ClassifiedValue<string>.Live("AMD Ryzen 9 (detected)", FixedNow),
            ClassifiedValue<int>.Live(16, FixedNow),
            ClassifiedValue<int>.Live(8, FixedNow),
            ClassifiedValue<double>.Live(22.5, FixedNow),
            ClassifiedValue<double>.Live(61.0, FixedNow),
            ClassifiedValue<double>.Live(4200.0, FixedNow)),
        new GpuCapability(
            ClassifiedValue<string>.Live("NVIDIA GeForce RTX 5060 Laptop GPU", FixedNow),
            ClassifiedValue<string>.Live("32.0.15.6614", FixedNow),
            ClassifiedValue<long>.Live(8192, FixedNow),
            ClassifiedValue<long>.Live(1024, FixedNow),
            ClassifiedValue<double>.Live(30.0, FixedNow),
            ClassifiedValue<double>.Live(63.0, FixedNow),
            ClassifiedValue<double>.Live(70.0, FixedNow)),
        // Every field in this fixture is deliberately Live/Derived (never
        // Unknown): Unknown() stamps DateTime.UtcNow internally, which would
        // make two independently-built "fully known" fixtures compare
        // unequal purely on wall-clock jitter. Unknown-field behavior is
        // covered separately by Unknown_EveryFieldIsExplicitlyUnknown... .
        new NpuCapability(
            ClassifiedValue<bool>.Live(false, FixedNow),
            ClassifiedValue<string>.Live("not_applicable", FixedNow),
            ClassifiedValue<double>.Live(0, FixedNow)),
        new MemoryCapability(
            ClassifiedValue<long>.Live(16384, FixedNow),
            ClassifiedValue<long>.Live(9000, FixedNow),
            ClassifiedValue<long>.Live(512, FixedNow)),
        new StorageCapability(
            ClassifiedValue<string>.Live("NOSAI-SSD", FixedNow),
            ClassifiedValue<long>.Live(2_000_000, FixedNow),
            ClassifiedValue<long>.Live(1_500_000, FixedNow),
            ClassifiedValue<double>.Live(450.0, FixedNow),
            ClassifiedValue<double>.Live(400.0, FixedNow),
            ClassifiedValue<string>.Live("USB 3.2 Gen2", FixedNow)),
        new ThermalCapability(
            ClassifiedValue<double>.Live(61.0, FixedNow),
            ClassifiedValue<double>.Live(63.0, FixedNow),
            ClassifiedValue<HardwareThrottleState>.Live(HardwareThrottleState.Nominal, FixedNow),
            ClassifiedValue<PowerSourceKind>.Live(PowerSourceKind.ACPower, FixedNow)));
}
