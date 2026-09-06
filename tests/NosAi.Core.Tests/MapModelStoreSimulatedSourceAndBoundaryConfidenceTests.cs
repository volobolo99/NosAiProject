using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// AP-03/A5 audit item 5 (storage round-trip fidelity), independently
/// re-verified in one respect <c>MapModelStoreTests</c>'s own
/// <c>SaveThenTryLoad_RoundTripsTilesPortalsAndBounds_WithFullFidelity</c>
/// does not exercise: <see cref="DataSourceKind.Simulated"/> (that fixture
/// only ever uses Live/Derived/Cached/Unknown) and the exact boundary
/// confidence values 0.0 and 1.0, which a lossy numeric round trip (e.g. a
/// serializer that special-cases 0/1 or applies floating-point rounding)
/// could plausibly disturb even when the middling values already tested
/// (0.6, 0.7, 0.8, 0.9) survive intact.
/// </summary>
[Trait("Category", "Gate1")]
public sealed class MapModelStoreSimulatedSourceAndBoundaryConfidenceTests : IDisposable
{
    private readonly string _databasePath;

    public MapModelStoreSimulatedSourceAndBoundaryConfidenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-map-model-store-sim-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static readonly DateTime T0 = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveThenTryLoad_RoundTripsASimulatedSourceTile_AndBoundaryConfidenceValues()
    {
        var id = new MapId("map-simulated-1");
        var map = new MapModel(
            id,
            WorldFact<string>.Unknown("map_name_catalog_not_available", T0),
            WorldFact<MapBounds>.Unknown("map_bounds_not_yet_reconstructed", T0),
            EquatableArray<Tile>.From(new[]
            {
                // Confidence 0.0, exactly -- the lower boundary WorldFact.Confidence promises never to violate.
                new Tile(new TileCoordinate(0, 0), WorldFact<TileTraversability>.Simulated(TileTraversability.Walkable, 0.0, T0, reason: "predicted_open_area")),
                // Confidence 1.0, exactly -- the upper boundary.
                new Tile(new TileCoordinate(1, 0), WorldFact<TileTraversability>.Simulated(TileTraversability.Blocked, 1.0, T0, reason: "predicted_wall")),
            }),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 3,
            T0);

        var options = new SqliteJournalOptions(FileName: "irrelevant.db");
        using (var store = new MapModelStore(_databasePath, options))
            store.Save(map);

        using var reopened = new MapModelStore(_databasePath, options);
        Assert.True(reopened.TryLoad(id, out MapModel loaded));

        Assert.Equal(2, loaded.Tiles.Count);

        Tile lowBoundary = loaded.Tiles[0];
        Assert.Equal(DataSourceKind.Simulated, lowBoundary.Traversability.Source);
        Assert.Equal(TileTraversability.Walkable, lowBoundary.Traversability.Value);
        Assert.Equal(0.0, lowBoundary.Traversability.Confidence);
        Assert.True(lowBoundary.Traversability.HasObservedValue);
        Assert.Equal("predicted_open_area", lowBoundary.Traversability.Reason);

        Tile highBoundary = loaded.Tiles[1];
        Assert.Equal(DataSourceKind.Simulated, highBoundary.Traversability.Source);
        Assert.Equal(TileTraversability.Blocked, highBoundary.Traversability.Value);
        Assert.Equal(1.0, highBoundary.Traversability.Confidence);
        Assert.Equal("predicted_wall", highBoundary.Traversability.Reason);

        // Simulated data is never authoritative -- HasValue still reports
        // true (it IS a real, if predicted, reading), the same as any other
        // non-Unknown source. This round trip must not silently promote or
        // demote a Simulated fact into a different DataSourceKind.
        Assert.True(lowBoundary.Traversability.HasValue);
        Assert.True(highBoundary.Traversability.HasValue);
    }
}
