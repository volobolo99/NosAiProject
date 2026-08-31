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
public sealed class WindowsHardwareProbe : IHardwareProbe, IHardwareProbeDiagnostics
{
    public string? LastFailureReason { get; private set; }

    public HardwareFingerprint Detect()
    {
        LastFailureReason = null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsHardwareProbe requires Windows WMI.");

        // Empty, not a sentinel label: absence is expressed by the value being
        // missing, so no caller has to pattern-match on the word "Unknown".
        var cpu = Read("Win32_Processor", "Name") ?? string.Empty;
        var cores = int.TryParse(Read("Win32_Processor", "NumberOfLogicalProcessors"), out var c) ? c : 0;
        var ram = long.TryParse(Read("Win32_ComputerSystem", "TotalPhysicalMemory"), out var bytes) ? bytes / (1024 * 1024) : 0;
        var gpu = Read("Win32_VideoController", "Name") ?? string.Empty;
        var vram = long.TryParse(Read("Win32_VideoController", "AdapterRAM"), out var v) ? v / (1024 * 1024) : 0;
        var hz = int.TryParse(Read("Win32_VideoController", "CurrentRefreshRate"), out var r) ? r : 0;
        return new HardwareFingerprint("Windows", cpu.Trim(), cores, ram, gpu.Trim(), vram, hz, Environment.OSVersion.VersionString);
    }

    private string? Read(string className, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
                return item[property]?.ToString();
            return null;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            LastFailureReason ??= $"wmi_{className}_{property}:{ex.GetType().Name}";
            return null;
        }
    }
}
