using System.Diagnostics;
using System.Text.RegularExpressions;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The runtime process has to declare per-monitor v2, otherwise
/// <c>GetClientRect</c> virtualises the client area on any display not at 100%.
/// </summary>
public sealed class ClientWindowDpiProbeTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RuntimeManifestDeclaresPerMonitorV2()
    {
        string root = RepositoryRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "app.manifest"));
        string csproj = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "NosAi.Runtime.csproj"));

        Assert.Contains("true/pm", manifest, StringComparison.Ordinal);
        Assert.Contains(">PerMonitorV2</dpiAwareness>", manifest, StringComparison.Ordinal);
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csproj, StringComparison.Ordinal);

        string probe = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Perception", "ClientWindowDpiProbe.cs"));
        Assert.Contains("GeometryEpoch.Read", probe, StringComparison.Ordinal);
        Assert.Contains("Epoch:", probe, StringComparison.Ordinal);
        Assert.Contains("CalibrationIsUsable", probe, StringComparison.Ordinal);
        Assert.Contains("NOT USABLE", probe, StringComparison.Ordinal);
        Assert.Contains("CalibrationNotUsableExitCode", probe, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unknown is not unaware. Kept from the probe's own naming table, which moved
    /// into <see cref="DpiAwareness"/> when the calibration started recording the
    /// regime: two copies of that reading could disagree about the thing a refusal
    /// now depends on. The property being pinned is the same one — a regime that
    /// could not be identified must not read as any regime in particular, least of
    /// all as the one that would let a calibration through.
    /// </summary>
    [Theory]
    [InlineData("unaware", DpiAwarenessRegime.Unaware)]
    [InlineData("system", DpiAwarenessRegime.System)]
    [InlineData("permonitor", DpiAwarenessRegime.PerMonitor)]
    [InlineData("permonitorv2", DpiAwarenessRegime.PerMonitorV2)]
    [InlineData("unaware-gdi-scaled", DpiAwarenessRegime.UnawareGdiScaled)]
    [InlineData("something-a-later-build-writes", DpiAwarenessRegime.Unknown)]
    [InlineData("", DpiAwarenessRegime.Unknown)]
    [InlineData(null, DpiAwarenessRegime.Unknown)]
    public void AnUnrecognisedRegimeTokenReadsAsUnknownAndNeverAsUnaware(string? token, DpiAwarenessRegime expected)
        => Assert.Equal(expected, DpiAwareness.FromWire(token));

    [Fact]
    public void EveryRegimeSurvivesTheRoundTripToItsWireForm()
    {
        foreach (DpiAwarenessRegime regime in Enum.GetValues<DpiAwarenessRegime>())
            Assert.Equal(regime, DpiAwareness.FromWire(regime.ToWire()));
    }

    /// <summary>
    /// The regime is read, never assumed. On the operator's machine the same build
    /// answers PerMonitor under <c>dotnet exec</c> and PerMonitorV2 from the apphost,
    /// so whatever this returns here, it has to be a reading.
    /// </summary>
    [Fact]
    public void TheCurrentRegimeIsReadableAndIsNotDefaultedToUnaware()
    {
        DpiAwarenessRegime regime = DpiAwareness.Current();

        if (OperatingSystem.IsWindows())
            Assert.NotEqual(DpiAwarenessRegime.Unknown, regime);

        Assert.Equal(regime, DpiAwareness.FromWire(regime.ToWire()));
    }

    [Fact]
    public void ProbeRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, ClientWindowDpiProbe.Run());
    }

    [Fact]
    public void ACalibrationFromThisRegimeAtThisShapeIsUsable()
    {
        ScreenProjectionCalibration calibration = CalibratedUnder(DpiAwarenessRegime.PerMonitorV2, dpi: 96);

        Assert.True(ClientWindowDpiProbe.CalibrationIsUsable(
            calibration,
            DpiAwarenessRegime.PerMonitorV2,
            new GeometryShape(1024, 768, 96),
            out string? reason), reason);
        Assert.Null(reason);
    }

    [Fact]
    public void ACalibrationFromAnotherRegimeIsNotUsableAndTheReasonNamesBoth()
    {
        ScreenProjectionCalibration calibration = CalibratedUnder(DpiAwarenessRegime.PerMonitorV2, dpi: 96);

        Assert.False(ClientWindowDpiProbe.CalibrationIsUsable(
            calibration,
            DpiAwarenessRegime.PerMonitor,
            new GeometryShape(1024, 768, 96),
            out string? reason));
        Assert.StartsWith(CalibratedScreenProjection.RegimeChangedReason, reason, StringComparison.Ordinal);
        Assert.Contains("permonitorv2_to_permonitor", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACalibrationAtADifferentDpiIsNotUsableAtTheSameSize()
    {
        ScreenProjectionCalibration calibration = CalibratedUnder(DpiAwarenessRegime.PerMonitorV2, dpi: 96);

        Assert.False(ClientWindowDpiProbe.CalibrationIsUsable(
            calibration,
            DpiAwarenessRegime.PerMonitorV2,
            new GeometryShape(1024, 768, 144),
            out string? reason));
        Assert.StartsWith(CalibratedScreenProjection.ClientDpiChangedReason, reason, StringComparison.Ordinal);
        Assert.Contains("96_to_144", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRegimeOnEitherSideIsNeverUsable()
    {
        ScreenProjectionCalibration known = CalibratedUnder(DpiAwarenessRegime.PerMonitorV2, dpi: 96);
        Assert.False(ClientWindowDpiProbe.CalibrationIsUsable(
            known, DpiAwarenessRegime.Unknown, new GeometryShape(1024, 768, 96), out _));

        Assert.False(ClientWindowDpiProbe.CalibrationIsUsable(
            ScreenProjectionCalibration.Uncalibrated,
            DpiAwarenessRegime.PerMonitorV2,
            new GeometryShape(1024, 768, 96),
            out string? missing));
        Assert.Equal(ScreenProjectionCalibration.NotCalibratedReason, missing);
    }

    /// <summary>
    /// The probe prints the regime and whether the stored file is usable under it,
    /// and leaves with a non-zero code when it is not — even when no client window
    /// is there to compare a shape against.
    /// </summary>
    [Fact]
    public void ProbePrintsThatACalibrationFromAnotherRegimeIsNotUsable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nosai-window-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "screen-projection.calibration");

        try
        {
            CalibratedUnder(DpiAwarenessRegime.Unaware, dpi: 96).Save(path);

            string output = CaptureConsole(() =>
            {
                int code = ClientWindowDpiProbe.Run(
                    processName: "no-such-process-zzzz",
                    calibrationPath: path);
                Assert.Equal(1, code);
            });

            Assert.Contains("Process DPI awareness:", output, StringComparison.Ordinal);
            Assert.Contains("NOT USABLE", output, StringComparison.Ordinal);
            Assert.Contains(CalibratedScreenProjection.RegimeChangedReason, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The trap measured on the operator's machine, pinned rather than described:
    /// the apphost reports PerMonitorV2 and <c>dotnet exec</c> reports PerMonitor,
    /// because the manifest is embedded in the exe and the <c>dotnet</c> host
    /// carries its own.
    /// </summary>
    /// <remarks>
    /// The apphost is launched through PowerShell rather than as a child of
    /// testhost. Testhost is itself a <c>dotnet exec</c> host (PerMonitor v1),
    /// and a CreateProcess child of that host does not receive the exe's
    /// PerMonitorV2 context — GetProcessDpiAwareness then collapses v1 and v2
    /// into one value, which is exactly the reading this test exists to keep
    /// distinct. PowerShell starts a new process with its own activation
    /// context, which is how the operator launches it too.
    /// </remarks>
    [Fact]
    public void TheApphostAndDotnetExecReportDifferentRegimes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string assembly = typeof(ClientWindowDpiProbe).Assembly.Location;
        string exe = Path.ChangeExtension(assembly, ".exe");
        Assert.True(File.Exists(exe), $"apphost not copied next to {assembly}");
        Assert.True(File.Exists(assembly), $"runtime assembly missing at {assembly}");

        string apphostOutput = RunApphostProbe(exe);
        string dotnetOutput = RunProbe("dotnet", ["exec", assembly, "--window-probe"]);

        Assert.Equal("permonitorv2", ParseRegimeWire(apphostOutput));
        Assert.Equal("permonitor", ParseRegimeWire(dotnetOutput));
        Assert.NotEqual(ParseRegimeWire(apphostOutput), ParseRegimeWire(dotnetOutput));
    }

    private static ScreenProjectionCalibration CalibratedUnder(DpiAwarenessRegime regime, uint dpi)
    {
        const double a = 16.0, b = -16.0, c = 512.0, d = 8.0, e = 8.0, f = 380.0;

        List<ScreenProjectionSample> samples =
            new[] { (6, 2), (-4, 5), (1, -7), (8, 8) }
                .Select(p => new ScreenProjectionSample(
                    new MapPoint(p.Item1, p.Item2),
                    (int)Math.Round((a * p.Item1) + (b * p.Item2) + c),
                    (int)Math.Round((d * p.Item1) + (e * p.Item2) + f)))
                .ToList();

        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, 1024, 768, At,
            out ScreenProjectionCalibration calibration, out string? reason,
            regime: regime, clientDpi: dpi), reason);

        return calibration;
    }

    private static string CaptureConsole(Action action)
    {
        TextWriter previous = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static string RunProbe(string fileName, IReadOnlyList<string> arguments)
    {
        string directory = Path.GetDirectoryName(typeof(ClientWindowDpiProbe).Assembly.Location)
            ?? AppContext.BaseDirectory;

        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start);
        Assert.NotNull(process);

        string output = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), $"{fileName} did not exit within 30s.\n{errors}");
        Assert.False(string.IsNullOrWhiteSpace(output), $"no stdout from {fileName}: {errors}");
        return output;
    }

    /// <summary>
    /// Runs the apphost out of testhost's CreateProcess tree so the exe's own
    /// manifest is what the OS assigns, not the test host's PerMonitor v1.
    /// </summary>
    private static string RunApphostProbe(string exe)
    {
        string directory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        string outFile = Path.Combine(Path.GetTempPath(), "nosai-apphost-probe-" + Guid.NewGuid().ToString("N") + ".txt");
        string escapedExe = exe.Replace("'", "''", StringComparison.Ordinal);
        string escapedOut = outFile.Replace("'", "''", StringComparison.Ordinal);

        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"& '{escapedExe}' --window-probe | Out-File -FilePath '{escapedOut}' -Encoding utf8");

        try
        {
            using var process = Process.Start(start);
            Assert.NotNull(process);
            string errors = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), $"apphost probe did not exit within 30s.\n{errors}");
            Assert.True(File.Exists(outFile), $"apphost probe wrote nothing: {errors}");
            string output = File.ReadAllText(outFile);
            Assert.False(string.IsNullOrWhiteSpace(output), $"apphost probe empty. stderr: {errors}");
            return output;
        }
        finally
        {
            try { File.Delete(outFile); } catch (IOException) { }
        }
    }

    private static string ParseRegimeWire(string probeOutput)
    {
        Match match = Regex.Match(
            probeOutput,
            @"Process DPI awareness: \w+ \(([^)]+)\)");
        Assert.True(match.Success, $"regime line missing from:\n{probeOutput}");
        return match.Groups[1].Value;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
