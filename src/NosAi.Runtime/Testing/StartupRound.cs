using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NosAi.LiveIntegration;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.Testing;

public static class StartupRound
{
    public const string Flag = "--startup-round";
    public const string QuickFlag = "--quick";
    public const string JsonFlag = "--json";

    private delegate T CalibrationLoader<T>(string path, out string? failureReason);

    public static IReadOnlyList<CertificationSuite> DefaultSuites(StartupRoundMode mode)
    {
        if (mode == StartupRoundMode.Quick)
        {
            return Array.Empty<CertificationSuite>();
        }

        return CertificationSuites.All;
    }

    public static Task<StartupRoundReport> RunAsync(
        StartupRoundMode mode,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(mode, DefaultSuites(mode), includeEnvironment: true, cancellationToken);
    }

    public static async Task<StartupRoundReport> RunAsync(
        StartupRoundMode mode,
        IReadOnlyList<CertificationSuite> suites,
        bool includeEnvironment,
        CancellationToken cancellationToken = default)
    {
        DateTime startedUtc = DateTime.UtcNow;
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        List<StartupCheckRecord> records = new List<StartupCheckRecord>();

        foreach (CertificationSuite suite in suites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch suiteStopwatch = Stopwatch.StartNew();
            bool ok;
            try
            {
                ok = await suite.Run().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                suiteStopwatch.Stop();
                records.Add(new StartupCheckRecord(
                    "suite." + suite.Key,
                    suite.Description,
                    StartupCheckRecord.SuiteCategory,
                    StartupCheckOutcome.Fail,
                    suiteStopwatch.ElapsedMilliseconds,
                    "eccezione",
                    ex.GetType().Name + ": " + ex.Message));
                continue;
            }

            suiteStopwatch.Stop();
            records.Add(new StartupCheckRecord(
                "suite." + suite.Key,
                suite.Description,
                StartupCheckRecord.SuiteCategory,
                ok ? StartupCheckOutcome.Pass : StartupCheckOutcome.Fail,
                suiteStopwatch.ElapsedMilliseconds,
                ok ? "esito=true" : "esito=false",
                ok ? null : "la suite ha riportato esito negativo"));
        }

        if (includeEnvironment)
        {
            records.AddRange(RunEnvironmentChecks(cancellationToken));
        }

        totalStopwatch.Stop();
        return StartupRoundReport.Create(mode, startedUtc, totalStopwatch.ElapsedMilliseconds, records);
    }

    public static IReadOnlyList<StartupCheckRecord> RunEnvironmentChecks(CancellationToken cancellationToken)
    {
        List<StartupCheckRecord> records = new List<StartupCheckRecord>(10);

        records.Add(CheckDesktopCapture(cancellationToken));
        records.Add(CheckInputLayer(cancellationToken));
        records.Add(CheckClientProcess(cancellationToken));
        records.Add(CheckCalibrationFile<ScreenProjectionCalibration>(
            ScreenProjectionCalibration.RelativePath,
            "env.calibration.screen-projection",
            "Calibrazione proiezione schermo",
            ScreenProjectionCalibration.Load,
            cancellationToken));
        records.Add(CheckCalibrationFile<TargetRoiCalibration>(
            TargetRoiCalibration.RelativePath,
            "env.calibration.target-roi",
            "Calibrazione ROI bersaglio",
            TargetRoiCalibration.Load,
            cancellationToken));
        records.Add(CheckCalibrationFile<InventoryPanelRoiCalibration>(
            InventoryPanelRoiCalibration.RelativePath,
            "env.calibration.inventory-panel-roi",
            "Calibrazione ROI pannello inventario",
            InventoryPanelRoiCalibration.Load,
            cancellationToken));
        records.Add(CheckCalibrationFile<DialogRoiCalibration>(
            DialogRoiCalibration.RelativePath,
            "env.calibration.dialog-roi",
            "Calibrazione ROI dialoghi",
            DialogRoiCalibration.Load,
            cancellationToken));
        records.Add(CheckKeybinds(cancellationToken));
        records.Add(CheckEventLog(cancellationToken));
        records.Add(CheckDataWritable(cancellationToken));

        return records;
    }

    public static int ExitCode(StartupRoundReport report)
    {
        return report.HasFailures ? 1 : 0;
    }

    public static async Task<int> RunCommandAsync(string[] args)
    {
        StartupRoundMode mode = args.Any(a => string.Equals(a, QuickFlag, StringComparison.OrdinalIgnoreCase))
            ? StartupRoundMode.Quick
            : StartupRoundMode.Full;

        string? jsonPath = null;
        int jsonIndex = Array.FindIndex(args, a => string.Equals(a, JsonFlag, StringComparison.OrdinalIgnoreCase));
        if (jsonIndex >= 0)
        {
            if (jsonIndex + 1 >= args.Length
                || string.IsNullOrWhiteSpace(args[jsonIndex + 1])
                || args[jsonIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--json <percorso> richiede il percorso del file.");
                return 2;
            }

            jsonPath = args[jsonIndex + 1];
        }

        StartupRoundReport report = await RunAsync(mode).ConfigureAwait(false);
        Console.Write(StartupRoundReport.Format(report));

        string latestPath = string.Empty;
        try
        {
            if (jsonPath is null)
            {
                latestPath = report.Write(StartupRoundReport.DefaultDirectory);
            }
            else
            {
                string? directory = Path.GetDirectoryName(jsonPath);
                if (string.IsNullOrEmpty(directory))
                {
                    directory = Directory.GetCurrentDirectory();
                }

                latestPath = report.Write(directory);
                if (!string.Equals(
                        Path.GetFileName(jsonPath),
                        StartupRoundReport.LatestFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(latestPath, jsonPath, overwrite: true);
                    latestPath = jsonPath;
                }
            }
        }
        catch (IOException ex)
        {
            // The round already measured; a disk problem is reported by env.data.writable.
            Console.Error.WriteLine(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }

        Console.WriteLine($"Report: {latestPath}");
        return ExitCode(report);
    }

    private static StartupCheckRecord Complete(
        Stopwatch stopwatch,
        string id,
        string title,
        StartupCheckOutcome outcome,
        string evidence,
        string? reason = null)
    {
        stopwatch.Stop();
        return new StartupCheckRecord(
            id,
            title,
            StartupCheckRecord.EnvironmentCategory,
            outcome,
            stopwatch.ElapsedMilliseconds,
            evidence,
            reason);
    }

    private static StartupCheckRecord CheckDesktopCapture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            if (!DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
            {
                return Complete(
                    stopwatch,
                    "env.capture.dxgi",
                    "Cattura desktop DXGI",
                    StartupCheckOutcome.Unknown,
                    "duplicazione non disponibile",
                    $"{unavailable!.Reason} (hr=0x{unavailable.HResult:X8})");
            }

            using (capture!)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (attempt > 0)
                    {
                        Thread.Sleep(50);
                    }

                    if (capture!.TryAcquire(out var frame))
                    {
                        // Pixel analysis belongs to --dxgi-probe; here only presence is checked.
                        return Complete(
                            stopwatch,
                            "env.capture.dxgi",
                            "Cattura desktop DXGI",
                            StartupCheckOutcome.Pass,
                            $"{frame.Width}x{frame.Height}, {frame.Bgra.Length} byte");
                    }
                }
            }

            return Complete(
                stopwatch,
                "env.capture.dxgi",
                "Cattura desktop DXGI",
                StartupCheckOutcome.Unknown,
                "nessun frame",
                "nessun frame entro il budget di tentativi (un desktop immobile lo fa)");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.capture.dxgi",
                "Cattura desktop DXGI",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckInputLayer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            // Decided by the architect: an automatic startup round never injects mouse or
            // keyboard input into the operator session, so this check is always skipped.
            return Complete(
                stopwatch,
                "env.input.layer",
                "Layer di input",
                StartupCheckOutcome.Skipped,
                $"sessione interattiva={System.Environment.UserInteractive}",
                "iniezione non provata automaticamente: usare --input-probe");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.input.layer",
                "Layer di input",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckClientProcess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (string name in RealClientConnector.DefaultProcessNames)
            {
                Process[] processes = Process.GetProcessesByName(name);
                try
                {
                    foreach (Process process in processes)
                    {
                        return Complete(
                            stopwatch,
                            "env.client.process",
                            "Processo client NosTale",
                            StartupCheckOutcome.Pass,
                            $"{process.ProcessName} pid {process.Id}");
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return Complete(
                stopwatch,
                "env.client.process",
                "Processo client NosTale",
                StartupCheckOutcome.Unknown,
                "nessun client",
                "nessun processo fra NostaleClientX/NostaleClient/NosTale");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.client.process",
                "Processo client NosTale",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckCalibrationFile<T>(
        string relativePath,
        string id,
        string title,
        CalibrationLoader<T> loader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (!File.Exists(path))
            {
                return Complete(
                    stopwatch,
                    id,
                    title,
                    StartupCheckOutcome.Unknown,
                    "file assente",
                    "non calibrato");
            }

            loader(path, out string? failureReason);
            if (failureReason is not null)
            {
                return Complete(
                    stopwatch,
                    id,
                    title,
                    StartupCheckOutcome.Fail,
                    "file illeggibile",
                    failureReason);
            }

            return Complete(
                stopwatch,
                id,
                title,
                StartupCheckOutcome.Pass,
                $"{relativePath}, {new FileInfo(path).Length} byte");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                id,
                title,
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckKeybinds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            KeybindsCheckReport report = KeybindsCheck.Inspect(KeybindsCheck.ResolvePath());

            if (!report.Exists)
            {
                return Complete(
                    stopwatch,
                    "env.keybinds",
                    "Keybind",
                    StartupCheckOutcome.Unknown,
                    "file assente",
                    report.Path);
            }

            if (report.LoadFailure is not null)
            {
                return Complete(
                    stopwatch,
                    "env.keybinds",
                    "Keybind",
                    StartupCheckOutcome.Fail,
                    "file illeggibile",
                    report.LoadFailure);
            }

            if (report.UncoveredPrefixes.Count > 0)
            {
                // Decided by the architect: uncovered prefixes are yellow, not red.
                return Complete(
                    stopwatch,
                    "env.keybinds",
                    "Keybind",
                    StartupCheckOutcome.Unknown,
                    $"{report.Configured.Count} intenti configurati",
                    "prefissi scoperti: " + string.Join(", ", report.UncoveredPrefixes));
            }

            return Complete(
                stopwatch,
                "env.keybinds",
                "Keybind",
                StartupCheckOutcome.Pass,
                $"{report.Configured.Count} intenti configurati, 0 prefissi scoperti");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.keybinds",
                "Keybind",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckEventLog(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            EventLogHealth health = EventLogDiagnostics.Inspect();

            if (!health.Exists)
            {
                return Complete(
                    stopwatch,
                    "env.eventlog",
                    "Log eventi durevole",
                    StartupCheckOutcome.Unknown,
                    "log non ancora creato",
                    health.DatabasePath);
            }

            if (!health.Readable)
            {
                return Complete(
                    stopwatch,
                    "env.eventlog",
                    "Log eventi durevole",
                    StartupCheckOutcome.Fail,
                    "log illeggibile",
                    health.FailureReason);
            }

            if (!health.IsComplete)
            {
                return Complete(
                    stopwatch,
                    "env.eventlog",
                    "Log eventi durevole",
                    StartupCheckOutcome.Fail,
                    $"{health.EventCount} eventi",
                    $"{health.GapCount} buchi, {health.LostEventCount} eventi persi");
            }

            return Complete(
                stopwatch,
                "env.eventlog",
                "Log eventi durevole",
                StartupCheckOutcome.Pass,
                $"{health.EventCount} eventi, 0 buchi");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.eventlog",
                "Log eventi durevole",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheckRecord CheckDataWritable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        string probe = Path.Combine("data", "health", ".write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine("data", "health"));
            File.WriteAllText(probe, "nosai");
            _ = File.ReadAllText(probe);
            return Complete(
                stopwatch,
                "env.data.writable",
                "Scrivibilità di data/",
                StartupCheckOutcome.Pass,
                "data/health scrivibile");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return Complete(
                stopwatch,
                "env.data.writable",
                "Scrivibilità di data/",
                StartupCheckOutcome.Fail,
                "scrittura rifiutata",
                ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Complete(
                stopwatch,
                "env.data.writable",
                "Scrivibilità di data/",
                StartupCheckOutcome.Fail,
                "scrittura rifiutata",
                ex.Message);
        }
        catch (Exception ex)
        {
            return Complete(
                stopwatch,
                "env.data.writable",
                "Scrivibilità di data/",
                StartupCheckOutcome.Unknown,
                "non misurabile",
                ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (IOException)
            {
                // The probe is best effort cleanup and must never fail the check.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: best effort cleanup only.
            }
        }
    }
}
