using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class SpatialContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Tile_UnobservedTraversability_IsExplicitlyUnknown_NotWalkableByDefault()
    {
        var tile = new Tile(new TileCoordinate(3, 4), WorldFact<TileTraversability>.Unknown("not_yet_scanned", Now));

        Assert.False(tile.Traversability.HasValue);
    }

    [Fact]
    public void Tile_ObservedTraversability_CarriesTheClassifiedEnumValue()
    {
        var tile = new Tile(new TileCoordinate(1, 1), WorldFact<TileTraversability>.Live(TileTraversability.Walkable, 1.0, Now));

        Assert.True(tile.Traversability.HasValue);
        Assert.Equal(TileTraversability.Walkable, tile.Traversability.Value);
    }

    [Fact]
    public void Polygon_PreservesVertexOrder()
    {
        var vertices = EquatableArray<WorldPosition>.From(new[]
        {
            new WorldPosition(0, 0),
            new WorldPosition(1, 0),
            new WorldPosition(1, 1)
        });

        var polygon = new Polygon(vertices);

        Assert.Equal(3, polygon.Vertices.Count);
        Assert.Equal(new WorldPosition(1, 0), polygon.Vertices[1]);
    }

    [Fact]
    public void Portal_UnknownDestination_DoesNotFabricateAMap()
    {
        var portal = new Portal(
            new PortalId("p1"),
            new MapId("map-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(10, 10), 1.0, Now),
            WorldFact<MapId>.Unknown("destination_not_yet_observed", Now),
            WorldFact<bool>.Live(true, 1.0, Now));

        Assert.False(portal.DestinationMap.HasValue);
    }

    [Fact]
    public void TwoPortalsBuiltFromTheSameInputs_AreEqual()
    {
        Portal Build() => new(
            new PortalId("p1"),
            new MapId("map-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(10, 10), 1.0, Now),
            WorldFact<MapId>.Live(new MapId("map-2"), 1.0, Now),
            WorldFact<bool>.Live(true, 1.0, Now));

        Assert.Equal(Build(), Build());
    }
}
