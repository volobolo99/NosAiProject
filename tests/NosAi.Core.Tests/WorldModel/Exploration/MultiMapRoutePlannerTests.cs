using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Exploration;

public sealed class MultiMapRoutePlannerTests
{
    private static readonly DateTime At = DateTime.UnixEpoch;

    private static Portal UsablePortal(string id, MapId sourceMap, WorldPosition sourcePosition, MapId destinationMap) =>
        new(
            new PortalId(id),
            sourceMap,
            WorldFact<WorldPosition>.Live(sourcePosition, 1d, At),
            WorldFact<MapId>.Live(destinationMap, 1d, At),
            WorldFact<bool>.Live(true, 1d, At));

    private static MapModel MapWithPortals(MapId id, params Portal[] portals) => new(
        id,
        WorldFact<string>.Unknown("not_needed_for_this_test", At),
        WorldFact<MapBounds>.Unknown("not_needed_for_this_test", At),
        EquatableArray<Tile>.Empty,
        EquatableArray<Portal>.From(portals),
        EquatableArray<Polygon>.Empty,
        Version: 1,
        At);

    [Fact]
    public void SameStartAndDestinationMap_ReturnsASingleWaypointPlan_NoPortalNeeded()
    {
        var map = new MapId("map-1");
        var destination = new WorldPosition(10, 20);

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            new Dictionary<MapId, MapModel>(),
            map, new WorldPosition(0, 0),
            map, destination,
            At);

        Assert.True(plan.IsReachable.HasValue);
        Assert.True(plan.IsReachable.Value);
        NavigationWaypoint waypoint = Assert.Single(plan.Waypoints);
        Assert.Equal(map, waypoint.MapId);
        Assert.Equal(destination, waypoint.Position);
        Assert.Null(waypoint.UsePortal);
    }

    [Fact]
    public void OneKnownPortal_PlansADirectTwoWaypointRoute()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        Portal portal = UsablePortal("p-ab", mapA, new WorldPosition(5, 5), mapB);
        var knownMaps = new Dictionary<MapId, MapModel> { [mapA] = MapWithPortals(mapA, portal) };
        var destination = new WorldPosition(50, 50);

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapB, destination, At);

        Assert.True(plan.IsReachable.Value);
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.Equal(mapA, plan.Waypoints[0].MapId);
        Assert.Equal(new WorldPosition(5, 5), plan.Waypoints[0].Position);
        Assert.Equal(portal.Id, plan.Waypoints[0].UsePortal);
        Assert.Equal(mapB, plan.Waypoints[1].MapId);
        Assert.Equal(destination, plan.Waypoints[1].Position);
        Assert.Null(plan.Waypoints[1].UsePortal);
    }

    [Fact]
    public void TwoChainedPortals_PlansAThreeWaypointRouteThroughTheIntermediateMap()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        var mapC = new MapId("map-c");
        Portal portalAb = UsablePortal("p-ab", mapA, new WorldPosition(1, 1), mapB);
        Portal portalBc = UsablePortal("p-bc", mapB, new WorldPosition(2, 2), mapC);
        var knownMaps = new Dictionary<MapId, MapModel>
        {
            [mapA] = MapWithPortals(mapA, portalAb),
            [mapB] = MapWithPortals(mapB, portalBc),
        };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapC, new WorldPosition(9, 9), At);

        Assert.True(plan.IsReachable.Value);
        Assert.Equal(3, plan.Waypoints.Count);
        Assert.Equal((mapA, portalAb.Id), (plan.Waypoints[0].MapId, plan.Waypoints[0].UsePortal));
        Assert.Equal((mapB, (PortalId?)portalBc.Id), (plan.Waypoints[1].MapId, plan.Waypoints[1].UsePortal));
        Assert.Equal((mapC, (PortalId?)null), (plan.Waypoints[2].MapId, plan.Waypoints[2].UsePortal));
    }

    [Fact]
    public void NoPortalChainConnectsTheMaps_ReturnsUnreachable()
    {
        var mapA = new MapId("map-a");
        var mapIsolated = new MapId("map-isolated");
        var knownMaps = new Dictionary<MapId, MapModel> { [mapA] = MapWithPortals(mapA) };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapIsolated, new WorldPosition(0, 0), At);

        Assert.False(plan.IsReachable.HasValue);
        Assert.Equal(MultiMapRoutePlanner.NoRouteKnownReason, plan.IsReachable.Reason);
        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void AnUnconfirmedActivePortal_IsNotUsedAsAnEdge()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        var unconfirmed = new Portal(
            new PortalId("p-ab"),
            mapA,
            WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1d, At),
            WorldFact<MapId>.Live(mapB, 1d, At),
            WorldFact<bool>.Unknown("not_yet_confirmed_open", At));
        var knownMaps = new Dictionary<MapId, MapModel> { [mapA] = MapWithPortals(mapA, unconfirmed) };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapB, new WorldPosition(0, 0), At);

        Assert.False(plan.IsReachable.HasValue);
        Assert.Equal(MultiMapRoutePlanner.NoRouteKnownReason, plan.IsReachable.Reason);
    }

    [Fact]
    public void APortalWithNoConfirmedDestination_IsNotUsedAsAnEdge()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        var noDestination = new Portal(
            new PortalId("p-ab"),
            mapA,
            WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1d, At),
            WorldFact<MapId>.Unknown("destination_not_yet_observed", At),
            WorldFact<bool>.Live(true, 1d, At));
        var knownMaps = new Dictionary<MapId, MapModel> { [mapA] = MapWithPortals(mapA, noDestination) };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapB, new WorldPosition(0, 0), At);

        Assert.False(plan.IsReachable.HasValue);
    }

    [Fact]
    public void APortalIsNeverTreatedAsBidirectional_TheReturnTripNeedsItsOwnObservedPortal()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        // Only the A -> B crossing has ever been observed; nothing records B -> A.
        Portal portalAb = UsablePortal("p-ab", mapA, new WorldPosition(1, 1), mapB);
        var knownMaps = new Dictionary<MapId, MapModel>
        {
            [mapA] = MapWithPortals(mapA, portalAb),
            [mapB] = MapWithPortals(mapB),
        };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapB, new WorldPosition(0, 0), mapA, new WorldPosition(0, 0), At);

        Assert.False(plan.IsReachable.HasValue);
        Assert.Equal(MultiMapRoutePlanner.NoRouteKnownReason, plan.IsReachable.Reason);
    }

    [Fact]
    public void WhenTwoRoutesExist_TheShorterHopCountWins()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        var mapC = new MapId("map-c");
        var mapD = new MapId("map-d");
        // Direct A -> D, and a longer A -> B -> C -> D.
        Portal direct = UsablePortal("p-ad", mapA, new WorldPosition(1, 1), mapD);
        Portal ab = UsablePortal("p-ab", mapA, new WorldPosition(2, 2), mapB);
        Portal bc = UsablePortal("p-bc", mapB, new WorldPosition(3, 3), mapC);
        Portal cd = UsablePortal("p-cd", mapC, new WorldPosition(4, 4), mapD);
        var knownMaps = new Dictionary<MapId, MapModel>
        {
            [mapA] = MapWithPortals(mapA, ab, direct),
            [mapB] = MapWithPortals(mapB, bc),
            [mapC] = MapWithPortals(mapC, cd),
        };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapD, new WorldPosition(9, 9), At);

        Assert.True(plan.IsReachable.Value);
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.Equal(direct.Id, plan.Waypoints[0].UsePortal);
    }

    [Fact]
    public void ARouteThatWouldRevisitAMap_DoesNotLoopForever()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        var mapIsolated = new MapId("map-isolated");
        Portal ab = UsablePortal("p-ab", mapA, new WorldPosition(1, 1), mapB);
        Portal ba = UsablePortal("p-ba", mapB, new WorldPosition(2, 2), mapA);
        var knownMaps = new Dictionary<MapId, MapModel>
        {
            [mapA] = MapWithPortals(mapA, ab),
            [mapB] = MapWithPortals(mapB, ba),
        };

        NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
            knownMaps, mapA, new WorldPosition(0, 0), mapIsolated, new WorldPosition(0, 0), At);

        Assert.False(plan.IsReachable.HasValue);
        Assert.Equal(MultiMapRoutePlanner.NoRouteKnownReason, plan.IsReachable.Reason);
    }

    [Fact]
    public void TwoCallsWithTheSameInputs_ProduceTheIdenticalPlan()
    {
        var mapA = new MapId("map-a");
        var mapB = new MapId("map-b");
        Portal portal = UsablePortal("p-ab", mapA, new WorldPosition(1, 1), mapB);
        var knownMaps = new Dictionary<MapId, MapModel> { [mapA] = MapWithPortals(mapA, portal) };

        NavigationPlan first = MultiMapRoutePlanner.PlanRoute(knownMaps, mapA, new WorldPosition(0, 0), mapB, new WorldPosition(9, 9), At);
        NavigationPlan second = MultiMapRoutePlanner.PlanRoute(knownMaps, mapA, new WorldPosition(0, 0), mapB, new WorldPosition(9, 9), At);

        Assert.Equal(first, second);
    }
}
