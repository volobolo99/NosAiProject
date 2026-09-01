using System.Globalization;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.LowLevel;

namespace NosAi.Runtime.Perception;

/// <summary>
/// The operator's side of F2-3: collect map/pixel pairs, then solve them.
/// </summary>
/// <remarks>
/// <para>
/// Two commands, for the reason <c>--memory-scan</c> and <c>--memory-narrow</c>
/// are two: a calibration is produced across several moments in the game, so the
/// samples have to outlive one invocation. They persist in
/// <see cref="SamplesRelativePath"/> until the operator solves or clears them.
/// </para>
/// <para>
/// The screen half of each pair is the cursor. The operator puts the pointer on
/// their character and types the coordinates the game's own interface is showing,
/// which is the same independent reading T-03 used for the HUD: a number the
/// runtime did not produce.
/// </para>
/// <para>
/// Reading the cursor is not injection — <see cref="GatedInputBackend"/> allows
/// it with every switch off — so calibrating never requires arming the runtime.
/// </para>
/// </remarks>
public static class ScreenProjectionProbe
{
    /// <summary>Where the pending samples live, relative to the repository root.</summary>
    public const string SamplesRelativePath = "data/perception/screen-samples.txt";

    /// <summary>Records one pair: the map coordinate typed, the cursor where it is.</summary>
    public static int RunSample(string? repoRoot, int mapX, int mapY, string? processName = null)
    {
        repoRoot ??= Directory.GetCurrentDirectory();

        if (!TryClientArea(processName, out PixelRect area, out string? why))
        {
            Console.WriteLine($"[REFUSED] {why}");
            Console.WriteLine("  Without the client area a pixel cannot be made relative to the window,");
            Console.WriteLine("  and a calibration in desktop coordinates dies the first time it moves.");
            return 1;
        }

        var backend = new Win32InputBackend();
        if (!backend.TryGetCursorPosition(out int cursorX, out int cursorY))
        {
            Console.WriteLine("[REFUSED] cursor_position_unavailable");
            return 1;
        }

        int relativeX = cursorX - area.X;
        int relativeY = cursorY - area.Y;
        if (relativeX < 0 || relativeX >= area.Width || relativeY < 0 || relativeY >= area.Height)
        {
            Console.WriteLine($"[REFUSED] cursor_outside_client_area ({cursorX},{cursorY})");
            Console.WriteLine("  Put the pointer on the character inside the game window, then run this again.");
            return 1;
        }

        string path = Path.Combine(repoRoot, SamplesRelativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.AppendAllText(path, string.Create(
            CultureInfo.InvariantCulture,
            $"{mapX} {mapY} {relativeX} {relativeY} {area.Width} {area.Height}\n"));

        int count = ReadSamples(path, out _, out _).Count;
        Console.WriteLine($"Sample recorded: map ({mapX},{mapY}) -> client pixel ({relativeX},{relativeY}).");
        Console.WriteLine($"  Client area: {area.Width}x{area.Height}. Samples so far: {count}.");
        if (count < ScreenProjectionCalibration.MinimumSamples)
        {
            Console.WriteLine(
                $"  {ScreenProjectionCalibration.MinimumSamples - count} more needed. Move the character so the");
            Console.WriteLine("  three points do not fall on one line — walk in two different directions.");
        }
        else
        {
            Console.WriteLine("  Enough to solve. A fourth sample is not fitted: it checks the result.");
        }

        Console.WriteLine($"  {path}");
        return 0;
    }

    /// <summary>Solves the recorded samples into a calibration, or says why not.</summary>
    public static int RunSolve(string? repoRoot)
    {
        repoRoot ??= Directory.GetCurrentDirectory();
        string samplePath = Path.Combine(repoRoot, SamplesRelativePath);

        List<ScreenProjectionSample> samples = ReadSamples(samplePath, out int clientWidth, out int clientHeight);
        if (samples.Count == 0)
        {
            Console.WriteLine($"[REFUSED] no_samples_recorded ({samplePath})");
            Console.WriteLine("  Record them with --screen-sample <mapX> <mapY>.");
            return 1;
        }

        if (!ScreenProjectionCalibration.TrySolve(
                samples, clientWidth, clientHeight, DateTime.UtcNow,
                out ScreenProjectionCalibration calibration, out string? reason))
        {
            Console.WriteLine($"[REFUSED] {reason}");
            Console.WriteLine("  Nothing was written. The old calibration, if any, is untouched.");
            if (reason is not null && reason.StartsWith("samples_are_collinear", StringComparison.Ordinal))
                Console.WriteLine("  The three map points lie on a line: walk in a second direction and sample again.");
            return 1;
        }

        string path = Path.Combine(repoRoot, ScreenProjectionCalibration.RelativePath);
        calibration.Save(path);

        Console.WriteLine($"Screen projection calibrated from {samples.Count} samples.");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  screenX = {calibration.A:F4}·mapX + {calibration.B:F4}·mapY + {calibration.C:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  screenY = {calibration.D:F4}·mapX + {calibration.E:F4}·mapY + {calibration.F:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Worst residual: {calibration.WorstResidualPixels:F2} px over {samples.Count} samples"
            + $" ({calibration.VerifiedAgainstSamples} held back as a check)."));
        if (calibration.VerifiedAgainstSamples == 0)
        {
            Console.WriteLine("  Solved from exactly three pairs, so nothing independent confirmed it.");
            Console.WriteLine("  One more sample would.");
        }

        Console.WriteLine($"  {path}");
        Console.WriteLine("  Valid for this client at this size only. Resize the window and it is refused.");
        return 0;
    }

    /// <summary>Discards the pending samples.</summary>
    public static int RunClear(string? repoRoot)
    {
        repoRoot ??= Directory.GetCurrentDirectory();
        string path = Path.Combine(repoRoot, SamplesRelativePath);
        if (File.Exists(path))
            File.Delete(path);

        Console.WriteLine($"Samples cleared: {path}");
        Console.WriteLine("  The calibration itself, if one was written, is untouched.");
        return 0;
    }

    /// <summary>
    /// Reads the pending samples, keeping only those taken at the client size the
    /// most recent one used.
    /// </summary>
    /// <remarks>
    /// Mixing sizes would fit one transform to two different zooms and produce a
    /// map that is wrong for both, which the residual check would then reject —
    /// but silently dropping the stale ones and saying so is more use to the
    /// operator than a refusal they have to diagnose.
    /// </remarks>
    private static List<ScreenProjectionSample> ReadSamples(
        string path, out int clientWidth, out int clientHeight)
    {
        clientWidth = 0;
        clientHeight = 0;
        var samples = new List<ScreenProjectionSample>();
        if (!File.Exists(path))
            return samples;

        var parsed = new List<(ScreenProjectionSample Sample, int Width, int Height)>();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 6) continue;
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mx)) continue;
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int my)) continue;
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sx)) continue;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sy)) continue;
            if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) continue;
            if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) continue;
            parsed.Add((new ScreenProjectionSample(new MapPoint(mx, my), sx, sy), w, h));
        }

        if (parsed.Count == 0)
            return samples;

        (_, clientWidth, clientHeight) = parsed[^1];
        foreach ((ScreenProjectionSample sample, int width, int height) in parsed)
        {
            if (width == clientWidth && height == clientHeight)
                samples.Add(sample);
        }

        int dropped = parsed.Count - samples.Count;
        if (dropped > 0)
        {
            Console.WriteLine(
                $"  {dropped} sample(s) taken at a different client size were ignored.");
        }

        return samples;
    }

    /// <summary>
    /// Finds the client window, trying every name the runtime knows the client by.
    /// </summary>
    /// <remarks>
    /// The same list <see cref="NosAi.LiveIntegration.RealClientConnector"/> uses,
    /// rather than one name here and three there: the operator should not have to
    /// discover that calibration looks for a different process than attachment
    /// does.
    /// </remarks>
    private static bool TryClientArea(string? processName, out PixelRect area, out string? failureReason)
    {
        area = default;
        failureReason = null;

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "client_window_not_located:not_windows";
            return false;
        }

        string[] names = string.IsNullOrWhiteSpace(processName)
            ? NosAi.LiveIntegration.RealClientConnector.DefaultProcessNames
            : [processName];

        foreach (string name in names)
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (ClientWindowLocator.TryFind(process.Id, out _) is { } window)
                    {
                        area = window.ClientArea;
                        return true;
                    }
                }
            }
        }

        failureReason = $"client_window_not_located:{string.Join('/', names)}";
        return false;
    }
}
