using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

public sealed class MapGridObservationProjectorTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly DateTime FixedInstant = DateTime.UnixEpoch;

    [Fact]
    public void Project_GridNotLoaded_ReturnsEmptyBatchWithReason()
    {
        MapGrid notLoaded = default;

        MapObservationBatch batch = MapGridObservationProjector.Project(in notLoaded, TestMapId, FixedInstant);

        Assert.Empty(batch.Tiles);
        Assert.Empty(batch.Portals);
        Assert.Equal("map_grid_not_loaded", batch.SourceDescription);
        Assert.Equal(FixedInstant, batch.ObservedAtUtc);
    }

    [Fact]
    public void Project_LoadedGrid_EmitsOneTilePerCell()
    {
        // 2x2 grid: (0,0) open, (1,0) walk-blocked, (0,1) open, (1,1) open.
        byte[] cells = { 0x00, 0x01, 0x00, 0x00 };
        var grid = new MapGrid(mapId: 7, width: 2, height: 2, cells);

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        Assert.Equal(4, batch.Tiles.Count);
        Assert.Empty(batch.Portals);
        Assert.Equal(TestMapId, batch.MapId);
    }

    [Fact]
    public void Project_WalkBlockedCell_IsClassifiedBlocked()
    {
        byte[] cells = { 0x00, 0x01, 0x00, 0x00 };
        var grid = new MapGrid(mapId: 7, width: 2, height: 2, cells);

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        Tile blocked = Single(batch, new TileCoordinate(1, 0));
        Assert.Equal(TileTraversability.Blocked, blocked.Traversability.Value);
    }

    [Fact]
    public void Project_OpenCell_IsClassifiedWalkable()
    {
        byte[] cells = { 0x00, 0x01, 0x00, 0x00 };
        var grid = new MapGrid(mapId: 7, width: 2, height: 2, cells);

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        Tile open = Single(batch, new TileCoordinate(0, 0));
        Assert.Equal(TileTraversability.Walkable, open.Traversability.Value);
    }

    [Fact]
    public void Project_EveryTile_IsClassifiedCached_NeverLive()
    {
        byte[] cells = { 0x00 };
        var grid = new MapGrid(mapId: 1, width: 1, height: 1, cells);

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        // One cell in, one tile out: without this, a projector that produced no
        // tiles would pass a test whose name promises something about every tile.
        Assert.Single(batch.Tiles);
        Assert.All(batch.Tiles, tile => Assert.Equal(DataSourceKind.Cached, tile.Traversability.Source));
    }

    [Fact]
    public void Project_UsesTheGivenInstant_NeverWallClock()
    {
        byte[] cells = { 0x00 };
        var grid = new MapGrid(mapId: 1, width: 1, height: 1, cells);

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        Assert.Equal(FixedInstant, batch.ObservedAtUtc);
        Assert.All(batch.Tiles, tile => Assert.Equal(FixedInstant, tile.Traversability.ObservedAtUtc));
    }

    [Fact]
    public void Project_IsDeterministic_SameGridProducesEqualBatches()
    {
        byte[] cells = { 0x00, 0x01, 0x00, 0x00 };
        var grid = new MapGrid(mapId: 7, width: 2, height: 2, cells);

        MapObservationBatch first = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);
        MapObservationBatch second = MapGridObservationProjector.Project(in grid, TestMapId, FixedInstant);

        Assert.Equal(first, second);
    }

    private static Tile Single(MapObservationBatch batch, TileCoordinate coordinate)
    {
        foreach (Tile tile in batch.Tiles)
        {
            if (tile.Coordinate == coordinate)
                return tile;
        }

        throw new Xunit.Sdk.XunitException($"No tile at {coordinate}.");
    }
}
