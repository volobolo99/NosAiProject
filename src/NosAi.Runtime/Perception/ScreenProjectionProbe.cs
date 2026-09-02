using System.Globalization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// The storage half of F2-3: hold the collected pairs, then solve them.
/// </summary>
/// <remarks>
/// <para>
/// Solving is separate from collecting, for the reason <c>--memory-scan</c> and
/// <c>--memory-narrow</c> are two commands: a calibration is produced across
/// several moments in the game, so the samples have to outlive one invocation.
/// They persist in <see cref="SamplesRelativePath"/> until they are solved or
/// cleared.
/// </para>
/// <para>
/// <b>Who fills the file.</b> Not a person any more.
/// <see cref="ScreenProjectionAutoCalibrator"/> clicks pixels it chooses and reads
/// the square the client resolved each one to, and
/// <see cref="ScreenProjectionWatcher"/> does the same by watching the operator
/// click. Both record an <i>offset from the character</i>, which is the only
/// quantity a camera that follows the character leaves measurable;
/// <see cref="RunSample"/> recorded absolute coordinates and now refuses.
/// </para>
/// </remarks>
public static class ScreenProjectionProbe
{
    /// <summary>Where the pending samples live, relative to the repository root.</summary>
    public const string SamplesRelativePath = "data/perception/screen-samples.txt";

    /// <summary>
    /// The hand-aimed sample, which is refused: it records the wrong quantity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This wrote an absolute map coordinate against a cursor pixel, and no
    /// transform between those two exists — the camera follows the character, so
    /// the same square is drawn wherever the character happens to be standing.
    /// The samples it produced fitted a transform with a residual of 0.00 that
    /// described nothing, which is why the model is now an offset from the
    /// character and the file format carries a version that refuses the old one.
    /// </para>
    /// <para>
    /// It is refused rather than deleted so that following an older note produces
    /// an explanation instead of a sample file that solves into a calibration
    /// aimed somewhere nobody chose.
    /// </para>
    /// </remarks>
    public static int RunSample(string? repoRoot, int mapX, int mapY, string? processName = null)
    {
        _ = repoRoot;
        _ = processName;

        Console.WriteLine("[REFUSED] absolute_samples_are_not_measurable");
        Console.WriteLine($"  A pair of map ({mapX},{mapY}) with a cursor pixel cannot calibrate anything:");
        Console.WriteLine("  the camera follows the character, so that square is drawn at a different");
        Console.WriteLine("  pixel every time the character moves.");
        Console.WriteLine();
        Console.WriteLine("  What is measurable is the offset from the character to the target, and the");
        Console.WriteLine("  client itself will state one — a click to walk makes it resolve a pixel into");
        Console.WriteLine("  a square and write the answer down. Either command collects those:");
        Console.WriteLine("    --screen-autocalibrate --arm-input   the runtime clicks, nobody aims");
        Console.WriteLine("    --screen-watch <seconds>             you click, it watches");
        return 1;
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
            Console.WriteLine("  Collect them with --screen-autocalibrate --arm-input, or with");
            Console.WriteLine("  --screen-watch <seconds> to record your own clicks instead.");
            return 1;
        }

        if (!ScreenProjectionCalibration.TrySolve(
                samples, clientWidth, clientHeight, DateTime.UtcNow,
                out ScreenProjectionCalibration calibration, out string? reason))
        {
            Console.WriteLine($"[REFUSED] {reason}");
            Console.WriteLine("  Nothing was written. The old calibration, if any, is untouched.");
            if (reason is not null && reason.StartsWith("samples_are_collinear", StringComparison.Ordinal))
                Console.WriteLine("  The offsets lie on a line: one of them has to cross the others' direction.");
            return 1;
        }

        string path = Path.Combine(repoRoot, ScreenProjectionCalibration.RelativePath);
        calibration.Save(path);

        Console.WriteLine($"Screen projection calibrated from {samples.Count} samples.");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  screenX = {calibration.A:F4}*dx + {calibration.B:F4}*dy + {calibration.C:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  screenY = {calibration.D:F4}*dx + {calibration.E:F4}*dy + {calibration.F:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Character drawn at {calibration.Anchor.X:F0},{calibration.Anchor.Y:F0}"
            + $" of {clientWidth}x{clientHeight}."));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Worst residual: {calibration.WorstResidualPixels:F2} px over {samples.Count} samples"
            + $" ({calibration.VerifiedAgainstSamples} more than the fit needs)."));
        if (calibration.VerifiedAgainstSamples == 0)
        {
            Console.WriteLine("  Solved from exactly three pairs, which three pairs always reproduce");
            Console.WriteLine("  exactly, so the residual confirmed nothing. One more sample would.");
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
