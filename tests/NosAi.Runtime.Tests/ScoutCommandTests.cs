using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-04/A4: <see cref="ScoutCommand.ExecuteOneRound"/> -- one round of frontier
/// scouting against the <c>WalkCommandTests</c> rig style (<c>WalkRig</c>/
/// <c>ChainRig</c>/<c>RecordingInputBackend</c>/<c>Fast</c>
/// <see cref="MovementVerifier"/>), proving: a reachable frontier is walked with
/// evidence per emitted step in order; a fully explored map returns
/// <see langword="null"/> with an unreachable plan and never calls
/// <see cref="WalkCommand.Execute"/> at all (the recorder sees zero emitted
/// input); a map with no reconstructed tiles behaves the same way, honestly.
/// </summary>
/// <remarks>
/// These tests drive the planner with real <see cref="MapModel"/>s built the same
/// way AP-03 reconstructs them (Cached traversability tiles), and let the walk
/// actually arrive by serving readings that follow the cells the rig's projection
/// aimed the clicks at -- the same "the client resolves the click to a square"
/// technique the real calibration uses. They never touch the network, client
/// memory or an input backend: everything I/O-shaped is the rig.
/// </remarks>
public sealed class ScoutCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7300;
    private static readonly ActuationAuthority ScoutAuthority = ActuationAuthority.Commanded(ScoutCommand.Flag);
    private static readonly MapId TestMapId = new("map-1");

    // ------------------------------------------------------------------ maps

    /// <summary>One Cached-walkable tile per cell, exactly like AP-03's projector produces them.</summary>
    private static MapModel MapOf(params TileCoordinate[] walkable)
    {
        var tiles = new Tile[walkable.Length];
        for (int i = 0; i < walkable.Length; i++)
        {
            tiles[i] = new Tile(
                walkable[i],
                WorldFact<TileTraversability>.Cached(TileTraversability.Walkable, 1d, Now, reason: "client-map-grid"));
        }

        return new MapModel(
            TestMapId,
            WorldFact<string>.Unknown("map_name_catalog_not_available", Now),
            WorldFact<MapBounds>.Unknown("map_bounds_not_yet_reconstructed", Now),
            EquatableArray<Tile>.From(tiles),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 1,
            Now);
    }

    private static ExplorationFootprint Footprint(params TileCoordinate[] visited) =>
        new(
            TestMapId,
            EquatableArray<TileCoordinate>.From(visited),
            WorldFact<bool>.Unknown("not_yet_evaluated", Now),
            Now);

    /// <summary>Open ground, 10x10 -- the same shape <c>WalkCommandTests</c> walks on.</summary>
    private static MapGrid OpenMap() => new(mapId: 1, width: 10, height: 10, new byte[100]);

    private static OccupancyView FreshView() =>
        new(Array.Empty<SelectableEntity>(), Now);

    // ------------------------------------------------------------------ rig

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

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public bool Works { get; set; } = true;
        public string? Failure { get; set; }
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = Works ? null : Failure ?? "projection_refused";
            return Works;
        }
    }

    private sealed class WalkRig
    {
        public ChainRig Chain { get; } = new();
        public RecordingInputBackend Recorder { get; } = new();
        public PathWalkController Controller { get; } = new();

        public GatedInputBackend Gate() => new(
            Recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Chain.Armed });
    }

    private static readonly MovementVerifier Fast =
        new(window: TimeSpan.FromMilliseconds(60), tolerance: TimeSpan.FromMilliseconds(10),
            pollInterval: TimeSpan.FromMilliseconds(2));

    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    /// <summary>
    /// Serves position readings that follow the walk. The rig's projection aims a
    /// click at <c>100+mapX*32, 200+mapY*16</c>; the recording backend keeps the
    /// <c>move-absolute</c> events, and a real client would resolve each click back
    /// to the square the projection aimed at. So the reading decodes the last
    /// emitted move back into the map cell the character now stands on, exactly as
    /// <see cref="CalibratedScreenProjection"/>'s documented round trip works.
    /// </summary>
    private sealed class FollowingPosition
    {
        private readonly RecordingInputBackend _recorder;
        private MapPoint _current;

        public FollowingPosition(MapPoint start, RecordingInputBackend recorder)
        {
            _current = start;
            _recorder = recorder;
        }

        public PositionReading? Read()
        {
            foreach (string last in _recorder.Events.Reverse())
            {
                if (last.StartsWith("move-absolute:", StringComparison.Ordinal))
                {
                    string[] parts = last["move-absolute:".Length..].Split(',');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int x)
                        && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int y))
                    {
                        _current = new MapPoint((x - 100) / 32, (y - 200) / 16);
                        break;
                    }
                }
            }

            return new PositionReading(_current, Now.AddYears(1), NosAi.Runtime.Contracts.DataSourceKind.Live);
        }
    }

    private sealed record RoundOutcome(
        WalkRun? Run,
        List<MovementExecutionEvidence> Evidence,
        RecordingInputBackend Recorder,
        ExplorationFootprint UpdatedFootprint,
        NavigationPlan Plan);

    private static RoundOutcome RunRound(
        MapModel map,
        ExplorationFootprint footprint,
        MapPoint origin,
        MapGrid? grid = null)
    {
        var rig = new WalkRig();
        var evidence = new List<MovementExecutionEvidence>();
        var follower = new FollowingPosition(origin, rig.Recorder);
        StepGuardChain chain = rig.Chain.Build();
        SingleStepExecutor executor = new(
            chain, rig.Gate(), () => Session, Fast, readGeometry: StatedGeometry);
        MapGrid effectiveGrid = grid ?? OpenMap();

        WalkRun? run = ScoutCommand.ExecuteOneRound(
            map,
            footprint,
            new WorldPosition(origin.X, origin.Y),
            EquatableArray<Mob>.Empty,
            origin,
            in effectiveGrid,
            FreshView(),
            rig.Controller,
            chain,
            executor,
            in ScoutAuthority,
            follower.Read,
            evidence.Add,
            out ExplorationFootprint updatedFootprint,
            out NavigationPlan plan,
            Now);

        return new RoundOutcome(run, evidence, rig.Recorder, updatedFootprint, plan);
    }

    // ------------------------------------------------------- reachable frontier

    [Fact]
    public void ReachableFrontier_WalksAndReportsEvidenceForEachEmittedStepInOrder()
    {
        // Walkable line from (1,1) to (6,1); the player has only ever visited
        // (1,1) and (2,1), so (3,1) is the highest-scoring unvisited frontier
        // (every candidate here has equal information gain and the nearest wins)
        // and the walk takes the one adjacent step onto it.
        MapModel map = MapOf(
            new TileCoordinate(1, 1), new TileCoordinate(2, 1), new TileCoordinate(3, 1),
            new TileCoordinate(4, 1), new TileCoordinate(5, 1), new TileCoordinate(6, 1));
        ExplorationFootprint footprint = Footprint(new TileCoordinate(1, 1), new TileCoordinate(2, 1));
        var origin = new MapPoint(2, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.NotNull(outcome.Run);
        Assert.Equal(WalkCommand.ExitArrived, outcome.Run!.Value.ExitCode);
        Assert.Equal(nameof(WalkOutcome.Arrived), outcome.Run.Value.StoppedBecause);
        Assert.Equal(1, outcome.Run.Value.StepsEmitted);
        Assert.Equal(1, outcome.Run.Value.StepsSucceeded);
        Assert.NotEmpty(outcome.Recorder.Events);

        // The one emitted step was the walk from (2,1) onto the frontier (3,1),
        // and its evidence names exactly that requested cell.
        MovementExecutionEvidence single = Assert.Single(outcome.Evidence);
        Assert.Equal(new TileCoordinate(3, 1), single.Requested);
        Assert.Equal(MovementExecutionResult.Succeeded, single.Result);
        Assert.True(single.Observed.HasValue);
        Assert.Equal(new TileCoordinate(3, 1), single.Observed.Value);
        Assert.Equal(TestMapId, single.MapId);
        Assert.Equal(Now, single.ObservedAtUtc);
    }

    [Fact]
    public void DistantFrontier_WalksEveryCell_EmittingOneEvidencePerStepInOrder()
    {
        // Only (6,1) is still unvisited, so it is the sole candidate: the walk
        // must step through every cell between the player at (2,1) and it, and
        // each emitted step reports its evidence in the order it was emitted.
        MapModel map = MapOf(
            new TileCoordinate(2, 1), new TileCoordinate(3, 1), new TileCoordinate(4, 1),
            new TileCoordinate(5, 1), new TileCoordinate(6, 1));
        ExplorationFootprint footprint =
            Footprint(new TileCoordinate(2, 1), new TileCoordinate(3, 1), new TileCoordinate(4, 1), new TileCoordinate(5, 1));
        var origin = new MapPoint(2, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.NotNull(outcome.Run);
        Assert.Equal(WalkCommand.ExitArrived, outcome.Run!.Value.ExitCode);
        Assert.Equal(4, outcome.Run.Value.StepsEmitted);
        Assert.Equal(4, outcome.Run.Value.StepsSucceeded);

        Assert.Equal(4, outcome.Evidence.Count);
        Assert.Equal(new TileCoordinate(3, 1), outcome.Evidence[0].Requested);
        Assert.Equal(new TileCoordinate(4, 1), outcome.Evidence[1].Requested);
        Assert.Equal(new TileCoordinate(5, 1), outcome.Evidence[2].Requested);
        Assert.Equal(new TileCoordinate(6, 1), outcome.Evidence[3].Requested);
        Assert.All(outcome.Evidence, e => Assert.Equal(MovementExecutionResult.Succeeded, e.Result));
        Assert.All(outcome.Evidence, e => Assert.Equal(TestMapId, e.MapId));
    }

    [Fact]
    public void TheUpdatedFootprint_FoldsThePlayersPositionIn_ForTheNextRound()
    {
        // The footprint is updated from the player's observed position at the start
        // of the round: a tile the player is seen standing on is marked visited even
        // when the footprint did not have it yet. The walked-to frontier is not
        // marked until a later round observes the player standing on it -- scouting
        // never claims a tile was visited before the player was seen there
        // (ExplorationPlanner.UpdateFootprint's own discipline).
        MapModel map = MapOf(
            new TileCoordinate(1, 1), new TileCoordinate(2, 1), new TileCoordinate(3, 1));
        ExplorationFootprint footprint = Footprint(new TileCoordinate(1, 1));
        var origin = new MapPoint(2, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.NotNull(outcome.Run);
        Assert.Equal(WalkCommand.ExitArrived, outcome.Run!.Value.ExitCode);
        Assert.Equal(1, outcome.Run.Value.StepsEmitted);
        Assert.Contains(new TileCoordinate(1, 1), outcome.UpdatedFootprint.VisitedTiles);
        Assert.Contains(new TileCoordinate(2, 1), outcome.UpdatedFootprint.VisitedTiles);
        Assert.DoesNotContain(new TileCoordinate(3, 1), outcome.UpdatedFootprint.VisitedTiles);
        Assert.Equal(TestMapId, outcome.UpdatedFootprint.MapId);
    }

    // ---------------------------------------------------------- nothing to scout

    [Fact]
    public void FullyExploredMap_ReturnsNullWithAnUnreachablePlan_AndNeverCallsWalkCommand()
    {
        MapModel map = MapOf(new TileCoordinate(1, 1), new TileCoordinate(2, 1));
        ExplorationFootprint footprint = Footprint(new TileCoordinate(1, 1), new TileCoordinate(2, 1));
        var origin = new MapPoint(1, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.Null(outcome.Run);
        Assert.Empty(outcome.Evidence);
        Assert.Empty(outcome.Recorder.Events); // WalkCommand.Execute was never reached
        Assert.False(outcome.Plan.IsReachable.HasValue);
        Assert.Empty(outcome.Plan.Waypoints);
    }

    [Fact]
    public void MapWithNoReconstructedTiles_ReturnsNullWithAnUnreachablePlan_AndEmitsNothing()
    {
        MapModel map = MapModel.Unknown(TestMapId, "map_not_reconstructed", Now);
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "scout_command_session_start", Now);
        var origin = new MapPoint(1, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.Null(outcome.Run);
        Assert.Empty(outcome.Evidence);
        Assert.Empty(outcome.Recorder.Events);
        Assert.False(outcome.Plan.IsReachable.HasValue);
        Assert.Empty(outcome.Plan.Waypoints);
    }

    // ------------------------------------------------- a frontier that cannot be walked

    [Fact]
    public void UnreachableDestination_WalkReturnsNoPath_AndNoStepIsEverEmitted()
    {
        // An unvisited walkable tile exists on the map model, but the client's own
        // grid walls off every route to it. The round still hands the frontier to
        // WalkCommand.Execute (the plan's reachability is about whether a frontier
        // was found, not about the path), and the walk answers honestly: no path,
        // no step, no evidence -- never a fabricated arrival.
        MapModel map = MapOf(new TileCoordinate(0, 0), new TileCoordinate(8, 0));
        ExplorationFootprint footprint = Footprint(new TileCoordinate(0, 0));
        var origin = new MapPoint(0, 0);

        // Column 1..7 walled on a single-row grid: (0,0) is open, (8,0) exists
        // and is open, and no route connects them.
        var cells = new byte[10];
        for (int x = 1; x <= 7; x++)
            cells[x] = (byte)MapCellFlags.WalkBlocked;
        var grid = new MapGrid(mapId: 1, width: 10, height: 1, cells);

        RoundOutcome outcome = RunRound(map, footprint, origin, grid);

        Assert.NotNull(outcome.Run);
        Assert.Equal(WalkCommand.ExitNoPath, outcome.Run!.Value.ExitCode);
        Assert.Empty(outcome.Evidence);
        Assert.Empty(outcome.Recorder.Events);
        Assert.Equal(0, outcome.Run.Value.StepsEmitted);
    }

    // ------------------------------------------------- footprint bookkeeping

    [Fact]
    public void ExecuteOneRound_FoldsThePlayersPositionIntoTheUpdatedFootprint()
    {
        // The players' standing tile at round start is folded into the footprint
        // even when it began empty. (The walked-to frontier is only marked once a
        // later round observes the player there.)
        MapModel map = MapOf(new TileCoordinate(1, 1), new TileCoordinate(2, 1));
        ExplorationFootprint footprint = Footprint();
        var origin = new MapPoint(1, 1);

        RoundOutcome outcome = RunRound(map, footprint, origin);

        Assert.NotNull(outcome.Run);
        Assert.Equal(WalkCommand.ExitArrived, outcome.Run!.Value.ExitCode);
        Assert.Contains(new TileCoordinate(1, 1), outcome.UpdatedFootprint.VisitedTiles);
        Assert.DoesNotContain(new TileCoordinate(2, 1), outcome.UpdatedFootprint.VisitedTiles);
    }

    // ------------------------------------------------------------ the wiring

    [Fact]
    public void TheRuntimeWiresTheScoutFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(ScoutCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("ScoutCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + ScoutCommand.Flag + "\"", program, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
