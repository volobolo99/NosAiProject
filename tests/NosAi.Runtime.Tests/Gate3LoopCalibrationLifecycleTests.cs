using Microsoft.Data.Sqlite;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Learning;
using NosAi.Runtime.Observability;
using NosAi.Storage;
using Xunit;

using RuntimeSafetyPolicy = NosAi.Runtime.Safety.RuntimeSafetyPolicy;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il ciclo di decisione rilegge la calibrazione appresa quando parte e la
/// riscrive quando chiude.
/// </summary>
/// <remarks>
/// <para>
/// <b>Il buco che questi test chiudono.</b> Fino al 2026-09-07
/// <see cref="Gate3ExecutionOrchestrator"/> scriveva nel registro delle
/// previsioni a ogni giro e nessuno rileggeva: lo stato erano tre dizionari in
/// memoria, quindi il runtime rifaceva da capo a ogni avvio l'apprendimento del
/// giorno prima. I due metodi di salvataggio sono arrivati con Q-132, ma senza un
/// chiamante: il ciclo di vita non era di nessuno.
/// </para>
/// <para>
/// <b>Perche' il ciclo e non l'host.</b> Il registro vive dentro l'orchestratore,
/// e l'orchestratore vive dentro <see cref="Gate3DecisionLoop"/>, che e' gia'
/// <see cref="IAsyncDisposable"/>: e' l'unico posto che sa quando quel registro
/// comincia e quando finisce, senza inventare un ciclo di vita che non c'era.
/// </para>
/// </remarks>
public sealed class Gate3LoopCalibrationLifecycleTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"nosai-loop-calibration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false,
        RequireClientHealthy: true, RequireGuardApproval: true);

    private static GoalStack Hunting() => GoalStack.With(HuntGoal.Hunt("loop-persist", new[] { 36 }));

    private static Gate3WorldState Fighting() => Gate3WorldState.Live(800, 1000, 100, true, false);

    private sealed class LiveVitalsObserver : IWorldStateObserver
    {
        public bool CanObserve => true;

        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ObservedState.Live(800, 40, DateTime.UtcNow));
    }

    private sealed class CountingEffector : IActionEffector
    {
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(
            ActionCandidate candidate, NosAi.Runtime.Safety.SafetyToken token,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 5, null));
    }

    private sealed class OneStateSource : IWorldStateSource
    {
        public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Fighting());
    }

    private static Gate3ExecutionOrchestrator Orchestrator() =>
        new(ExecutionAllowed, new CountingEffector(), new LiveVitalsObserver(),
            goals: Hunting(), skillCostOf: _ => new SkillCost(MpCost: 35, CastTimeMs: 800));

    /// <summary>
    /// Il giro completo: un ciclo impara, chiude, e il ciclo dopo ritrova quello
    /// che il primo aveva imparato.
    /// </summary>
    [Fact]
    public async Task Cio_che_un_ciclo_impara_il_ciclo_successivo_lo_ritrova()
    {
        Gate3ExecutionOrchestrator first = Orchestrator();
        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
        await using (var loop = new Gate3DecisionLoop(
                         new OneStateSource(), first, new NullRuntimeLogger(),
                         calibrationStore: store))
        {
            await loop.RunOnceAsync();
        }

        Calibration? learned = first.Learning.CalibrationOf(ActionType.UseSkill.ToString());
        Assert.NotNull(learned);

        Gate3ExecutionOrchestrator second = Orchestrator();
        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
        // Un intervallo lunghissimo perche' la pompa non completi un giro fra
        // Start e la lettura: quello che si misura qui e' il ripristino, e un
        // ciclo che gira nel frattempo lo sporcherebbe con nuove prove.
        await using (var loop = new Gate3DecisionLoop(
                         new OneStateSource(), second, new NullRuntimeLogger(),
                         interval: TimeSpan.FromHours(1), calibrationStore: store))
        {
            loop.Start();
        }

        Calibration? restored = second.Learning.CalibrationOf(ActionType.UseSkill.ToString());
        Assert.NotNull(restored);
        // Confrontare due calibrazioni entrambe a zero prove non dimostra niente:
        // prima si stabilisce che c'era qualcosa da ripristinare.
        Assert.True(learned!.Trials > 0, "il primo orchestratore non ha imparato niente");
        Assert.Equal(learned.Trials, restored!.Trials);
        Assert.Equal(learned.ExpectedAccuracy, restored.ExpectedAccuracy);
    }

    /// <summary>
    /// Senza archivio il ciclo funziona come prima: impara e dimentica, e non
    /// finge di ricordare.
    /// </summary>
    [Fact]
    public async Task Senza_archivio_il_ciclo_gira_lo_stesso()
    {
        Gate3ExecutionOrchestrator orchestrator = Orchestrator();
        await using var loop = new Gate3DecisionLoop(
            new OneStateSource(), orchestrator, new NullRuntimeLogger());

        Gate3LoopCycle cycle = await loop.RunOnceAsync();

        // `Assert.NotNull` su un record non annullabile non asseriva niente. Il
        // giro ha davvero deciso: un'azione scelta e un istante suo.
        Assert.NotEqual(default, cycle.AtUtc);
        Assert.False(string.IsNullOrWhiteSpace(cycle.Summary));
        Assert.False(File.Exists(_databasePath));
    }
}
