using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// AP-03/A4: <see cref="MapModelStore"/>'s own persistence contract, tested
/// against a real temp-file SQLite database (never <c>:memory:</c> -- the WAL
/// pragma verification below is part of what this file tests), the same
/// pattern <see cref="SqliteEventJournalTests"/> already establishes for the
/// Gate 1 journal.
/// </summary>
[Trait("Category", "Gate1")]
public sealed class MapModelStoreTests : IDisposable
{
    private readonly string _databasePath;

    public MapModelStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-map-model-store-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static readonly DateTime T0 = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    private static MapModel FullyPopulatedMap(MapId id) => new(
        id,
        WorldFact<string>.Live("Fernon Field", 0.9, T0, reason: "client_map_name_table"),
        WorldFact<MapBounds>.Derived(new MapBounds(new TileCoordinate(0, 0), new TileCoordinate(3, 4)), 1.0, T0),
        EquatableArray<Tile>.From(new[]
        {
            new Tile(new TileCoordinate(0, 0), WorldFact<TileTraversability>.Cached(TileTraversability.Walkable, 1.0, T0, reason: "client-map-grid")),
            new Tile(new TileCoordinate(1, 0), WorldFact<TileTraversability>.Cached(TileTraversability.Blocked, 1.0, T0, reason: "client-map-grid")),
            new Tile(new TileCoordinate(3, 4), WorldFact<TileTraversability>.Cached(TileTraversability.Hazardous, 0.7, T0, reason: "client-map-grid")),
        }),
        EquatableArray<Portal>.From(new[]
        {
            new Portal(
                new PortalId("portal-1"),
                id,
                WorldFact<WorldPosition>.Live(new WorldPosition(1.5f, 2.5f), 0.8, T0),
                WorldFact<MapId>.Derived(new MapId("map-99"), 0.6, T0, reason: "portal_table"),
                WorldFact<bool>.Unknown("portal_active_state_not_observed", T0)),
        }),
        EquatableArray<Polygon>.From(new[]
        {
            new Polygon(EquatableArray<WorldPosition>.From(new[]
            {
                new WorldPosition(0f, 0f),
                new WorldPosition(10f, 0f),
                new WorldPosition(10f, 10f),
            })),
        }),
        Version: 7,
        T0);

    [Fact]
    public void SaveThenTryLoad_RoundTripsTilesPortalsAndBounds_WithFullFidelity()
    {
        var id = new MapId("map-1");
        MapModel original = FullyPopulatedMap(id);
        var options = new SqliteJournalOptions(FileName: "irrelevant.db");

        using (var store = new MapModelStore(_databasePath, options))
            store.Save(original);

        using (var reopened = new MapModelStore(_databasePath, options))
        {
            Assert.True(reopened.TryLoad(id, out MapModel loaded));

            Assert.Equal(original.Id, loaded.Id);
            Assert.Equal(original.Version, loaded.Version);
            Assert.Equal(original.ObservedAtUtc, loaded.ObservedAtUtc);

            Assert.Equal(original.Name.Value, loaded.Name.Value);
            Assert.Equal(original.Name.Source, loaded.Name.Source);
            Assert.Equal(original.Name.Confidence, loaded.Name.Confidence);
            Assert.Equal(original.Name.ObservedAtUtc, loaded.Name.ObservedAtUtc);
            Assert.Equal(original.Name.HasObservedValue, loaded.Name.HasObservedValue);
            Assert.Equal(original.Name.Reason, loaded.Name.Reason);

            Assert.True(loaded.ObservedBounds.HasValue);
            Assert.Equal(original.ObservedBounds.Value, loaded.ObservedBounds.Value);
            Assert.Equal(original.ObservedBounds.Source, loaded.ObservedBounds.Source);

            Assert.Equal(original.Tiles.Count, loaded.Tiles.Count);
            for (int i = 0; i < original.Tiles.Count; i++)
            {
                Assert.Equal(original.Tiles[i].Coordinate, loaded.Tiles[i].Coordinate);
                Assert.Equal(original.Tiles[i].Traversability.Value, loaded.Tiles[i].Traversability.Value);
                Assert.Equal(original.Tiles[i].Traversability.Source, loaded.Tiles[i].Traversability.Source);
                Assert.Equal(original.Tiles[i].Traversability.Confidence, loaded.Tiles[i].Traversability.Confidence);
                Assert.Equal(original.Tiles[i].Traversability.ObservedAtUtc, loaded.Tiles[i].Traversability.ObservedAtUtc);
                Assert.Equal(original.Tiles[i].Traversability.Reason, loaded.Tiles[i].Traversability.Reason);
            }

            Assert.Equal(original.Portals.Count, loaded.Portals.Count);
            Portal originalPortal = original.Portals[0];
            Portal loadedPortal = loaded.Portals[0];
            Assert.Equal(originalPortal.Id, loadedPortal.Id);
            Assert.Equal(originalPortal.SourceMap, loadedPortal.SourceMap);
            Assert.Equal(originalPortal.SourcePosition.Value, loadedPortal.SourcePosition.Value);
            Assert.Equal(originalPortal.SourcePosition.Source, loadedPortal.SourcePosition.Source);
            Assert.Equal(originalPortal.DestinationMap.Value, loadedPortal.DestinationMap.Value);
            Assert.Equal(originalPortal.DestinationMap.Source, loadedPortal.DestinationMap.Source);
            Assert.False(loadedPortal.IsActive.HasValue);
            Assert.Equal(originalPortal.IsActive.Reason, loadedPortal.IsActive.Reason);

            Assert.Equal(original.Landmarks.Count, loaded.Landmarks.Count);
            Assert.Equal(original.Landmarks[0].Vertices.Count, loaded.Landmarks[0].Vertices.Count);
            for (int i = 0; i < original.Landmarks[0].Vertices.Count; i++)
                Assert.Equal(original.Landmarks[0].Vertices[i], loaded.Landmarks[0].Vertices[i]);
        }
    }

    [Fact]
    public void SaveThenTryLoad_RoundTripsAnUnknownMap()
    {
        var id = new MapId("map-2");
        MapModel unknown = MapModel.Unknown(id, "not_yet_entered", T0);
        var options = new SqliteJournalOptions(FileName: "irrelevant.db");

        using var store = new MapModelStore(_databasePath, options);
        store.Save(unknown);

        Assert.True(store.TryLoad(id, out MapModel loaded));
        Assert.Equal(id, loaded.Id);
        Assert.False(loaded.Name.HasValue);
        Assert.False(loaded.ObservedBounds.HasValue);
        Assert.Empty(loaded.Tiles);
        Assert.Empty(loaded.Portals);
        Assert.Empty(loaded.Landmarks);
        Assert.Equal(0, loaded.Version);
    }

    [Fact]
    public void TryLoad_ForAMapIdNeverSaved_ReturnsFalseWithAnUnchangedUnknownMap()
    {
        var options = new SqliteJournalOptions(FileName: "irrelevant.db");
        using var store = new MapModelStore(_databasePath, options);

        var neverSaved = new MapId("map-never-saved");
        bool found = store.TryLoad(neverSaved, out MapModel map);

        Assert.False(found);
        Assert.Equal(neverSaved, map.Id);
        Assert.False(map.Name.HasValue);
        Assert.False(map.ObservedBounds.HasValue);
        Assert.Empty(map.Tiles);
        Assert.Equal(0, map.Version);
    }

    [Fact]
    public void SavingTheSameMapIdTwice_OverwritesRatherThanDuplicating()
    {
        var id = new MapId("map-3");
        var options = new SqliteJournalOptions(FileName: "irrelevant.db");
        using var store = new MapModelStore(_databasePath, options);

        MapModel first = MapModel.Unknown(id, "not_yet_entered", T0) with { Version = 1 };
        MapModel second = FullyPopulatedMap(id) with { Version = 2 };

        store.Save(first);
        store.Save(second);

        Assert.True(store.TryLoad(id, out MapModel loaded));
        Assert.Equal(2, loaded.Version);
        Assert.Equal(second.Tiles.Count, loaded.Tiles.Count);

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM map_models WHERE map_id = $mapId";
        count.Parameters.AddWithValue("$mapId", id.Value);
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()));
    }

    [Fact]
    public void ConstructingTheStore_AppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy()
    {
        // Same reasoning as SqliteEventJournalTests.JournalAppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy:
        // synchronous/busy_timeout are per-connection pragmas a second connection
        // cannot observe, so constructing without throwing (ApplyPolicyOrThrow
        // already re-reads and verifies both) is itself the evidence for those
        // two. journal_mode=WAL is persisted to the file header, so a fresh,
        // independent connection can confirm it directly.
        var options = new SqliteJournalOptions(FileName: "irrelevant.db");
        using var store = new MapModelStore(_databasePath, options);

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        string journalMode = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;

        Assert.Equal("wal", journalMode);
    }
}
