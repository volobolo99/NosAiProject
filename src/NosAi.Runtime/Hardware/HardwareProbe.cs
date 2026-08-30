using System.Management;
using System.Runtime.Versioning;

namespace NosAi.Runtime.Hardware;

public interface IHardwareProbe
{
    HardwareFingerprint Detect();
}

/// <summary>
/// Optional companion to <see cref="IHardwareProbe"/> for probes that recover from a
/// failure internally. Without it the reason a probe fell back is lost and every
/// derived field reports a generic "not reported" instead of the real cause.
/// </summary>
public interface IHardwareProbeDiagnostics
{
    /// <summary>Why the last <c>Detect()</c> fell back, or null when it succeeded.</summary>
    string? LastFailureReason { get; }
}

/// <summary>Windows hardware probe used by PlayAi on first run.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareProbe : IHardwareProbe
{
    public HardwareFingerprint Detect()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsHardwareProbe requires Windows WMI.");

        // Empty, not a sentinel label: absence is expressed by the value being
        // missing, so no caller has to pattern-match on the word "Unknown".
        var cpu = GetSingle("Win32_Processor", "Name") ?? string.Empty;
        var cores = int.TryParse(GetSingle("Win32_Processor", "NumberOfLogicalProcessors"), out var c) ? c : Environment.ProcessorCount;
        var ram = long.TryParse(GetSingle("Win32_ComputerSystem", "TotalPhysicalMemory"), out var bytes) ? bytes / (1024 * 1024) : 0;
        var gpu = GetSingle("Win32_VideoController", "Name") ?? string.Empty;
        var vram = long.TryParse(GetSingle("Win32_VideoController", "AdapterRAM"), out var v) ? v / (1024 * 1024) : 0;
        var hz = int.TryParse(GetSingle("Win32_VideoController", "CurrentRefreshRate"), out var r) ? r : 0;
        return new HardwareFingerprint("Windows", cpu.Trim(), cores, ram, gpu.Trim(), vram, hz, Environment.OSVersion.VersionString);
    }

    private static string? GetSingle(string className, string property)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
            return item[property]?.ToString();
        return null;
    }
}
