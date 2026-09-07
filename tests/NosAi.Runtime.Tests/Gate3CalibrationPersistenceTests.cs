using Microsoft.Data.Sqlite;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Learning;
using NosAi.Runtime.Safety;
using NosAi.Storage;
using Xunit;

using RuntimeSafetyPolicy = NosAi.Runtime.Safety.RuntimeSafetyPolicy;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="Gate3ExecutionOrchestrator"/>'s two additive persistence methods,
/// <see cref="Gate3ExecutionOrchestrator.SaveCalibration"/> and
/// <see cref="Gate3ExecutionOrchestrator.RestoreCalibration"/>: the calibration
/// can be written and read back without touching the decision cycle, which is
/// the only thing that keeps SQLite off <c>Observe → … → Execute</c>.
/// </summary>
public sealed class Gate3CalibrationPersistenceTests : IDisposable
{
    private readonly string _databasePath;

    public Gate3CalibrationPersistenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-gate3-calibration-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static readonly Func<int, SkillCost?> MeasuredSkillCost =
        _ => new SkillCost(MpCost: 35, CastTimeMs: 800);

    private static GoalStack Hunting() => GoalStack.With(HuntGoal.Hunt("learning-persist", new[] { 36 }));

    private static Gate3WorldState Fighting() => Gate3WorldState.Live(800, 1000, 100, true, false);

    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false,
        RequireClientHealthy: true, RequireGuardApproval: true);

    private sealed class LiveVitalsObserver : IWorldStateObserver
    {
        private readonly int _hp;
        private readonly int _mp;
        public LiveVitalsObserver(int hp, int mp) { _hp = hp; _mp = mp; }
        public bool CanObserve => true;

        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ObservedState.Live(_hp, _mp, DateTime.UtcNow));
    }

    private sealed class CountingEffector : IActionEffector
    {
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(
            ActionCandidate candidate, SafetyToken token, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 5, null));
    }

    [Fact]
    public async Task SaveThenRestore_AcrossTwoOrchestrators_ReproducesTheCalibration()
    {
        var first = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, new CountingEffector(), new LiveVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(), skillCostOf: MeasuredSkillCost);
        await first.ExecuteCycleAsync(Fighting());

        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
            first.SaveCalibration(store);

        var second = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, new CountingEffector(), new LiveVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(), skillCostOf: MeasuredSkillCost);
        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
            second.RestoreCalibration(store);

        Calibration expected = first.Learning.CalibrationOf(ActionType.UseSkill.ToString())!;
        Calibration actual = second.Learning.CalibrationOf(ActionType.UseSkill.ToString())!;

        Assert.NotNull(expected);
        Assert.Equal(expected.ExpectedAccuracy, actual.ExpectedAccuracy);
        Assert.Equal(expected.Trials, actual.Trials);
        Assert.Equal(expected.Confirmed, actual.Confirmed);
        Assert.Equal(expected.Refuted, actual.Refuted);
    }

    [Fact]
    public void RestoreCalibration_OnAnEmptyStore_LeavesTheLedgerUnchanged()
    {
        // A store with no saved calibration restores nothing, and must not
        // fabricate a belief. Restore from empty is the "no prior" case, and it
        // must read as "we have no evidence", not as a coin flip.
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, new CountingEffector(), new LiveVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(), skillCostOf: MeasuredSkillCost);

        using var store = new PredictionCalibrationStore(_databasePath, Options());
        orchestrator.RestoreCalibration(store);

        Assert.Null(orchestrator.Learning.CalibrationOf(ActionType.UseSkill.ToString()));
    }
}
