using NosAi.Runtime.Autonomy;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The atomic revalidation immediately before the irreversible step
/// (<c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 2.1), its five conditions, and
/// the abort that always lets go.
/// </summary>
public sealed class CommitPointTests
{
    private static readonly IntPtr Session = 0x1000;
    private static readonly IntPtr Other = 0x2000;
    private static readonly IntPtr Monitor = 0xABCD;

    /// <summary>
    /// The authority every act in these tests is emitted under. ADR-0020 § 2: there is
    /// no way to open a scope without naming one, so a test that wanted to omit it
    /// would not compile — which is the property the record asks for.
    /// </summary>
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded("test");

    private static GeometryEpoch Epoch(uint dpi = 96, int x = 0, int y = 0, int w = 1024, int h = 768) =>
        new(Session, new PixelRect(x, y, w, h), dpi, Monitor);

    private static CommitRequest Request(uint scaleDpi = 96, uint epochDpi = 96) =>
        new(
            new GeometryStamp(Epoch(epochDpi), DateTimeOffset.UnixEpoch),
            ScreenX: 500,
            ScreenY: 400,
            Scale: new GeometryShape(1024, 768, scaleDpi));

    private sealed class FakeDesktop : ICommitEnvironment
    {
        public IntPtr Foreground { get; set; } = Session;
        public IntPtr RootAtPoint { get; set; } = Session;
        public bool? Cloaked { get; set; }
        public GeometryEpoch Live { get; set; } = Epoch();

        public IntPtr ForegroundWindow() => Foreground;
        public IntPtr RootWindowFromPoint(int x, int y) => RootAtPoint;
        public bool? IsCloaked(IntPtr window) => Cloaked;
        public GeometryEpoch ReadEpoch(IntPtr window) => Live;

        public FakeDesktop() => Cloaked = false;
    }

    private sealed class FakeHuman : IHumanInputMonitor
    {
        public bool IsWatching { get; set; } = true;
        public TimeSpan? SinceLastHumanInput { get; set; } = TimeSpan.FromMinutes(1);
        public long HumanEventCount => 0;
        public long InjectedEventCount => 0;
    }

    private static CommitPointValidator Validator(FakeDesktop desktop, FakeHuman human) =>
        new(desktop, human);

    // ------------------------------------------------------ the five conditions

    [Fact]
    public void AllFiveConditionsMetAuthorises()
    {
        CommitDecision decision = Validator(new FakeDesktop(), new FakeHuman()).Validate(Request());

        Assert.True(decision.IsAuthorised, decision.RefusalReason);
        Assert.Null(decision.RefusalReason);
    }

    /// <summary>
    /// One: the geometry the coordinate was computed against. The point was correct
    /// when it was computed and is not correct now.
    /// </summary>
    [Fact]
    public void AGeometryThatMovedSinceAuthorisationRefuses()
    {
        var desktop = new FakeDesktop { Live = Epoch(x: 300, y: 300) };

        CommitDecision decision = Validator(desktop, new FakeHuman()).Validate(Request());

        Assert.False(decision.IsAuthorised);
        Assert.StartsWith(CommitPointValidator.GeometryChangedPrefix, decision.RefusalReason, StringComparison.Ordinal);
        Assert.Contains(GeometryEpoch.MovedReason, decision.RefusalReason, StringComparison.Ordinal);
    }

    /// <summary>Two: SendInput goes to whatever holds the foreground.</summary>
    [Fact]
    public void AnotherApplicationInFrontRefuses()
    {
        var desktop = new FakeDesktop { Foreground = Other };

        CommitDecision decision = Validator(desktop, new FakeHuman()).Validate(Request());

        Assert.False(decision.IsAuthorised);
        Assert.Equal(CommitPointValidator.NotForegroundReason, decision.RefusalReason);
    }

    /// <summary>
    /// Three: the exact pixel. A small window over the click point passes an area
    /// check and intercepts the act anyway, which is why this is point-wise.
    /// </summary>
    [Fact]
    public void SomethingElseOwningTheExactPixelRefuses()
    {
        var desktop = new FakeDesktop { RootAtPoint = Other };

        CommitDecision decision = Validator(desktop, new FakeHuman()).Validate(Request());

        Assert.False(decision.IsAuthorised);
        Assert.Equal(CommitPointValidator.PointNotOursReason, decision.RefusalReason);
    }

    [Fact]
    public void ACloakedWindowRefusesAndAnUnreadableCloakRefusesToo()
    {
        var cloaked = new FakeDesktop { Cloaked = true };
        Assert.Equal(
            CommitPointValidator.WindowCloakedReason,
            Validator(cloaked, new FakeHuman()).Validate(Request()).RefusalReason);

        var unknown = new FakeDesktop { Cloaked = null };
        Assert.Equal(
            CommitPointValidator.CloakUnknownReason,
            Validator(unknown, new FakeHuman()).Validate(Request()).RefusalReason);
    }

    /// <summary>Four: the operator's hand wins (DOMAIN-16).</summary>
    [Fact]
    public void AHandOnTheMouseInsideTheCourtesyWindowRefuses()
    {
        var human = new FakeHuman { SinceLastHumanInput = TimeSpan.FromMilliseconds(200) };

        CommitDecision decision = Validator(new FakeDesktop(), human).Validate(Request());

        Assert.False(decision.IsAuthorised);
        Assert.StartsWith(CommitPointValidator.HumanActiveReason, decision.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("200ms_of_1500ms", decision.RefusalReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A monitor that is not watching has not seen an absence of people. Reading its
    /// silence as an idle desk would hand the runtime the mouse on the evidence it
    /// does not have.
    /// </summary>
    [Fact]
    public void AMonitorThatIsNotWatchingRefusesRatherThanReadingAsIdle()
    {
        var human = new FakeHuman { IsWatching = false, SinceLastHumanInput = null };

        CommitDecision decision = Validator(new FakeDesktop(), human).Validate(Request());

        Assert.False(decision.IsAuthorised);
        Assert.Equal(CommitPointValidator.HumanUnknownReason, decision.RefusalReason);
    }

    /// <summary>
    /// Five, and the answer to § 7. The projection may skip an unreadable DPI because
    /// it produces a pixel; the act may not, because it is the act.
    /// </summary>
    [Fact]
    public void AnUnknownScaleRefusesTheAct()
    {
        CommitDecision decision = Validator(new FakeDesktop(), new FakeHuman())
            .Validate(Request(scaleDpi: 0));

        Assert.False(decision.IsAuthorised);
        Assert.Equal(CommitPointValidator.ScaleUnknownReason, decision.RefusalReason);
    }

    [Fact]
    public void AScaleThatIsNoLongerTheLiveOneRefuses()
    {
        // The stamp and the live geometry agree at 120; the calibration behind the
        // coordinate was fitted at 96.
        var desktop = new FakeDesktop { Live = Epoch(dpi: 120) };

        CommitDecision decision = Validator(desktop, new FakeHuman())
            .Validate(Request(scaleDpi: 96, epochDpi: 120));

        Assert.False(decision.IsAuthorised);
        Assert.StartsWith(CommitPointValidator.ScaleChangedReason, decision.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("96_to_120", decision.RefusalReason, StringComparison.Ordinal);
    }

    /// <summary>The most structural fact is the one reported when several are wrong.</summary>
    [Fact]
    public void TheGeometryIsJudgedBeforeEverythingElse()
    {
        var desktop = new FakeDesktop { Live = Epoch(x: 300), Foreground = Other, RootAtPoint = Other };
        var human = new FakeHuman { SinceLastHumanInput = TimeSpan.Zero };

        CommitDecision decision = Validator(desktop, human).Validate(Request(scaleDpi: 0));

        Assert.StartsWith(CommitPointValidator.GeometryChangedPrefix, decision.RefusalReason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ the latency

    /// <summary>
    /// There is no zero-risk window and this does not pretend otherwise: the interval
    /// between the last check and the emission is measured and reported either way.
    /// </summary>
    [Fact]
    public void TheEmissionLatencyIsMeasuredAndReported()
    {
        var validator = Validator(new FakeDesktop(), new FakeHuman());
        CommitDecision decision = validator.Validate(Request());

        Assert.True(validator.MayEmit(decision, out _, out TimeSpan latency));
        Assert.True(latency >= TimeSpan.Zero);
        Assert.True(decision.ValidationDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void AnActThatArrivesLateIsAbandonedRatherThanEmitted()
    {
        var validator = new CommitPointValidator(
            new FakeDesktop(), new FakeHuman(), maxEmissionLatency: TimeSpan.Zero);

        CommitDecision decision = validator.Validate(Request());
        Assert.True(decision.IsAuthorised, decision.RefusalReason);

        Thread.Sleep(2);

        Assert.False(validator.MayEmit(decision, out string? reason, out TimeSpan latency));
        Assert.StartsWith(CommitPointValidator.LatencyExceededReason, reason, StringComparison.Ordinal);
        Assert.True(latency > TimeSpan.Zero);
    }

    [Fact]
    public void ARefusedDecisionNeverBecomesEmittable()
    {
        var validator = Validator(new FakeDesktop { Foreground = Other }, new FakeHuman());
        CommitDecision decision = validator.Validate(Request());

        Assert.False(validator.MayEmit(decision, out string? reason, out _));
        Assert.Equal(CommitPointValidator.NotForegroundReason, reason);
    }

    // ---------------------------------------------------------- the graft

    private static (GatedInputBackend Gate, RecordingInputBackend Inner, FakeDesktop Desktop, FakeHuman Human)
        LiveGate(TimeSpan? maxLatency = null)
    {
        var inner = new RecordingInputBackend();
        var desktop = new FakeDesktop();
        var human = new FakeHuman();
        var validator = new CommitPointValidator(desktop, human, maxEmissionLatency: maxLatency);
        var policy = new RuntimeSafetyPolicy(
            LiveInputEnabled: true, PacketInjectionEnabled: false,
            RequireClientHealthy: false, RequireGuardApproval: false);

        return (new GatedInputBackend(inner, () => policy, validator), inner, desktop, human);
    }

    /// <summary>
    /// The property that makes the commit point unavoidable: with a validator wired,
    /// an act with no scope open does not reach the desktop.
    /// </summary>
    [Fact]
    public void WithACommitPointWiredAnActWithoutAScopeIsRefused()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.RequiresCommitPoint);
        Assert.False(gate.Click(MouseButton.Left));
        Assert.False(gate.KeyPress(0x41));
        Assert.False(gate.ScrollWheel(1));
        Assert.False(gate.MoveAbsolute(10, 10));

        Assert.Empty(inner.Events);
        Assert.Equal(GatedInputBackend.CommitScopeRequiredReason, gate.LastRefusal!.Reason);
    }

    [Fact]
    public void InsideAScopeWithEveryConditionMetTheActReachesTheBackend()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out string? why), why);
        using (scope)
        {
            Assert.True(gate.MoveAbsolute(500, 400));
            Assert.True(gate.Click(MouseButton.Left));
        }

        Assert.Contains("click:Left", inner.Events);
        Assert.Null(gate.CurrentScope);
    }

    /// <summary>One act at a time. DOMAIN-17 allows one irreversible step, not two interleaved.</summary>
    [Fact]
    public void OnlyOneScopeIsOpenAtATime()
    {
        (GatedInputBackend gate, _, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? first, out _));
        using (first)
        {
            Assert.False(gate.TryBeginActuation(Request(), Operator, out ActuationScope? second, out string? reason));
            Assert.Null(second);
            Assert.Equal(GatedInputBackend.ScopeAlreadyOpenReason, reason);
        }

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? third, out _));
        third!.Dispose();
    }

    /// <summary>
    /// A hand arriving between the authorisation and the click abandons the act. The
    /// scope stays aborted afterwards, so a caller cannot retry past the refusal by
    /// holding on to the object that was refused.
    /// </summary>
    [Fact]
    public void AHandArrivingMidActAbortsItAndTheScopeStaysAborted()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, FakeHuman human) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        using (scope)
        {
            Assert.True(gate.MoveAbsolute(500, 400));

            human.SinceLastHumanInput = TimeSpan.FromMilliseconds(10);

            Assert.False(gate.Click(MouseButton.Left));
            Assert.True(scope!.IsAborted);

            human.SinceLastHumanInput = TimeSpan.FromMinutes(1);
            Assert.False(gate.Click(MouseButton.Left));
        }

        Assert.DoesNotContain("click:Left", inner.Events);
    }

    /// <summary>A window brought to the front mid-act aborts it, rather than clicking into it.</summary>
    [Fact]
    public void AWindowComingToTheFrontMidActAbortsIt()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, FakeDesktop desktop, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        using (scope)
        {
            desktop.Foreground = Other;
            Assert.False(gate.Click(MouseButton.Left));
            Assert.Equal(CommitPointValidator.NotForegroundReason, scope!.AbortReason);
        }

        Assert.DoesNotContain("click:Left", inner.Events);
    }

    // ------------------------------------------------------- the abort machine

    /// <summary>
    /// The abort releases what was pressed, even when the policy has since been turned
    /// off: a refused release leaves a key down, and a key down outlives the process
    /// that pressed it.
    /// </summary>
    [Fact]
    public void AbortReleasesWhatWasPressedEvenWithThePolicyOff()
    {
        var inner = new RecordingInputBackend();
        var desktop = new FakeDesktop();
        var human = new FakeHuman();
        var live = new RuntimeSafetyPolicy(true, false, false, false);
        RuntimeSafetyPolicy policy = live;
        var gate = new GatedInputBackend(
            inner, () => policy, new CommitPointValidator(desktop, human));

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));

        // Pretend a press is in flight, as it is between the down and the up.
        scope!.RecordButton(MouseButton.Left);
        scope.RecordKey(0x41);
        Assert.Equal(2, scope.HeldCount);

        policy = RuntimeSafetyPolicy.SafeDefault;
        scope.Abort("test");

        Assert.Equal(0, scope.HeldCount);
        Assert.Contains("release-key:65", inner.Events);
        Assert.Contains("release-button:Left", inner.Events);
    }

    /// <summary>
    /// The operator's suspend has to reach the act already in flight, not only the next
    /// one (§ 2.3). Disarming the policy refuses the following call and says nothing
    /// about the key this program already pressed.
    /// </summary>
    [Fact]
    public void AbortingTheOpenScopeReleasesWhatTheActWasHolding()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        scope!.RecordKey(0x41);
        scope.RecordButton(MouseButton.Left);

        Assert.True(gate.AbortOpenScope("operator_emergency_stop"));

        Assert.Equal(0, scope.HeldCount);
        Assert.True(scope.IsAborted);
        Assert.Equal("operator_emergency_stop", scope.AbortReason);
        Assert.Contains("release-key:65", inner.Events);
        Assert.Contains("release-button:Left", inner.Events);
    }

    /// <summary>An emergency stop has to be callable twice, so nothing to abort is not a failure.</summary>
    [Fact]
    public void AbortingWithNoActInFlightIsNotAnError()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.False(gate.AbortOpenScope("operator_emergency_stop"));

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        scope!.RecordKey(0x41);
        Assert.True(gate.AbortOpenScope("operator_emergency_stop"));
        Assert.False(gate.AbortOpenScope("operator_emergency_stop"));

        Assert.Single(inner.Events, e => e == "release-key:65");
    }

    // ------------------------------------------------- the authority behind the act

    /// <summary>
    /// ADR-0020 § 1: two entries to the boundary are legitimate, and the third state —
    /// an emission the gate cannot attribute to anybody — is not.
    /// </summary>
    [Fact]
    public void AScopeCannotBeOpenedWithoutAnAuthority()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.False(gate.TryBeginActuation(Request(), default, out ActuationScope? scope, out string? why));

        Assert.Null(scope);
        Assert.Equal(ActuationAuthority.MissingReason, why);
        Assert.Empty(inner.Events);
        Assert.Equal(ActuationAuthority.MissingReason, gate.LastRefusal?.Reason);
    }

    [Fact]
    public void AnExpiredTokenIsNotALiveAuthorisation()
    {
        (GatedInputBackend gate, _, _, _) = LiveGate();
        var expired = new SafetyToken(
            Guid.NewGuid(), TrustTier.Tier1_Assisted, new byte[32], TimeSpan.FromMilliseconds(-1));

        Assert.False(gate.TryBeginActuation(
            Request(), ActuationAuthority.Planned(expired), out _, out string? why));

        Assert.StartsWith(ActuationAuthority.ExpiredPrefix + ":", why);
    }

    [Fact]
    public void TheScopeNamesWhoAuthorisedIt()
    {
        (GatedInputBackend gate, _, _, _) = LiveGate();
        var token = new SafetyToken(
            Guid.NewGuid(), TrustTier.Tier1_Assisted, new byte[32], TimeSpan.FromSeconds(2));

        Assert.True(gate.TryBeginActuation(
            Request(), ActuationAuthority.Planned(token), out ActuationScope? planned, out _));
        Assert.Equal(ActuationAuthorityKind.Planned, planned!.Authority.Kind);
        Assert.Contains(token.TokenId.ToString("N"), planned.Authority.Describe());
        planned.Dispose();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? commanded, out _));
        Assert.Equal(ActuationAuthorityKind.Commanded, commanded!.Authority.Kind);
        Assert.Equal("operator:test", commanded.Authority.Describe());
        commanded.Dispose();
    }

    /// <summary>An anonymous command is not an authority; it is the missing one, spelled.</summary>
    [Fact]
    public void ACommandWithoutANameIsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => ActuationAuthority.Commanded("  "));
        Assert.Throws<ArgumentNullException>(() => ActuationAuthority.Commanded(null!));
    }

    /// <summary>Modifiers come up after the key they were held around, as a completed press would.</summary>
    [Fact]
    public void ReleasesHappenInReverseOrderOfPressing()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        scope!.RecordKey(0x11);   // control down
        scope.RecordKey(0x41);    // 'A' down
        scope.Abort("test");

        int keyIndex = inner.Events.ToList().IndexOf("release-key:65");
        int modifierIndex = inner.Events.ToList().IndexOf("release-key:17");

        Assert.True(keyIndex >= 0 && modifierIndex >= 0);
        Assert.True(keyIndex < modifierIndex, "the modifier was released before the key it was held around");
    }

    /// <summary>Disposing a scope releases anything a failed call left recorded.</summary>
    [Fact]
    public void DisposingAScopeReleasesWhateverIsStillHeld()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        scope!.RecordButton(MouseButton.Right);
        scope.Dispose();

        Assert.Contains("release-button:Right", inner.Events);
        Assert.Equal(0, scope.HeldCount);
    }

    /// <summary>A completed press leaves nothing behind to release.</summary>
    [Fact]
    public void ACompletedPressIsNotReleasedTwice()
    {
        (GatedInputBackend gate, RecordingInputBackend inner, _, _) = LiveGate();

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        using (scope)
        {
            Assert.True(gate.Click(MouseButton.Left));
            Assert.Equal(0, scope!.HeldCount);
        }

        Assert.DoesNotContain("release-button:Left", inner.Events);
    }

    // -------------------------------------------------- the policy-only gate

    /// <summary>
    /// A gate built without a validator behaves exactly as before, and says which of
    /// the two it is rather than leaving the difference to be inferred.
    /// </summary>
    [Fact]
    public void AGateWithoutACommitPointIsPolicyOnlyAndSaysSo()
    {
        var inner = new RecordingInputBackend();
        var policy = new RuntimeSafetyPolicy(true, false, false, false);
        var gate = new GatedInputBackend(inner, policy);

        Assert.False(gate.RequiresCommitPoint);
        Assert.True(gate.Click(MouseButton.Left));
        Assert.Contains("click:Left", inner.Events);
    }

    [Fact]
    public void ThePolicyStillRefusesFirstEvenInsideAScope()
    {
        var inner = new RecordingInputBackend();
        var desktop = new FakeDesktop();
        var gate = new GatedInputBackend(
            inner,
            () => RuntimeSafetyPolicy.SafeDefault,
            new CommitPointValidator(desktop, new FakeHuman()));

        Assert.True(gate.TryBeginActuation(Request(), Operator, out ActuationScope? scope, out _));
        using (scope)
        {
            Assert.False(gate.Click(MouseButton.Left));
        }

        Assert.Empty(inner.Events);
        Assert.Equal("live_input_disabled_by_policy", gate.LastRefusal!.Reason);
    }
}
