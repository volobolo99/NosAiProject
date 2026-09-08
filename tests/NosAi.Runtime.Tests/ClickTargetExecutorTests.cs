using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A2A4: <see cref="ClickTargetExecutor"/> -- one click on an established
/// target, assessed, projected, confined to the window, emitted through the
/// gate, and verified on the wire (no desktop, no real mouse: the executor takes
/// the gated backend as a dependency, so it is driven with a
/// <see cref="RecordingInputBackend"/> that records what it was asked to do).
/// </summary>
public sealed class ClickTargetExecutorTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7200;
    private static readonly IntPtr OtherWindow = 0x7300;
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded(ClickTargetCommand.Flag);

    private static readonly TimeSpan FastWindow = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(2);

    // ------------------------------------------------------------- the projection

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public bool Works { get; set; } = true;
        public string? Failure { get; set; }
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            // A hand-computable transform so a test can assert the exact pixel.
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = Works ? null : Failure ?? "projection_refused";
            return Works;
        }
    }

    // ------------------------------------------------------------- the commit point

    private sealed class DesktopStandIn : ICommitEnvironment
    {
        public IntPtr Foreground { get; set; } = Session;
        public GeometryEpoch Live { get; set; } =
            new(Session, new PixelRect(0, 0, 1024, 768), 96, 0xABCD);

        public IntPtr ForegroundWindow() => Foreground;
        public IntPtr RootWindowFromPoint(int x, int y) => Session;
        public bool? IsCloaked(IntPtr window) => false;
        public GeometryEpoch ReadEpoch(IntPtr window) => Live;
    }

    private sealed class AlwaysIdleHuman : IHumanInputMonitor
    {
        public bool IsWatching => true;
        public TimeSpan? SinceLastHumanInput => TimeSpan.FromHours(1);
        public long HumanEventCount => 0;
        public long InjectedEventCount => 0;
    }

    // ------------------------------------------------------------- entities

    private static SelectableEntity Monster(long id = 313816, MapPoint? at = null) =>
        new(id, at ?? new MapPoint(3, 2), 1.0, Now, Vnum: 36, Vitals: null, Kind: EntitySighting.MonsterKind);

    private static SelectableEntity Bystander() =>
        new(2328703, new MapPoint(3, 2), 1.0, Now, Vnum: 1488, Vitals: null, Kind: EntitySighting.BystanderKind);

    private static ClickTargetRequest Request(
        SelectableEntity entity,
        ClassifiedValue<Aggressor>? hitBy = null,
        ClassifiedValue<TargetedEntity>? selected = null) =>
        new(entity, hitBy, selected, null);

    /// <summary>A monster that hit us is established beyond any catalogue or wire label.</summary>
    private static ClickTargetRequest EstablishedMonster(long id = 313816) =>
        Request(Monster(id), hitBy: ClassifiedValue<Aggressor>.Live(new Aggressor(id, 3), Now));

    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    private static ClickTargetExecutor Build(
        RecordingInputBackend recorder,
        ProjectionStandIn projection,
        GatedInputBackend? gate = null,
        Func<IntPtr, GeometryStamp>? readGeometry = null)
    {
        var gated = gate ?? new GatedInputBackend(
            recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        return new ClickTargetExecutor(
            gated, projection, () => Session,
            readGeometry: readGeometry ?? StatedGeometry,
            verificationWindow: FastWindow,
            pollInterval: FastPoll);
    }

    private static Func<PlayerTargetSelection?> SelectionReader(params PlayerTargetSelection?[] sequence)
    {
        var queue = new Queue<PlayerTargetSelection?>(sequence);
        return () => queue.Count > 0 ? queue.Dequeue() : null;
    }

    // ------------------------------------------------------- 1. a bystander is not clicked

    [Fact]
    public void ABystander_IsNotClicked_AndTheBackendStaysStill()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = Request(Bystander());

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.False(report.Emitted);
        Assert.Equal(TargetEstablishment.BystanderReason, report.RefusalReason);
        Assert.False(report.Verdict.IsEstablished);
        // The backend was never asked to move: the refusal is an act that did not
        // happen, not a click that was refused at the last moment.
        Assert.Empty(recorder.Events);
        Assert.Equal(TargetSelectionOutcome.NotAttempted, report.Verification.Outcome);
    }

    // ------------------------------------------------------- 2. an established monster is clicked

    [Fact]
    public void AnEstablishedMonster_IsClickedAtTheProjectedPixel()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.True(report.Emitted);
        Assert.True(report.Verdict.IsEstablished);
        // The projection stand-in maps (3,2) -> 100+96, 200+32.
        Assert.Equal(196, report.ScreenX);
        Assert.Equal(232, report.ScreenY);
        // The move and the click went out exactly once, in order.
        Assert.Equal(new[] { "move-absolute:196,232", "click:Left" }, recorder.Events);
        Assert.NotNull(report.EmittedAtUtc);
    }

    // ------------------------------------------------------- 3. missing calibration

    [Fact]
    public void ARefusedProjection_RefusesWithTheProjectionsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn { Works = false, Failure = "screen_projection_not_calibrated" };
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.False(report.Emitted);
        Assert.Equal("screen_projection_not_calibrated", report.RefusalReason);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 4. pixel outside the window

    [Fact]
    public void APixelOutsideTheWindow_IsNotClicked_WithItsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);

        // (40,40) -> 100+1280, 200+640 = 1380,840, outside the 1024x768 window.
        SelectableEntity far = Monster(at: new MapPoint(40, 40));
        ClickTargetRequest request = Request(far, hitBy: ClassifiedValue<Aggressor>.Live(new Aggressor(far.EntityId, 3), Now));

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.False(report.Emitted);
        Assert.Equal(ClickTargetExecutor.PixelOutsideWindowReason, report.RefusalReason);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 5. the commit point refused the click

    [Fact]
    public void ACommitPointRefusal_ReachesTheCallerUnrewritten_AndNothingIsClicked()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        var desktop = new DesktopStandIn { Foreground = OtherWindow };
        var gate = new GatedInputBackend(
            recorder,
            () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true },
            new CommitPointValidator(desktop, new AlwaysIdleHuman()));
        ClickTargetExecutor executor = Build(recorder, projection, gate: gate);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.False(report.Emitted);
        // The cursor moved (reversible), then the commit point refused the click
        // itself: the validator's reason reaches the caller unrewritten.
        Assert.Equal(new[] { "move-absolute:196,232" }, recorder.Events);
        Assert.Equal(
            $"{ClickTargetExecutor.ClickRefusedPrefix}:{CommitPointValidator.NotForegroundReason}",
            report.RefusalReason);
    }

    // ------------------------------------------------------- 6. the wire verdict, three outcomes

    [Fact]
    public void ACtNamingTheTargetId_Confirms()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(
            in request,
            in Operator,
            SelectionReader(
                null,
                new PlayerTargetSelection(new TargetedEntity(313816, 3), Now.AddSeconds(1), DataSourceKind.Live)));

        Assert.True(report.Emitted);
        Assert.Equal(TargetSelectionOutcome.Confirmed, report.Verification.Outcome);
        Assert.True(report.Verification.Confirmed);
        Assert.Equal(313816L, report.Verification.ObservedEntityId);
    }

    [Fact]
    public void ACtNamingADifferentId_IsADistinctOutcomeFromNoConfirmation()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(
            in request,
            in Operator,
            SelectionReader(
                null,
                new PlayerTargetSelection(new TargetedEntity(999999, 3), Now.AddSeconds(1), DataSourceKind.Live)));

        Assert.True(report.Emitted);
        Assert.Equal(TargetSelectionOutcome.DifferentTarget, report.Verification.Outcome);
        Assert.Equal(999999L, report.Verification.ObservedEntityId);
        Assert.False(report.Verification.Confirmed);
        Assert.NotEqual(TargetSelectionOutcome.NotConfirmed, report.Verification.Outcome);
    }

    [Fact]
    public void NoCtWithinTheWindow_IsNotConfirmed_AndReportsHowLongItWaited()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader());

        Assert.True(report.Emitted);
        Assert.Equal(TargetSelectionOutcome.NotConfirmed, report.Verification.Outcome);
        Assert.Null(report.Verification.ObservedEntityId);
        // The window closed having watched for at least the declared duration.
        Assert.True(report.Verification.Waited >= FastWindow);
    }

    // ------------------------------------------------------- a remembered selection is not a new one

    [Fact]
    public void ASelectionAlreadyOnTheWireBeforeTheClick_DoesNotCountAsConfirmation()
    {
        var recorder = new RecordingInputBackend();
        var projection = new ProjectionStandIn();
        ClickTargetExecutor executor = Build(recorder, projection);
        ClickTargetRequest request = EstablishedMonster();

        // The wire already named the target *before* the click: that testifies to
        // nothing the click did, so the verdict is NotConfirmed, not Confirmed.
        var baseline = new PlayerTargetSelection(new TargetedEntity(313816, 3), Now, DataSourceKind.Live);
        ClickTargetReport report = executor.Click(in request, in Operator, SelectionReader(baseline));

        Assert.True(report.Emitted);
        Assert.Equal(TargetSelectionOutcome.NotConfirmed, report.Verification.Outcome);
    }
}
