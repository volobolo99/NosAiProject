using Microsoft.Win32;
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
        var wmiVram = long.TryParse(Read("Win32_VideoController", "AdapterRAM"), out var v) ? v / (1024 * 1024) : 0;
        var vram = ResolveVramMb(wmiVram, gpu);
        var hz = int.TryParse(Read("Win32_VideoController", "CurrentRefreshRate"), out var r) ? r : 0;
        return new HardwareFingerprint("Windows", cpu.Trim(), cores, ram, gpu.Trim(), vram, hz, Environment.OSVersion.VersionString);
    }

    /// <summary>
    /// Video memory as reported by WMI saturates at 4095 MB, because
    /// <c>Win32_VideoController.AdapterRAM</c> is a <c>UInt32</c> and cannot hold more
    /// than 4 GB. An 8 GB card then reads as 4 GB and, being one megabyte short of the
    /// 4096 threshold, drops the graphics tier by two steps.
    /// </summary>
    private const long WmiVramSaturationMb = 4095;

    /// <summary>Display adapters class key, whose entries carry the size as a 64-bit QWORD.</summary>
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>
    /// Keeps the WMI figure when it is trustworthy and falls back to the registry only when
    /// WMI is missing or saturated, so a machine whose registry is unreadable is no worse off.
    /// </summary>
    /// <param name="wmiVramMb">Megabytes reported by WMI; zero when it could not be read.</param>
    /// <param name="gpuName">Adapter name used to pick the matching registry entry.</param>
    /// <returns>The largest trustworthy figure, or zero when none could be established.</returns>
    private long ResolveVramMb(long wmiVramMb, string gpuName)
    {
        if (wmiVramMb > 0 && wmiVramMb < WmiVramSaturationMb)
        {
            return wmiVramMb;
        }

        return ChooseVramMb(wmiVramMb, ReadRegistryVramMb(gpuName));
    }

    /// <summary>
    /// Decides which of the two readings to trust. Kept free of I/O so the rule can be
    /// verified without a graphics card: a saturated or missing WMI figure yields to the
    /// registry, and a registry that reads back smaller never shrinks a good WMI figure.
    /// </summary>
    /// <param name="wmiVramMb">Megabytes from WMI; zero when unavailable, 4095 when saturated.</param>
    /// <param name="registryVramMb">Megabytes from the 64-bit registry value; zero when unavailable.</param>
    /// <returns>The figure to report, or zero when neither source produced one.</returns>
    public static long ChooseVramMb(long wmiVramMb, long registryVramMb)
    {
        if (wmiVramMb > 0 && wmiVramMb < WmiVramSaturationMb)
        {
            return wmiVramMb;
        }

        return registryVramMb > wmiVramMb ? registryVramMb : wmiVramMb;
    }

    /// <summary>
    /// Reads the 64-bit video memory size from the display class keys, preferring the adapter
    /// whose <c>DriverDesc</c> matches the detected GPU; with no match the largest adapter wins,
    /// because an integrated chip must never shadow the discrete card.
    /// </summary>
    /// <param name="gpuName">Adapter name to match; empty falls back to the largest entry.</param>
    /// <returns>Megabytes of dedicated video memory, or zero when nothing could be read.</returns>
    private long ReadRegistryVramMb(string gpuName)
    {
        try
        {
            using RegistryKey? displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (displayClass is null)
            {
                LastFailureReason ??= "registry_display_class_missing";
                return 0;
            }

            long largest = 0;
            foreach (string subKeyName in displayClass.GetSubKeyNames())
            {
                using RegistryKey? adapter = displayClass.OpenSubKey(subKeyName);
                if (adapter?.GetValue("HardwareInformation.qwMemorySize") is not long qwMemorySize || qwMemorySize <= 0)
                {
                    continue;
                }

                long megabytes = qwMemorySize / (1024 * 1024);
                string description = adapter.GetValue("DriverDesc") as string ?? string.Empty;
                if (gpuName.Length > 0 && description.Trim().Equals(gpuName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return megabytes;
                }

                if (megabytes > largest)
                {
                    largest = megabytes;
                }
            }

            return largest;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            LastFailureReason ??= $"registry_vram:{ex.GetType().Name}";
            return 0;
        }
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
