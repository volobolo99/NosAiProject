using System.Runtime.Versioning;
using NosAi.Runtime.Gate1;
using CoreHardware = NosAi.Core.Hardware;
using RuntimeContracts = NosAi.Runtime.Contracts;

// Deliberately the existing, already-declared-Integrated NosAi.Runtime.Hardware
// namespace (see src/NosAi.Runtime/Observability/ModuleReachability.cs) rather than
// a new NosAi.Runtime.Hardware.Gate namespace: this file lives in a new physical
// subfolder (Hardware/Gate/, matching precedent already in this codebase — e.g.
// Hardware/Autoscale/HardwareAutoscaleController.cs declares namespace
// NosAi.Hardware.Autoscale, a folder/namespace split that is already normal here)
// so it is a brand-new file under AP-00/A4 ownership, but it is genuinely part of
// the Hardware capability module, not a new module the runtime's module-reachability
// audit needs to learn about. Introducing an undeclared namespace would fail
// ModuleReachabilityTests.Every_namespace_in_the_runtime_is_declared, and that
// manifest is explicitly out of this task's file ownership to edit.
namespace NosAi.Runtime.Hardware;

/// <summary>
/// Real (non-mock) implementation of <see cref="CoreHardware.IHardwareCapabilityProvider"/>
/// for <c>NosAi.Runtime</c>, built entirely on top of the already-verified Gate 1
/// hardware path (<see cref="LiveHardwareTelemetry"/> / <see cref="IHardwareProbe"/>
/// in <c>src/NosAi.Runtime/Hardware/</c>). This class performs no I/O and no P/Invoke
/// of its own — it only asks the existing telemetry for its
/// <see cref="ClassifiedHardwareSnapshot"/> and re-shapes it into the
/// <c>NosAi.Core.Hardware</c> contract shape that A3's scheduling policy and
/// <see cref="HardwareInferenceCapabilityGate"/> consume.
///
/// Field coverage is intentionally partial: <see cref="LiveHardwareTelemetry"/> (the
/// most complete existing probe path — see <see cref="HardwareProbe"/> for what it
/// reads via WMI) exposes CPU model/logical-core-count, system RAM, GPU
/// name/memory, display refresh rate, platform and OS version. It reports nothing
/// about physical core count, CPU/GPU utilization or temperature, GPU driver
/// version or used VRAM, NPU presence, storage, or thermal/power state. Every one
/// of those fields is mapped to <see cref="CoreHardware.ClassifiedValue{T}.Unknown"/>
/// with an explicit, distinct reason rather than a guessed or zeroed value — per
/// CLAUDE.md's "Unknown is not zero, false or empty" invariant and this task's
/// explicit instruction that a field absent from the existing probe stays Unknown,
/// never invented.
///
/// <see cref="NosAi.Hardware.Autoscale.HardwareAutoscaleController"/> was
/// deliberately NOT used as a data source here even though it also produces a
/// hardware-shaped snapshot: its telemetry is either a caller-supplied
/// "simulatedGpuTemp" parameter or literal constants (a hardcoded device model
/// string, hardcoded CPU/GPU utilization percentages, a hardcoded 16 GB RAM
/// total) — i.e. genuinely <see cref="RuntimeContracts.DataSourceKind.Simulated"/>
/// data, not live telemetry. Feeding that into a capability gate that other
/// components will trust for "is it safe to run this tier" decisions would both
/// violate "never label simulated data as live" and reintroduce the machine-identity
/// hardcoding CLAUDE.md and docs/ROADMAP_ESECUTIVA.md explicitly forbid.
/// </summary>
public sealed class RuntimeHardwareCapabilityProvider : CoreHardware.IHardwareCapabilityProvider
{
    private readonly LiveHardwareTelemetry _telemetry;

    /// <summary>
    /// Uses the same default probe selection as the verified Gate 1 bootstrap path
    /// (<c>Gate1BootstrapHost.CreateDefaultProbe</c>): a real WMI-backed probe on
    /// Windows, wrapped so a probe failure degrades to UNKNOWN instead of throwing,
    /// and the non-throwing fallback probe everywhere else (so this type can also be
    /// constructed — and its Unknown-mapping exercised — off Windows).
    /// </summary>
    public RuntimeHardwareCapabilityProvider() : this(new LiveHardwareTelemetry(new SafeHardwareProbe(CreateDefaultProbe())))
    {
    }

    /// <summary>
    /// Test/composition seam: accepts any already-constructed
    /// <see cref="LiveHardwareTelemetry"/>, e.g. one built over a fake
    /// <see cref="IHardwareProbe"/> so tests can exercise the mapping without
    /// touching real hardware or WMI.
    /// </summary>
    public RuntimeHardwareCapabilityProvider(LiveHardwareTelemetry telemetry)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <inheritdoc />
    public CoreHardware.HardwareCapabilitySnapshot GetSnapshot()
    {
        ClassifiedHardwareSnapshot captured;
        try
        {
            captured = _telemetry.Capture();
        }
        catch (Exception ex)
        {
            // LiveHardwareTelemetry.Capture() does not throw as written today (every
            // internal failure path already returns an Unknown-shaped snapshot), but
            // IHardwareCapabilityProvider's contract promises callers a snapshot, never
            // an exception, and that promise must hold even if Capture()'s internals
            // change later. Falling back to a fully-Unknown snapshot here — rather than
            // letting the exception propagate — is what keeps
            // InferenceCapabilityGate.TryAuthorize fail-closed for every tier above
            // Tier 0 without this provider needing to know anything about the gate.
            return CoreHardware.HardwareCapabilitySnapshot.Unknown(
                $"hardware_capability_capture_failed:{ex.GetType().Name}:{ex.Message}");
        }

        return Map(captured.View);
    }

    /// <summary>
    /// Pure mapping from the Gate 1 hardware view to the Core hardware-capability
    /// contract. Exposed as internal (not private) purely so its per-field Unknown
    /// reasons can be asserted directly in tests without re-deriving a full
    /// <see cref="ClassifiedHardwareSnapshot"/> for every case.
    /// </summary>
    internal static CoreHardware.HardwareCapabilitySnapshot Map(Gate1HardwareView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var observedAtUtc = DateTime.UtcNow;

        var cpu = new CoreHardware.CpuCapability(
            Model: Convert(view.Cpu),
            LogicalCores: Convert(view.LogicalCores),
            PhysicalCores: CoreHardware.ClassifiedValue<int>.Unknown("cpu_physical_cores_not_exposed_by_existing_probe"),
            UtilizationPercent: CoreHardware.ClassifiedValue<double>.Unknown("cpu_utilization_not_exposed_by_existing_probe"),
            TemperatureCelsius: CoreHardware.ClassifiedValue<double>.Unknown("cpu_temperature_not_exposed_by_existing_probe"),
            CurrentClockMhz: CoreHardware.ClassifiedValue<double>.Unknown("cpu_clock_not_exposed_by_existing_probe"));

        var gpu = new CoreHardware.GpuCapability(
            Model: Convert(view.Gpu),
            DriverVersion: CoreHardware.ClassifiedValue<string>.Unknown("gpu_driver_version_not_exposed_by_existing_probe"),
            TotalVramMb: Convert(view.GpuMemoryMb),
            UsedVramMb: CoreHardware.ClassifiedValue<long>.Unknown("gpu_used_vram_not_exposed_by_existing_probe"),
            UtilizationPercent: CoreHardware.ClassifiedValue<double>.Unknown("gpu_utilization_not_exposed_by_existing_probe"),
            TemperatureCelsius: CoreHardware.ClassifiedValue<double>.Unknown("gpu_temperature_not_exposed_by_existing_probe"),
            PowerDrawWatts: CoreHardware.ClassifiedValue<double>.Unknown("gpu_power_draw_not_exposed_by_existing_probe"));

        // Presence is never inferred from the GPU reading: NosAi.Core.Hardware's
        // NpuCapability doc explicitly forbids that, and the existing probe performs
        // no NPU detection of its own at all (no WMI class for it is queried).
        var npu = CoreHardware.NpuCapability.Unknown("npu_detection_not_implemented_by_existing_probe");

        var memory = new CoreHardware.MemoryCapability(
            TotalRamMb: Convert(view.SystemRamMb),
            AvailableRamMb: CoreHardware.ClassifiedValue<long>.Unknown("available_ram_not_exposed_by_existing_probe"),
            ProcessWorkingSetMb: Convert(view.ProcessWorkingSetMb));

        var storage = CoreHardware.StorageCapability.Unknown("storage_telemetry_not_exposed_by_existing_probe");

        var thermal = CoreHardware.ThermalCapability.Unknown("thermal_telemetry_not_exposed_by_existing_probe");

        return new CoreHardware.HardwareCapabilitySnapshot(observedAtUtc, cpu, gpu, npu, memory, storage, thermal);
    }

    /// <summary>
    /// Bridges one <c>NosAi.Runtime.Contracts.ClassifiedValue&lt;T&gt;</c> reading
    /// (the shape <see cref="LiveHardwareTelemetry"/> already produces) into the
    /// structurally-identical but separately-declared
    /// <c>NosAi.Core.Hardware.ClassifiedValue&lt;T&gt;</c> (see
    /// <c>NosAi.Core.Hardware.HardwareClassification.cs</c> for why the two types are
    /// not shared — <c>NosAi.Core</c> cannot reference <c>NosAi.Runtime.Contracts</c>
    /// without a circular project reference). Provenance, freshness and the exact
    /// failure reason all cross the bridge unchanged; nothing here upgrades or
    /// downgrades a reading's trust level.
    /// </summary>
    private static CoreHardware.ClassifiedValue<T> Convert<T>(RuntimeContracts.ClassifiedValue<T> source)
    {
        if (!source.HasValue)
        {
            return CoreHardware.ClassifiedValue<T>.Unknown(
                source.FailureReason ?? "not_reported_by_runtime_hardware_probe",
                source.Warning);
        }

        return source.Source switch
        {
            RuntimeContracts.DataSourceKind.Live => CoreHardware.ClassifiedValue<T>.Live(source.Value, source.ObservedAtUtc, source.Warning),
            RuntimeContracts.DataSourceKind.Derived => CoreHardware.ClassifiedValue<T>.Derived(source.Value, source.ObservedAtUtc, source.Warning),
            RuntimeContracts.DataSourceKind.Cached => CoreHardware.ClassifiedValue<T>.Cached(source.Value, source.ObservedAtUtc, source.Warning),
            RuntimeContracts.DataSourceKind.Simulated => CoreHardware.ClassifiedValue<T>.Simulated(source.Value, source.ObservedAtUtc, source.Warning),
            // HasValue already excludes DataSourceKind.Unknown, so reaching here means
            // a future DataSourceKind member was added on the Runtime.Contracts side
            // without a matching branch here. Refusing to guess a trust level for it
            // is the fail-closed choice, not an oversight.
            _ => CoreHardware.ClassifiedValue<T>.Unknown($"unrecognized_runtime_data_source_kind:{source.Source}", source.Warning)
        };
    }

    private static IHardwareProbe CreateDefaultProbe()
        => OperatingSystem.IsWindows() ? CreateWindowsProbe() : new FallbackHardwareProbe();

    [SupportedOSPlatform("windows")]
    private static IHardwareProbe CreateWindowsProbe() => new WindowsHardwareProbe();
}
