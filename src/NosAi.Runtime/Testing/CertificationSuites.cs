using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Gate4;
using NosAi.Runtime.Gate5;

namespace NosAi.Runtime.Testing;

/// <summary>One in-process certification suite, its flag and what it certifies.</summary>
public sealed record CertificationSuite(string Key, string Flag, string Description, Func<Task<bool>> Run);

/// <summary>
/// Every in-process certification suite the runtime carries, in one table.
/// </summary>
/// <remarks>
/// <para>
/// This table used to live inside <c>Program.cs</c>, reachable only from the
/// command line. The operator's test page needs the same list, and a second copy
/// would have been wrong the first time a suite was added to one and not the
/// other — so there is one table and both read it.
/// </para>
/// <para>
/// Pinning <c>StartupObject</c> makes every other <c>Main</c> in the assembly
/// unreachable, so a subsystem's own entry point cannot run its suite. Seven
/// suites were written and never executed once for exactly that reason. Adding a
/// runner without wiring it here is the failure mode this single list exists to
/// make obvious.
/// </para>
/// </remarks>
public static class CertificationSuites
{
    /// <summary>The suites, in the order the page and the CLI list them.</summary>
    public static IReadOnlyList<CertificationSuite> All { get; } = new CertificationSuite[]
    {
        new("gate1", "--gate1-test", "Gate 1 — bootstrap, canale, classificazione", Gate1TestRunner.RunAllAsync),
        new("gate2", "--gate2-test", "Gate 2 — world model, persistenza, replay", Gate2TestRunner.RunAllTestsAsync),
        new("gate3", "--gate3-test", "Gate 3 — ciclo decisione/sicurezza/verifica", Gate3TestRunner.RunAllTestsAsync),
        new("gate4", "--gate4-test", "Gate 4 — progressione e knowledge base", Gate4TestRunner.RunAllTestsAsync),
        new("gate5", "--gate5-test", "Gate 5 — routing inferenza e provider", Gate5TestRunner.RunAllTestsAsync),
        new("gate6", "--gate6-test", "Gate 6 — certificazione di rilascio",
            NosAi.Runtime.Gate6.Gate6ReleaseCertifier.RunFullReleaseCertificationAsync),
        new("host", "--host-test", "Host master — orchestrazione runtime",
            NosAi.Host.MasterHostTestRunner.RunAllTestsAsync),
        new("storage", "--storage-test", "Storage — volume, schema, backup",
            NosAi.Storage.Infrastructure.StorageInfrastructureTestRunner.RunAllTestsAsync),
        new("navigation", "--navigation-test", "Navigazione — pathfinding",
            NosAi.Navigation.Pathfinding.NavigationPathfindingTestRunner.RunAllTestsAsync),
        new("gateway", "--gateway-test", "Gateway di rete — telemetria non eseguibile",
            NosAi.Network.Gateway.ControlPanelGatewayTestRunner.RunAllTestsAsync),
        new("raids", "--raids-test", "Raid Dodekatheon",
            NosAi.Raids.Dodekatheon.DodekatheonRaidTestRunner.RunAllTestsAsync),
        new("miniland", "--miniland-test", "Miniland — produzione",
            NosAi.Miniland.Production.MinilandProductionTestRunner.RunAllTestsAsync),
        new("localai", "--localai-test", "Inferenza locale",
            NosAi.AI.LocalInference.LocalAiInferenceTestRunner.RunAllTestsAsync),
        new("hardware", "--hardware-test", "Hardware — autoscale",
            NosAi.Hardware.Autoscale.HardwareAutoscaleTestRunner.RunAllTestsAsync),
        new("input", "--input-test", "Controllo input di basso livello",
            NosAi.Runtime.LowLevel.InputControlTestRunner.RunAllTestsAsync),
        new("decision", "--decision-test", "Motore decisionale a utilita",
            NosAi.Runtime.AI.Decision.DecisionEngineTestRunner.RunAllTestsAsync),

        // Synchronous RunAll(), adapted here rather than by editing their files.
        new("netobserve", "--netobserve-test", "Osservazione di rete",
            () => Task.FromResult(NosAi.Runtime.Perception.Network.NetworkObservationTestRunner.RunAll())),
        new("economy", "--economy-test", "Economia e inventario",
            () => Task.FromResult(NosAi.Economy.Inventory.InventoryEconomyTestRunner.RunAll())),
        new("perception", "--perception-test", "Pipeline di percezione",
            () => Task.FromResult(NosAi.Runtime.Perception.PerceptionPipelineTestRunner.RunAll())),
        new("security", "--security-test", "Sessioni effimere e crittografia",
            () => Task.FromResult(NosAi.Runtime.Security.EphemeralSessionTestRunner.RunAll()))
    };

    /// <summary>
    /// The command-line flags, including the aliases the CLI has always accepted.
    /// </summary>
    /// <remarks>
    /// <c>--crypto-test</c> is a second name for the security suite. It is kept
    /// because removing a flag someone may have in a script is a silent break, but
    /// it is not a separate entry in <see cref="All"/>: the page would otherwise
    /// list the same suite twice and inflate the totals.
    /// </remarks>
    public static IReadOnlyDictionary<string, Func<Task<bool>>> ByFlag { get; } =
        All.ToDictionary(s => s.Flag, s => s.Run, StringComparer.OrdinalIgnoreCase)
           .Concat(new[]
           {
               new KeyValuePair<string, Func<Task<bool>>>("--crypto-test",
                   All.First(s => s.Key == "security").Run)
           })
           .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds a suite runner by its short key, or null when unknown.</summary>
    public static Func<Task<bool>>? Resolve(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))?.Run;
}
