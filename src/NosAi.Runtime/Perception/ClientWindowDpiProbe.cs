using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Prints the client window's physical geometry and the process's actual DPI
/// awareness.
/// </summary>
/// <remarks>
/// <para>
/// Declaring per-monitor v2 in the manifest is not the same as having it. This
/// reads the mode the OS assigned, the window's current DPI, and the monitor it
/// sits on, so a scaled display is a printed fact rather than a silently
/// virtualised rectangle.
/// </para>
/// <para>
/// It does not keep an epoch and it does not invalidate a calibration. Those
/// are safety decisions; this only reports what is true of one window at one
/// moment. The regime reading itself lives in <see cref="DpiAwareness"/>, because
/// <see cref="ScreenProjectionCalibration"/> now records it and
/// <see cref="CalibratedScreenProjection"/> refuses across a change in it — and two
/// copies of that reading could disagree about the thing a refusal depends on.
/// </para>
/// </remarks>
public static class ClientWindowDpiProbe
{
    public const string NotWindowsReason = "window_probe_requires_windows";
    public const string WindowNotLocatedReason = "client_window_not_located";

    /// <param name="processName">
    /// One client executable, or null to try
    /// <see cref="RealClientConnector.DefaultProcessNames"/> in order — the same
    /// names attachment uses, so the probe does not look for a different process
    /// than the rest of the runtime.
    /// </param>
    public static int Run(string? processName = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return 2;
        }

        return RunWindows(processName);
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(string? processName)
    {
        DpiAwarenessRegime regime = DpiAwareness.Current();
        Console.WriteLine($"Process DPI awareness: {regime} ({regime.ToWire()})");

        ReportDisplayScale();

        if (!TryFindWindow(processName, out ClientWindow window, out string? failure))
        {
            Console.WriteLine($"[REFUSED] {failure}");
            return 1;
        }

        PixelRect rect = window.ClientArea;
        uint dpi = GetDpiForWindow(window.Handle);
        IntPtr monitor = MonitorFromWindow(window.Handle, MonitorDefaultToNearest);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Window: 0x{window.Handle.ToInt64():X} class={window.ClassName}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Client rect: {rect.X},{rect.Y} {rect.Width}x{rect.Height}"));
        Console.WriteLine(dpi == 0
            ? "DPI: UNKNOWN (GetDpiForWindow returned 0)"
            : string.Create(CultureInfo.InvariantCulture, $"DPI: {dpi}"));
        Console.WriteLine(monitor == IntPtr.Zero
            ? "Monitor: UNKNOWN (MonitorFromWindow returned 0)"
            : string.Create(CultureInfo.InvariantCulture, $"Monitor: 0x{monitor.ToInt64():X}"));
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryFindWindow(string? processName, out ClientWindow window, out string? failureReason)
    {
        string[] names = string.IsNullOrWhiteSpace(processName)
            ? RealClientConnector.DefaultProcessNames
            : [processName];

        foreach (string name in names)
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    ClientWindow? found = ClientWindowLocator.TryFind(process.Id, out string? why);
                    if (found is not null)
                    {
                        window = found;
                        failureReason = null;
                        return true;
                    }

                    failureReason = why;
                }
            }
        }

        window = null!;
        failureReason = $"{WindowNotLocatedReason}:{string.Join('/', names)}";
        return false;
    }

    /// <summary>
    /// The primary display's scale, read so that the answer does not depend on the
    /// reader's own awareness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither reading alone is the scale.</b> A first version of this printed the
    /// ratio of <c>VERTRES</c> to <c>DESKTOPVERTRES</c> and reported "100%" on a
    /// display running at 125%, because an aware process gets the physical height for
    /// both. The reported DPI has the mirror-image fault: an unaware process is told
    /// 96 whatever the display is doing. Each reading is blind in exactly the regime
    /// where the other one sees.
    /// </para>
    /// <para>
    /// <b>The product is the scale.</b> Virtualisation moves the factor from one
    /// reading into the other and never destroys it: aware at 125% gives 120/96 = 1.25
    /// and an extents ratio of 1; unaware at 125% gives 96/96 = 1 and an extents ratio
    /// of 1200/960 = 1.25. Both come to 1.25, and at 100% both come to 1. So the two
    /// are multiplied, and each is printed as well, because a scale that disagreed
    /// with its own two inputs would be worth seeing rather than trusting.
    /// </para>
    /// <para>
    /// The height is used rather than the width because a multi-monitor desktop is
    /// normally wider than one screen and rarely taller.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void ReportDisplayScale()
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            Console.WriteLine("Display scale: UNKNOWN (GetDC returned nothing)");
            return;
        }

        int logical, physical, dpi;
        try
        {
            logical = GetDeviceCaps(screen, VertRes);
            physical = GetDeviceCaps(screen, DesktopVertRes);
            dpi = GetDeviceCaps(screen, LogPixelsY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        if (logical <= 0 || physical <= 0 || dpi <= 0)
        {
            Console.WriteLine("Display scale: UNKNOWN (GetDeviceCaps returned nothing usable)");
            return;
        }

        double fromDpi = dpi / 96.0;
        double fromExtents = physical / (double)logical;
        double scale = fromDpi * fromExtents;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Display scale: {scale * 100:F0}% on the primary display"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  reported DPI {dpi} (x{fromDpi:F2}), extents {logical}\u2192{physical}px (x{fromExtents:F2})"));

        if (Math.Abs(scale - 1.0) > 0.001)
        {
            Console.WriteLine(
                "  Not 100%. Logical and physical pixels differ here, so a calibration");
            Console.WriteLine(
                "  estimated under one awareness regime is in the wrong unit under another.");
        }
    }

    private const int VertRes = 10;
    private const int LogPixelsY = 90;
    private const int DesktopVertRes = 117;

    private const uint MonitorDefaultToNearest = 2;

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [SupportedOSPlatform("windows")]
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}
