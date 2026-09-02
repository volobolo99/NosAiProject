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
/// It prints the current <see cref="GeometryEpoch"/> as a reading, and it does
/// not keep one. Invalidating a calibration is a safety decision; this only
/// reports what is true of one window at one moment, and whether the stored
/// calibration can be applied under this process's regime and that shape. The
/// regime reading itself lives in <see cref="DpiAwareness"/>, because
/// <see cref="ScreenProjectionCalibration"/> now records it and
/// <see cref="CalibratedScreenProjection"/> refuses across a change in it — and two
/// copies of that reading could disagree about the thing a refusal depends on.
/// </para>
/// </remarks>
public static class ClientWindowDpiProbe
{
    public const string NotWindowsReason = "window_probe_requires_windows";
    public const string WindowNotLocatedReason = "client_window_not_located";

    /// <summary>
    /// Returned when the window was found but the stored calibration cannot be
    /// applied under this process regime or the live window shape.
    /// </summary>
    public const int CalibrationNotUsableExitCode = 3;

    /// <param name="processName">
    /// One client executable, or null to try
    /// <see cref="RealClientConnector.DefaultProcessNames"/> in order — the same
    /// names attachment uses, so the probe does not look for a different process
    /// than the rest of the runtime.
    /// </param>
    /// <param name="calibrationPath">
    /// The stored projection to judge, or null to read
    /// <see cref="ScreenProjectionCalibration.RelativePath"/> from the current
    /// directory — the same file the auto-calibrator writes.
    /// </param>
    public static int Run(string? processName = null, string? calibrationPath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return 2;
        }

        return RunWindows(processName, calibrationPath);
    }

    /// <summary>
    /// Whether a stored calibration can be applied under this process regime and,
    /// when a live window shape is known, under that shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Regime first, then size, then DPI — the same order
    /// <see cref="CalibratedScreenProjection.TryProject"/> uses, and for the same
    /// reason: the regime is the unit every other comparison would be expressed
    /// in. Unknown on either side of the regime is a refusal, never a pass.
    /// </para>
    /// <para>
    /// A missing live shape (the window was not located) still judges the regime,
    /// because that is a property of how this process was launched, not of one
    /// window. Size and DPI are skipped rather than invented.
    /// </para>
    /// </remarks>
    public static bool CalibrationIsUsable(
        ScreenProjectionCalibration calibration,
        DpiAwarenessRegime regime,
        GeometryShape? liveShape,
        out string? refusalReason)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        if (!calibration.IsCalibrated)
        {
            refusalReason = ScreenProjectionCalibration.NotCalibratedReason;
            return false;
        }

        if (regime != calibration.Regime || regime == DpiAwarenessRegime.Unknown)
        {
            refusalReason =
                $"{CalibratedScreenProjection.RegimeChangedReason}:{calibration.Regime.ToWire()}_to_{regime.ToWire()}";
            return false;
        }

        if (liveShape is { } shape && shape.IsKnown)
        {
            if (shape.Width != calibration.ClientWidth || shape.Height != calibration.ClientHeight)
            {
                refusalReason = CalibratedScreenProjection.ClientResizedReason;
                return false;
            }

            if (calibration.ClientDpi != 0 && shape.Dpi != 0 && shape.Dpi != calibration.ClientDpi)
            {
                refusalReason =
                    $"{CalibratedScreenProjection.ClientDpiChangedReason}:{calibration.ClientDpi}_to_{shape.Dpi}";
                return false;
            }
        }

        refusalReason = null;
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(string? processName, string? calibrationPath)
    {
        DpiAwarenessRegime regime = DpiAwareness.Current();
        Console.WriteLine($"Process DPI awareness: {regime} ({regime.ToWire()})");

        ReportDisplayScale();

        bool windowFound = TryFindWindow(processName, out ClientWindow window, out string? failure);
        GeometryEpoch epoch = GeometryEpoch.Unknown;

        if (!windowFound)
        {
            Console.WriteLine($"[REFUSED] {failure}");
        }
        else
        {
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

            epoch = GeometryEpoch.Read(window);
            Console.WriteLine(epoch.IsKnown
                ? string.Create(CultureInfo.InvariantCulture,
                    $"Epoch: {epoch.ClientArea.X},{epoch.ClientArea.Y} "
                    + $"{epoch.ClientArea.Width}x{epoch.ClientArea.Height} "
                    + $"dpi={epoch.Dpi} monitor=0x{epoch.Monitor.ToInt64():X}")
                : "Epoch: UNKNOWN");
        }

        string path = calibrationPath
            ?? Path.Combine(Directory.GetCurrentDirectory(), ScreenProjectionCalibration.RelativePath);
        ScreenProjectionCalibration stored = ScreenProjectionCalibration.Load(path, out _);
        GeometryShape? liveShape = epoch.IsKnown ? epoch.Shape : null;
        bool usable = CalibrationIsUsable(stored, regime, liveShape, out string? whyNot);
        ReportCalibration(path, stored, usable, whyNot);

        if (!windowFound)
            return 1;

        return usable ? 0 : CalibrationNotUsableExitCode;
    }

    private static void ReportCalibration(
        string path,
        ScreenProjectionCalibration stored,
        bool usable,
        string? whyNot)
    {
        if (stored.IsCalibrated)
        {
            string verdict = usable ? "usable" : $"NOT USABLE {whyNot}";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Calibration: {stored.ClientWidth}x{stored.ClientHeight} dpi={stored.ClientDpi} under {stored.Regime.ToWire()} ({path}) — {verdict}"));
            return;
        }

        Console.WriteLine($"Calibration: none ({path}) — NOT USABLE {whyNot}");
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
