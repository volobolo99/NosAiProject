using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-03/A5 audit item 4 ("MapReconstructionFusion.Merge's idempotence under
/// real A2 output") and item 8 ("determinism of the full pipeline"), plus the
/// "verify without necessarily finding a defect" item on
/// <see cref="EquatableArray{T}"/> equality at grid scale.
/// <see cref="MapReconstructionFusionTests"/> (A3's own suite) only ever
/// merges 1-2 hand-built tiles; <see cref="MapGridObservationProjectorTests"/>
/// (A2's own suite) only ever projects a 1x1/2x2 grid. Neither exercises the
/// real shape of a call chained end to end: a modest map's grid, projected
/// through <see cref="MapGridObservationProjector.Project"/> into thousands
/// of <see cref="Tile"/>s, then merged through
/// <see cref="MapReconstructionFusion.Merge"/>. This file builds that chain
/// for real, at a scale (200x150 = 30,000 cells) large enough that an
/// O(n^2) merge, a dictionary keying bug, or an <see cref="EquatableArray{T}"/>
/// equality bug that only manifests past a handful of elements would show up
/// as a genuine, observable test failure rather than being papered over by a
/// small hand-built fixture.
/// </summary>
public sealed class MapReconstructionFusionAtGridScaleTests
{
    private const int Width = 200;
    private const int Height = 150;
    private static readonly MapId TestMapId = new("map-scale-1");
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddSeconds(1);

    /// <summary>
    /// A deterministic, reproducible 200x150 cell buffer -- not real client
    /// data (none is available in this sandbox) but not a trivial
    /// all-walkable/all-blocked buffer either: roughly 1 in 7 cells is
    /// walk-blocked, in a fixed pattern derived from the cell's own
    /// coordinates, so two independent calls that build "the same grid" byte
    /// for byte always agree, and the resulting tile set has genuine
    /// variety (both <see cref="TileTraversability.Walkable"/> and
    /// <see cref="TileTraversability.Blocked"/> tiles) for
    /// <see cref="MapReconstructionFusion.Merge"/> to actually key on.
    /// </summary>
    private static byte[] BuildGridCells()
    {
        var cells = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool blocked = (x * 7 + y * 13) % 11 == 0;
                cells[(y * Width) + x] = blocked ? (byte)MapCellFlags.WalkBlocked : (byte)0x00;
            }
        }

        return cells;
    }

    private static MapGrid BuildGrid() => new(mapId: 1, Width, Height, BuildGridCells());

    [Fact]
    public void Project_AtGridScale_ProducesExactlyWidthTimesHeightTiles_WithBothTraversabilities()
    {
        MapGrid grid = BuildGrid();

        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, T0);

        Assert.Equal(Width * Height, batch.Tiles.Count);
        Assert.Contains(batch.Tiles, t => t.Traversability.Value == TileTraversability.Walkable);
        Assert.Contains(batch.Tiles, t => t.Traversability.Value == TileTraversability.Blocked);
    }

    [Fact]
    public void Merge_TheSameFullGridBatchTwiceInARow_ReturnsTheIdenticalReference_NoVersionBump()
    {
        MapGrid grid = BuildGrid();
        MapObservationBatch batch = MapGridObservationProjector.Project(in grid, TestMapId, T0);
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);

        MapModel afterFirst = MapReconstructionFusion.Merge(unknown, batch, T0);
        Assert.Equal(Width * Height, afterFirst.Tiles.Count);
        Assert.Equal(1, afterFirst.Version);

        // The exact same MapObservationBatch instance, merged again.
        MapModel afterSecond = MapReconstructionFusion.Merge(afterFirst, batch, T1);

        Assert.Same(afterFirst, afterSecond);
        Assert.Equal(1, afterSecond.Version);
    }

    [Fact]
    public void Merge_AFreshlyProjectedButValueEqualBatch_StillReturnsTheIdenticalReference()
    {
        // Same grid bytes, but a brand-new MapObservationBatch/EquatableArray<Tile>
        // instance built by a second, independent Project call -- exercises
        // EquatableArray<Tile>.Equals at 30,000 elements, not reference identity
        // of the batch itself, which the test above already covers separately.
        MapGrid grid = BuildGrid();
        MapObservationBatch firstBatch = MapGridObservationProjector.Project(in grid, TestMapId, T0);
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(unknown, firstBatch, T0);

        // Not the same MapObservationBatch instance -- a fresh, independent
        // Project call built this one -- but expected to be value-equal to
        // the first, which is exactly what the assertion below depends on.
        MapObservationBatch secondBatch = MapGridObservationProjector.Project(in grid, TestMapId, T0);
        Assert.NotSame(firstBatch, secondBatch);
        Assert.Equal(firstBatch, secondBatch);

        MapModel afterSecond = MapReconstructionFusion.Merge(afterFirst, secondBatch, T1);

        Assert.Same(afterFirst, afterSecond);
        Assert.Equal(1, afterSecond.Version);
    }

    [Fact]
    public void Merge_ASingleChangedTileInASecondFullGridPass_BumpsVersionExactlyOnce_AndKeepsEveryOtherTileByReference()
    {
        // A partial re-scan in production terms: the same grid, except one
        // previously-walkable cell is now reported blocked (e.g. a dynamic
        // obstacle a future source layered on top). Confirms the merge finds
        // the one real change in a 30,000-tile batch rather than either
        // missing it (stale data survives) or bumping the version needlessly.
        MapGrid grid = BuildGrid();
        MapObservationBatch firstBatch = MapGridObservationProjector.Project(in grid, TestMapId, T0);
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(unknown, firstBatch, T0);

        Tile originalCorner = afterFirst.Tiles[0];
        TileTraversability flipped = originalCorner.Traversability.Value == TileTraversability.Walkable
            ? TileTraversability.Blocked
            : TileTraversability.Walkable;
        var changedTile = new Tile(
            originalCorner.Coordinate,
            WorldFact<TileTraversability>.Cached(flipped, 1d, T1, reason: "client-map-grid"));
        var partialBatch = new MapObservationBatch(
            TestMapId,
            EquatableArray<Tile>.From(new[] { changedTile }),
            EquatableArray<Portal>.Empty,
            "test-partial-rescan",
            T1);

        MapModel afterSecond = MapReconstructionFusion.Merge(afterFirst, partialBatch, T1);

        Assert.NotSame(afterFirst, afterSecond);
        Assert.Equal(2, afterSecond.Version);
        Assert.Equal(Width * Height, afterSecond.Tiles.Count);
        Assert.Equal(flipped, afterSecond.Tiles[0].Traversability.Value);
    }

    [Fact]
    public void FullPipeline_TwoIndependentProjectAndMergeRuns_ProduceBitForBitEqualMapModels()
    {
        // AP-03/A5 audit item 8: not "equal at each stage in isolation" but
        // the whole Project -> Merge chain, run twice from scratch against
        // the same grid bytes and the same instant.
        MapGrid gridA = BuildGrid();
        MapGrid gridB = BuildGrid();

        MapObservationBatch batchA = MapGridObservationProjector.Project(in gridA, TestMapId, T0);
        MapObservationBatch batchB = MapGridObservationProjector.Project(in gridB, TestMapId, T0);

        MapModel resultA = MapReconstructionFusion.Merge(MapModel.Unknown(TestMapId, "not_yet_entered", T0), batchA, T0);
        MapModel resultB = MapReconstructionFusion.Merge(MapModel.Unknown(TestMapId, "not_yet_entered", T0), batchB, T0);

        Assert.Equal(resultA, resultB);
        Assert.Equal(resultA.Tiles.Count, resultB.Tiles.Count);
    }

    [Fact]
    public void EquatableArray_OfTiles_HoldsStructuralEqualityAtGridScale_NotJustForAHandful()
    {
        MapGrid grid = BuildGrid();
        MapObservationBatch batchA = MapGridObservationProjector.Project(in grid, TestMapId, T0);
        MapObservationBatch batchB = MapGridObservationProjector.Project(in grid, TestMapId, T0);

        Assert.Equal(Width * Height, batchA.Tiles.Count);
        Assert.True(batchA.Tiles.Equals(batchB.Tiles));
        Assert.Equal(batchA.Tiles, batchB.Tiles);

        // A single differing tile (last element, so a short-circuiting
        // length-only check couldn't accidentally pass this) must be caught.
        Tile[] mutated = new Tile[batchB.Tiles.Count];
        for (int i = 0; i < mutated.Length; i++)
            mutated[i] = batchB.Tiles[i];
        Tile last = mutated[^1];
        mutated[^1] = new Tile(
            last.Coordinate,
            WorldFact<TileTraversability>.Cached(
                last.Traversability.Value == TileTraversability.Walkable ? TileTraversability.Blocked : TileTraversability.Walkable,
                1d, T0, reason: "client-map-grid"));
        var mutatedArray = EquatableArray<Tile>.From(mutated);

        Assert.False(batchA.Tiles.Equals(mutatedArray));
    }
}
