using System.Collections;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Gate4;
using NosAi.Runtime.Gate5;
using NosAi.Runtime.Observability;

namespace NosAi.Runtime;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Every certification suite the runtime carries, in one table.
        //
        // Pinning StartupObject makes every other Main in the assembly unreachable,
        // so a subsystem's own entry point cannot run it. Seven suites were written
        // and then never executed once for exactly that reason -- Gate 3 hid two
        // defects behind it, and the Gate 4 suite sat failing. A table beats a
        // ladder of ifs here: adding a runner without wiring it is the failure mode,
        // and one list makes the omission obvious.
        IReadOnlyDictionary<string, Func<Task<bool>>> suites = new Dictionary<string, Func<Task<bool>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["--gate1-test"] = Gate1TestRunner.RunAllAsync,
            ["--gate2-test"] = Gate2TestRunner.RunAllTestsAsync,
            ["--gate3-test"] = Gate3TestRunner.RunAllTestsAsync,
            ["--gate4-test"] = Gate4TestRunner.RunAllTestsAsync,
            ["--gate5-test"] = Gate5TestRunner.RunAllTestsAsync,
            ["--gate6-test"] = NosAi.Runtime.Gate6.Gate6ReleaseCertifier.RunFullReleaseCertificationAsync,
            ["--host-test"] = NosAi.Host.MasterHostTestRunner.RunAllTestsAsync,
            ["--storage-test"] = NosAi.Storage.Infrastructure.StorageInfrastructureTestRunner.RunAllTestsAsync,
            ["--navigation-test"] = NosAi.Navigation.Pathfinding.NavigationPathfindingTestRunner.RunAllTestsAsync,
            ["--gateway-test"] = NosAi.Network.Gateway.ControlPanelGatewayTestRunner.RunAllTestsAsync,
            ["--raids-test"] = NosAi.Raids.Dodekatheon.DodekatheonRaidTestRunner.RunAllTestsAsync,
            ["--miniland-test"] = NosAi.Miniland.Production.MinilandProductionTestRunner.RunAllTestsAsync,
            ["--localai-test"] = NosAi.AI.LocalInference.LocalAiInferenceTestRunner.RunAllTestsAsync,
            ["--hardware-test"] = NosAi.Hardware.Autoscale.HardwareAutoscaleTestRunner.RunAllTestsAsync,
            // Synchronous RunAll(), adapted here rather than by editing their files.
            ["--economy-test"] = () => Task.FromResult(NosAi.Economy.Inventory.InventoryEconomyTestRunner.RunAll()),
            ["--perception-test"] = () => Task.FromResult(NosAi.Runtime.Perception.PerceptionPipelineTestRunner.RunAll()),
            ["--security-test"] = () => Task.FromResult(NosAi.Runtime.Security.EphemeralSessionTestRunner.RunAll()),
            // Subsystems with synchronous runners are adapted to the table shape.
            ["--perception-test"] = () => Task.FromResult(NosAi.Runtime.Perception.PerceptionPipelineTestRunner.RunAll()),
            ["--crypto-test"] = () => Task.FromResult(NosAi.Runtime.Security.EphemeralSessionTestRunner.RunAll()),
            ["--economy-test"] = () => Task.FromResult(NosAi.Economy.Inventory.InventoryEconomyTestRunner.RunAll()),
        };

        foreach (string argument in args)
        {
            if (suites.TryGetValue(argument, out Func<Task<bool>>? suite))
                return await suite().ConfigureAwait(false) ? 0 : 1;
        }

        if (args.Any(a => string.Equals(a, "--list-suites", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string flag in suites.Keys.OrderBy(f => f, StringComparer.Ordinal))
                Console.WriteLine(flag);
            return 0;
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
