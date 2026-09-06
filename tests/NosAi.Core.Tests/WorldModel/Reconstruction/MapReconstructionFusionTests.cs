using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Reconstruction;

public sealed class MapReconstructionFusionTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly DateTime T0 = DateTime.UnixEpoch;
    private static readonly DateTime T1 = DateTime.UnixEpoch + TimeSpan.FromMinutes(1);

    private static Tile WalkableTile(int column, int row, DateTime observedAtUtc) =>
        new(new TileCoordinate(column, row), WorldFact<TileTraversability>.Cached(TileTraversability.Walkable, 1d, observedAtUtc));

    private static Tile BlockedTile(int column, int row, DateTime observedAtUtc) =>
        new(new TileCoordinate(column, row), WorldFact<TileTraversability>.Cached(TileTraversability.Blocked, 1d, observedAtUtc));

    private static MapObservationBatch BatchOf(MapId mapId, DateTime observedAtUtc, params Tile[] tiles) =>
        new(mapId, EquatableArray<Tile>.From(tiles), EquatableArray<Portal>.Empty, "test", observedAtUtc);

    [Fact]
    public void Merge_IntoUnknownMap_ProducesRealBoundsAndVersionOne()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapObservationBatch batch = BatchOf(TestMapId, T1, WalkableTile(0, 0, T1), WalkableTile(3, 4, T1));

        MapModel merged = MapReconstructionFusion.Merge(unknown, batch, T1);

        Assert.Equal(1, merged.Version);
        Assert.Equal(2, merged.Tiles.Count);
        Assert.True(merged.ObservedBounds.HasValue);
        Assert.Equal(new TileCoordinate(0, 0), merged.ObservedBounds.Value.Min);
        Assert.Equal(new TileCoordinate(3, 4), merged.ObservedBounds.Value.Max);
    }

    [Fact]
    public void Merge_NeverDropsPreviouslyObservedTiles()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(unknown, BatchOf(TestMapId, T0, WalkableTile(0, 0, T0)), T0);

        MapModel afterSecond = MapReconstructionFusion.Merge(afterFirst, BatchOf(TestMapId, T1, WalkableTile(5, 5, T1)), T1);

        Assert.Equal(2, afterSecond.Tiles.Count);
        Assert.Contains(afterSecond.Tiles, t => t.Coordinate == new TileCoordinate(0, 0));
        Assert.Contains(afterSecond.Tiles, t => t.Coordinate == new TileCoordinate(5, 5));
    }

    [Fact]
    public void Merge_BoundsNeverShrink_AfterASmallerSubsequentBatch()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(
            unknown, BatchOf(TestMapId, T0, WalkableTile(0, 0, T0), WalkableTile(10, 10, T0)), T0);

        // Second batch only re-observes the origin tile with a fresher timestamp -- a
        // narrower observation than the first pass, e.g. a partial re-scan.
        MapModel afterSecond = MapReconstructionFusion.Merge(
            afterFirst, BatchOf(TestMapId, T1, WalkableTile(0, 0, T1)), T1);

        Assert.Equal(new TileCoordinate(0, 0), afterSecond.ObservedBounds.Value.Min);
        Assert.Equal(new TileCoordinate(10, 10), afterSecond.ObservedBounds.Value.Max);
    }

    [Fact]
    public void Merge_SameCoordinateObservedAgain_NewEvidenceReplacesOld()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(
            unknown, BatchOf(TestMapId, T0, WalkableTile(0, 0, T0)), T0);

        MapModel afterSecond = MapReconstructionFusion.Merge(
            afterFirst, BatchOf(TestMapId, T1, BlockedTile(0, 0, T1)), T1);

        Assert.Single(afterSecond.Tiles);
        Assert.Equal(TileTraversability.Blocked, afterSecond.Tiles[0].Traversability.Value);
        Assert.Equal(T1, afterSecond.Tiles[0].Traversability.ObservedAtUtc);
    }

    [Fact]
    public void Merge_BatchThatChangesNothing_ReturnsTheSameInstance_AndDoesNotBumpVersion()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(
            unknown, BatchOf(TestMapId, T0, WalkableTile(0, 0, T0)), T0);

        MapModel afterReObserve = MapReconstructionFusion.Merge(
            afterFirst, BatchOf(TestMapId, T1, WalkableTile(0, 0, T0)), T1);

        Assert.Same(afterFirst, afterReObserve);
        Assert.Equal(1, afterReObserve.Version);
    }

    [Fact]
    public void Merge_EmptyBatch_ReturnsTheSameInstance()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapModel afterFirst = MapReconstructionFusion.Merge(
            unknown, BatchOf(TestMapId, T0, WalkableTile(0, 0, T0)), T0);

        MapModel afterEmpty = MapReconstructionFusion.Merge(
            afterFirst, MapObservationBatch.Empty(TestMapId, "no_evidence_this_pass", T1), T1);

        Assert.Same(afterFirst, afterEmpty);
    }

    [Fact]
    public void Merge_MismatchedMapId_Throws()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapObservationBatch batch = BatchOf(new MapId("map-2"), T0, WalkableTile(0, 0, T0));

        Assert.Throws<ArgumentException>(() => MapReconstructionFusion.Merge(unknown, batch, T0));
    }

    [Fact]
    public void Merge_IsDeterministic_SameInputsProduceEqualOutput()
    {
        MapModel unknown = MapModel.Unknown(TestMapId, "not_yet_entered", T0);
        MapObservationBatch batch = BatchOf(TestMapId, T0, WalkableTile(0, 0, T0), WalkableTile(1, 1, T0));

        MapModel first = MapReconstructionFusion.Merge(unknown, batch, T0);
        MapModel second = MapReconstructionFusion.Merge(unknown, batch, T0);

        Assert.Equal(first, second);
    }
}
