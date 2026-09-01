using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// How the client's static geometry joins the observed dynamic layer, and what
/// <see cref="TileType.Unobserved"/> means once it does.
/// </summary>
/// <remarks>
/// The risk this file exists for: <see cref="MapGridData"/> keeps one byte per cell,
/// so geometry and occupancy share a slot. Stamping the file's answer into that byte
/// would make the runtime say "nothing is standing here" on the authority of a file
/// that cannot know, and would leave a cleared monster with nowhere correct to
/// revert to. The rule is two layers composed in one direction, and these are the
/// cases that hold it to that.
/// </remarks>
public sealed class StaticGeometryLayerTests
{
    private static MapGrid Grid(params string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var cells = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                cells[(y * width) + x] = rows[y][x] == '#' ? (byte)0x01 : (byte)0x00;
        }

        return new MapGrid(1, width, height, cells);
    }

    // --------------------------------------------------- the direction of the join

    /// <summary>
    /// The decision worth arguing about, stated as a test. Open ground with nothing
    /// observed on it is walkable, and that is not "unknown treated as empty".
    /// </summary>
    /// <remarks>
    /// The runtime never observes most cells of a map. If the absence of a sighting
    /// blocked a cell then every cell would block, planning would return nothing
    /// forever, and the pressure to loosen the rule would land on the geometry
    /// guarantee — the one that must not move. What this tile claims is exactly what
    /// it can support: the client's geometry permits the cell and nothing observed is
    /// on it. The claim it does not make — that the cell is still clear when the
    /// character arrives — is answered in time, by the observation-age limit and by
    /// revalidating before each segment.
    /// </remarks>
    [Fact]
    public void OpenGroundWithNothingObservedOnItIsWalkable()
    {
        MapGrid grid = Grid("..");

        Assert.Equal(TileType.Walkable, StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Clear));
    }

    /// <summary>
    /// The dynamic layer subtracts and never adds. An observation that a wall is
    /// clear is an observation that is wrong about a wall.
    /// </summary>
    [Fact]
    public void NoObservationCanPromoteACellTheGeometryBlocks()
    {
        MapGrid grid = Grid("#");

        foreach (DynamicOccupancy occupancy in Enum.GetValues<DynamicOccupancy>())
            Assert.Equal(TileType.BlockedObstacle, StaticGeometryLayer.Compose(grid, 0, 0, occupancy));
    }

    [Fact]
    public void SomethingObservedOnOpenGroundBlocksIt()
    {
        MapGrid grid = Grid("..");

        Assert.Equal(
            TileType.BlockedObstacle,
            StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Occupied));
    }

    /// <summary>
    /// The job <see cref="TileType.Unobserved"/> keeps in the dynamic domain: a named
    /// entity might be on this cell and the sighting is too old to act on. Positive
    /// suspicion about something specific, bounded by how many entities are tracked
    /// rather than by the size of the map — which is what makes it, in the
    /// architecture's words, a much smaller and more honest domain.
    /// </summary>
    [Fact]
    public void ASuspectedEntityLeavesTheCellUnobservedAndThereforeBlocked()
    {
        MapGrid grid = Grid("..");
        var map = new MapGridData(1, "test", 2, 1);

        TileType composed = StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Suspected);

        Assert.Equal(TileType.Unobserved, composed);

        // And Unobserved still blocks, which is the property that had to survive.
        map.SetTileType(0, 0, composed);
        Assert.False(map.IsWalkable(0, 0));
    }

    /// <summary>
    /// The other job it keeps, and the one the build-identity check produces on
    /// purpose: no grid for this map, so geometry is genuinely unknown and planning
    /// must stop.
    /// </summary>
    [Fact]
    public void WithNoGridLoadedEveryCellIsUnobservedWhateverWasObserved()
    {
        MapGrid none = default;

        foreach (DynamicOccupancy occupancy in Enum.GetValues<DynamicOccupancy>())
        {
            Assert.Equal(TileType.Unobserved, StaticGeometryLayer.Compose(none, 0, 0, occupancy));
            Assert.Equal(TileType.Unobserved, StaticGeometryLayer.Compose(none, 99, 99, occupancy));
        }
    }

    [Fact]
    public void OutsideTheRectangleIsBlockedEvenWithAGridLoaded()
    {
        MapGrid grid = Grid("..");

        Assert.Equal(
            TileType.BlockedObstacle,
            StaticGeometryLayer.Compose(grid, 5, 5, DynamicOccupancy.Clear));
    }

    /// <summary>
    /// The baseline is where a cleared dynamic obstacle reverts to. Reverting to
    /// Walkable would open cells the client walls off; reverting to Unobserved would
    /// re-block ground already read from the file.
    /// </summary>
    [Fact]
    public void TheBaselineIsGeometryAloneAndIsWhereAClearedObstacleReverts()
    {
        MapGrid grid = Grid(
            ".#",
            "..");

        Assert.Equal(TileType.Walkable, StaticGeometryLayer.BaselineFor(grid, 0, 0));
        Assert.Equal(TileType.BlockedObstacle, StaticGeometryLayer.BaselineFor(grid, 1, 0));
        Assert.Equal(TileType.Unobserved, StaticGeometryLayer.BaselineFor(default, 0, 0));

        // A monster arrives on open ground and leaves again.
        TileType occupied = StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Occupied);
        TileType cleared = StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Clear);

        Assert.Equal(TileType.BlockedObstacle, occupied);
        Assert.Equal(StaticGeometryLayer.BaselineFor(grid, 0, 0), cleared);
    }

    // ------------------------------------------------------------- projection

    [Fact]
    public void ProjectingWritesGeometryAndNeverWritesUnobserved()
    {
        MapGrid grid = Grid(
            "..#",
            "#..");
        var map = new MapGridData(1, "test", 3, 2);

        // Everything starts unobserved, so everything starts blocked.
        Assert.Equal(6, map.UnobservedTileCount);

        StaticGeometryLayer.ProjectionReport report = StaticGeometryLayer.Project(grid, map);

        Assert.Equal(6, report.CellsWritten);
        Assert.Equal(0, map.UnobservedTileCount);

        Assert.True(map.IsWalkable(0, 0));
        Assert.True(map.IsWalkable(1, 0));
        Assert.False(map.IsWalkable(2, 0));
        Assert.False(map.IsWalkable(0, 1));
        Assert.True(map.IsWalkable(1, 1));
        Assert.True(map.IsWalkable(2, 1));
    }

    /// <summary>
    /// A portal mouth is a walkable tile with a meaning the grid has no bit for.
    /// Stamping Walkable over it would erase the portal table into the geometry.
    /// </summary>
    [Fact]
    public void ProjectingPreservesTilesCarryingMeaningTheGridCannotExpress()
    {
        MapGrid grid = Grid("...");
        var map = new MapGridData(1, "test", 3, 1);
        map.SetTileType(1, 0, TileType.PortalEntrance);
        map.SetTileType(2, 0, TileType.SafeZoneTown);

        StaticGeometryLayer.ProjectionReport report = StaticGeometryLayer.Project(grid, map);

        Assert.Equal(TileType.PortalEntrance, map.GetTileType(1, 0));
        Assert.Equal(TileType.SafeZoneTown, map.GetTileType(2, 0));
        Assert.Equal(2, report.SemanticTilesPreserved);
        Assert.Equal(0, report.SemanticTilesOverruled);
    }

    /// <summary>
    /// Where the two disagree, geometry wins and the disagreement is counted: a
    /// portal inside a wall means the portal table and the grid are out of step, and
    /// that is worth someone's attention rather than a silent overwrite.
    /// </summary>
    [Fact]
    public void GeometryOverrulesASemanticTileAndTheDisagreementIsReported()
    {
        MapGrid grid = Grid("#");
        var map = new MapGridData(1, "test", 1, 1);
        map.SetTileType(0, 0, TileType.PortalEntrance);

        StaticGeometryLayer.ProjectionReport report = StaticGeometryLayer.Project(grid, map);

        Assert.Equal(TileType.BlockedObstacle, map.GetTileType(0, 0));
        Assert.Equal(1, report.SemanticTilesOverruled);
    }

    /// <summary>
    /// An observed guess about terrain is replaced by the file that answers it. Left
    /// standing it would keep an observation in front of the authority that
    /// superseded it.
    /// </summary>
    [Fact]
    public void AnObservedTerrainGuessIsReplacedByTheFile()
    {
        MapGrid grid = Grid("..");
        var map = new MapGridData(1, "test", 2, 1);
        map.SetTileType(0, 0, TileType.WaterOrChasm);

        StaticGeometryLayer.Project(grid, map);

        Assert.Equal(TileType.Walkable, map.GetTileType(0, 0));
    }

    [Fact]
    public void ProjectingWithoutAGridIsRefusedRatherThanTreatedAsOpenGround()
    {
        var map = new MapGridData(1, "test", 2, 2);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => StaticGeometryLayer.Project(default, map));

        Assert.Contains("stop planning", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, map.UnobservedTileCount);
    }

    /// <summary>
    /// A partial projection would leave the rest of the map holding whatever it held
    /// before, which is the one outcome nobody could diagnose afterwards.
    /// </summary>
    [Fact]
    public void ProjectingOntoADifferentlySizedMapIsRefused()
    {
        MapGrid grid = Grid(
            "..",
            "..");
        var map = new MapGridData(1, "test", 4, 4);

        Assert.Throws<ArgumentException>(() => StaticGeometryLayer.Project(grid, map));
        Assert.Equal(16, map.UnobservedTileCount);
    }

    /// <summary>
    /// End to end: a projected map is one A* can plan across, where the same map
    /// before projection could not be planned across at all.
    /// </summary>
    [Fact]
    public void APathIsFoundOnProjectedGeometryWhereTheUnprojectedMapHadNone()
    {
        MapGrid grid = Grid(
            ".....",
            ".###.",
            ".....");

        var map = new MapGridData(1, "test", 5, 3);
        var pathfinder = new AStarPathfinder();

        CalculatedPathResult before = pathfinder.FindPath(map, new GridPoint(0, 0), new GridPoint(4, 2));
        Assert.False(before.IsPathFound);

        StaticGeometryLayer.Project(grid, map);

        CalculatedPathResult after = pathfinder.FindPath(map, new GridPoint(0, 0), new GridPoint(4, 2));
        Assert.True(after.IsPathFound);
        Assert.All(after.Waypoints, p => Assert.True(grid.IsWalkable(p.X, p.Y)));
    }
}
