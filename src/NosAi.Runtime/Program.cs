using System.Collections;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
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
        new(StringComparer.OrdinalIgnoreCase) { "--dxgi-probe", "--input-probe" };

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
