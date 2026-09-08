using System;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Exploration;

/// <summary>
/// An empty frontier list cannot tell a map never observed from one walked end to end, nor
/// either of those from a map whose remaining tiles are wall. The three demand different
/// behaviour — look first, leave, or stop trying to reach the unreachable — so the planner
/// has to say which one it means.
/// </summary>
public sealed class ExplorationVerdictTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly MapId OtherMapId = new("map-2");
    private static readonly DateTime FixedInstant = DateTime.UnixEpoch;

    [Fact]
    public void AMapWithNoObservedTile_IsUnknownNotExplored()
    {
        ExplorationVerdict verdict = ExplorationPlanner.ExplainFrontier(BuildMap(), Footprint());

        Assert.Equal(ExplorationVerdict.MapUnknown, verdict);
    }

    [Fact]
    public void AnUnvisitedWalkableTile_IsAFrontier()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));

        Assert.Equal(ExplorationVerdict.FrontierAvailable, ExplorationPlanner.ExplainFrontier(map, Footprint()));
    }

    [Fact]
    public void EveryTileVisited_IsFullyExplored()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (1, 0, TileTraversability.Walkable));

        ExplorationVerdict verdict = ExplorationPlanner.ExplainFrontier(
            map,
            Footprint(new TileCoordinate(0, 0), new TileCoordinate(1, 0)));

        Assert.Equal(ExplorationVerdict.FullyExplored, verdict);
    }

    /// <summary>
    /// What is left being wall is a different statement from having covered the map, and the
    /// player must not be told it finished exploring something it never could.
    /// </summary>
    [Fact]
    public void UnvisitedButBlockedTiles_AreNotReachableRatherThanExplored()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (1, 0, TileTraversability.Blocked));

        ExplorationVerdict verdict = ExplorationPlanner.ExplainFrontier(
            map,
            Footprint(new TileCoordinate(0, 0)));

        Assert.Equal(ExplorationVerdict.NoReachableFrontier, verdict);
    }

    [Fact]
    public void AFootprintFromAnotherMap_IsRefused()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        var foreign = new ExplorationFootprint(
            OtherMapId,
            EquatableArray<TileCoordinate>.Empty,
            WorldFact<bool>.Unknown("not_observed", FixedInstant),
            FixedInstant);

        Assert.Throws<ArgumentException>(() => ExplorationPlanner.ExplainFrontier(map, foreign));
    }

    /// <summary>
    /// The footprint's own FullyExplored flag can sit Unknown while the tiles already answer the
    /// question. Judging by the tiles means an unset flag no longer costs the whole signal.
    /// </summary>
    [Fact]
    public void AnUnsetFullyExploredFlag_DoesNotSilenceTheSignal()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));

        StrategicSignal? signal = StrategyPlanner.AssessExplorationUrgency(map, Footprint());

        Assert.NotNull(signal);
        Assert.Equal(1.0, signal!.Urgency);
        Assert.Equal("frontier_available", signal.Reason);
    }

    [Fact]
    public void AMapWhoseRemainderIsWall_ScoresZeroForItsOwnReason()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (1, 0, TileTraversability.Blocked));

        StrategicSignal? signal = StrategyPlanner.AssessExplorationUrgency(
            map,
            Footprint(new TileCoordinate(0, 0)));

        Assert.NotNull(signal);
        Assert.Equal(0.0, signal!.Urgency);
        Assert.Equal("remaining_tiles_unreachable", signal.Reason);
    }

    [Fact]
    public void AMapWithNoObservedTile_ReportsNothingRatherThanNoUrgency()
    {
        Assert.Null(StrategyPlanner.AssessExplorationUrgency(BuildMap(), Footprint()));
    }

    private static ExplorationFootprint Footprint(params TileCoordinate[] visited) =>
        new(
            TestMapId,
            EquatableArray<TileCoordinate>.From(visited),
            WorldFact<bool>.Unknown("not_observed", FixedInstant),
            FixedInstant);

    private static MapModel BuildMap(params (int Column, int Row, TileTraversability Traversability)[] tiles)
    {
        var built = new Tile[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            (int column, int row, TileTraversability traversability) = tiles[i];
            built[i] = new Tile(
                new TileCoordinate(column, row),
                WorldFact<TileTraversability>.Cached(traversability, 1d, FixedInstant));
        }

        return new MapModel(
            TestMapId,
            WorldFact<string>.Unknown("not_needed_for_this_test", FixedInstant),
            WorldFact<MapBounds>.Derived(new MapBounds(new TileCoordinate(0, 0), new TileCoordinate(10, 10)), 1d, FixedInstant),
            EquatableArray<Tile>.From(built),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 1,
            FixedInstant);
    }
}
