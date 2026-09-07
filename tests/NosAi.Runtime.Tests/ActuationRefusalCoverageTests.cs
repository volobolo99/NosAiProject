using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The refusals that keep the actuation boundary closed, named. Each test here
/// builds the condition that provokes one refusal and checks two things: that it
/// happens, and that it carries the name the operator sees.
/// </summary>
/// <remarks>
/// <para>
/// The name is the contract. A refusal that changed its name would stop the
/// operator's tooling (and the audit) from recognising it while the behaviour
/// kept working, which is the failure mode these tests exist to catch.
/// </para>
/// <para>
/// Where a refusal is genuinely unreachable from the public boundary — the
/// <c>Win32ProcessIntegrityReader</c> token failures, the composition-time
/// <c>*_input_backend_not_gated</c> checks — it is declared in
/// <c>RefusalReasonRegisterTests</c> with its reason, not exercised here.
/// </para>
/// </remarks>
public sealed class ActuationRefusalCoverageTests
{
    private static readonly IntPtr Session = 0x4100;
    private static readonly IntPtr Monitor = 0xBEEF;
    private const int ClientPid = 4321;
    private const uint Medium = 0x2000;

    private static readonly DateTime Now = new(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------ the desktop seam

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
        public IntegrityLevel Read(int processId, out string? failureReason)
        {
            failureReason = null;
            return IntegrityLevel.FromRid(Medium);
        }
    }

    // ------------------------------------------------------------------- backends

    /// <summary>A backend that cannot report where the cursor is.</summary>
    private sealed class CursorFailingBackend : IInputBackend
    {
        public bool IsLive => true;
        public bool TryGetCursorPosition(out int x, out int y) { x = 0; y = 0; return false; }
        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => true;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default) => true;
        public bool ScrollWheel(int detents) => true;
    }

    /// <summary>A backend whose pointer moves are all refused.</summary>
    private sealed class MoveFailingBackend : IInputBackend
    {
        public bool IsLive => true;
        public bool TryGetCursorPosition(out int x, out int y) { x = 500; y = 400; return true; }
        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => false;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default) => true;
        public bool ScrollWheel(int detents) => true;
    }

    /// <summary>
    /// A backend with no release capability at all: it is <see cref="IInputBackend"/>
    /// and deliberately not <see cref="IInputReleaseBackend"/>.
    /// </summary>
    private sealed class NoReleaseBackend : IInputBackend
    {
        public bool IsLive => true;
        public bool TryGetCursorPosition(out int x, out int y) { x = 0; y = 0; return true; }
        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => true;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default) => true;
        public bool ScrollWheel(int detents) => true;
    }

    // ------------------------------------------------------------------ assembling

    private static SessionActuationAuthority Authority(IInputBackend inner, DesktopStandIn desktop)
    {
        var gate = new GatedInputBackend(
            inner, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var authority = new SessionActuationAuthority(new IntegrityStandIn(), desktop, gate, () => true);
        authority.BeginSession(Session, ClientPid);
        return authority;
    }

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public GeometryShape Scale { get; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = null;
            return true;
        }
    }

    private static StepRequest AStep() => new(
        new MapPoint(2, 2),
        new MapPoint(3, 2),
        new MapGrid(mapId: 1, width: 10, height: 10, cells: new byte[100]),
        new OccupancyView(Array.Empty<SelectableEntity>(), Now),
        Now);

    private static GeometryStamp KnownGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, Monitor),
        new DateTimeOffset(Now));

    private static StepGuardChain AuthorizingChain() => new(
        () => null,
        () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true },
        new ProjectionStandIn());

    private static SingleStepExecutor Executor(
        IInputBackend inner,
        Func<IntPtr, GeometryStamp>? readGeometry = null) =>
        new(
            AuthorizingChain(),
            new GatedInputBackend(inner, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true }),
            () => Session,
            readGeometry: readGeometry ?? KnownGeometry);

    // --------------------------------------------------- the session-authority gate

    [Fact]
    public void ClientAreaTooSmallIsNamedAuthorityClientAreaTooSmall()
    {
        var desktop = new DesktopStandIn
        {
            Live = new GeometryEpoch(Session, new PixelRect(100, 100, 4, 4), 96, Monitor)
        };

        SessionAuthorityVerdict verdict = Authority(new RecordingInputBackend(500, 400), desktop).Verify();

        Assert.False(verdict.IsActuating);
        Assert.Equal(SessionActuationAuthority.ClientAreaTooSmallReason, verdict.RefusalReason);
    }

    [Fact]
    public void UnreadableCursorIsNamedAuthorityCursorUnreadable()
    {
        SessionAuthorityVerdict verdict = Authority(new CursorFailingBackend(), new DesktopStandIn()).Verify();

        Assert.False(verdict.IsActuating);
        Assert.Equal(SessionActuationAuthority.CursorUnreadableReason, verdict.RefusalReason);
    }

    [Fact]
    public void ARefusedPointerMoveIsNamedAuthorityPointerMoveRefused()
    {
        SessionAuthorityVerdict verdict = Authority(new MoveFailingBackend(), new DesktopStandIn()).Verify();

        Assert.False(verdict.IsActuating);
        Assert.Equal(SessionActuationAuthority.MoveRefusedReason, verdict.RefusalReason);
    }

    [Fact]
    public void ANeverVerifiedSessionIsNamedAuthorityNotVerified()
    {
        // The "nothing verified yet" state is the None verdict, and it is the one
        // place NoVerdictReason reaches the operator today: the two CurrentRefusal
        // branches that also return it are unreachable through the public API.
        Assert.False(SessionAuthorityVerdict.None.WasProbed);
        Assert.False(SessionAuthorityVerdict.None.IsActuating);
        Assert.Equal(SessionActuationAuthority.NoVerdictReason, SessionAuthorityVerdict.None.RefusalReason);
    }

    // ------------------------------------------------------------ the actuation scope

    [Fact]
    public void AClosedScopeRefusesAFurtherEntryAsAborted()
    {
        var gate = new GatedInputBackend(
            new RecordingInputBackend(), () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var request = new CommitRequest(default, 0, 0, default);
        var authority = ActuationAuthority.Commanded("actuation-refusal-coverage");

        Assert.True(gate.TryBeginActuation(in request, in authority, out ActuationScope? scope, out _));
        scope!.Dispose();

        Assert.False(scope.TryEnter(out string? refusal));
        Assert.Equal(ActuationScope.AlreadyAbortedReason, refusal);
    }

    // ---------------------------------------------------------------- the gate release

    [Fact]
    public void ABackendThatCannotReleaseNamesTheReleaseRefusal()
    {
        var gate = new GatedInputBackend(
            new NoReleaseBackend(), () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var request = new CommitRequest(default, 0, 0, default);
        var authority = ActuationAuthority.Commanded("actuation-refusal-coverage");

        Assert.True(gate.TryBeginActuation(in request, in authority, out ActuationScope? scope, out _));
        scope!.RecordKey(0x41);
        scope.Dispose();

        Assert.Equal(GatedInputBackend.ReleaseUnsupportedReason, gate.LastRefusal!.Reason);
    }

    // --------------------------------------------------------- the single-step executor

    [Fact]
    public void UnknownGeometryIsNamedStepSessionGeometryUnknown()
    {
        SingleStepExecutor executor = Executor(
            new RecordingInputBackend(),
            readGeometry: _ => new GeometryStamp(default, default));

        StepRequest request = AStep();
        ActuationAuthority authority = ActuationAuthority.Commanded("actuation-refusal-coverage");
        StepReport report = executor.Step(in request, in authority, () => null);

        Assert.False(report.Emitted);
        Assert.Equal(SingleStepExecutor.GeometryUnknownReason, report.EmissionRefusal);
    }

    [Fact]
    public void ARefusedCursorMoveIsNamedStepCursorMoveRefused()
    {
        SingleStepExecutor executor = Executor(new MoveFailingBackend());

        StepRequest request = AStep();
        ActuationAuthority authority = ActuationAuthority.Commanded("actuation-refusal-coverage");
        StepReport report = executor.Step(in request, in authority, () => null);

        Assert.False(report.Emitted);
        Assert.Equal(SingleStepExecutor.CursorMoveRefusedReason, report.EmissionRefusal);
    }
}
