using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NosAi.Runtime.Perception;

/// <summary>
/// The DPI awareness regime a process is running under, which is what decides the
/// unit every window coordinate it reads is expressed in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Unknown is a value, not a gap.</b> A regime this cannot identify is not
/// reported as <see cref="Unaware"/>: those are opposite claims, and the one that
/// gets defaulted into is the one that would let a calibration be reused across the
/// change it exists to catch.
/// </para>
/// </remarks>
public enum DpiAwarenessRegime : byte
{
    /// <summary>The regime could not be read. Not the same as unaware.</summary>
    Unknown = 0,

    /// <summary>
    /// Windows virtualises coordinates: every rectangle is reported in logical
    /// pixels at 96 DPI, whatever the display is actually running at.
    /// </summary>
    Unaware = 1,

    /// <summary>Physical pixels, but frozen at the primary display's scale at start-up.</summary>
    System = 2,

    /// <summary>Physical pixels, per-monitor, v1.</summary>
    PerMonitor = 3,

    /// <summary>Physical pixels, per-monitor, v2.</summary>
    PerMonitorV2 = 4,

    /// <summary>Virtualised, with GDI content scaled by the system.</summary>
    UnawareGdiScaled = 5
}

/// <summary>
/// Reads the DPI awareness regime of the current process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is read and not assumed.</b> The manifest is embedded in the
/// <i>apphost</i>, so it applies to <c>NosAi.Runtime.exe</c> and not to the
/// assembly. Measured on 1 Sep 2026 on the operator's machine:
/// <c>NosAi.Runtime.exe --window-probe</c> reports <see cref="PerMonitorV2"/>, while
/// <c>dotnet NosAi.Runtime.dll --window-probe</c> reports <see cref="PerMonitor"/> —
/// the regime of the <c>dotnet</c> host, which carries its own manifest. The regime
/// a calibration was estimated under is therefore a function of the command that
/// launched it, which is not something anybody would think to record.
/// </para>
/// <para>
/// <b>What actually changes the numbers.</b> Aware against unaware: on a display at
/// 125% the same window measures 1536x912 to an unaware reader and 1920x1140 to an
/// aware one. Between the two aware regimes, a <c>GetClientRect</c> on another
/// process's window is physical pixels either way, so no difference in this unit is
/// known. They are still distinguished and still refused across, because "no known
/// difference" is not "no difference", recording it costs nothing, and the refusal
/// falls exactly where the trap was found — a calibration estimated from one launch
/// command and reused under the other.
/// </para>
/// </remarks>
public static class DpiAwareness
{
    /// <summary>The regime this process is running under.</summary>
    public static DpiAwarenessRegime Current()
    {
        if (!OperatingSystem.IsWindows())
            return DpiAwarenessRegime.Unknown;

        return CurrentWindows();
    }

    /// <summary>The wire form written into a calibration file.</summary>
    /// <remarks>
    /// A token rather than the enum's numeric value: a file outlives the enum, and a
    /// reordered enum must not silently reinterpret a stored regime as a different
    /// one.
    /// </remarks>
    public static string ToWire(this DpiAwarenessRegime regime) => regime switch
    {
        DpiAwarenessRegime.Unaware => "unaware",
        DpiAwarenessRegime.System => "system",
        DpiAwarenessRegime.PerMonitor => "permonitor",
        DpiAwarenessRegime.PerMonitorV2 => "permonitorv2",
        DpiAwarenessRegime.UnawareGdiScaled => "unaware-gdi-scaled",
        _ => "unknown"
    };

    /// <summary>Parses the wire form. An unrecognised token reads as <see cref="DpiAwarenessRegime.Unknown"/>.</summary>
    public static DpiAwarenessRegime FromWire(string? token) => token switch
    {
        "unaware" => DpiAwarenessRegime.Unaware,
        "system" => DpiAwarenessRegime.System,
        "permonitor" => DpiAwarenessRegime.PerMonitor,
        "permonitorv2" => DpiAwarenessRegime.PerMonitorV2,
        "unaware-gdi-scaled" => DpiAwarenessRegime.UnawareGdiScaled,
        _ => DpiAwarenessRegime.Unknown
    };

    [SupportedOSPlatform("windows")]
    private static DpiAwarenessRegime CurrentWindows()
    {
        // The context is what distinguishes per-monitor v2 from v1.
        // GetProcessDpiAwareness collapses both to PROCESS_PER_MONITOR_DPI_AWARE, so
        // it is the fallback and not the first reading.
        try
        {
            IntPtr context = GetThreadDpiAwarenessContext();
            if (AreDpiAwarenessContextsEqual(context, ContextPerMonitorV2))
                return DpiAwarenessRegime.PerMonitorV2;
            if (AreDpiAwarenessContextsEqual(context, ContextPerMonitor))
                return DpiAwarenessRegime.PerMonitor;
            if (AreDpiAwarenessContextsEqual(context, ContextSystem))
                return DpiAwarenessRegime.System;
            if (AreDpiAwarenessContextsEqual(context, ContextUnawareGdiScaled))
                return DpiAwarenessRegime.UnawareGdiScaled;
            if (AreDpiAwarenessContextsEqual(context, ContextUnaware))
                return DpiAwarenessRegime.Unaware;
        }
        catch (EntryPointNotFoundException)
        {
            // Windows 10 1607+. Fall through to the older call.
        }

        try
        {
            int hr = GetProcessDpiAwareness(GetCurrentProcess(), out int awareness);
            if (hr < 0)
                return DpiAwarenessRegime.Unknown;

            return awareness switch
            {
                0 => DpiAwarenessRegime.Unaware,
                1 => DpiAwarenessRegime.System,

                // v1 and v2 are one value here. Reporting v2 would be a guess that
                // happens to be right on modern Windows and wrong where it matters.
                2 => DpiAwarenessRegime.PerMonitor,
                _ => DpiAwarenessRegime.Unknown
            };
        }
        catch (DllNotFoundException)
        {
            return DpiAwarenessRegime.Unknown;
        }
        catch (EntryPointNotFoundException)
        {
            return DpiAwarenessRegime.Unknown;
        }
    }

    // DPI_AWARENESS_CONTEXT values are documented as (DPI_AWARENESS_CONTEXT)-N.
    private static readonly IntPtr ContextUnaware = (IntPtr)(-1);
    private static readonly IntPtr ContextSystem = (IntPtr)(-2);
    private static readonly IntPtr ContextPerMonitor = (IntPtr)(-3);
    private static readonly IntPtr ContextPerMonitorV2 = (IntPtr)(-4);
    private static readonly IntPtr ContextUnawareGdiScaled = (IntPtr)(-5);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    [SupportedOSPlatform("windows")]
    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
