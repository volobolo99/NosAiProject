using Xunit;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The way back from a halt: what evidence it takes, and what it deliberately does
/// not take.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 6.1 asks whether the return
/// from a halt is driven by a sliding window or by a counter, and says what is
/// wrong with the counter: ten successes interleaved with nine failures put the
/// system back at full speed, which is the scenario the halt exists to prevent. It
/// was a counter. These tests are the answer to that question, and the first of
/// them is the scenario the document names.
/// </para>
/// <para>
/// The controller is driven directly rather than through a gate runtime, because
/// what is under test is the escalation ladder and the trial, not a cycle.
/// </para>
/// </remarks>
public sealed class RecoveryCircuitBreakerTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(5);

    private static RecoveryController NewController(
        out TrustBoundary trust,
        out FakeTimeProvider clock,
        int probeSuccessesToClose = RecoveryController.DefaultProbeSuccessesToClose)
    {
        trust = new TrustBoundary(TrustTier.Tier4_FullAutonomous);
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        return new RecoveryController(
            trust,
            maxRetries: 2,
            clock: clock,
            probeSuccessesToClose: probeSuccessesToClose,
            baseCooldown: Base);
    }

    // --------------------------------------------------------- the named case

    /// <summary>
    /// The scenario § 6.1 names. Ten successes alternating with nine failures — a
    /// runtime failing very nearly half the time — must not end up at full speed.
    /// </summary>
    /// <remarks>
    /// Under the consecutive counter this sequence never reached even the first
    /// rung: no two failures were ever adjacent, so the count returned to zero
    /// nineteen times and the ladder never saw anything at all. The window sees the
    /// same sequence as nine failures in nineteen attempts, which is what it is.
    /// </remarks>
    [Fact]
    public void TenSuccessesAlternatingWithNineFailuresDoNotReturnTheRuntimeToFullSpeed()
    {
        RecoveryController recovery = NewController(out TrustBoundary trust, out _);
        var mode = RuntimeMode.Normal;

        // S F S F … S — ten successes, nine failures, strictly alternating.
        for (var i = 0; i < 19; i++)
        {
            if (i % 2 == 0)
                recovery.HandleSuccess(ref mode);
            else
                recovery.HandleFailure(ref mode);
        }

        Assert.NotEqual(RuntimeMode.Normal, mode);
        Assert.NotEqual(RecoveryState.Closed, recovery.State);
        Assert.Equal(RecoveryState.Halted, recovery.State);
        Assert.Equal(RuntimeMode.Stopped, mode);

        // And the trust given up on the way down is still given up.
        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);

        // The counter's own reading of the same nineteen outcomes, for contrast:
        // the last outcome was a success, so it saw a clean runtime.
        Assert.Equal(0, recovery.ConsecutiveFailures);
        Assert.Equal(9, recovery.FailuresInWindow);
    }

    /// <summary>
    /// The same sequence, gated the way a caller must gate it. Once the breaker
    /// halts, the attempts stop happening at all — which is the half of the fix that
    /// the escalation labels alone never provided.
    /// </summary>
    [Fact]
    public void OnceHaltedTheAlternatingRunStopsBeingAllowedToAct()
    {
        RecoveryController recovery = NewController(out _, out _);
        var mode = RuntimeMode.Normal;

        var attempted = 0;
        var refused = 0;

        for (var i = 0; i < 19; i++)
        {
            if (!recovery.TryBeginAction(ref mode, out string? refusal))
            {
                refused++;
                Assert.StartsWith("recovery_halted_cooling_down", refusal);
                continue;
            }

            attempted++;
            if (i % 2 == 0)
                recovery.HandleSuccess(ref mode);
            else
                recovery.HandleFailure(ref mode);
        }

        Assert.True(refused > 0, "a run failing half the time was never once refused");
        Assert.True(attempted < 19);
        Assert.Equal(RecoveryState.Halted, recovery.State);
    }

    // ------------------------------------------------------------- the window

    /// <summary>
    /// One success used to set the failure count to zero. It now moves the window by
    /// one, which is all it is evidence of.
    /// </summary>
    [Fact]
    public void ASingleSuccessDoesNotEraseTheFailuresThatLedToTheHalt()
    {
        RecoveryController recovery = NewController(out _, out _);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(RecoveryState.Halted, recovery.State);

        recovery.HandleSuccess(ref mode);

        Assert.Equal(RecoveryState.Halted, recovery.State);
        Assert.Equal(RuntimeMode.Stopped, mode);
        Assert.Equal(4, recovery.FailuresInWindow);
    }

    /// <summary>
    /// Failures leave the window by being worked off, not by being cancelled. A run
    /// of clean attempts long enough to push them all out is what returns a
    /// never-halted runtime to Normal.
    /// </summary>
    [Fact]
    public void FailuresAgeOutOfTheWindowAfterAFullWindowOfCleanWork()
    {
        RecoveryController recovery = NewController(out _, out _);
        var mode = RuntimeMode.Normal;

        recovery.HandleFailure(ref mode);
        recovery.HandleFailure(ref mode);
        Assert.Equal(2, recovery.FailuresInWindow);
        Assert.Equal(RecoveryState.Closed, recovery.State);
        Assert.Equal(RuntimeMode.Recovery, mode);

        for (var i = 0; i < recovery.WindowSize; i++)
            recovery.HandleSuccess(ref mode);

        Assert.Equal(0, recovery.FailuresInWindow);
        Assert.Equal(RuntimeMode.Normal, mode);
    }

    /// <summary>
    /// The rungs are unchanged from the consecutive version — four failures still
    /// walk retry, retry, degrade, halt. Only what is counted changed.
    /// </summary>
    [Fact]
    public void TheLadderStillWalksRetryRetryDegradeHalt()
    {
        RecoveryController recovery = NewController(out TrustBoundary trust, out _);
        var mode = RuntimeMode.Normal;

        Assert.Equal(RecoveryStrategy.Retry, recovery.HandleFailure(ref mode));
        Assert.Equal(RecoveryStrategy.Retry, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Recovery, mode);

        Assert.Equal(RecoveryStrategy.DegradedReplan, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Degraded, mode);
        Assert.Equal(TrustTier.Tier1_Assisted, trust.CurrentTier);

        Assert.Equal(RecoveryStrategy.HaltAndAlert, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Stopped, mode);
        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);
    }

    // -------------------------------------------------------------- the trial

    [Fact]
    public void NothingIsAttemptedWhileTheCooldownIsStillRunning()
    {
        RecoveryController recovery = NewController(out _, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.False(recovery.TryBeginAction(ref mode, out string? refusal));
        Assert.StartsWith("recovery_halted_cooling_down", refusal);

        clock.Advance(Base - TimeSpan.FromMilliseconds(1));
        Assert.False(recovery.TryBeginAction(ref mode, out _));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(recovery.TryBeginAction(ref mode, out _));
        Assert.Equal(RecoveryState.Probing, recovery.State);
    }

    /// <summary>
    /// One action at a time. The trial is worth nothing if the runtime can put ten
    /// actions through it at once and report the one that worked.
    /// </summary>
    [Fact]
    public void TheTrialAdmitsOneActionAtATime()
    {
        RecoveryController recovery = NewController(out _, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        clock.Advance(Base);

        Assert.True(recovery.TryBeginAction(ref mode, out _));
        Assert.False(recovery.TryBeginAction(ref mode, out string? refusal));
        Assert.Equal("recovery_probe_in_flight", refusal);

        // Resolving the one in flight is what admits the next.
        recovery.HandleSuccess(ref mode);
        Assert.True(recovery.TryBeginAction(ref mode, out _));
    }

    [Fact]
    public void FullSpeedReturnsOnlyAfterConsecutiveProbeSuccesses()
    {
        RecoveryController recovery = NewController(out _, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        clock.Advance(Base);

        for (var i = 0; i < recovery.ProbeSuccessesToClose - 1; i++)
        {
            Assert.True(recovery.TryBeginAction(ref mode, out _));
            recovery.HandleSuccess(ref mode);
            Assert.Equal(RecoveryState.Probing, recovery.State);
            Assert.NotEqual(RuntimeMode.Normal, mode);
        }

        Assert.True(recovery.TryBeginAction(ref mode, out _));
        recovery.HandleSuccess(ref mode);

        Assert.Equal(RecoveryState.Closed, recovery.State);
        Assert.Equal(RuntimeMode.Normal, mode);
        Assert.Equal(0, recovery.FailuresInWindow);
    }

    /// <summary>
    /// A probe that fails is the strongest evidence the fault is still there, so it
    /// costs the whole trial and not one step of it.
    /// </summary>
    [Fact]
    public void AFailedProbeHaltsAgainAndTheEarlierProbeSuccessesAreSpent()
    {
        RecoveryController recovery = NewController(out _, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        clock.Advance(Base);
        Assert.True(recovery.TryBeginAction(ref mode, out _));
        recovery.HandleSuccess(ref mode);
        Assert.Equal(RecoveryState.Probing, recovery.State);

        Assert.True(recovery.TryBeginAction(ref mode, out _));
        Assert.Equal(RecoveryStrategy.HaltAndAlert, recovery.HandleFailure(ref mode));

        Assert.Equal(RecoveryState.Halted, recovery.State);

        // The next trial starts from zero, not from the one success already banked.
        clock.Advance(recovery.CurrentCooldown);
        Assert.True(recovery.TryBeginAction(ref mode, out _));
        recovery.HandleSuccess(ref mode);
        Assert.Equal(RecoveryState.Probing, recovery.State);
    }

    // ----------------------------------------------------------- the cooldown

    [Fact]
    public void TheCooldownDoublesWithEachHalt()
    {
        RecoveryController recovery = NewController(out _, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(1, recovery.Halts);
        Assert.Equal(Base, recovery.CurrentCooldown);

        for (var halt = 2; halt <= 4; halt++)
        {
            clock.Advance(recovery.CurrentCooldown);
            Assert.True(recovery.TryBeginAction(ref mode, out _));
            recovery.HandleFailure(ref mode);

            Assert.Equal(halt, recovery.Halts);
            Assert.Equal(Base * Math.Pow(2, halt - 1), recovery.CurrentCooldown);
        }
    }

    [Fact]
    public void TheCooldownStopsGrowingAtTheCeiling()
    {
        var trust = new TrustBoundary(TrustTier.Tier4_FullAutonomous);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var recovery = new RecoveryController(
            trust,
            maxRetries: 2,
            clock: clock,
            baseCooldown: Base,
            maxCooldown: TimeSpan.FromSeconds(20));

        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        for (var i = 0; i < 6; i++)
        {
            clock.Advance(recovery.CurrentCooldown);
            Assert.True(recovery.TryBeginAction(ref mode, out _));
            recovery.HandleFailure(ref mode);
        }

        Assert.Equal(TimeSpan.FromSeconds(20), recovery.CurrentCooldown);
    }

    /// <summary>
    /// Failures arriving while already halted are recorded but do not push the wait
    /// out again: a burst of them would otherwise compound into a cooldown nobody
    /// chose.
    /// </summary>
    [Fact]
    public void FailuresWhileAlreadyHaltedDoNotLengthenTheWait()
    {
        RecoveryController recovery = NewController(out _, out _);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(1, recovery.Halts);

        for (var i = 0; i < 5; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(1, recovery.Halts);
        Assert.Equal(Base, recovery.CurrentCooldown);
    }

    // -------------------------------------------------------------- the floor

    /// <summary>
    /// The invariant the rewrite must not have loosened: this class can lower trust
    /// and can never raise it. Closing the breaker restores the runtime mode and
    /// nothing else, so a halt that dropped trust to read-only still needs whoever
    /// is watching.
    /// </summary>
    [Fact]
    public void ClosingTheBreakerRestoresTheModeAndNeverTheTrust()
    {
        RecoveryController recovery = NewController(out TrustBoundary trust, out FakeTimeProvider clock);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);

        clock.Advance(Base);
        for (var i = 0; i < recovery.ProbeSuccessesToClose; i++)
        {
            Assert.True(recovery.TryBeginAction(ref mode, out _));
            recovery.HandleSuccess(ref mode);
        }

        Assert.Equal(RecoveryState.Closed, recovery.State);
        Assert.Equal(RuntimeMode.Normal, mode);
        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);
    }

    /// <summary>The operator's reset clears the history and the cooldown, never the trust.</summary>
    [Fact]
    public void TheOperatorResetClearsTheWindowAndTheCooldownButNotTheTrust()
    {
        RecoveryController recovery = NewController(out TrustBoundary trust, out _);
        var mode = RuntimeMode.Normal;

        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        recovery.Reset();

        Assert.Equal(RecoveryState.Closed, recovery.State);
        Assert.Equal(0, recovery.FailuresInWindow);
        Assert.Equal(0, recovery.ConsecutiveFailures);
        Assert.Equal(0, recovery.Halts);
        Assert.Equal(TrustTier.Tier0_ReadOnly, trust.CurrentTier);
    }

    /// <summary>
    /// A test clock. The cooldown is the point of the design, and a test that waited
    /// for it in real time would either take minutes or assert nothing.
    /// </summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
