namespace NosAi.Runtime.Hardware;

public sealed record HardwareFingerprint(
    string Platform,
    string Cpu,
    int LogicalCores,
    long RamMb,
    string Gpu,
    long GpuMemoryMb,
    int DisplayRefreshHz,
    string OsVersion);

public sealed record RuntimeSettings(
    int ComputeTier,
    int MemoryTier,
    int GraphicsTier,
    int InferenceBudgetMs,
    int PerceptionBudgetMs,
    int TelemetryIntervalMs,
    int WorkerCount,
    string PowerThermalPolicy);

/// <summary>
/// Pure policy engine. Device probing and persistence are deliberately injected,
/// making Auto-Setting deterministic and testable without touching the OS.
/// </summary>
public sealed class HardwareAutoSettings
{
    public RuntimeSettings Calculate(HardwareFingerprint h)
    {
        ArgumentNullException.ThrowIfNull(h);
        var compute = h.LogicalCores >= 16 ? 4 : h.LogicalCores >= 8 ? 3 : h.LogicalCores >= 4 ? 2 : 1;
        var memory = h.RamMb >= 32768 ? 4 : h.RamMb >= 16384 ? 3 : h.RamMb >= 8192 ? 2 : 1;
        var graphics = h.GpuMemoryMb >= 8192 ? 4 : h.GpuMemoryMb >= 4096 ? 3 : h.GpuMemoryMb >= 2048 ? 2 : 1;
        var workers = Math.Clamp(Math.Min(h.LogicalCores, 12), 2, 12);
        return new RuntimeSettings(compute, memory, graphics, 16, 33, 1000, workers, "adaptive");
    }
}
