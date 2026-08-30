using System.Diagnostics;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;

namespace NosAi.Runtime.Hardware;

public sealed record ClassifiedHardwareSnapshot(Gate1HardwareView View, HardwareFingerprint? Fingerprint, string? FailureReason);

/// <summary>
/// Captures PC baseline telemetry for Gate 1. Probe failures become UNKNOWN rather than zeros.
/// </summary>
public sealed class LiveHardwareTelemetry
{
    private readonly IHardwareProbe _probe;

    public LiveHardwareTelemetry(IHardwareProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public ClassifiedHardwareSnapshot Capture()
    {
        var now = DateTime.UtcNow;
        long workingSetMb;
        try
        {
            using var process = Process.GetCurrentProcess();
            workingSetMb = process.WorkingSet64 / (1024 * 1024);
        }
        catch (Exception ex)
        {
            var unknownRam = ClassifiedValue<long>.Unknown($"process_working_set_failed:{ex.GetType().Name}");
            return BuildUnknown(now, unknownRam, $"process_working_set_failed:{ex.GetType().Name}");
        }

        var processRam = ClassifiedValue<long>.Live(workingSetMb, now);
        HardwareFingerprint? fingerprint = null;
        string? failure = null;
        try
        {
            fingerprint = _probe.Detect();
        }
        catch (Exception ex)
        {
            failure = $"hardware_probe_failed:{ex.GetType().Name}:{ex.Message}";
        }

        // A probe that recovers internally never throws, so without this the real
        // cause would be dropped and the snapshot would report success.
        failure ??= (_probe as IHardwareProbeDiagnostics)?.LastFailureReason;

        if (fingerprint is null)
        {
            return BuildUnknown(now, processRam, failure ?? "hardware_probe_unavailable");
        }

        var cores = fingerprint.LogicalCores > 0
            ? ClassifiedValue<int>.Live(fingerprint.LogicalCores, now)
            : ClassifiedValue<int>.Live(Environment.ProcessorCount, now, "logical cores taken from Environment.ProcessorCount");

        var systemRam = fingerprint.RamMb > 0
            ? ClassifiedValue<long>.Live(fingerprint.RamMb, now)
            : ClassifiedValue<long>.Unknown(failure ?? "system_ram_not_reported");

        var gpuMemory = fingerprint.GpuMemoryMb > 0
            ? ClassifiedValue<long>.Live(fingerprint.GpuMemoryMb, now)
            : ClassifiedValue<long>.Unknown(failure ?? "gpu_memory_not_reported");

        var refresh = fingerprint.DisplayRefreshHz > 0
            ? ClassifiedValue<int>.Live(fingerprint.DisplayRefreshHz, now)
            : ClassifiedValue<int>.Unknown(failure ?? "display_refresh_not_reported");

        var cpuUnknown = !NonEmpty(fingerprint.Cpu);
        var gpuUnknown = !NonEmpty(fingerprint.Gpu);

        var view = new Gate1HardwareView(
            Platform: NonEmpty(fingerprint.Platform)
                ? ClassifiedValue<string>.Live(fingerprint.Platform, now)
                : ClassifiedValue<string>.Unknown("platform_not_reported"),
            Cpu: cpuUnknown ? ClassifiedValue<string>.Unknown(failure ?? "cpu_not_reported") : ClassifiedValue<string>.Live(fingerprint.Cpu, now),
            LogicalCores: cores,
            ProcessWorkingSetMb: processRam,
            SystemRamMb: systemRam,
            Gpu: gpuUnknown ? ClassifiedValue<string>.Unknown(failure ?? "gpu_not_reported") : ClassifiedValue<string>.Live(fingerprint.Gpu, now),
            GpuMemoryMb: gpuMemory,
            DisplayRefreshHz: refresh,
            OsVersion: NonEmpty(fingerprint.OsVersion)
                ? ClassifiedValue<string>.Live(fingerprint.OsVersion, now)
                : ClassifiedValue<string>.Unknown("os_version_not_reported"));

        return new ClassifiedHardwareSnapshot(view, fingerprint, failure);
    }

    private static ClassifiedHardwareSnapshot BuildUnknown(DateTime now, ClassifiedValue<long> processRam, string reason)
    {
        var view = new Gate1HardwareView(
            Platform: ClassifiedValue<string>.Live(Environment.OSVersion.Platform.ToString(), now),
            Cpu: ClassifiedValue<string>.Unknown(reason),
            LogicalCores: ClassifiedValue<int>.Live(Environment.ProcessorCount, now),
            ProcessWorkingSetMb: processRam,
            SystemRamMb: ClassifiedValue<long>.Unknown(reason),
            Gpu: ClassifiedValue<string>.Unknown(reason),
            GpuMemoryMb: ClassifiedValue<long>.Unknown(reason),
            DisplayRefreshHz: ClassifiedValue<int>.Unknown(reason),
            OsVersion: ClassifiedValue<string>.Live(Environment.OSVersion.VersionString, now));
        return new ClassifiedHardwareSnapshot(view, null, reason);
    }

    private static bool NonEmpty(string? value) => !string.IsNullOrWhiteSpace(value);
}

/// <summary>Probe that never throws; used when WMI/OS APIs are unavailable.</summary>
public sealed class FallbackHardwareProbe : IHardwareProbe
{
    public HardwareFingerprint Detect()
        => new(
            Environment.OSVersion.Platform.ToString(),
            string.Empty,
            Environment.ProcessorCount,
            0,
            string.Empty,
            0,
            0,
            Environment.OSVersion.VersionString);
}

public sealed class SafeHardwareProbe : IHardwareProbe, IHardwareProbeDiagnostics
{
    private readonly IHardwareProbe _inner;

    public SafeHardwareProbe(IHardwareProbe inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public string? LastFailureReason { get; private set; }

    public HardwareFingerprint Detect()
    {
        try
        {
            var fingerprint = _inner.Detect();
            LastFailureReason = null;
            return fingerprint;
        }
        catch (Exception ex)
        {
            // Recovering silently would erase why the real probe failed, leaving
            // the snapshot indistinguishable from a probe that simply found nothing.
            LastFailureReason = $"hardware_probe_failed:{ex.GetType().Name}:{ex.Message}";
            return new FallbackHardwareProbe().Detect();
        }
    }
}
