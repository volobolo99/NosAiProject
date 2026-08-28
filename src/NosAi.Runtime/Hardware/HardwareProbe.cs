using System.Management;

namespace NosAi.Runtime.Hardware;

public interface IHardwareProbe
{
    HardwareFingerprint Detect();
}

/// <summary>Windows hardware probe used by PlayAi on first run.</summary>
public sealed class WindowsHardwareProbe : IHardwareProbe
{
    public HardwareFingerprint Detect()
    {
        var cpu = GetSingle("Win32_Processor", "Name") ?? "Unknown CPU";
        var cores = int.TryParse(GetSingle("Win32_Processor", "NumberOfLogicalProcessors"), out var c) ? c : Environment.ProcessorCount;
        var ram = long.TryParse(GetSingle("Win32_ComputerSystem", "TotalPhysicalMemory"), out var bytes) ? bytes / (1024 * 1024) : 0;
        var gpu = GetSingle("Win32_VideoController", "Name") ?? "Unknown GPU";
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
