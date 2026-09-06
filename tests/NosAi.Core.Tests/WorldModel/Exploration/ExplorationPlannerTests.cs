using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Exploration;

public sealed class ExplorationPlannerTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly DateTime FixedInstant = DateTime.UnixEpoch;

    private static MapModel BuildMap(params (int Column, int Row, TileTraversability Traversability)[] tiles)
    {
        var built = new Tile[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            (int column, int row, TileTraversability traversability) = tiles[i];
            built[i] = new Tile(new TileCoordinate(column, row), WorldFact<TileTraversability>.Cached(traversability, 1d, FixedInstant));
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

    private static Mob BuildHostileMob(WorldPosition position) =>
        new(
            new EntityId("mob-1"),
            WorldFact<WorldPosition>.Live(position, 1d, FixedInstant),
            WorldFact<string>.Live("wolf", 1d, FixedInstant),
            WorldFact<bool>.Live(true, 1d, FixedInstant),
            WorldFact<bool>.Live(true, 1d, FixedInstant),
            CombatantStatus.Empty);

    // ---- UpdateFootprint ----

    [Fact]
    public void UpdateFootprint_NewPosition_AddsItsTile()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Live(new WorldPosition(0f, 0f), 1d, FixedInstant), FixedInstant);

        Assert.Contains(new TileCoordinate(0, 0), updated.VisitedTiles);
    }

    [Fact]
    public void UpdateFootprint_AlreadyVisitedTile_DoesNotDuplicate()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint previous = ExplorationPlanner.UpdateFootprint(
            ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant),
            map,
            WorldFact<WorldPosition>.Live(new WorldPosition(0f, 0f), 1d, FixedInstant),
            FixedInstant);

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Live(new WorldPosition(0.4f, 0.4f), 1d, FixedInstant), FixedInstant);

        Assert.Single(updated.VisitedTiles);
    }

    [Fact]
    public void UpdateFootprint_UnknownPosition_LeavesVisitedTilesUnchanged()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Unknown("player_position_unknown", FixedInstant), FixedInstant);

        Assert.Empty(updated.VisitedTiles);
    }

    [Fact]
    public void UpdateFootprint_NoTilesInMap_FullyExploredIsUnknown()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel map = MapModel.Unknown(TestMapId, "map_not_reconstructed", FixedInstant);

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Unknown("player_position_unknown", FixedInstant), FixedInstant);

        Assert.False(updated.FullyExplored.HasValue);
    }

    [Fact]
    public void UpdateFootprint_UnvisitedWalkableTileRemains_FullyExploredIsFalse()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (1, 0, TileTraversability.Walkable));

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Live(new WorldPosition(0f, 0f), 1d, FixedInstant), FixedInstant);

        Assert.True(updated.FullyExplored.HasValue);
        Assert.False(updated.FullyExplored.Value);
    }

    [Fact]
    public void UpdateFootprint_EveryWalkableTileVisited_FullyExploredIsTrue()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (1, 0, TileTraversability.Blocked));

        ExplorationFootprint updated = ExplorationPlanner.UpdateFootprint(
            previous, map, WorldFact<WorldPosition>.Live(new WorldPosition(0f, 0f), 1d, FixedInstant), FixedInstant);

        Assert.True(updated.FullyExplored.HasValue);
        Assert.True(updated.FullyExplored.Value);
    }

    [Fact]
    public void UpdateFootprint_NothingChanged_ReturnsSameInstance()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint first = ExplorationPlanner.UpdateFootprint(
            ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant),
            map,
            WorldFact<WorldPosition>.Live(new WorldPosition(0f, 0f), 1d, FixedInstant),
            FixedInstant);

        ExplorationFootprint second = ExplorationPlanner.UpdateFootprint(
            first, map, WorldFact<WorldPosition>.Unknown("player_position_unknown", FixedInstant), FixedInstant);

        Assert.Same(first, second);
    }

    [Fact]
    public void UpdateFootprint_MapMismatch_Throws()
    {
        ExplorationFootprint previous = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", FixedInstant);
        MapModel otherMap = new MapModel(
            new MapId("map-2"),
            WorldFact<string>.Unknown("r", FixedInstant),
            WorldFact<MapBounds>.Unknown("r", FixedInstant),
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 0,
            FixedInstant);

        Assert.Throws<ArgumentException>(() => ExplorationPlanner.UpdateFootprint(
            previous, otherMap, WorldFact<WorldPosition>.Unknown("r", FixedInstant), FixedInstant));
    }

    // ---- BuildFrontierCandidates ----

    [Fact]
    public void BuildFrontierCandidates_ExcludesVisitedTiles()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable), (5, 5, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant) with
        {
            VisitedTiles = EquatableArray<TileCoordinate>.From(new[] { new TileCoordinate(0, 0) })
        };

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty);

        Assert.Single(candidates);
        Assert.Equal(new TileCoordinate(5, 5), candidates[0].Coordinate);
    }

    [Fact]
    public void BuildFrontierCandidates_ExcludesBlockedTiles()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Blocked));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty);

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildFrontierCandidates_NoTilesInMap_ReturnsEmpty()
    {
        MapModel map = MapModel.Unknown(TestMapId, "r", FixedInstant);
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty);

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildFrontierCandidates_InformationGain_CountsNearbyUnvisitedWalkableTiles()
    {
        MapModel map = BuildMap(
            (0, 0, TileTraversability.Walkable),
            (1, 0, TileTraversability.Walkable),
            (0, 1, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty);

        FrontierCandidate origin = Assert.Single(candidates, c => c.Coordinate == new TileCoordinate(0, 0));
        Assert.Equal(2d, origin.InformationGain);
    }

    [Fact]
    public void BuildFrontierCandidates_HostileMobNearby_ContributesRisk()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);
        EquatableArray<Mob> mobs = EquatableArray<Mob>.From(new[] { BuildHostileMob(new WorldPosition(0f, 0f)) });

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), mobs);

        Assert.True(candidates[0].Risk > 0d);
    }

    [Fact]
    public void BuildFrontierCandidates_DeadMobNearby_ContributesNoRisk()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);
        Mob deadMob = BuildHostileMob(new WorldPosition(0f, 0f)) with { IsAlive = WorldFact<bool>.Live(false, 1d, FixedInstant) };
        EquatableArray<Mob> mobs = EquatableArray<Mob>.From(new[] { deadMob });

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), mobs);

        Assert.Equal(0d, candidates[0].Risk);
    }

    [Fact]
    public void BuildFrontierCandidates_NoMissionFocus_MissionRelevanceIsZero()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty, missionFocus: null);

        Assert.Equal(0d, candidates[0].MissionRelevance);
    }

    [Fact]
    public void BuildFrontierCandidates_MissionFocusFar_MissionRelevanceIsNegative()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", FixedInstant);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty, missionFocus: new WorldPosition(10f, 0f));

        Assert.True(candidates[0].MissionRelevance < 0d);
    }

    [Fact]
    public void BuildFrontierCandidates_MapMismatch_Throws()
    {
        MapModel map = BuildMap((0, 0, TileTraversability.Walkable));
        ExplorationFootprint footprint = ExplorationFootprint.Empty(new MapId("map-2"), "r", FixedInstant);

        Assert.Throws<ArgumentException>(() => ExplorationPlanner.BuildFrontierCandidates(
            map, footprint, new WorldPosition(0f, 0f), EquatableArray<Mob>.Empty));
    }

    // ---- SelectNextFrontier ----

    [Fact]
    public void SelectNextFrontier_EmptyList_ReturnsNull()
    {
        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(Array.Empty<FrontierCandidate>());

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNextFrontier_PicksHighestScoringCandidate()
    {
        var low = new FrontierCandidate(TestMapId, new TileCoordinate(0, 0), InformationGain: 1d, TravelCost: 0d, Risk: 0d, MissionRelevance: 0d);
        var high = new FrontierCandidate(TestMapId, new TileCoordinate(1, 0), InformationGain: 10d, TravelCost: 0d, Risk: 0d, MissionRelevance: 0d);

        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(new[] { low, high });

        Assert.Equal(high, selected);
    }

    [Fact]
    public void SelectNextFrontier_TiedScores_PicksTheFirstOne()
    {
        var first = new FrontierCandidate(TestMapId, new TileCoordinate(0, 0), InformationGain: 1d, TravelCost: 0d, Risk: 0d, MissionRelevance: 0d);
        var second = new FrontierCandidate(TestMapId, new TileCoordinate(1, 0), InformationGain: 1d, TravelCost: 0d, Risk: 0d, MissionRelevance: 0d);

        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(new[] { first, second });

        Assert.Equal(first, selected);
    }

    [Fact]
    public void SelectNextFrontier_HighRiskWeight_PrefersTheSaferCandidate()
    {
        var risky = new FrontierCandidate(TestMapId, new TileCoordinate(0, 0), InformationGain: 5d, TravelCost: 0d, Risk: 10d, MissionRelevance: 0d);
        var safe = new FrontierCandidate(TestMapId, new TileCoordinate(1, 0), InformationGain: 4d, TravelCost: 0d, Risk: 0d, MissionRelevance: 0d);
        var weights = new FrontierScoringWeights(InformationGain: 1d, TravelCost: 0d, Risk: 5d, MissionRelevance: 0d);

        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(new[] { risky, safe }, weights);

        Assert.Equal(safe, selected);
    }

    // ---- BuildNavigationPlan ----

    [Fact]
    public void BuildNavigationPlan_NoCandidate_ReturnsUnreachable()
    {
        NavigationPlan plan = ExplorationPlanner.BuildNavigationPlan(TestMapId, selected: null, FixedInstant);

        Assert.False(plan.IsReachable.HasValue);
        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void BuildNavigationPlan_WithCandidate_ProducesSingleSameMapWaypoint()
    {
        var candidate = new FrontierCandidate(TestMapId, new TileCoordinate(3, 4), 1d, 1d, 0d, 0d);

        NavigationPlan plan = ExplorationPlanner.BuildNavigationPlan(TestMapId, candidate, FixedInstant);

        NavigationWaypoint waypoint = Assert.Single(plan.Waypoints);
        Assert.Equal(TestMapId, waypoint.MapId);
        Assert.Null(waypoint.UsePortal);
        Assert.Equal(new WorldPosition(3f, 4f), waypoint.Position);
        Assert.True(plan.IsReachable.HasValue);
        Assert.True(plan.IsReachable.Value);
    }

    [Fact]
    public void BuildNavigationPlan_CandidateMapMismatch_Throws()
    {
        var candidate = new FrontierCandidate(new MapId("map-2"), new TileCoordinate(0, 0), 1d, 1d, 0d, 0d);

        Assert.Throws<ArgumentException>(() => ExplorationPlanner.BuildNavigationPlan(TestMapId, candidate, FixedInstant));
    }
}
