using System.Diagnostics;
using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Walking a path: admitted once against the client's geometry, revalidated at every
/// segment, and abandoned when re-routing has run out of things to try (C2-7, P5).
/// </summary>
public sealed class PathWalkTests
{
    private readonly ITestOutputHelper _output;

    public PathWalkTests(ITestOutputHelper output) => _output = output;

    private static readonly DateTime Now = new(2026, 9, 2, 18, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ the maps

    /// <summary>Open ground, 40x40. Nothing in the way.</summary>
    private static MapGrid OpenMap(int mapId = 1) => new(mapId, 40, 40, new byte[1600]);

    /// <summary>A wall down column 20, with a doorway at row 20.</summary>
    private static MapGrid WalledMap(int mapId = 2)
    {
        var cells = new byte[1600];
        for (var y = 0; y < 40; y++)
            if (y != 20)
                cells[(y * 40) + 20] = (byte)MapCellFlags.WalkBlocked;

        return new MapGrid(mapId, 40, 40, cells);
    }

    /// <summary>Pillars every four cells: a map where a route has to weave.</summary>
    private static MapGrid PillarMap(int mapId = 3)
    {
        var cells = new byte[1600];
        for (var y = 4; y < 36; y += 4)
            for (var x = 4; x < 36; x += 4)
                cells[(y * 40) + x] = (byte)MapCellFlags.WalkBlocked;

        return new MapGrid(mapId, 40, 40, cells);
    }

    private static MapGridData Project(in MapGrid grid)
    {
        var data = MapGridData.CreateFullyWalkable(grid.MapId, "test", grid.Width, grid.Height);
        StaticGeometryLayer.Project(in grid, data);
        return data;
    }

    /// <summary>A route the real planner produced, cell by cell — never smoothed.</summary>
    private static IReadOnlyList<MapPoint> Plan(in MapGrid grid, MapPoint from, MapPoint to)
    {
        CalculatedPathResult result = new AStarPathfinder().FindPath(
            Project(in grid), new GridPoint(from.X, from.Y), new GridPoint(to.X, to.Y));

        return result.IsPathFound
            ? PathRevalidation.ToCells(result.Waypoints)
            : Array.Empty<MapPoint>();
    }

    private static OccupancyView Clear(DateTime at) =>
        new(Array.Empty<SelectableEntity>(), at);

    private static OccupancyView Occupied(DateTime at, MapPoint cell) =>
        new(new[] { new SelectableEntity(99, cell, null, at) }, at);

    private static IReadOnlyList<MapPoint> Line(int fromX, int y, int toX)
    {
        var cells = new List<MapPoint>();
        for (int x = fromX; x <= toX; x++)
            cells.Add(new MapPoint(x, y));
        return cells;
    }

    // -------------------------------------------------------------- admission

    [Fact]
    public void AnOpenRouteIsAdmitted()
    {
        MapGrid grid = OpenMap();
        PathAdmission admission = PathRevalidation.Admit(in grid, Line(2, 5, 20));

        Assert.True(admission.IsAdmitted);
        Assert.Equal(-1, admission.FirstBadIndex);
        Assert.Equal(19, admission.CellsChecked);
    }

    /// <summary>
    /// The half of P5's DoD that has to be decided before the first step: a route through
    /// a wall is found by looking at every cell, not at the endpoints.
    /// </summary>
    [Fact]
    public void ARouteThroughAWallIsRefusedAndSaysWhichCell()
    {
        MapGrid grid = WalledMap();
        PathAdmission admission = PathRevalidation.Admit(in grid, Line(15, 5, 25));

        Assert.False(admission.IsAdmitted);
        Assert.StartsWith(PathRevalidation.CellBlockedPrefix + ":", admission.RefusalReason);
        Assert.Contains("@20,5", admission.RefusalReason);
        Assert.Equal(5, admission.FirstBadIndex);
    }

    [Fact]
    public void ARouteWithAGapIsNotARoute()
    {
        MapGrid grid = OpenMap();
        var jumped = new List<MapPoint> { new(2, 2), new(3, 2), new(9, 2) };

        PathAdmission admission = PathRevalidation.Admit(in grid, jumped);

        Assert.False(admission.IsAdmitted);
        Assert.StartsWith(PathRevalidation.CellsNotAdjacentPrefix + ":", admission.RefusalReason);
    }

    [Fact]
    public void ARouteLeavingTheGridIsRefused()
    {
        MapGrid grid = OpenMap();
        var offEdge = new List<MapPoint> { new(38, 2), new(39, 2), new(40, 2) };

        Assert.StartsWith(
            PathRevalidation.CellOffGridPrefix + ":",
            PathRevalidation.Admit(in grid, offEdge).RefusalReason);
    }

    [Fact]
    public void WithoutAGridNothingIsAdmitted()
    {
        Assert.Equal(
            PathRevalidation.GridNotLoadedReason,
            PathRevalidation.Admit(default, Line(2, 5, 20)).RefusalReason);
    }

    [Fact]
    public void APathOfOneCellIsNotAPath()
    {
        MapGrid grid = OpenMap();
        Assert.Equal(
            PathRevalidation.EmptyPathReason,
            PathRevalidation.Admit(in grid, new List<MapPoint> { new(2, 2) }).RefusalReason);
    }

    // ---------------------------------------------------------- revalidation

    [Fact]
    public void AClearSegmentPasses()
    {
        MapGrid grid = OpenMap();
        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, new MapPoint(5, 5), new MapPoint(6, 5), Clear(Now), Now);

        Assert.True(segment.IsClear);
        Assert.Null(segment.RefusalReason);
    }

    [Fact]
    public void ABlockedNextCellAsksForAnotherRoute()
    {
        MapGrid grid = WalledMap();
        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, new MapPoint(19, 5), new MapPoint(20, 5), Clear(Now), Now);

        Assert.False(segment.IsClear);
        Assert.True(segment.NeedsReplan);
        Assert.StartsWith(PathRevalidation.CellBlockedPrefix + ":", segment.RefusalReason);
    }

    [Fact]
    public void AnOccupiedNextCellAsksForAnotherRoute()
    {
        MapGrid grid = OpenMap();
        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, new MapPoint(5, 5), new MapPoint(6, 5), Occupied(Now, new MapPoint(6, 5)), Now);

        Assert.False(segment.IsClear);
        Assert.True(segment.NeedsReplan);
        Assert.StartsWith(OccupancyFreshness.DestinationOccupiedPrefix + ":", segment.RefusalReason);
    }

    /// <summary>
    /// The distinction that keeps a blind runtime from looking busy: re-routing against an
    /// observation too old to trust produces a different path with the same defect.
    /// </summary>
    [Fact]
    public void AStaleWorldIsNotSomethingAnotherRouteCanFix()
    {
        MapGrid grid = OpenMap();
        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, new MapPoint(5, 5), new MapPoint(6, 5), Clear(Now.AddSeconds(-30)), Now);

        Assert.False(segment.IsClear);
        Assert.False(segment.NeedsReplan);
        Assert.StartsWith(OccupancyFreshness.ViewStalePrefix + ":", segment.RefusalReason);
    }

    [Fact]
    public void BeingSomewhereElseIsNamedRatherThanWalkedThrough()
    {
        MapGrid grid = OpenMap();
        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, new MapPoint(2, 2), new MapPoint(6, 5), Clear(Now), Now);

        Assert.False(segment.IsClear);
        Assert.True(segment.NeedsReplan);
        Assert.StartsWith(PathRevalidation.OffPathPrefix + ":", segment.RefusalReason);
    }

    // ------------------------------------------------------------- the walk

    private static PathWalkController Walk(
        in MapGrid grid,
        IReadOnlyList<MapPoint> path,
        ReplanPolicy? policy = null)
    {
        var controller = new PathWalkController(policy);
        Assert.True(controller.TryStart(in grid, path, path[0], out string? why), why);
        return controller;
    }

    [Fact]
    public void AClearWalkArrives()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        MapPoint at = path[0];
        var steps = 0;
        while (steps++ < 100)
        {
            WalkDecision decision = walk.Next(in grid, at, Clear(Now), Now);
            if (decision.Outcome == WalkOutcome.Arrived)
                break;

            Assert.Equal(WalkOutcome.Stepping, decision.Outcome);
            at = decision.StepTo!.Value;
            walk.NoteStepOutcome(Arrived(at));
        }

        Assert.Equal(path[^1], at);
        Assert.Equal(18, walk.CellsAdvanced);
        Assert.Equal(0, walk.ReplansUsed);
    }

    private static MovementVerification Arrived(MapPoint at) =>
        new(MovementOutcome.Succeeded, null, at, TimeSpan.FromMilliseconds(120), 1);

    private static readonly MovementVerification Stalled =
        new(MovementOutcome.Stalled, MovementVerifier.StalledReason, null, TimeSpan.FromMilliseconds(370), 3);

    private static readonly MovementVerification Unobserved =
        new(MovementOutcome.Unobserved, MovementVerifier.NoFreshReadingReason, null, TimeSpan.FromMilliseconds(370), 0);

    /// <summary>§ 4.1: a stall re-routes, it does not send the same click again.</summary>
    [Fact]
    public void AStalledStepReRoutesInsteadOfRepeating()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        Assert.Equal(WalkOutcome.Stepping, walk.Next(in grid, path[0], Clear(Now), Now).Outcome);
        walk.NoteStepOutcome(Stalled);

        WalkDecision decision = walk.Next(in grid, path[0], Clear(Now), Now);

        Assert.Equal(WalkOutcome.Replan, decision.Outcome);
        Assert.Equal("walk_step_stalled", decision.Reason);
    }

    /// <summary>
    /// § 4.1 again, and the opposite conclusion: a displaced step means the projection is
    /// aiming elsewhere, and repeating is worse than stopping.
    /// </summary>
    [Fact]
    public void ADisplacedStepEndsTheWalkRatherThanReRouting()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        walk.Next(in grid, path[0], Clear(Now), Now);
        walk.NoteStepOutcome(new MovementVerification(
            MovementOutcome.Displaced, "movement_landed_elsewhere:9,9_not_3,5", new MapPoint(9, 9),
            TimeSpan.FromMilliseconds(200), 1));

        Assert.False(walk.IsWalking);
        WalkDecision decision = walk.Next(in grid, path[0], Clear(Now), Now);
        Assert.Equal(WalkOutcome.Abandoned, decision.Outcome);
        Assert.StartsWith(PathWalkController.DisplacedPrefix + ":", decision.Reason);
    }

    /// <summary>
    /// VER-05 keeps an unobservable outcome out of the failure ledger. It does not licence
    /// emitting a fourth act nobody can confirm.
    /// </summary>
    [Fact]
    public void ThreeUnconfirmableStepsEndTheWalk()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(WalkOutcome.Stepping, walk.Next(in grid, path[0], Clear(Now), Now).Outcome);
            walk.NoteStepOutcome(Unobserved);
        }

        Assert.False(walk.IsWalking);
        Assert.StartsWith(
            PathWalkController.UnverifiedLimitPrefix + ":",
            walk.Next(in grid, path[0], Clear(Now), Now).Reason);
    }

    // ------------------------------------------------------- the replan budget

    /// <summary>
    /// The limit that makes the difference between re-routing and spinning: a fourth
    /// consecutive replan without progress does not happen.
    /// </summary>
    [Fact]
    public void TheFourthConsecutiveReplanIsAnAbandonment()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        // The next cell is taken, every time, and the character never moves.
        OccupancyView blocked = Occupied(Now, path[1]);

        for (var i = 1; i <= 3; i++)
        {
            WalkDecision decision = walk.Next(in grid, path[0], blocked, Now);
            Assert.Equal(WalkOutcome.Replan, decision.Outcome);
            Assert.Equal(i, decision.ReplansUsed);
            Assert.True(walk.TryAdoptReplan(in grid, path, path[0], out string? why), why);
        }

        WalkDecision last = walk.Next(in grid, path[0], blocked, Now);

        Assert.Equal(WalkOutcome.Abandoned, last.Outcome);
        Assert.StartsWith(PathWalkController.ReplanLimitPrefix + ":", last.Reason);
        Assert.False(walk.IsWalking);
    }

    /// <summary>Ground gained refills the budget; that is what makes the limit a limit and not a cap on effort.</summary>
    [Fact]
    public void ProgressRefillsTheBudget()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);
        OccupancyView blocked = Occupied(Now, path[1]);

        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, path[0], blocked, Now).Outcome);
        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, path[0], blocked, Now).Outcome);
        Assert.Equal(2, walk.ReplansUsed);

        // A cell closer to the destination than anything seen so far.
        walk.Next(in grid, path[4], Clear(Now), Now);

        Assert.Equal(0, walk.ReplansUsed);
    }

    /// <summary>
    /// The defect P0 found in the recovery breaker, in its navigation form: a character
    /// bouncing between two cells changes cell constantly and arrives nowhere. If that
    /// refilled the budget, the limit would never fire.
    /// </summary>
    [Fact]
    public void OscillatingIsNotProgress()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path, new ReplanPolicy(MaxConsecutiveReplans: 3));

        MapPoint a = path[0];
        MapPoint b = path[1];
        OccupancyView blockedAtA = Occupied(Now, path[1]);

        // Bounce: a, b, a, b — the cell changes every time and the best distance never does.
        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, a, blockedAtA, Now).Outcome);
        Assert.True(walk.TryAdoptReplan(in grid, path, a, out _));

        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, a, blockedAtA, Now).Outcome);
        Assert.True(walk.TryAdoptReplan(in grid, path, a, out _));

        // Stepping onto b is closer, so this one is genuine progress and resets.
        walk.Next(in grid, b, Clear(Now), Now);
        Assert.Equal(0, walk.ReplansUsed);

        // Going back to a is not.
        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, a, blockedAtA, Now).Outcome);
        Assert.Equal(1, walk.ReplansUsed);
        Assert.True(walk.TryAdoptReplan(in grid, path, a, out _));
        Assert.Equal(WalkOutcome.Replan, walk.Next(in grid, a, blockedAtA, Now).Outcome);
        Assert.Equal(2, walk.ReplansUsed);
    }

    [Fact]
    public void AdoptingAReplanDoesNotRefillTheBudget()
    {
        MapGrid grid = OpenMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 20);
        PathWalkController walk = Walk(in grid, path);

        walk.Next(in grid, path[0], Occupied(Now, path[1]), Now);
        Assert.Equal(1, walk.ReplansUsed);

        Assert.True(walk.TryAdoptReplan(in grid, path, path[0], out _));

        Assert.Equal(1, walk.ReplansUsed);
        Assert.Equal(1, walk.ReplansAdopted);
    }

    [Fact]
    public void AReplacementRouteThroughAWallIsRefusedToo()
    {
        MapGrid grid = WalledMap();
        IReadOnlyList<MapPoint> path = Line(2, 5, 19);
        PathWalkController walk = Walk(in grid, path);

        Assert.False(walk.TryAdoptReplan(in grid, Line(2, 5, 25), path[0], out string? why));
        Assert.StartsWith(PathWalkController.NotAdmittedPrefix + ":", why);
    }

    [Fact]
    public void AWalkThatDidNotStartWhereTheCharacterIsIsRefused()
    {
        MapGrid grid = OpenMap();
        var walk = new PathWalkController();

        Assert.False(walk.TryStart(in grid, Line(2, 5, 20), new MapPoint(9, 9), out string? why));
        Assert.StartsWith(PathWalkController.WrongStartPrefix + ":", why);
    }

    // ------------------------------------------------- P5's DoD, both halves

    /// <summary>
    /// Twenty routes of at least fifteen cells across three maps, planned by the real A*
    /// and walked to the destination.
    /// </summary>
    [Fact]
    public void TwentyRoutesOfFifteenCellsAcrossThreeMapsArrive()
    {
        MapGrid[] maps = { OpenMap(), WalledMap(), PillarMap() };
        var walked = 0;
        var totalCells = 0L;

        foreach (MapGrid map in maps)
        {
            MapGrid grid = map;
            for (var i = 0; i < 7; i++)
            {
                var from = new MapPoint(2, 2 + (i * 4));
                var to = new MapPoint(37, 5 + (i * 4));

                IReadOnlyList<MapPoint> path = Plan(in grid, from, to);
                Assert.True(path.Count >= 16, $"map {grid.MapId} route {i} was {path.Count} cells");

                PathWalkController walk = Walk(in grid, path);
                MapPoint at = path[0];

                for (var guard = 0; guard < 400; guard++)
                {
                    WalkDecision decision = walk.Next(in grid, at, Clear(Now), Now);
                    if (decision.Outcome == WalkOutcome.Arrived)
                        break;

                    Assert.Equal(WalkOutcome.Stepping, decision.Outcome);
                    at = decision.StepTo!.Value;
                    walk.NoteStepOutcome(Arrived(at));
                }

                Assert.Equal(to, at);
                totalCells += walk.CellsAdvanced;
                walked++;
            }
        }

        Assert.Equal(21, walked);
        _output.WriteLine($"21 routes across 3 maps, {totalCells} cells advanced, 0 replans.");
    }

    /// <summary>
    /// The other half, and the one that matters: a route across a blocked cell reaches the
    /// input backend not at all. Proved against the real executor rather than by reading
    /// the controller, because "no input" is a claim about what left the process.
    /// </summary>
    [Fact]
    public void ARouteThroughABlockedCellEmitsNoInputAtAll()
    {
        MapGrid[] maps = { WalledMap(), PillarMap() };

        foreach (MapGrid map in maps)
        {
            MapGrid grid = map;
            var recorder = new RecordingInputBackend();
            RuntimeSafetyPolicy armed = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };
            var gate = new GatedInputBackend(recorder, () => armed);

            var chain = new StepGuardChain(
                sessionAuthority: () => null,
                policySource: () => armed,
                projection: new StraightProjection());

            var executor = new SingleStepExecutor(
                chain, gate, () => 0x9000, readGeometry: StatedGeometry);

            // A straight line through the wall on map 2, and through a pillar on map 3.
            IReadOnlyList<MapPoint> through = grid.MapId == 2 ? Line(15, 5, 25) : Line(2, 4, 20);
            var walk = new PathWalkController();

            Assert.False(walk.TryStart(in grid, through, through[0], out string? why));
            Assert.StartsWith(PathWalkController.NotAdmittedPrefix + ":", why);

            // The walk never yields a step, so the executor is never reached. Driving the
            // loop anyway is the point: if a refused walk ever produced one, this is where
            // it would show.
            for (var i = 0; i < 10; i++)
            {
                WalkDecision decision = walk.Next(in grid, through[0], Clear(Now), Now);
                Assert.Equal(WalkOutcome.Abandoned, decision.Outcome);

                if (decision.Outcome == WalkOutcome.Stepping)
                {
                    executor.Step(
                        new StepRequest(through[0], decision.StepTo!.Value, grid, Clear(Now), Now),
                        ActuationAuthority.Commanded("--walk"),
                        () => null);
                }
            }

            Assert.Empty(recorder.Events);
        }
    }

    private sealed class StraightProjection : IScreenProjection
    {
        public GeometryShape Scale => new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 100 + (mapX * 8);
            screenY = 100 + (mapY * 8);
            failureReason = null;
            return true;
        }
    }

    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    // ------------------------------------------- what the JPS question needs measured

    /// <summary>
    /// The measurement the roadmap makes the condition for touching A*: Jump Point Search
    /// is evaluated <b>only</b> if continuous revalidation measures a cost.
    /// </summary>
    /// <remarks>
    /// It does not, and the assertion below is what makes that falsifiable rather than an
    /// opinion. Revalidating a segment is a handful of array reads and one pass over the
    /// entities list; planning is a search over the map. If revalidation ever came to
    /// dominate, this test fails and the JPS question reopens on evidence.
    /// </remarks>
    [Fact]
    public void RevalidationDoesNotDominatePlanning()
    {
        MapGrid grid = PillarMap();
        var from = new MapPoint(2, 2);
        var to = new MapPoint(37, 37);

        IReadOnlyList<MapPoint> path = Plan(in grid, from, to);
        Assert.True(path.Count >= 16);

        // Warm both paths so the measurement is not of the first JIT.
        _ = Plan(in grid, from, to);
        for (var i = 0; i + 1 < path.Count; i++)
            _ = PathRevalidation.Revalidate(in grid, path[i], path[i + 1], Clear(Now), Now);

        const int Rounds = 50;

        long planStart = Stopwatch.GetTimestamp();
        for (var r = 0; r < Rounds; r++)
            _ = Plan(in grid, from, to);
        TimeSpan planning = Stopwatch.GetElapsedTime(planStart);

        long revalStart = Stopwatch.GetTimestamp();
        for (var r = 0; r < Rounds; r++)
            for (var i = 0; i + 1 < path.Count; i++)
                _ = PathRevalidation.Revalidate(in grid, path[i], path[i + 1], Clear(Now), Now);
        TimeSpan revalidating = Stopwatch.GetElapsedTime(revalStart);

        double perPlan = planning.TotalMicroseconds / Rounds;
        double perWalk = revalidating.TotalMicroseconds / Rounds;

        _output.WriteLine(
            $"path {path.Count} cells | one plan {perPlan:F1} us | revalidating the whole path {perWalk:F1} us "
            + $"| ratio {perWalk / perPlan:F3}");

        // Revalidating every cell of the route costs less than planning it once. Jump Point
        // Search speeds up planning, so on this evidence it would be optimising the half
        // that is already cheap.
        Assert.True(
            perWalk < perPlan,
            $"revalidation ({perWalk:F1} us) has come to cost more than planning ({perPlan:F1} us); "
            + "the Jump Point Search question reopens");
    }
}
