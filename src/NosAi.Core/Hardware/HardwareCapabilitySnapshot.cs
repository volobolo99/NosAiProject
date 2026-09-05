namespace NosAi.Core.Hardware;

/// <summary>
/// Thermal/power throttling condition of the machine. This enum never has an
/// "Unknown" member by design: absence of a trustworthy reading is expressed
/// by wrapping the enum in <see cref="ClassifiedValue{T}.Unknown"/> instead,
/// so "we don't know the throttle state" and "the throttle state is nominal"
/// can never be confused (docs/ROADMAP_ESECUTIVA.md invariant "Unknown is not
/// zero, false or empty").
/// </summary>
public enum HardwareThrottleState
{
    /// <summary>No observed thermal or power limiting.</summary>
    Nominal = 0,

    /// <summary>Temperatures or power draw are elevated but clocks are not yet reduced.</summary>
    Elevated = 1,

    /// <summary>The OS/firmware is actively reducing CPU/GPU clocks to manage heat or power.</summary>
    Throttling = 2,

    /// <summary>Thermal or power state is critical; sustained heavy inference must not be scheduled.</summary>
    Critical = 3
}

/// <summary>The machine's current power source. See <see cref="HardwareThrottleState"/> remarks on why there is no "Unknown" member.</summary>
public enum PowerSourceKind
{
    /// <summary>Running on mains/adapter power.</summary>
    ACPower = 0,

    /// <summary>Running on battery only.</summary>
    Battery = 1
}

/// <summary>
/// CPU capability/telemetry. Field set follows
/// docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8 (AI scheduling budget inputs)
/// and S:9 (thermal/power adaptation) plus the SKU-detection requirement in
/// docs/ROADMAP_ESECUTIVA.md S:2 ("il runtime deve rilevare SKU, CPU...").
/// No field carries an assumed AMD Ryzen model or core count; every value is
/// either a real reading or explicitly <see cref="DataSourceKind.Unknown"/>.
/// </summary>
public sealed record CpuCapability(
    ClassifiedValue<string> Model,
    ClassifiedValue<int> LogicalCores,
    ClassifiedValue<int> PhysicalCores,
    ClassifiedValue<double> UtilizationPercent,
    ClassifiedValue<double> TemperatureCelsius,
    ClassifiedValue<double> CurrentClockMhz)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> CPU capability, used when no probe has run yet or the last probe failed.</summary>
    public static CpuCapability Unknown(string reason) => new(
        ClassifiedValue<string>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason));
}

/// <summary>
/// GPU capability/telemetry. The RTX 5060 Laptop class target is an 8 GB
/// GDDR7 device with a 45-100 W subsystem range
/// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:2), so total/used VRAM and
/// power draw are modelled as independently-classified live readings rather
/// than a single hardcoded ceiling.
/// </summary>
public sealed record GpuCapability(
    ClassifiedValue<string> Model,
    ClassifiedValue<string> DriverVersion,
    ClassifiedValue<long> TotalVramMb,
    ClassifiedValue<long> UsedVramMb,
    ClassifiedValue<double> UtilizationPercent,
    ClassifiedValue<double> TemperatureCelsius,
    ClassifiedValue<double> PowerDrawWatts)
{
    /// <summary>
    /// Free VRAM headroom, derived from <see cref="TotalVramMb"/> and
    /// <see cref="UsedVramMb"/> when both are known. <see cref="DataSourceKind.Derived"/>
    /// because it is computed rather than read directly from the driver.
    /// </summary>
    public ClassifiedValue<long> FreeVramMb =>
        TotalVramMb.HasValue && UsedVramMb.HasValue && TotalVramMb.Value >= 0 && UsedVramMb.Value >= 0
            ? ClassifiedValue<long>.Derived(Math.Max(0, TotalVramMb.Value - UsedVramMb.Value), TotalVramMb.ObservedAtUtc < UsedVramMb.ObservedAtUtc ? UsedVramMb.ObservedAtUtc : TotalVramMb.ObservedAtUtc)
            : ClassifiedValue<long>.Unknown(
                TotalVramMb.HasValue && UsedVramMb.HasValue
                    // Both are "known" but at least one is negative -- a corrupted/impossible
                    // reading, never usable to derive a real headroom (AP-00/A5 audit: plain
                    // unchecked `long` subtraction on a negative TotalVramMb can overflow and
                    // wrap around to a large *positive* number instead of staying non-positive).
                    ? "gpu_vram_reading_is_negative_and_therefore_invalid"
                    : "gpu_free_vram_requires_known_total_and_used");

    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> GPU capability, used when no discrete/integrated GPU has been confirmed.</summary>
    public static GpuCapability Unknown(string reason) => new(
        ClassifiedValue<string>.Unknown(reason),
        ClassifiedValue<string>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason));
}

/// <summary>
/// NPU capability. Modelled separately from <see cref="GpuCapability"/>
/// because docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md S:2 lists "GPU/NPU when
/// available" as an independent, optional accelerator — its presence must
/// never be inferred from GPU presence.
/// </summary>
public sealed record NpuCapability(
    ClassifiedValue<bool> IsPresent,
    ClassifiedValue<string> Model,
    ClassifiedValue<double> UtilizationPercent)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> NPU capability, used when NPU presence has not been confirmed one way or the other.</summary>
    public static NpuCapability Unknown(string reason) => new(
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<string>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason));
}

/// <summary>
/// System RAM capability. 16 GB DDR5 is the constrained baseline
/// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:5); no capacity is assumed
/// here, it is only ever a classified reading.
/// </summary>
public sealed record MemoryCapability(
    ClassifiedValue<long> TotalRamMb,
    ClassifiedValue<long> AvailableRamMb,
    ClassifiedValue<long> ProcessWorkingSetMb)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> memory capability.</summary>
    public static MemoryCapability Unknown(string reason) => new(
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason));
}

/// <summary>
/// Storage capability for the canonical external SSD target
/// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:6). The runtime identifies the
/// volume by stable label/identity rather than a fixed drive letter, so the
/// identity itself is a classified string, not an assumed path.
/// </summary>
public sealed record StorageCapability(
    ClassifiedValue<string> VolumeIdentity,
    ClassifiedValue<long> TotalCapacityMb,
    ClassifiedValue<long> FreeSpaceMb,
    ClassifiedValue<double> ReadThroughputMbPerSecond,
    ClassifiedValue<double> WriteThroughputMbPerSecond,
    ClassifiedValue<string> ConnectionMode)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> storage capability, used before the canonical volume has been located/verified.</summary>
    public static StorageCapability Unknown(string reason) => new(
        ClassifiedValue<string>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<string>.Unknown(reason));
}

/// <summary>
/// Thermal/power state of the machine, treated as part of runtime health
/// rather than mere telemetry (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:9,
/// docs/NOSAI_ARCHITECTURE_BASELINE.md S:5 "Thermal/power").
/// </summary>
public sealed record ThermalCapability(
    ClassifiedValue<double> CpuTemperatureCelsius,
    ClassifiedValue<double> GpuTemperatureCelsius,
    ClassifiedValue<HardwareThrottleState> ThrottleState,
    ClassifiedValue<PowerSourceKind> PowerSource)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> thermal capability.</summary>
    public static ThermalCapability Unknown(string reason) => new(
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<HardwareThrottleState>.Unknown(reason),
        ClassifiedValue<PowerSourceKind>.Unknown(reason));
}

/// <summary>
/// A single, self-consistent, point-in-time view of everything the resource
/// budget policy (A3) needs to decide which <see cref="InferenceTier"/>
/// jobs may run: CPU, GPU, NPU, RAM, VRAM, storage and thermal state
/// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8). This type carries no
/// machine identity (no serial number, no fixed drive letter, no assumed
/// SKU) — every identifying string is a <see cref="ClassifiedValue{T}"/>
/// obtained from the real machine or explicitly Unknown.
///
/// Two snapshots built from the same inputs at the same instant compare
/// equal (record value semantics over a tree of records/value
/// types/strings — no collections are involved anywhere in this type), so
/// it is deterministic and trivially serializable (e.g. with
/// <c>System.Text.Json</c>) without any custom converters.
/// </summary>
public sealed record HardwareCapabilitySnapshot(
    DateTime ObservedAtUtc,
    CpuCapability Cpu,
    GpuCapability Gpu,
    NpuCapability Npu,
    MemoryCapability Memory,
    StorageCapability Storage,
    ThermalCapability Thermal)
{
    /// <summary>
    /// A snapshot in which every field is explicitly <see cref="DataSourceKind.Unknown"/>.
    /// Used before the first successful probe, or when a probe fails outright —
    /// never replaced by a snapshot of zeros/defaults.
    /// </summary>
    public static HardwareCapabilitySnapshot Unknown(string reason, DateTime? observedAtUtc = null) => new(
        observedAtUtc ?? DateTime.UtcNow,
        CpuCapability.Unknown(reason),
        GpuCapability.Unknown(reason),
        NpuCapability.Unknown(reason),
        MemoryCapability.Unknown(reason),
        StorageCapability.Unknown(reason),
        ThermalCapability.Unknown(reason));
}
