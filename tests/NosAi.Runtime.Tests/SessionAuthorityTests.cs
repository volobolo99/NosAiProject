using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Input authority bound to the session
/// (<c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 4, P3): the integrity comparison,
/// the harmless act that turns it into evidence, the latch that stops a permanent
/// condition being retried forever, and the capability the decision level is not
/// offered when the session cannot be driven.
/// </summary>
public sealed class SessionAuthorityTests
{
    private static readonly IntPtr Session = 0x4100;
    private static readonly IntPtr OtherWindow = 0x4200;
    private static readonly IntPtr Monitor = 0xBEEF;
    private const int ClientPid = 4321;

    private const uint Medium = 0x2000;
    private const uint High = 0x3000;

    private static readonly DateTimeOffset Start = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ the desktop

    private sealed class DesktopStandIn : ICommitEnvironment
    {
        public IntPtr Foreground { get; set; } = Session;
        public GeometryEpoch Live { get; set; } =
            new(Session, new PixelRect(100, 100, 1024, 768), 96, Monitor);

        public IntPtr ForegroundWindow() => Foreground;
        public IntPtr RootWindowFromPoint(int x, int y) => Session;
        public bool? IsCloaked(IntPtr window) => false;
        public GeometryEpoch ReadEpoch(IntPtr window) => Live;
    }

    private sealed class IntegrityStandIn : IProcessIntegrityReader
    {
        private readonly Dictionary<int, uint> _levels = new();
        public string? Failure { get; set; }

        public void Set(int processId, uint rid) => _levels[processId] = rid;

        public IntegrityLevel Read(int processId, out string? failureReason)
        {
            if (Failure is not null)
            {
                failureReason = Failure;
                return IntegrityLevel.Unknown;
            }

            if (!_levels.TryGetValue(processId, out uint rid))
            {
                failureReason = "not_configured";
                return IntegrityLevel.Unknown;
            }

            failureReason = null;
            return IntegrityLevel.FromRid(rid);
        }
    }

    /// <summary>A desktop that accepts the move and never applies it — UIPI, in effect.</summary>
    private sealed class UnresponsiveInputBackend : IInputBackend, IInputReleaseBackend
    {
        private readonly int _x;
        private readonly int _y;

        public UnresponsiveInputBackend(int x, int y)
        {
            _x = x;
            _y = y;
        }

        public int Moves { get; private set; }
        public bool IsLive => false;

        public bool TryGetCursorPosition(out int x, out int y)
        {
            x = _x;
            y = _y;
            return true;
        }

        public bool MoveRelative(int dx, int dy) { Moves++; return true; }
        public bool MoveAbsolute(int x, int y) { Moves++; return true; }
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default) => true;
        public bool ScrollWheel(int detents) => true;
        public bool ReleaseMouseButton(MouseButton button) => true;
        public bool ReleaseKey(ushort virtualKey) => true;
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // ------------------------------------------------------------------- assembling

    private sealed record Rig(
        SessionActuationAuthority Authority,
        DesktopStandIn Desktop,
        IntegrityStandIn Integrity,
        RecordingInputBackend Recorder,
        TestClock Clock)
    {
        public bool Armed { get; set; } = true;
    }

    private static Rig Build(
        uint runtimeIntegrity = Medium,
        uint clientIntegrity = Medium,
        int cursorX = 500,
        int cursorY = 400,
        bool armed = true)
    {
        var desktop = new DesktopStandIn();
        var integrity = new IntegrityStandIn();
        integrity.Set(Environment.ProcessId, runtimeIntegrity);
        integrity.Set(ClientPid, clientIntegrity);

        var recorder = new RecordingInputBackend(cursorX, cursorY);
        var clock = new TestClock(Start);

        Rig? rig = null;
        var gate = new GatedInputBackend(
            recorder,
            () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = rig!.Armed },
            new CommitPointValidator(desktop, AlwaysIdleHuman.Instance));

        var authority = new SessionActuationAuthority(
            integrity,
            desktop,
            gate,
            () => rig!.Armed,
            clock);

        rig = new Rig(authority, desktop, integrity, recorder, clock) { Armed = armed };
        authority.BeginSession(Session, ClientPid);
        return rig;
    }

    private sealed class AlwaysIdleHuman : IHumanInputMonitor
    {
        public static AlwaysIdleHuman Instance { get; } = new();
        public bool IsWatching => true;
        public TimeSpan? SinceLastHumanInput => TimeSpan.FromHours(1);
        public long HumanEventCount => 0;
        public long InjectedEventCount => 0;
    }

    // ------------------------------------------------------- the structural comparison

    [Fact]
    public void RuntimeBelowClientIsNonActuatingAndTerminal()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);

        SessionAuthorityVerdict verdict = rig.Authority.Verify();

        Assert.False(verdict.IsActuating);
        Assert.True(verdict.IsTerminal);
        Assert.Equal("authority_integrity_below_client:medium_under_high", verdict.RefusalReason);
        // Nothing was emitted: the comparison decided before the act.
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void TerminalVerdictIsNotProbedAgain()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();
        long probes = rig.Authority.ProbeCount;

        // The DoD: no retry. Ten attempts, and the answer is the first one.
        for (int i = 0; i < 10; i++)
            Assert.Equal("authority_integrity_below_client:medium_under_high", rig.Authority.EnsureVerified());

        Assert.Equal(probes, rig.Authority.ProbeCount);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void RuntimeAboveClientIsAllowedToProbe()
    {
        Rig rig = Build(runtimeIntegrity: High, clientIntegrity: Medium);

        Assert.True(rig.Authority.Verify().IsActuating);
    }

    [Fact]
    public void UnreadableIntegrityRefusesWithoutLatching()
    {
        Rig rig = Build();
        rig.Integrity.Failure = "open_process_failed:5";

        SessionAuthorityVerdict verdict = rig.Authority.Verify();

        Assert.False(verdict.IsActuating);
        Assert.False(verdict.IsTerminal);
        Assert.StartsWith("authority_runtime_integrity_unknown:", verdict.RefusalReason);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void UnreadableClientIntegrityIsNamedSeparately()
    {
        var desktop = new DesktopStandIn();
        var integrity = new IntegrityStandIn();
        integrity.Set(Environment.ProcessId, Medium);
        // The client is deliberately not configured: its level is the unknown one.
        var recorder = new RecordingInputBackend(500, 400);
        var gate = new GatedInputBackend(recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var authority = new SessionActuationAuthority(
            integrity, desktop, gate, () => true, new TestClock(Start));
        authority.BeginSession(Session, ClientPid);

        SessionAuthorityVerdict verdict = authority.Verify();

        Assert.StartsWith("authority_client_integrity_unknown:", verdict.RefusalReason);
        Assert.False(verdict.IsTerminal);
    }

    // ------------------------------------------------------------------ the probe

    [Fact]
    public void ArmedForegroundSessionIsActuatingAndPutsThePointerBack()
    {
        Rig rig = Build(cursorX: 500, cursorY: 400);

        SessionAuthorityVerdict verdict = rig.Authority.Verify();

        Assert.True(verdict.IsActuating);
        Assert.Null(verdict.RefusalReason);
        Assert.Equal(0, verdict.PointerErrorPixels);
        Assert.Equal(Session, verdict.Window);
        Assert.Equal(ClientPid, verdict.ClientProcessId);

        // Out and back, and nothing else: no button, no key.
        Assert.Equal(new[] { "move-absolute:504,400", "move-absolute:500,400" }, rig.Recorder.Events);
        Assert.True(rig.Recorder.TryGetCursorPosition(out int x, out int y));
        Assert.Equal(500, x);
        Assert.Equal(400, y);
    }

    [Fact]
    public void ProbeStaysInsideTheClientAreaWhenTheCursorIsOutside()
    {
        // Client area is 100,100 1024x768; this cursor is nowhere near it.
        Rig rig = Build(cursorX: 5, cursorY: 5);

        Assert.True(rig.Authority.Verify().IsActuating);

        Assert.Equal("move-absolute:612,484", rig.Recorder.Events[0]);
        Assert.Equal("move-absolute:5,5", rig.Recorder.Events[1]);
    }

    [Fact]
    public void PointerThatDoesNotMoveIsNonActuatingAndTerminal()
    {
        var desktop = new DesktopStandIn();
        var integrity = new IntegrityStandIn();
        integrity.Set(Environment.ProcessId, Medium);
        integrity.Set(ClientPid, Medium);

        var deaf = new UnresponsiveInputBackend(500, 400);
        var gate = new GatedInputBackend(deaf, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var authority = new SessionActuationAuthority(
            integrity, desktop, gate, () => true, new TestClock(Start));
        authority.BeginSession(Session, ClientPid);

        SessionAuthorityVerdict verdict = authority.Verify();

        Assert.False(verdict.IsActuating);
        Assert.True(verdict.IsTerminal);
        Assert.StartsWith("authority_pointer_did_not_move:", verdict.RefusalReason);

        // And it is not asked again — which is the whole point of the latch: the
        // pointer would otherwise twitch on every cycle, forever.
        int movesAfterProbe = deaf.Moves;
        for (int i = 0; i < 5; i++)
            authority.EnsureVerified();
        Assert.Equal(movesAfterProbe, deaf.Moves);
    }

    [Fact]
    public void UnarmedInputRefusesBeforeTheAct()
    {
        Rig rig = Build(armed: false);

        SessionAuthorityVerdict verdict = rig.Authority.Verify();

        Assert.Equal("authority_live_input_not_armed", verdict.RefusalReason);
        Assert.False(verdict.IsTerminal);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void ForegroundHeldBySomethingElseRefusesTheProbe()
    {
        Rig rig = Build();
        rig.Desktop.Foreground = OtherWindow;

        SessionAuthorityVerdict verdict = rig.Authority.Verify();

        Assert.Equal("authority_window_not_foreground", verdict.RefusalReason);
        Assert.False(verdict.IsTerminal);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void UnreadableGeometryRefusesTheProbe()
    {
        Rig rig = Build();
        rig.Desktop.Live = GeometryEpoch.Unknown;

        Assert.Equal("authority_geometry_unknown", rig.Authority.Verify().RefusalReason);
        Assert.Empty(rig.Recorder.Events);
    }

    // ------------------------------------------------------------- validity in time

    [Fact]
    public void VerdictExpiresAndSaysByHowMuch()
    {
        Rig rig = Build();
        Assert.True(rig.Authority.Verify().IsActuating);
        Assert.Null(rig.Authority.CurrentRefusal());

        rig.Clock.Advance(rig.Authority.Validity + TimeSpan.FromSeconds(1));

        string? refusal = rig.Authority.CurrentRefusal();
        Assert.NotNull(refusal);
        Assert.StartsWith("authority_verdict_expired:", refusal);
    }

    [Fact]
    public void ExpiredVerdictIsRetakenOnDemand()
    {
        Rig rig = Build();
        rig.Authority.Verify();
        rig.Clock.Advance(rig.Authority.Validity + TimeSpan.FromSeconds(1));

        Assert.Null(rig.Authority.EnsureVerified());
        Assert.Equal(2, rig.Authority.ProbeCount);
    }

    [Fact]
    public void ForegroundRestoreRequiresANewVerdict()
    {
        Rig rig = Build();
        Assert.True(rig.Authority.Verify().IsActuating);

        rig.Authority.NoteForegroundRestored();

        Assert.Equal("authority_reverification_pending", rig.Authority.CurrentRefusal());
        Assert.Null(rig.Authority.EnsureVerified());
    }

    [Fact]
    public void ForegroundRestoreDoesNotClearATerminalVerdict()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();

        rig.Authority.NoteForegroundRestored();

        Assert.Equal("authority_integrity_below_client:medium_under_high", rig.Authority.CurrentRefusal());
    }

    [Fact]
    public void ANewSessionClearsATerminalVerdict()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();
        Assert.True(rig.Authority.Current.IsTerminal);

        // The client was restarted, this time without elevation.
        rig.Integrity.Set(9999, Medium);
        rig.Desktop.Foreground = OtherWindow;
        rig.Authority.BeginSession(OtherWindow, 9999);
        rig.Desktop.Foreground = OtherWindow;
        rig.Desktop.Live = new GeometryEpoch(OtherWindow, new PixelRect(100, 100, 1024, 768), 96, Monitor);

        Assert.False(rig.Authority.Current.IsTerminal);
        Assert.True(rig.Authority.Verify().IsActuating);
    }

    [Fact]
    public void ResetClearsALatchOnTheOperatorsWord()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();

        rig.Authority.Reset("operator re-launched the client without elevation");

        Assert.False(rig.Authority.Current.IsTerminal);
        Assert.Equal("authority_reverification_pending", rig.Authority.CurrentRefusal());
    }

    [Fact]
    public void WithNoSessionNothingIsProbed()
    {
        var desktop = new DesktopStandIn();
        var integrity = new IntegrityStandIn();
        integrity.Set(Environment.ProcessId, Medium);
        var recorder = new RecordingInputBackend();
        var gate = new GatedInputBackend(recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var authority = new SessionActuationAuthority(integrity, desktop, gate, () => true, new TestClock(Start));

        Assert.Equal("authority_no_session", authority.CurrentRefusal());
        Assert.Equal("authority_no_session", authority.EnsureVerified());
        Assert.Equal(0, authority.ProbeCount);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void ReadingTheStateNeverEmitsInput()
    {
        Rig rig = Build();
        rig.Authority.Verify();
        int afterProbe = rig.Recorder.Events.Count;

        for (int i = 0; i < 100; i++)
        {
            _ = rig.Authority.CurrentRefusal();
            _ = rig.Authority.IsActuating;
            _ = rig.Authority.Current;
        }

        Assert.Equal(afterProbe, rig.Recorder.Events.Count);
    }

    // ------------------------------------------- the capability the planner is offered

    [Fact]
    public void NonActuatingSessionExposesNoActuationCapability()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();

        var policy = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };
        var effector = new InputActionEffector(
            new GatedInputBackend(new RecordingInputBackend(), () => policy),
            KeybindMap.Empty,
            () => policy,
            projection: null,
            sessionAuthority: rig.Authority.CurrentRefusal);

        Assert.False(effector.CanApply);
        Assert.Equal("authority_integrity_below_client:medium_under_high", effector.UnavailableReason);
    }

    [Fact]
    public void ActuatingSessionExposesTheCapability()
    {
        Rig rig = Build();
        Assert.True(rig.Authority.Verify().IsActuating);

        var policy = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };
        var effector = new InputActionEffector(
            new GatedInputBackend(new RecordingInputBackend(), () => policy),
            KeybindMap.Empty,
            () => policy,
            projection: null,
            sessionAuthority: rig.Authority.CurrentRefusal);

        Assert.True(effector.CanApply);
        Assert.Null(effector.UnavailableReason);
    }

    [Fact]
    public void ThePolicyIsNamedFirstWhenBothRefuse()
    {
        Rig rig = Build(runtimeIntegrity: Medium, clientIntegrity: High);
        rig.Authority.Verify();

        RuntimeSafetyPolicy policy = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = false };
        var effector = new InputActionEffector(
            new GatedInputBackend(new RecordingInputBackend(), () => policy),
            KeybindMap.Empty,
            () => policy,
            projection: null,
            sessionAuthority: rig.Authority.CurrentRefusal);

        Assert.Equal("live_input_disabled_by_policy", effector.UnavailableReason);
    }

    // --------------------------------------------------------------- integrity level

    [Fact]
    public void UnknownIntegrityIsNotTheUntrustedLevel()
    {
        Assert.False(IntegrityLevel.Unknown.IsKnown);
        Assert.True(IntegrityLevel.FromRid(0).IsKnown);
        Assert.Equal("untrusted", IntegrityLevel.FromRid(0).Name);
        Assert.Equal("unknown", IntegrityLevel.Unknown.Name);
        Assert.Equal("0x2800", IntegrityLevel.FromRid(0x2800).Name);
    }
}
