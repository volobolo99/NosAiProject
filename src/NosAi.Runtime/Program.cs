using System.Globalization;
using System.Collections;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Gate4;
using NosAi.Runtime.Gate5;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Every certification suite the runtime carries lives in CertificationSuites,
        // because the operator's test page needs the same list. Two copies would have
        // diverged the first time a suite was added to one and not the other.
        IReadOnlyDictionary<string, Func<Task<bool>>> suites = CertificationSuites.ByFlag;

        foreach (string argument in args)
        {
            if (suites.TryGetValue(argument, out Func<Task<bool>>? suite))
                return await suite().ConfigureAwait(false) ? 0 : 1;
        }

        // A mistyped suite flag used to fall through to the normal bootstrap and
        // start the whole runtime: "--1-test" instead of "--gate1-test" left a
        // host running for as long as nobody noticed, holding the build's output
        // files. Anything shaped like a suite or probe flag that is not one is a
        // typo, and a typo must not boot the runtime.
        string? mistyped = args.FirstOrDefault(a =>
            a.StartsWith("--", StringComparison.Ordinal) &&
            (a.EndsWith("-test", StringComparison.OrdinalIgnoreCase) ||
             a.EndsWith("-probe", StringComparison.OrdinalIgnoreCase)) &&
            !suites.ContainsKey(a) &&
            !KnownProbeFlags.Contains(a));
        if (mistyped is not null)
        {
            Console.Error.WriteLine($"Unknown suite or probe flag: {mistyped}");
            Console.Error.WriteLine("Run --list-suites to see the available suites, or use --dxgi-probe / --input-probe.");
            return 2;
        }

        // One screen instead of a list of flags to remember. It adds no
        // capability: every entry calls the command its flag calls, and the ones
        // that actuate are not in it.
        if (args.Any(a => string.Equals(a, "--menu", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Operator.OperatorMenu.Run();

        if (args.Any(a => string.Equals(a, "--list-suites", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string flag in suites.Keys.OrderBy(f => f, StringComparer.Ordinal))
                Console.WriteLine(flag);
            return 0;
        }

        // Which modules the runtime actually reaches. --list-suites answers "what
        // can be run"; this answers the question the audit of 2026-08-30 asked and
        // a document could not keep answering: what is wired, what is only
        // reachable from its own suite, and what nothing reaches at all.
        if (args.Any(a => string.Equals(a, "--module-report", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Write(NosAi.Runtime.Observability.ModuleReachability.Report());
            return 0;
        }

        // Real-environment probe for the DXGI capture backend. The perception suite
        // certifies the contract without a desktop; only a real interactive session
        // can say whether Desktop Duplication actually yields live pixels here.
        if (args.Any(a => string.Equals(a, "--dxgi-probe", StringComparison.OrdinalIgnoreCase)))
            return RunDxgiProbe();

        // Real-environment probe for the input layer. --input-test certifies the
        // contract against a recording backend; only a real desktop can say
        // whether SendInput actually reaches the OS input queue.
        if (args.Any(a => string.Equals(a, "--input-probe", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.LowLevel.InputEnvironmentProbe.RunConsoleProbe();

        // Real-environment probe for the HUD reader (T-03). The Control Panel has
        // had this behind a button; running it here makes the test repeatable and
        // quotable instead of clicked and described.
        if (args.Any(a => string.Equals(a, "--hud-probe", StringComparison.OrdinalIgnoreCase)))
        {
            // --calibrate-target <x> <y> <w> <h> records the target-frame region
            // (ADR-0018). The four fractions are the operator's confirmation that
            // the crop they just looked at is the target frame; nothing infers
            // them, because a reading of the wrong pixels is what the calibration
            // exists to rule out.
            int calibrateFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--calibrate-target", StringComparison.OrdinalIgnoreCase));
            (double, double, double, double)? region = null;
            if (calibrateFlag >= 0)
            {
                if (calibrateFlag + 4 >= args.Length
                    || !double.TryParse(args[calibrateFlag + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double rx)
                    || !double.TryParse(args[calibrateFlag + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ry)
                    || !double.TryParse(args[calibrateFlag + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out double rw)
                    || !double.TryParse(args[calibrateFlag + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out double rh))
                {
                    Console.Error.WriteLine(
                        "--calibrate-target <x> <y> <width> <height> requires four fractions of the client area.");
                    return 2;
                }

                region = (rx, ry, rw, rh);
            }

            return NosAi.Runtime.Perception.HudProbe.RunConsoleProbe(calibrateTarget: region);
        }

        // Physical client rect, window DPI, monitor handle, epoch, the process's
        // actual awareness mode, and whether the stored calibration can be applied
        // under that regime. Non-zero when it cannot.
        if (args.Any(a => string.Equals(a, "--window-probe", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ClientWindowDpiProbe.Run();

        // The five commit-point conditions against the live window. Observation
        // only: a refused verdict is printed, nothing is emitted. --watch <s>
        // keeps the stamp taken at the start so the three real-client proofs
        // (window moved, point covered, hand on the mouse) are named refusals.
        if (args.Any(a => string.Equals(a, "--input-guards", StringComparison.OrdinalIgnoreCase)))
        {
            int guardsWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int seconds = guardsWatchFlag >= 0 && guardsWatchFlag + 1 < args.Length
                          && int.TryParse(args[guardsWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                          && parsed > 0
                ? parsed
                : 0;

            return NosAi.Runtime.LowLevel.InputGuardsProbe.Run(seconds);
        }

        // Session actuation verdict: integrity comparison and the harmless probe.
        // A verification command — non-zero when the session is not actuating.
        // --watch <n> repeats n times at 1 s, calling EnsureVerified so the
        // operator can bring the client forward and see the verdict change.
        if (args.Any(a => string.Equals(a, "--input-authority", StringComparison.OrdinalIgnoreCase)))
        {
            int authorityWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int repeats = authorityWatchFlag >= 0 && authorityWatchFlag + 1 < args.Length
                          && int.TryParse(args[authorityWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRepeats)
                          && parsedRepeats > 0
                ? parsedRepeats
                : 0;

            return NosAi.Runtime.LowLevel.InputAuthorityProbe.Run(repeats);
        }

        // One adjacent-cell step (S4 / C2-4). The chain and the executor already
        // exist; this only prints them, audits them, and names the operator
        // command as the authority of the act. It does not arm input.
        int stepFlag = Array.FindIndex(args, a =>
            string.Equals(a, NosAi.Runtime.Navigation.SingleStepCommand.Flag, StringComparison.OrdinalIgnoreCase));
        if (stepFlag >= 0)
        {
            if (stepFlag + 2 >= args.Length
                || !int.TryParse(args[stepFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stepDx)
                || !int.TryParse(args[stepFlag + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stepDy))
            {
                Console.Error.WriteLine("--step <dx> <dy> requires two integer cell offsets.");
                return NosAi.Runtime.Navigation.SingleStepCommand.ExitUsage;
            }

            return NosAi.Runtime.Navigation.SingleStepCommand.Run(stepDx, stepDy);
        }

        // Which intents the operator bound, and which the runtime can ask for
        // that are not bound. Non-zero when the file is missing or a required
        // prefix is uncovered. Does not write data/keybinds.json.
        if (args.Any(a => string.Equals(a, "--keybinds-check", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.LowLevel.KeybindsCheck.Run();

        if (args.Any(a => string.Equals(a, "--extract-maps", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapGridExtractor.RunExtract();

        int mapInfoFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--map-info", StringComparison.OrdinalIgnoreCase));
        if (mapInfoFlag >= 0)
        {
            if (mapInfoFlag + 1 >= args.Length
                || !int.TryParse(args[mapInfoFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
            {
                Console.Error.WriteLine("--map-info <mapId> requires the map identifier.");
                return 2;
            }

            return NosAi.Runtime.Navigation.MapGridExtractor.RunInfo(mapId);
        }

        // Standing-cell proof: map id and position from the live client, the
        // bytes of that cell and its eight neighbours from the extracted grid.
        // Read-only — a blocked cell is reported, not rewritten.
        if (args.Any(a => string.Equals(a, "--grid-check", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapGridCheck.Run();

        // The 777 grids as an oracle: a word in memory is a map id only while it
        // names a .grid that contains the character, and only while that word
        // changes across a portal. +0x30 was a pointer; this does not follow it.
        if (args.Any(a => string.Equals(a, "--find-mapid", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapIdFinder.Run();

        // The same oracle, aimed at the selected entity instead of the map id
        // (ADR-0021). --no-target declares the pass that tells the selection apart
        // from the client's own entity list, which every entry of that list would
        // otherwise survive.
        if (args.Any(a => string.Equals(a, "--find-target", StringComparison.OrdinalIgnoreCase)))
        {
            bool targetSelected = !args.Any(a =>
                string.Equals(a, "--no-target", StringComparison.OrdinalIgnoreCase));
            return NosAi.Runtime.Navigation.TargetIdFinder.Run(targetSelected);
        }

        // Map coordinate to window pixel (F2-3). Two commands because a
        // calibration is gathered across several moments in the game, so the
        // samples have to outlive one invocation, exactly as --memory-scan's
        // candidates do.
        int screenSampleFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--screen-sample", StringComparison.OrdinalIgnoreCase));
        if (screenSampleFlag >= 0)
        {
            if (screenSampleFlag + 2 >= args.Length
                || !int.TryParse(args[screenSampleFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapX)
                || !int.TryParse(args[screenSampleFlag + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapY))
            {
                Console.Error.WriteLine(
                    "--screen-sample <mapX> <mapY> requires the coordinates the game is showing.");
                return 2;
            }

            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunSample(null, mapX, mapY);
        }

        // The calibration with nobody in it: the runtime picks the pixels, clicks
        // them and reads back which square the client resolved each one to. It
        // walks the character, so the gate stays shut without --arm-input.
        if (args.Any(a => string.Equals(a, "--screen-autocalibrate", StringComparison.OrdinalIgnoreCase)))
        {
            bool armInput = args.Any(a => string.Equals(a, "--arm-input", StringComparison.OrdinalIgnoreCase));
            return NosAi.Runtime.Perception.ScreenProjectionAutoCalibrator.Run(armInput);
        }

        // Collects samples by watching the operator click to walk. The character's
        // own position cannot calibrate this: the camera follows it, so it stays
        // drawn in the same place and the samples describe nothing.
        int watchFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--screen-watch", StringComparison.OrdinalIgnoreCase));
        if (watchFlag >= 0)
        {
            int seconds = watchFlag + 1 < args.Length
                          && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSeconds)
                          && parsedSeconds > 0
                ? parsedSeconds
                : 60;

            return NosAi.Runtime.Perception.ScreenProjectionWatcher.Run(seconds, wanted: 5);
        }

        if (args.Any(a => string.Equals(a, "--screen-calibrate", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunSolve(null);

        if (args.Any(a => string.Equals(a, "--screen-samples-clear", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunClear(null);

        // T-11 in one command: resolve the character object in the running client
        // and check the id it holds against the id the server sent. Read-only.
        int playerProbeFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--player-probe", StringComparison.OrdinalIgnoreCase));
        if (playerProbeFlag >= 0)
        {
            int pidFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--pid", StringComparison.OrdinalIgnoreCase));
            int targetPid = pidFlag >= 0 && pidFlag + 1 < args.Length
                            && int.TryParse(args[pidFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPid)
                ? parsedPid
                : 0;

            int expectFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--expect-id", StringComparison.OrdinalIgnoreCase));
            long? expectedId = expectFlag >= 0 && expectFlag + 1 < args.Length
                               && long.TryParse(args[expectFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedId)
                ? parsedId
                : null;

            return NosAi.LiveIntegration.PlayerObjectProbe.Run(targetPid, expectedId);
        }

        // What a recording says, read by the runtime's own decoder and needing no
        // driver. WinDivertProbe --world does the same, but it has to sit beside a
        // staged WinDivert.dll and it holds the runtime assembly open while it
        // runs; this reads a capture with nothing staged and nothing locked.
        int worldReplayFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--world-replay", StringComparison.OrdinalIgnoreCase));
        if (worldReplayFlag >= 0)
        {
            string? capture = worldReplayFlag + 1 < args.Length ? args[worldReplayFlag + 1] : null;
            if (string.IsNullOrWhiteSpace(capture))
            {
                Console.Error.WriteLine("--world-replay <file.noscap> requires a recording path.");
                return 2;
            }

            if (!File.Exists(capture))
            {
                Console.Error.WriteLine($"Recording not found: {capture}");
                return 2;
            }

            GameReferenceDatabase? catalog = null;
            if (!GameReferenceLocator.TryOpen(out catalog, out string? catalogReason))
                Console.Error.WriteLine($"reference catalog: {catalogReason}");
            else
                Console.Error.WriteLine($"reference catalog: {catalog!.DatabasePath}");

            try
            {
                return NosAi.Runtime.Observability.WorldReplayCommand.Run(capture, catalog);
            }
            finally
            {
                catalog?.Dispose();
            }
        }

        // Which catalogue this process would open, and what is in it. A missing
        // file is reported rather than invented: replay without names is still
        // a replay, and this is how the operator tells the two cases apart.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.ReferenceInfoCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.ReferenceInfoCommand.Run();

        // The decision path over real game bytes, offline. WinDivertProbe --world
        // reports what a recording says; this reports what the runtime decides
        // about it, which is the half nothing exercised before.
        int replayFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--decide-replay", StringComparison.OrdinalIgnoreCase));
        if (replayFlag >= 0)
        {
            string? recording = replayFlag + 1 < args.Length ? args[replayFlag + 1] : null;
            if (string.IsNullOrWhiteSpace(recording))
            {
                Console.Error.WriteLine("--decide-replay <file.noscap> requires a recording path.");
                return 2;
            }

            int cycleFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--decide-cycles", StringComparison.OrdinalIgnoreCase));
            int cycles = cycleFlag >= 0 && cycleFlag + 1 < args.Length
                         && int.TryParse(args[cycleFlag + 1], out int parsed) && parsed > 0
                ? parsed
                : 200;

            return await NosAi.Runtime.Observability.DecideReplayCommand.RunAsync(recording, cycles).ConfigureAwait(false);
        }

        // Offset discovery for the memory provider (ADR-0014). Read-only, and it
        // answers nothing on its own: an address is identified by narrowing across
        // several changes of the value, which is why the candidate set persists
        // between invocations rather than living inside one run.
        int scanFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--memory-scan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--memory-narrow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--memory-dump", StringComparison.OrdinalIgnoreCase));
        if (scanFlag >= 0)
            return NosAi.LiveIntegration.MemoryScanProbe.Run(args, scanFlag);

        // Diagnostic read of the durable event log (M075-M076). Sola lettura: it
        // reports how complete the audit trail is, gaps included, so a missing
        // event is visible rather than silently absent. An optional path follows.
        int eventLogFlag = Array.FindIndex(args, a => string.Equals(a, "--event-log-report", StringComparison.OrdinalIgnoreCase));
        if (eventLogFlag >= 0)
        {
            string? path = eventLogFlag + 1 < args.Length && !args[eventLogFlag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[eventLogFlag + 1]
                : null;
            var health = NosAi.Runtime.Gate2.EventLogDiagnostics.Inspect(path);
            Console.WriteLine(NosAi.Runtime.Gate2.EventLogDiagnostics.Describe(health));
            // Exit non-zero when the log is present but incomplete, so a script can
            // notice a lossy audit trail without parsing the text.
            return health.Readable && health.IsComplete ? 0 : 1;
        }

        // Operator immediate halt against a runtime already listening. Disarms
        // then aborts; a new process cannot halt the one that is actually armed.
        if (args.Any(a => string.Equals(a, "--halt", StringComparison.OrdinalIgnoreCase)))
            return await NosAi.Runtime.Operator.HaltCli.RunAsync(
                NosAi.Runtime.Operator.HaltCli.PortFromArgs(args)).ConfigureAwait(false);

        var logger = new ConsoleRuntimeLogger();
        Gate1HostOptions options;
        try
        {
            options = Gate1HostOptionsLoader.Load(ReadEnvironment(), args);
        }
        catch (Exception ex)
        {
            logger.Error("Gate 1 configuration is invalid; refusing to start.", ex);
            return 2;
        }

        await using var host = new Gate1BootstrapHost(options, logger);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await host.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (GuardChannelBindException ex)
        {
            // The Guard channel is the authenticated PC-phone link, so a failed bind
            // must fail closed. It is a configuration problem, not a defect: report
            // the port and the remedy, without a stack trace the operator cannot use.
            logger.Error($"Gate 1 bootstrap failed; the runtime is not serving. reason={ex.Reason}");
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            // A raw unhandled stack trace told the operator nothing actionable and
            // left the runtime dead. Report the failure and exit deliberately.
            logger.Error("Gate 1 bootstrap failed; the runtime is not serving.", ex);
            return 3;
        }

        var snapshot = host.Capture();
        Console.WriteLine("NosAi Runtime 1.0 Beta — Gate 1");
        Console.WriteLine($"Health: {host.Health}");
        Console.WriteLine($"Guard port: {host.GuardPort}");
        if (host.DashboardPort is int dashboard)
        {
            Console.WriteLine($"Runtime operator API: http://127.0.0.1:{dashboard}/");
            // The UI defaults to the runtime's default port, so only a non-default
            // port needs the operator to export the override.
            Console.WriteLine(dashboard == Gate1HostOptions.DefaultDashboardPort
                ? "Operator UI: python -m nosai.dashboard.server   (then open http://127.0.0.1:8765/)"
                : $"Operator UI: set NOSAI_RUNTIME_URL=http://127.0.0.1:{dashboard} then run: python -m nosai.dashboard.server");
        }
        else
        {
            Console.WriteLine($"Runtime operator API: UNAVAILABLE ({host.DashboardFailureReason})");
        }
        Console.WriteLine($"Client: {snapshot.Client.Status} ({snapshot.Client.Availability.Source.ToWire()})");
        Console.WriteLine($"Client process: {FormatClassified(snapshot.Client.ProcessName)} pid={FormatClassified(snapshot.Client.ProcessId)}");
        Console.WriteLine($"Client window: {FormatClassified(snapshot.Client.WindowTitle)} {FormatClassified(snapshot.Client.WindowHandle)}");
        Console.WriteLine($"Gameplay baseline: {FormatClassified(snapshot.Client.GameplayBaseline)}");
        Console.WriteLine($"Hardware CPU: {FormatClassified(snapshot.Hardware.Cpu)}");
        Console.WriteLine("Live input and packet injection remain disabled. Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    private static string FormatClassified<T>(ClassifiedValue<T> field)
        => !field.HasValue
            ? $"UNKNOWN ({field.FailureReason})"
            : $"{field.Value} [{field.Source.ToWire()}]";

    /// <summary>Probe flags, which live outside the suite table.</summary>
    private static readonly HashSet<string> KnownProbeFlags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--dxgi-probe", "--input-probe", "--memory-scan", "--memory-narrow", "--memory-dump",
            "--hud-probe", "--window-probe", "--input-guards", "--input-authority", "--step", "--keybinds-check", "--halt", "--event-log-report", "--decide-replay", "--player-probe", "--world-replay", "--reference-info",
            "--screen-sample", "--screen-calibrate", "--screen-samples-clear", "--screen-watch",
            "--screen-autocalibrate", "--arm-input"
        };

    private static int RunDxgiProbe()
    {
        Console.WriteLine("=== DXGI Desktop Duplication probe ===");
        if (!NosAi.Runtime.Perception.DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
        {
            Console.WriteLine($"[UNAVAILABLE] {unavailable!.Reason} (hr=0x{unavailable.HResult:X8})");
            Console.WriteLine("No live capture in this session. Perception stays UNKNOWN; no pixels are invented.");
            return 1;
        }

        using (capture)
        {
            Console.WriteLine($"[OK] duplication open: {capture!.Width}x{capture.Height}");
            for (int attempt = 1; attempt <= 40; attempt++)
            {
                if (!capture.TryAcquire(out var frame))
                {
                    // A still desktop legitimately produces no new frame.
                    Thread.Sleep(50);
                    continue;
                }
                ReadOnlySpan<byte> pixels = frame.Bgra.Span;
                var sampled = new HashSet<int>();
                byte min = 255, max = 0;
                for (int i = 0; i + 3 < pixels.Length; i += 64)
                {
                    if (sampled.Count < 4096)
                        sampled.Add(pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16));
                    if (pixels[i] < min) min = pixels[i];
                    if (pixels[i] > max) max = pixels[i];
                }

                Console.WriteLine($"[frame {attempt}] {frame.Width}x{frame.Height} source={frame.Source.ToWire()} " +
                                  $"bytes={frame.Bgra.Length} distinctColours={sampled.Count} blueMin={min} blueMax={max}");
                if (sampled.Count > 1)
                {
                    Console.WriteLine("=== DXGI probe passed: real desktop pixels captured. ===");
                    return 0;
                }
                // A uniform frame is normal right after DuplicateOutput; keep asking
                // (and keep writing to the console, which itself changes the screen).
                Thread.Sleep(60);
            }
            Console.WriteLine("[TIMEOUT] no frame within the attempt budget (a fully static desktop can do this).");
            return 1;
        }
    }

    private static Dictionary<string, string?> ReadEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                result[key] = entry.Value as string;
        }
        return result;
    }
}
