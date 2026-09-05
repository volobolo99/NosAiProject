using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Exploration;

public sealed class ExplorationFootprintTests
{
    [Fact]
    public void Empty_HasNoVisitedTiles()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(new MapId("map-1"), "not_yet_observed");

        Assert.Empty(footprint.VisitedTiles);
    }

    [Fact]
    public void Empty_FullyExploredIsUnknown_NeverFalseByDefault()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(new MapId("map-1"), "not_yet_observed");

        Assert.False(footprint.FullyExplored.HasValue);
    }

    [Fact]
    public void Empty_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        ExplorationFootprint footprint = ExplorationFootprint.Empty(new MapId("map-1"), "reason", fixedInstant);

        Assert.Equal(fixedInstant, footprint.ObservedAtUtc);
        Assert.Equal(fixedInstant, footprint.FullyExplored.ObservedAtUtc);
    }

    [Fact]
    public void Empty_CalledTwiceWithSameInstant_ProducesEqualFootprints()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        var id = new MapId("map-1");

        ExplorationFootprint first = ExplorationFootprint.Empty(id, "reason", fixedInstant);
        ExplorationFootprint second = ExplorationFootprint.Empty(id, "reason", fixedInstant);

        Assert.Equal(first, second);
    }
}

public sealed class FrontierCandidateTests
{
    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var id = new MapId("map-1");
        var coordinate = new TileCoordinate(3, 4);

        var first = new FrontierCandidate(id, coordinate, InformationGain: 2.0, TravelCost: 5.0, Risk: 0.5, MissionRelevance: 1.0);
        var second = new FrontierCandidate(id, coordinate, InformationGain: 2.0, TravelCost: 5.0, Risk: 0.5, MissionRelevance: 1.0);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RecordEquality_DifferentCoordinate_NotEqual()
    {
        var id = new MapId("map-1");

        var first = new FrontierCandidate(id, new TileCoordinate(0, 0), 1.0, 1.0, 0.0, 0.0);
        var second = new FrontierCandidate(id, new TileCoordinate(1, 0), 1.0, 1.0, 0.0, 0.0);

        Assert.NotEqual(first, second);
    }
}

public sealed class NavigationPlanTests
{
    [Fact]
    public void Unreachable_HasNoWaypoints()
    {
        NavigationPlan plan = NavigationPlan.Unreachable("no_route_found");

        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void Unreachable_IsReachableIsUnknown()
    {
        NavigationPlan plan = NavigationPlan.Unreachable("no_route_found");

        Assert.False(plan.IsReachable.HasValue);
    }

    [Fact]
    public void Unreachable_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        NavigationPlan plan = NavigationPlan.Unreachable("reason", fixedInstant);

        Assert.Equal(fixedInstant, plan.ObservedAtUtc);
        Assert.Equal(fixedInstant, plan.IsReachable.ObservedAtUtc);
    }

    [Fact]
    public void Waypoint_UsePortal_IsNullByDefault_ForSameMapWaypoints()
    {
        var waypoint = new NavigationWaypoint(new MapId("map-1"), new WorldPosition(1f, 2f), UsePortal: null);

        Assert.Null(waypoint.UsePortal);
    }

    [Fact]
    public void Waypoint_UsePortal_CanCarryAMapTransition()
    {
        var portalId = new PortalId("portal-1");
        var waypoint = new NavigationWaypoint(new MapId("map-1"), new WorldPosition(1f, 2f), portalId);

        Assert.Equal(portalId, waypoint.UsePortal);
    }

    [Fact]
    public void RecordEquality_ComparesWaypointsStructurally()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        var waypoints = EquatableArray<NavigationWaypoint>.From(new[]
        {
            new NavigationWaypoint(new MapId("map-1"), new WorldPosition(0f, 0f), null),
            new NavigationWaypoint(new MapId("map-2"), new WorldPosition(5f, 5f), null),
        });

        var first = new NavigationPlan(waypoints, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);
        var second = new NavigationPlan(waypoints, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);

        Assert.Equal(first, second);
    }
}
