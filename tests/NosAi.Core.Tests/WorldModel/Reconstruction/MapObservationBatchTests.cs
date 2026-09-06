using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Reconstruction;

public sealed class MapObservationBatchTests
{
    [Fact]
    public void Empty_HasNoTilesOrPortals()
    {
        MapObservationBatch batch = MapObservationBatch.Empty(new MapId("map-1"), "grid_not_loaded");

        Assert.Empty(batch.Tiles);
        Assert.Empty(batch.Portals);
    }

    [Fact]
    public void Empty_PreservesMapIdAndReason()
    {
        var id = new MapId("map-42");
        MapObservationBatch batch = MapObservationBatch.Empty(id, "map_unreachable");

        Assert.Equal(id, batch.MapId);
        Assert.Equal("map_unreachable", batch.SourceDescription);
    }

    [Fact]
    public void Empty_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        MapObservationBatch batch = MapObservationBatch.Empty(new MapId("map-1"), "reason", fixedInstant);

        Assert.Equal(fixedInstant, batch.ObservedAtUtc);
    }

    [Fact]
    public void Empty_CalledTwiceWithTheSameInstant_ProducesEqualBatches()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        var id = new MapId("map-1");

        MapObservationBatch first = MapObservationBatch.Empty(id, "reason", fixedInstant);
        MapObservationBatch second = MapObservationBatch.Empty(id, "reason", fixedInstant);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RecordEquality_ComparesTilesStructurally()
    {
        var id = new MapId("map-1");
        var tile = new Tile(new TileCoordinate(1, 2), WorldFact<TileTraversability>.Cached(TileTraversability.Walkable, 1d, DateTime.UnixEpoch));
        EquatableArray<Tile> tiles = EquatableArray<Tile>.From(new[] { tile });

        var first = new MapObservationBatch(id, tiles, EquatableArray<Portal>.Empty, "src", DateTime.UnixEpoch);
        var second = new MapObservationBatch(id, tiles, EquatableArray<Portal>.Empty, "src", DateTime.UnixEpoch);

        Assert.Equal(first, second);
    }
}
