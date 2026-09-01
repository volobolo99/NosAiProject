using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Gate3;
using Xunit;

// Aliased rather than imported wholesale: TrustTier, VerificationResult and
// SafetyGate each exist in more than one namespace (Contracts, Gate3, Gate6,
// Safety, Host), so a broad using makes every reference ambiguous. The
// duplication is a real problem in its own right and is recorded in
// docs/GATE3_PIPELINE.md; collapsing it touches shared contracts and other
// gates, so it needs coordinating rather than doing halfway here.
using DataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;
using RuntimeSafetyPolicy = NosAi.Runtime.Safety.RuntimeSafetyPolicy;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gate 3 — the decision and safety closed loop.
/// </summary>
/// <remarks>
/// These tests concentrate on the property the pipeline exists to guarantee: it
/// must never report that something happened, or that a prediction held, unless
/// it did. Two defects made that untrue and both are pinned here — an executor
/// that slept and claimed completion, and a verifier handed a post-state derived
/// from the very prediction it was checking.
/// </remarks>
public sealed class Gate3Tests
{
    // A target of the shape each action type requires. The pairing is checked by
    // ActionCandidate itself now, so a test cannot quietly build an attack on
    // nothing the way "T", 0, 0 used to let it.
    private static readonly ActionTarget.Position Somewhere = new(new MapPoint(10, 10));
    private static readonly ActionTarget.Entity SomeMob = new(101, new MapPoint(10, 10));

    private static ActionTarget TargetFor(ActionType type) => type switch
    {
        ActionType.UseBasicAttack or ActionType.TargetEntity or ActionType.UseSkill => SomeMob,
        ActionType.MoveToPosition or ActionType.EmergencyFlee or ActionType.CollectGroundItem => Somewhere,
        ActionType.UseConsumable => new ActionTarget.InventorySlot(1),
        _ => ActionTarget.None.Instance,
    };

    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false, RequireClientHealthy: true, RequireGuardApproval: true);

    private sealed class CountingEffector : IActionEffector
    {
        public int Applications { get; private set; }
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(ActionCandidate candidate, CancellationToken cancellationToken = default)
        {
            Applications++;
            return Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null));
        }
    }

    private sealed class FixedObserver : IWorldStateObserver
    {
        private readonly ObservedState _state;
        public FixedObserver(ObservedState state) => _state = state;
        public bool CanObserve => true;
        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default) => Task.FromResult(_state);
    }

    // -- the safety posture --------------------------------------------------

    [Fact]
    public void TheDefaultPolicyBindsNoEffector()
    {
        // SafeDefault keeps live input off, so the pipeline must come up unable to
        // act rather than with something that stands in for acting.
        var orchestrator = new Gate3ExecutionOrchestrator();

        Assert.False(orchestrator.CanExecute);
        Assert.False(orchestrator.CanVerify);
    }

    [Fact]
    public void APolicyThatAllowsInputButSuppliesNoEffectorStillCannotAct()
    {
        // Fail closed on an incomplete configuration: a half-wired runtime must not
        // become one that quietly invents an effector.
        IActionEffector effector = ActionEffectorFactory.ForPolicy(ExecutionAllowed, liveEffector: null);

        Assert.False(effector.CanApply);
        Assert.Equal("no_live_effector_bound", effector.UnavailableReason);
    }

    [Fact]
    public async Task DisabledExecutionIsNeverReportedAsSuccess()
    {
        // The regression: 50 ms of sleep reported as a completed action while
        // nothing had touched the client.
        var orchestrator = new Gate3ExecutionOrchestrator();

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, hasTarget: true, isInCombat: false);

        Assert.Equal(CycleOutcome.ExecutionDisabled, result.Outcome);
        Assert.False(result.IsConfirmed);
        // And no recovery is triggered: nothing was attempted, so there is nothing
        // to degrade trust over.
        Assert.Equal(RuntimeMode.Normal, result.ModeAfter);
        Assert.Null(result.Strategy);
    }

    // -- verification is a real comparison -----------------------------------

    [Fact]
    public async Task ExecutedButUnobservedIsUnverifiedRatherThanConfirmed()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(ExecutionAllowed, new CountingEffector());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false));

        Assert.Equal(CycleOutcome.Unverified, result.Outcome);
        Assert.False(result.IsConfirmed);
    }

    [Fact]
    public void VerificationWithoutAnObservationIsClassifiedUnknown()
    {
        var verifier = new ActionExecutionVerifier();
        var candidate = new ActionCandidate(
            Guid.NewGuid(), ActionType.MoveToPosition, Somewhere, 0, TrustTier.Tier1_Assisted, "test");
        var predicted = new PredictedOutcome(candidate.CandidateId, 0, 0, 100, 1f, 0f, "POST_HP_10_MP_10");
        var executed = new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null);

        VerificationResult result = verifier.Verify(
            candidate, predicted, executed, ObservedState.Unobserved("no_perception"));

        Assert.Equal(VerificationOutcome.Unverified, result.Outcome);
        Assert.Equal(DataSourceKind.Unknown, result.Source);
        Assert.False(result.IsConfirmed);
        // Unverified is not a failure either: the action may well have worked, so it
        // must not drive trust downwards on its own.
        Assert.False(result.CountsAsFailure);
    }

    [Fact]
    public void AnObservedMismatchIsADiscrepancyAndCountsAsFailure()
    {
        var verifier = new ActionExecutionVerifier();
        var candidate = new ActionCandidate(
            Guid.NewGuid(), ActionType.UseSkill, SomeMob, 0, TrustTier.Tier2_SemiAutonomous, "test");
        var predicted = new PredictedOutcome(candidate.CandidateId, 0, 0, 100, 1f, 0f, "POST_HP_900_MP_65");
        var executed = new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null);

        VerificationResult result = verifier.Verify(candidate, predicted, executed, ObservedState.Live(120, 65));

        Assert.Equal(VerificationOutcome.Discrepant, result.Outcome);
        Assert.Equal(DataSourceKind.Live, result.Source);
        Assert.True(result.CountsAsFailure);
    }

    [Fact]
    public async Task AnObservedMismatchDrivesRecovery()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, new CountingEffector(), new FixedObserver(ObservedState.Live(1, 1)));

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false));

        Assert.Equal(CycleOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Strategy);
    }

    [Fact]
    public void AnUnobservedReadingIsNotZero()
    {
        // The distinction the whole classification model exists for. A verifier that
        // read UNKNOWN as 0 would confirm a prediction of death whenever perception
        // happened to be down.
        ObservedState unobserved = ObservedState.Unobserved("probe_unavailable");

        Assert.False(unobserved.IsFullyObserved);
        Assert.Equal(DataSourceKind.Unknown, unobserved.Hp.Source);
        Assert.False(unobserved.Hp.HasValue);
        Assert.Equal("probe_unavailable", unobserved.Hp.FailureReason);
    }

    [Fact]
    public async Task AFailingObserverLeavesTheCycleUnverifiedRatherThanThrowing()
    {
        var observer = new DelegateWorldStateObserver(_ => throw new InvalidOperationException("probe down"));
        var orchestrator = new Gate3ExecutionOrchestrator(ExecutionAllowed, new CountingEffector(), observer);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false));

        Assert.Equal(CycleOutcome.Unverified, result.Outcome);
    }

    // -- the input side ------------------------------------------------------

    [Fact]
    public async Task PlanningOverAnUnknownWorldStateIsRefused()
    {
        // The input-side twin of confirming an unobserved outcome: with nothing
        // known, any plan would be built on invented numbers.
        var orchestrator = new Gate3ExecutionOrchestrator();

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Unobserved("gameplay_provider_not_available"));

        Assert.Equal(CycleOutcome.NoWorldState, result.Outcome);
        Assert.Equal(ActionType.None, result.SelectedAction);
        Assert.Contains("gameplay_provider_not_available", result.Summary);
    }

    [Fact]
    public async Task SimulatedStateMayBePlannedOnWhenNothingCanAct()
    {
        // A dry run is legitimate: it is how the pipeline is exercised without a
        // client. It just must not end in an action.
        var orchestrator = new Gate3ExecutionOrchestrator();

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Simulated(800, 1000, 100, hasTarget: true, inCombat: false));

        Assert.Equal(CycleOutcome.ExecutionDisabled, result.Outcome);
        Assert.NotEqual(ActionType.None, result.SelectedAction);
    }

    [Fact]
    public async Task SimulatedStateIsRefusedWhenALiveEffectorIsBound()
    {
        // The rule this enforces: you may plan on simulated state, you may not act
        // on it. Without the check, a dry run wired to a real effector would drive
        // the client from numbers nobody observed.
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(ExecutionAllowed, effector);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Simulated(800, 1000, 100, true, false));

        Assert.Equal(CycleOutcome.RefusedSimulatedInput, result.Outcome);
        Assert.Equal(0, effector.Applications);
    }

    [Fact]
    public async Task ObservedStateReachesTheEffector()
    {
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(ExecutionAllowed, effector);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Live(800, 1000, 100, true, false));

        Assert.Equal(1, effector.Applications);
        // No observer bound, so it runs but cannot be confirmed.
        Assert.Equal(CycleOutcome.Unverified, result.Outcome);
    }

    [Fact]
    public void AWorldStateIsPlannableButNotObservedWhenSimulated()
    {
        Gate3WorldState simulated = Gate3WorldState.Simulated(1, 2, 3, true, false);

        Assert.True(simulated.IsPlannable);
        Assert.False(simulated.IsFullyObserved);
        Assert.Null(simulated.UnusableReason);
    }

    [Fact]
    public void AnUnknownWorldStateCarriesTheReasonItIsUnusable()
    {
        Gate3WorldState unknown = Gate3WorldState.Unobserved("client_not_attached");

        Assert.False(unknown.IsPlannable);
        Assert.False(unknown.IsFullyObserved);
        Assert.Equal("client_not_attached", unknown.UnusableReason);
    }

    [Fact]
    public async Task TheGate1AdapterReportsGameplayAsUnobserved()
    {
        // The adapter that joins Gate 3 to the real runtime. Gate 1 observes the
        // client's process, window and title, not its HP, so UNKNOWN with a reason
        // is the honest answer today — not a stub. When a gameplay provider exists
        // this starts returning LIVE and nothing else has to change.
        var runtime = NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = System.Security.Cryptography.RSA.Create(2048);
        using var auth = new NosAi.Runtime.Gate1.SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new NosAi.Runtime.Gate1.GuardAiNetworkChannel(0, auth);
        var provider = new NosAi.Runtime.Gate1.Gate1RuntimeSnapshotProvider(runtime, world, channel);

        var source = new Gate1SnapshotWorldStateSource(provider.Capture);
        Gate3WorldState state = await source.ReadAsync();

        Assert.False(state.IsPlannable);
        Assert.False(state.IsFullyObserved);
        Assert.NotNull(state.UnusableReason);
    }

    [Fact]
    public async Task AFailingSnapshotLeavesTheStateUnknownRatherThanThrowing()
    {
        var source = new Gate1SnapshotWorldStateSource(() => throw new InvalidOperationException("boom"));

        Gate3WorldState state = await source.ReadAsync();

        Assert.False(state.IsPlannable);
        Assert.Contains("snapshot_failed", state.UnusableReason!);
    }

    // -- authorisation -------------------------------------------------------

    [Fact]
    public async Task ABlockedCycleNeverReachesTheEffector()
    {
        // Denial has to stop the action before the world is touched, not report a
        // refusal after the fact.
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, new FixedObserver(ObservedState.Live(0, 0)), TrustTier.Tier0_ReadOnly);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false));

        Assert.Equal(CycleOutcome.Blocked, result.Outcome);
        Assert.Equal(0, effector.Applications);
    }

    [Fact]
    public async Task ATokenBoundToAnotherCandidateIsRefusedAndNotBurned()
    {
        var gate = new SafetyGate(new TrustBoundary(TrustTier.Tier4_FullAutonomous), new GuardPolicyEngine());
        var executor = new AuthorizedActionExecutor(gate, new CountingEffector());

        var mine = new ActionCandidate(Guid.NewGuid(), ActionType.MoveToPosition, Somewhere, 0, TrustTier.Tier1_Assisted, "a");
        var other = new ActionCandidate(Guid.NewGuid(), ActionType.MoveToPosition, Somewhere, 0, TrustTier.Tier1_Assisted, "b");
        var outcome = new PredictedOutcome(mine.CandidateId, 0, 0, 100, 1f, 0f, "SIG");

        Assert.True(gate.TryAuthorize(mine, outcome, RuntimeMode.Normal, out SafetyToken? token, out _));

        ExecutionResult refused = await executor.ExecuteAuthorizedAsync(other, token!);

        Assert.Equal(ExecutionState.Refused, refused.State);
        // The rightful holder's authorisation survives a misuse attempt.
        Assert.True(token!.TryConsume());
    }

    [Fact]
    public void AForgedTokenAuthorisesNothing()
    {
        var gate = new SafetyGate(new TrustBoundary(TrustTier.Tier4_FullAutonomous), new GuardPolicyEngine());
        var forged = new SafetyToken(Guid.NewGuid(), TrustTier.Tier4_FullAutonomous, new byte[32], TimeSpan.FromMinutes(1));

        Assert.False(gate.ValidateToken(forged));
    }

    [Fact]
    public void TrustOnlyEverMovesDown()
    {
        var trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);

        trust.DowngradeTrust(TrustTier.Tier0_ReadOnly);
        trust.DowngradeTrust(TrustTier.Tier4_FullAutonomous);

        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);
    }

    [Theory]
    [InlineData(RuntimeMode.Stopped, ActionType.UseConsumable, false)]
    [InlineData(RuntimeMode.Cooling, ActionType.UseBasicAttack, false)]
    [InlineData(RuntimeMode.Cooling, ActionType.UseConsumable, true)]
    [InlineData(RuntimeMode.Normal, ActionType.UseSkill, true)]
    public void GuardAppliesTheModePolicy(RuntimeMode mode, ActionType action, bool expectedAllowed)
    {
        // Recovery must stay possible while cooling, or thermal throttling would stop
        // the character from saving itself.
        var guard = new GuardPolicyEngine();
        var candidate = new ActionCandidate(Guid.NewGuid(), action, TargetFor(action), 0, TrustTier.Tier1_Assisted, "test");
        var outcome = new PredictedOutcome(candidate.CandidateId, 0, 0, 100, 1f, 0f, "SIG");

        Assert.Equal(expectedAllowed, guard.Evaluate(candidate, outcome, mode).IsAllowedByPolicy);
    }

    [Fact]
    public void FleeingIsExemptFromTheRiskCeiling()
    {
        // It is the action taken *because* the situation is dangerous, so the ceiling
        // that blocks risky actions must not block the escape.
        var guard = new GuardPolicyEngine();
        var flee = new ActionCandidate(Guid.NewGuid(), ActionType.EmergencyFlee, Somewhere, 0, TrustTier.Tier1_Assisted, "run");
        var risky = new ActionCandidate(Guid.NewGuid(), ActionType.UseSkill, SomeMob, 0, TrustTier.Tier1_Assisted, "hit");

        Assert.True(guard.Evaluate(flee, new PredictedOutcome(flee.CandidateId, 0, 0, 100, 1f, 0.9f, "S"), RuntimeMode.Normal).IsAllowedByPolicy);
        Assert.False(guard.Evaluate(risky, new PredictedOutcome(risky.CandidateId, 0, 0, 100, 1f, 0.9f, "S"), RuntimeMode.Normal).IsAllowedByPolicy);
    }
}
