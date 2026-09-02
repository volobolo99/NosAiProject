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
/// The guards for one step, the freshness condition that is new at the act, and the
/// verifier that decides whether the step happened (C-P4, roadmap P4).
/// </summary>
public sealed class StepGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7100;

    // --------------------------------------------------------------- scaffolding

    /// <summary>A 10x10 map, open ground, with a wall down column 5.</summary>
    private static MapGrid Grid()
    {
        var cells = new byte[100];
        for (var y = 0; y < 10; y++)
            cells[(y * 10) + 5] = (byte)MapCellFlags.WalkBlocked;

        return new MapGrid(mapId: 1, width: 10, height: 10, cells);
    }

    private static SelectableEntity Entity(long id, int x, int y, DateTime observedAt) =>
        new(id, new MapPoint(x, y), null, observedAt);

    private static OccupancyView FreshView(params SelectableEntity[] entities) =>
        new(entities, Now);

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public bool Works { get; set; } = true;
        public string? Failure { get; set; }
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            // A transform nobody has to believe: it only has to be a function.
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = Works ? null : Failure ?? "projection_refused";
            return Works;
        }
    }

    private sealed class ChainRig
    {
        public string? AuthorityRefusal { get; set; }
        public bool Armed { get; set; } = true;
        public ProjectionStandIn Projection { get; } = new();

        public StepGuardChain Build() => new(
            () => AuthorityRefusal,
            () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Armed },
            Projection);
    }

    private static StepRequest Request(
        MapPoint? from = null,
        MapPoint? to = null,
        MapGrid? grid = null,
        OccupancyView? view = null) =>
        new(
            from ?? new MapPoint(2, 2),
            to ?? new MapPoint(3, 2),
            grid ?? Grid(),
            view ?? FreshView(),
            Now);

    private static StepGuardState StateOf(StepAuthorization authorization, StepGuard guard) =>
        authorization.Outcomes.Single(o => o.Guard == guard).State;

    // ----------------------------------------------------------- occupancy freshness

    [Fact]
    public void NothingHasLookedIsNotClear()
    {
        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(
            new MapPoint(3, 2), new OccupancyView(null, Now), Now);

        Assert.False(verdict.IsClear);
        Assert.Equal(OccupancyFreshness.NeverObservedReason, verdict.RefusalReason);
    }

    [Fact]
    public void AViewWithoutAnInstantIsNotClear()
    {
        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(
            new MapPoint(3, 2), new OccupancyView(Array.Empty<SelectableEntity>(), null), Now);

        Assert.False(verdict.IsClear);
        Assert.Equal(OccupancyFreshness.ViewNotStampedReason, verdict.RefusalReason);
    }

    /// <summary>
    /// The sentence the roadmap makes the condition out of: an absent observation and
    /// an expired one are the same answer, and neither of them is "free".
    /// </summary>
    [Fact]
    public void AnExpiredViewIsNotClearEitherAndSaysByHowMuch()
    {
        var view = new OccupancyView(Array.Empty<SelectableEntity>(), Now.AddSeconds(-5));

        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(new MapPoint(3, 2), view, Now);

        Assert.False(verdict.IsClear);
        Assert.StartsWith(OccupancyFreshness.ViewStalePrefix + ":", verdict.RefusalReason);
        Assert.Contains("5000ms_of_1000ms", verdict.RefusalReason);
        Assert.Equal(DynamicOccupancy.Suspected, verdict.Occupancy);
    }

    [Fact]
    public void AViewStampedAheadOfNowIsRefusedRatherThanTreatedAsFresh()
    {
        var view = new OccupancyView(Array.Empty<SelectableEntity>(), Now.AddSeconds(5));

        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(new MapPoint(3, 2), view, Now);

        Assert.False(verdict.IsClear);
        Assert.Equal(OccupancyFreshness.ViewFromTheFutureReason, verdict.RefusalReason);
    }

    [Fact]
    public void AFreshViewWithNothingOnTheCellIsClear()
    {
        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(
            new MapPoint(3, 2), FreshView(Entity(1, 8, 8, Now)), Now);

        Assert.True(verdict.IsClear);
        Assert.Equal(DynamicOccupancy.Clear, verdict.Occupancy);
        Assert.Null(verdict.RefusalReason);
    }

    [Fact]
    public void AFreshSightingOnTheCellOccupiesIt()
    {
        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(
            new MapPoint(3, 2), FreshView(Entity(77, 3, 2, Now)), Now);

        Assert.False(verdict.IsClear);
        Assert.Equal(DynamicOccupancy.Occupied, verdict.Occupancy);
        Assert.Equal($"{OccupancyFreshness.DestinationOccupiedPrefix}:77", verdict.RefusalReason);
    }

    /// <summary>
    /// A sighting too old to be a sighting is a suspicion, and a suspicion blocks —
    /// but it is not reported as an occupancy, because nobody saw one.
    /// </summary>
    [Fact]
    public void AStaleSightingOnTheCellIsSuspectedNotOccupied()
    {
        var view = new OccupancyView(new[] { Entity(77, 3, 2, Now.AddMinutes(-5)) }, Now);

        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(new MapPoint(3, 2), view, Now);

        Assert.False(verdict.IsClear);
        Assert.Equal(DynamicOccupancy.Suspected, verdict.Occupancy);
        Assert.Equal($"{OccupancyFreshness.DestinationSuspectedPrefix}:77", verdict.RefusalReason);
    }

    /// <summary>
    /// The two ages are separate on purpose: a monster that has stood still for a
    /// minute must not make the runtime refuse every step near it, and a feed that
    /// stopped a minute ago must stop every step.
    /// </summary>
    [Fact]
    public void AStationaryEntityDoesNotAgeTheView()
    {
        var view = new OccupancyView(new[] { Entity(9, 8, 8, Now.AddSeconds(-20)) }, Now);

        Assert.True(OccupancyFreshness.Evaluate(new MapPoint(3, 2), view, Now).IsClear);
    }

    // ------------------------------------------------------------------- the chain

    [Fact]
    public void EveryGuardPassingAuthorisesTheStepAndCarriesThePixel()
    {
        StepAuthorization authorization = new ChainRig().Build().Authorize(Request());

        Assert.True(authorization.IsAuthorized);
        Assert.Null(authorization.RefusedAt);
        Assert.Equal(100 + (3 * 32), authorization.ScreenX);
        Assert.Equal(200 + (2 * 16), authorization.ScreenY);
        Assert.Equal(new GeometryShape(1024, 768, 96), authorization.Scale);
        Assert.All(authorization.Outcomes, o => Assert.Equal(StepGuardState.Passed, o.State));
    }

    /// <summary>The report shows the whole ladder, including the rungs never reached.</summary>
    [Fact]
    public void TheReportNamesEveryGuardWhicheverOneRefused()
    {
        var rig = new ChainRig { Armed = false };

        StepAuthorization authorization = rig.Build().Authorize(Request());

        Assert.Equal(6, authorization.Outcomes.Count);
        Assert.Equal(StepGuard.Policy, authorization.RefusedAt);
        Assert.Equal(StepGuardState.Passed, StateOf(authorization, StepGuard.Shape));
        Assert.Equal(StepGuardState.Refused, StateOf(authorization, StepGuard.Policy));
        Assert.Equal(StepGuardState.NotEvaluated, StateOf(authorization, StepGuard.Occupancy));
        Assert.Equal(StepGuardState.NotEvaluated, StateOf(authorization, StepGuard.Projection));
    }

    [Fact]
    public void AStepToWhereWeAlreadyAreIsRefused()
    {
        StepAuthorization authorization = new ChainRig().Build()
            .Authorize(Request(from: new MapPoint(2, 2), to: new MapPoint(2, 2)));

        Assert.Equal(StepGuard.Shape, authorization.RefusedAt);
        Assert.Equal(StepGuardChain.ZeroLengthReason, authorization.RefusalReason);
    }

    [Fact]
    public void TwoCellsIsNotAStep()
    {
        StepAuthorization authorization = new ChainRig().Build()
            .Authorize(Request(from: new MapPoint(2, 2), to: new MapPoint(4, 2)));

        Assert.Equal(StepGuard.Shape, authorization.RefusedAt);
        Assert.StartsWith(StepGuardChain.NotAdjacentPrefix + ":", authorization.RefusalReason);
    }

    [Fact]
    public void NoGridLoadedRefusesRatherThanTreatingTheMapAsOpen()
    {
        StepAuthorization authorization = new ChainRig().Build()
            .Authorize(Request(grid: default(MapGrid)));

        Assert.Equal(StepGuard.Geometry, authorization.RefusedAt);
        Assert.Equal(StepGuardChain.GridNotLoadedReason, authorization.RefusalReason);
    }

    [Fact]
    public void AWallInTheDestinationRefuses()
    {
        StepAuthorization authorization = new ChainRig().Build()
            .Authorize(Request(from: new MapPoint(4, 3), to: new MapPoint(5, 3)));

        Assert.Equal(StepGuard.Geometry, authorization.RefusedAt);
        Assert.StartsWith(StepGuardChain.DestinationBlockedPrefix + ":", authorization.RefusalReason);
    }

    /// <summary>
    /// P1's standing-cell proof, made a precondition of acting. A grid that says the
    /// character is inside a wall is a grid that disagrees with the world, and every
    /// conclusion drawn from it — including the one about the destination — is unsound.
    /// </summary>
    [Fact]
    public void StandingInsideAWallStopsTheStepBeforeTheDestinationIsConsidered()
    {
        StepAuthorization authorization = new ChainRig().Build()
            .Authorize(Request(from: new MapPoint(5, 3), to: new MapPoint(4, 3)));

        Assert.Equal(StepGuard.Geometry, authorization.RefusedAt);
        Assert.StartsWith(StepGuardChain.OriginNotWalkablePrefix + ":", authorization.RefusalReason);
    }

    [Fact]
    public void OffTheGridIsRefusedAtBothEnds()
    {
        StepGuardChain chain = new ChainRig().Build();

        Assert.StartsWith(
            StepGuardChain.OriginOffGridPrefix + ":",
            chain.Authorize(Request(from: new MapPoint(20, 20), to: new MapPoint(20, 19))).RefusalReason);

        Assert.StartsWith(
            StepGuardChain.DestinationOffGridPrefix + ":",
            chain.Authorize(Request(from: new MapPoint(9, 9), to: new MapPoint(10, 9))).RefusalReason);
    }

    [Fact]
    public void ANonActuatingSessionRefusesTheStep()
    {
        var rig = new ChainRig { AuthorityRefusal = "authority_integrity_below_client:medium_under_high" };

        StepAuthorization authorization = rig.Build().Authorize(Request());

        Assert.Equal(StepGuard.Authority, authorization.RefusedAt);
        Assert.Equal("authority_integrity_below_client:medium_under_high", authorization.RefusalReason);
    }

    [Fact]
    public void AStaleWorldRefusesTheStepAtOccupancy()
    {
        var stale = new OccupancyView(Array.Empty<SelectableEntity>(), Now.AddSeconds(-30));

        StepAuthorization authorization = new ChainRig().Build().Authorize(Request(view: stale));

        Assert.Equal(StepGuard.Occupancy, authorization.RefusedAt);
        Assert.StartsWith(OccupancyFreshness.ViewStalePrefix + ":", authorization.RefusalReason);
    }

    [Fact]
    public void AnUncalibratedProjectionRefusesTheStepByName()
    {
        var rig = new ChainRig();
        rig.Projection.Works = false;
        rig.Projection.Failure = UncalibratedScreenProjection.NotCalibratedReason;

        StepAuthorization authorization = rig.Build().Authorize(Request());

        Assert.Equal(StepGuard.Projection, authorization.RefusedAt);
        Assert.Equal(UncalibratedScreenProjection.NotCalibratedReason, authorization.RefusalReason);
    }

    /// <summary>
    /// A projection that produces a pixel under a scale it cannot name would reach the
    /// commit point and be refused there as commit_scale_unknown. Caught here, the
    /// refusal names the calibration instead of the last instant before the click.
    /// </summary>
    [Fact]
    public void AProjectionWithNoKnownScaleRefusesTheStep()
    {
        var rig = new ChainRig();
        rig.Projection.Scale = default;

        StepAuthorization authorization = rig.Build().Authorize(Request());

        Assert.Equal(StepGuard.Projection, authorization.RefusedAt);
        Assert.Equal("step_projection_scale_unknown", authorization.RefusalReason);
    }

    /// <summary>
    /// The order is the point of the chain: with two things wrong, the sentence names
    /// the structural one, and the volatile check is never even reached.
    /// </summary>
    [Fact]
    public void WhenTwoGuardsWouldRefuseTheUpstreamOneIsNamed()
    {
        var rig = new ChainRig { AuthorityRefusal = "authority_no_session" };
        var occupied = new OccupancyView(new[] { Entity(5, 3, 2, Now) }, Now);

        StepAuthorization authorization = rig.Build().Authorize(Request(view: occupied));

        Assert.Equal(StepGuard.Authority, authorization.RefusedAt);
        Assert.Equal(StepGuardState.NotEvaluated, StateOf(authorization, StepGuard.Occupancy));
    }

    // --------------------------------------------------------------- the verifier

    private static Func<PositionReading?> Readings(params PositionReading?[] sequence)
    {
        var index = 0;
        return () => index < sequence.Length ? sequence[index++] : sequence.LastOrDefault();
    }

    private static readonly MovementVerifier Fast =
        new(window: TimeSpan.FromMilliseconds(60), tolerance: TimeSpan.FromMilliseconds(10),
            pollInterval: TimeSpan.FromMilliseconds(2));

    [Fact]
    public void ArrivingOnTheDestinationSucceeds()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(
                new PositionReading(new MapPoint(2, 2), Now.AddMilliseconds(5), DataSourceKind.Live),
                new PositionReading(new MapPoint(3, 2), Now.AddMilliseconds(20), DataSourceKind.Live)));

        Assert.Equal(MovementOutcome.Succeeded, verification.Outcome);
        Assert.Equal(new MapPoint(3, 2), verification.Observed);
    }

    [Fact]
    public void StayingPutIsAStall()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(new PositionReading(new MapPoint(2, 2), Now.AddMilliseconds(5), DataSourceKind.Live)));

        Assert.Equal(MovementOutcome.Stalled, verification.Outcome);
        Assert.Equal(MovementVerifier.StalledReason, verification.Detail);
        Assert.True(verification.ReadingsAccepted > 0);
    }

    /// <summary>
    /// The rule the whole verifier rests on. A feed republishes what it last knew, so a
    /// position stamped before the click is the world the click was meant to change;
    /// comparing it would report a stall every time, on evidence about the wrong moment.
    /// </summary>
    [Fact]
    public void AReadingFromBeforeTheActIsNotTestimony()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(new PositionReading(new MapPoint(2, 2), Now.AddMilliseconds(-50), DataSourceKind.Live)));

        Assert.Equal(MovementOutcome.Unobserved, verification.Outcome);
        Assert.Equal(MovementVerifier.NoFreshReadingReason, verification.Detail);
        Assert.Equal(0, verification.ReadingsAccepted);
    }

    /// <summary>Even a reading that would say "arrived" does not count if it predates the act.</summary>
    [Fact]
    public void AStaleArrivalIsNotASuccess()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(new PositionReading(new MapPoint(3, 2), Now.AddMilliseconds(-1), DataSourceKind.Live)));

        Assert.Equal(MovementOutcome.Unobserved, verification.Outcome);
    }

    [Fact]
    public void ASimulatedPositionCannotConfirmARealAct()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(new PositionReading(new MapPoint(3, 2), Now.AddMilliseconds(10), DataSourceKind.Simulated)));

        Assert.Equal(MovementOutcome.Unobserved, verification.Outcome);
        Assert.Equal(MovementVerifier.SimulatedReadingReason, verification.Detail);
    }

    [Fact]
    public void MovingSomewhereElseIsNotASuccess()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2),
            new MapPoint(3, 2),
            Now,
            Readings(new PositionReading(new MapPoint(2, 3), Now.AddMilliseconds(10), DataSourceKind.Live)));

        Assert.Equal(MovementOutcome.Displaced, verification.Outcome);
        Assert.Contains("2,3", verification.Detail);
    }

    [Fact]
    public void NoReadingAtAllIsUnobservedAndNotAStall()
    {
        MovementVerification verification = Fast.Verify(
            new MapPoint(2, 2), new MapPoint(3, 2), Now, () => null);

        Assert.Equal(MovementOutcome.Unobserved, verification.Outcome);
        Assert.NotEqual(MovementOutcome.Stalled, verification.Outcome);
    }

    [Fact]
    public void TheWindowIsOneSidedAndClosesAtWindowPlusTolerance()
    {
        var verifier = new MovementVerifier();

        Assert.Equal(TimeSpan.FromMilliseconds(370), verifier.Deadline);
        Assert.Equal(TimeSpan.FromMilliseconds(350), verifier.Window);
    }

    // --------------------------------------------------------------- the executor

    /// <summary>
    /// A stated geometry, because a test does not own a window. Production reads the
    /// real one; this only replaces the reading, never the comparison.
    /// </summary>
    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    private static (SingleStepExecutor Executor, RecordingInputBackend Recorder, ChainRig Rig) Executor()
    {
        var rig = new ChainRig();
        var recorder = new RecordingInputBackend();
        var gate = new GatedInputBackend(
            recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = rig.Armed });

        var executor = new SingleStepExecutor(
            rig.Build(), gate, () => Session, Fast, readGeometry: StatedGeometry);
        return (executor, recorder, rig);
    }

    [Fact]
    public void ARefusedStepEmitsNothingAtAll()
    {
        (SingleStepExecutor executor, RecordingInputBackend recorder, ChainRig rig) = Executor();
        rig.AuthorityRefusal = "authority_no_session";

        StepReport report = executor.Step(Request(), () => null);

        Assert.False(report.Emitted);
        Assert.Equal(MovementOutcome.Aborted, report.Verification.Outcome);
        Assert.Equal("authority_no_session", report.EmissionRefusal);
        Assert.Empty(recorder.Events);
        Assert.Contains("refused at Authority", report.Summary);
    }

    /// <summary>
    /// The geometry stamp is taken from the session window, so a runtime that does not
    /// know which window it is acting on emits nothing rather than stamping a zero.
    /// </summary>
    [Fact]
    public void WithNoSessionWindowNothingIsEmitted()
    {
        var rig = new ChainRig();
        var recorder = new RecordingInputBackend();
        var gate = new GatedInputBackend(
            recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true });
        var executor = new SingleStepExecutor(
            rig.Build(), gate, () => IntPtr.Zero, Fast, readGeometry: StatedGeometry);

        StepReport report = executor.Step(Request(), () => null);

        Assert.False(report.Emitted);
        Assert.Equal(SingleStepExecutor.NoSessionWindowReason, report.EmissionRefusal);
        Assert.Empty(recorder.Events);
    }

    /// <summary>
    /// The order inside the act: the cursor first, because it can be taken back, and
    /// the click last, because it cannot (DOMAIN-17).
    /// </summary>
    [Fact]
    public void TheClickIsTheLastThingThatHappens()
    {
        (SingleStepExecutor executor, RecordingInputBackend recorder, _) = Executor();

        StepReport report = executor.Step(
            Request(),
            Readings(new PositionReading(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live)));

        Assert.True(report.Emitted);
        Assert.Equal(new[] { "move-absolute:196,232", "click:Left" }, recorder.Events);
        Assert.Equal(MovementOutcome.Succeeded, report.Verification.Outcome);
        Assert.NotNull(report.EmittedAtUtc);
    }

    [Fact]
    public void TheScopeIsClosedWhenTheStepIsDone()
    {
        (SingleStepExecutor executor, _, _) = Executor();

        executor.Step(Request(), () => null);

        // A scope left open would refuse every following act with
        // commit_scope_already_open, which is the shape of a leak that only shows up
        // on the second step.
        StepReport second = executor.Step(Request(), () => null);
        Assert.NotEqual(
            $"{SingleStepExecutor.ScopeRefusedPrefix}:{GatedInputBackend.ScopeAlreadyOpenReason}",
            second.EmissionRefusal);
    }
}
