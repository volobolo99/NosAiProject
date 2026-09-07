using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Learning;
using NosAi.Runtime.Safety;
using Xunit;

using RuntimeSafetyPolicy = NosAi.Runtime.Safety.RuntimeSafetyPolicy;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The loop now keeps what it predicted and what happened, instead of computing
/// both and dropping them.
/// </summary>
/// <remarks>
/// <para>
/// <c>Gate3ExecutionOrchestrator</c> produced a <c>PredictedOutcome</c> before
/// every act and a <c>VerificationResult</c> after it, used each once for that
/// cycle's own verdict, and kept neither. So the runtime could not answer "how
/// often is an act of this kind actually confirmed" although it had computed the
/// answer on every round. <c>PredictionLedger</c> did exactly that accounting and
/// had no caller.
/// </para>
/// <para>
/// The rule that makes it honest is the ledger's own: only a <c>Live</c>
/// observation moves a belief. These tests pin both halves -- that the cycle
/// records at all, and that a cycle verified against a reading that is not live
/// is counted and ignored rather than believed.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class Gate3LearningWiringTests
{
    private static readonly Func<int, SkillCost?> MeasuredSkillCost =
        _ => new SkillCost(MpCost: 35, CastTimeMs: 800);

    private static GoalStack Hunting() => GoalStack.With(Goal.Hunt("learning-test", new[] { 36 }));

    private static Gate3WorldState Fighting() => Gate3WorldState.Live(800, 1000, 100, true, false);

    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false,
        RequireClientHealthy: true, RequireGuardApproval: true);

    /// <summary>Vitals read back live, stamped when they are read.</summary>
    private sealed class LiveVitalsObserver : IWorldStateObserver
    {
        private readonly int _hp;
        private readonly int _mp;
        public LiveVitalsObserver(int hp, int mp) { _hp = hp; _mp = mp; }
        public bool CanObserve => true;

        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ObservedState.Live(_hp, _mp, DateTime.UtcNow));
    }

    /// <summary>The same reading, but cached: real, and not evidence about now.</summary>
    private sealed class CachedVitalsObserver : IWorldStateObserver
    {
        private readonly int _hp;
        private readonly int _mp;
        public CachedVitalsObserver(int hp, int mp) { _hp = hp; _mp = mp; }
        public bool CanObserve => true;

        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.UtcNow;
            return Task.FromResult(new ObservedState(
                ClassifiedValue<int>.Cached(_hp, now), ClassifiedValue<int>.Cached(_mp, now)));
        }
    }

    private sealed class CountingEffector : IActionEffector
    {
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(
            ActionCandidate candidate, SafetyToken token, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 5, null));
    }

    /// <summary>
    /// A live-verified cycle leaves a settled prediction behind, keyed by the
    /// action type it was about.
    /// </summary>
    [Fact]
    public async Task ALiveVerifiedCycle_TeachesTheLedger()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new LiveVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(),
            skillCostOf: MeasuredSkillCost);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Calibration? calibration = orchestrator.Learning.CalibrationOf(ActionType.UseSkill.ToString());
        Assert.NotNull(calibration);
        Assert.Equal(0, orchestrator.Learning.OpenPredictions);
        Assert.Equal(0, calibration!.Ignored);
        Assert.Equal(1, calibration.Confirmed + calibration.Refuted);

        // The ledger's verdict must agree with the cycle's own, or the two are
        // measuring different things.
        Assert.Equal(result.Outcome == CycleOutcome.Confirmed, calibration.Confirmed == 1);
    }

    /// <summary>
    /// A cycle verified against a cached reading is counted and ignored: real, and
    /// not evidence about the world now.
    /// </summary>
    /// <remarks>
    /// This is the half that keeps the accounting honest. A runtime that learned
    /// from its own replays would converge, quickly and confidently, on its own
    /// fiction, and from the inside that is indistinguishable from getting good.
    /// </remarks>
    [Fact]
    public async Task ACycleVerifiedAgainstACachedReading_IsCountedAndNotLearnedFrom()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new CachedVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(),
            skillCostOf: MeasuredSkillCost);

        await orchestrator.ExecuteCycleAsync(Fighting());

        Calibration? calibration = orchestrator.Learning.CalibrationOf(ActionType.UseSkill.ToString());
        Assert.NotNull(calibration);
        Assert.Equal(1, calibration!.Ignored);
        Assert.Equal(0, calibration.Confirmed);
        Assert.Equal(0, calibration.Refuted);
    }

    /// <summary>
    /// A cycle that never reaches the effector leaves no settled prediction and no
    /// prediction hanging open.
    /// </summary>
    /// <remarks>
    /// An abandoned prediction is neither right nor wrong, and counting it either
    /// way would be scoring the runtime on a round it did not take. It must also
    /// not accumulate: a ledger that leaks an open prediction per blocked cycle
    /// grows without bound in exactly the loop that runs forever.
    /// </remarks>
    [Fact]
    public async Task ABlockedCycleLeavesNothingOpenAndNothingLearned()
    {
        var refusing = new RuntimeSafetyPolicy(
            LiveInputEnabled: false, PacketInjectionEnabled: false,
            RequireClientHealthy: true, RequireGuardApproval: true);

        var orchestrator = new Gate3ExecutionOrchestrator(
            refusing,
            new CountingEffector(),
            new LiveVitalsObserver(hp: 800, mp: 40),
            goals: Hunting(),
            skillCostOf: MeasuredSkillCost);

        await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Null(orchestrator.Learning.CalibrationOf(ActionType.UseSkill.ToString()));
        Assert.Equal(0, orchestrator.Learning.OpenPredictions);
    }
}
