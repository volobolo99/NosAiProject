using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class MapModelTests
{
    [Fact]
    public void Unknown_HasNoTilesPortalsOrLandmarks_AndVersionZero()
    {
        var map = MapModel.Unknown(new MapId("map-1"), "not_yet_entered");

        Assert.False(map.Name.HasValue);
        Assert.False(map.ObservedBounds.HasValue);
        Assert.Empty(map.Tiles);
        Assert.Empty(map.Portals);
        Assert.Empty(map.Landmarks);
        Assert.Equal(0, map.Version);
    }

    [Fact]
    public void Unknown_PreservesTheGivenMapId()
    {
        var id = new MapId("map-42");
        var map = MapModel.Unknown(id, "reason");

        Assert.Equal(id, map.Id);
    }

    [Fact]
    public void PartiallyExploredMap_CanCarryObservedTilesWithoutClaimingFullBounds()
    {
        var now = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var map = new MapModel(
            new MapId("map-1"),
            WorldFact<string>.Live("Fernon Field", 1.0, now),
            WorldFact<MapBounds>.Unknown("exploration_in_progress", now),
            EquatableArray<Tile>.From(new[] { new Tile(new TileCoordinate(0, 0), WorldFact<TileTraversability>.Live(TileTraversability.Walkable, 1.0, now)) }),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 1,
            now);

        Assert.True(map.Name.HasValue);
        Assert.False(map.ObservedBounds.HasValue);
        Assert.Single(map.Tiles);
    }
}
