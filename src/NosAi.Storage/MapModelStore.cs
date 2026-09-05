using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;

namespace NosAi.Storage;

/// <summary>
/// One row per <see cref="MapId"/>, storing the latest reconstructed
/// <see cref="MapModel"/> for that map (docs/ROADMAP_ESECUTIVA.md S:AP-03 --
/// "una mappa parzialmente esplorata puo essere salvata, aggiornata e ripresa
/// senza perdere la storia precedente"). Only the latest version is kept, not
/// a history of every intermediate one: <see cref="MapModel.Version"/> and
/// <c>NosAi.Core.WorldModel.Reconstruction.MapReconstructionFusion</c>'s own
/// "never loses a tile" merge guarantee already give every consumer the
/// history that matters, so storing every intermediate version would be an
/// unbounded table for no reader.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same durability discipline as <see cref="SqliteEventJournal"/>.</b>
/// <c>journal_mode=WAL</c>, <c>synchronous=FULL</c> and
/// <c>busy_timeout=5000</c> are applied immediately after opening and
/// independently re-read and verified -- throwing if the engine did not
/// actually honor them -- before any table is touched. A store that could not
/// confirm its own durability policy has no business claiming a save
/// survives a restart.
/// </para>
/// <para>
/// <b>Why a DTO layer instead of serializing <see cref="MapModel"/> directly.</b>
/// <see cref="MapModel"/>'s collection fields are <see cref="EquatableArray{T}"/>,
/// which has no parameterless constructor for <see cref="JsonSerializer"/> to
/// deserialize into -- it serializes fine and then throws on read-back. The
/// private DTO records below mirror <see cref="MapModel"/>/<see cref="Tile"/>/
/// <see cref="Portal"/>/<see cref="Polygon"/>/<see cref="WorldFact{T}"/> with
/// <see cref="List{T}"/> in place of every <see cref="EquatableArray{T}"/> and
/// plain fields for every enum, and every conversion between the domain shape
/// and the DTO shape is explicit in both directions
/// (<see cref="ToDto(MapModel)"/>/<see cref="FromDto(MapModelDto)"/> and their
/// per-field counterparts below), so nothing -- including each tile's and
/// portal's own <see cref="DataSourceKind"/>, confidence and freshness -- is
/// lost across a restart. <see cref="MapModel.Landmarks"/> has no producer yet
/// in this phase but is persisted anyway, so a future phase that populates it
/// does not lose data a caller already wrote through this store.
/// </para>
/// </remarks>
public sealed class MapModelStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public MapModelStore(string databasePath, SqliteJournalOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        try
        {
            _connection.Open();
            ApplyPolicyOrThrow(options);
            EnsureSchema();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>Opens the store at the path resolved from <paramref name="options"/>'s labeled volume.</summary>
    /// <exception cref="InvalidOperationException">The labeled volume is not attached (see <see cref="VolumeLocator.ResolveDatabasePath"/>).</exception>
    public static MapModelStore OpenFromVolume(SqliteJournalOptions options) =>
        new(VolumeLocator.ResolveDatabasePath(options), options);

    /// <summary>
    /// Persists <paramref name="map"/> as the latest known state for its own
    /// <see cref="MapModel.Id"/>. A second save for the same id overwrites the
    /// first in place rather than accumulating rows.
    /// </summary>
    public void Save(MapModel map)
    {
        ArgumentNullException.ThrowIfNull(map);
        string json = JsonSerializer.Serialize(ToDto(map));

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO map_models (map_id, payload)
                VALUES ($mapId, $payload)
                ON CONFLICT(map_id) DO UPDATE SET payload = excluded.payload
                """;
            command.Parameters.AddWithValue("$mapId", map.Id.Value);
            command.Parameters.AddWithValue("$payload", json);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Loads the latest persisted <see cref="MapModel"/> for <paramref name="mapId"/>.</summary>
    /// <param name="map">
    /// Always assigned, even on <see langword="false"/> -- an explicit
    /// <see cref="MapModel.Unknown"/> sentinel, never a nullable/default value,
    /// matching this codebase's TryXxx convention
    /// (<c>MapGridExtractor.TryInfo</c>, <c>VolumeLocator.TryResolve</c>).
    /// </param>
    /// <returns><see langword="true"/> when a row for <paramref name="mapId"/> was found and deserialized.</returns>
    public bool TryLoad(MapId mapId, out MapModel map)
    {
        // DateTime.MinValue, not DateTime.UtcNow: this sentinel is either
        // overwritten below on a hit, or left for a caller who supplies their
        // own instant for the miss case (see MapReconstructionSource.Resolve).
        // Reading the wall clock here for a value nobody times against would
        // be exactly the kind of determinism leak this project's own
        // postmortems keep finding.
        map = MapModel.Unknown(mapId, "map_not_persisted", DateTime.MinValue);

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT payload FROM map_models WHERE map_id = $mapId LIMIT 1";
            command.Parameters.AddWithValue("$mapId", mapId.Value);

            if (command.ExecuteScalar() is not string json)
                return false;

            MapModelDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<MapModelDto>(json);
            }
            catch (JsonException)
            {
                return false;
            }

            if (dto is null)
                return false;

            map = FromDto(dto);
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS map_models (
                map_id  TEXT PRIMARY KEY,
                payload TEXT NOT NULL
            )
            """);
    }

    private void ApplyPolicyOrThrow(SqliteJournalOptions options)
    {
        string journalMode = ExecuteScalarString($"PRAGMA journal_mode={options.JournalMode}");
        if (!string.Equals(journalMode, options.JournalMode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite journal_mode mismatch: expected '{options.JournalMode}', got '{journalMode}'.");

        Execute($"PRAGMA synchronous={options.Synchronous}");
        long synchronous = ExecuteScalarInt64("PRAGMA synchronous");
        const long fullSynchronous = 2;
        if (synchronous != fullSynchronous)
            throw new InvalidOperationException($"SQLite synchronous mismatch: expected FULL(2), got {synchronous}.");

        Execute($"PRAGMA busy_timeout={options.BusyTimeoutMs}");
        long busyTimeout = ExecuteScalarInt64("PRAGMA busy_timeout");
        if (busyTimeout != options.BusyTimeoutMs)
            throw new InvalidOperationException($"SQLite busy_timeout mismatch: expected {options.BusyTimeoutMs}, got {busyTimeout}.");
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string ExecuteScalarString(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private long ExecuteScalarInt64(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------------------------
    // Full-fidelity DTO conversion, explicit in both directions. Every DTO
    // type below is private to this store: nothing outside it ever sees the
    // serialized shape, only the domain MapModel it round-trips to/from.
    // ----------------------------------------------------------------------

    private static MapModelDto ToDto(MapModel map) => new(
        map.Id.Value,
        ToFactDto(map.Name, static n => n),
        ToFactDto(map.ObservedBounds, static b => new MapBoundsDto(ToDto(b.Min), ToDto(b.Max))),
        map.Tiles.Select(ToDto).ToList(),
        map.Portals.Select(ToDto).ToList(),
        map.Landmarks.Select(ToDto).ToList(),
        map.Version,
        map.ObservedAtUtc);

    private static MapModel FromDto(MapModelDto dto) => new(
        new MapId(dto.Id),
        FromFactDto(dto.Name, static n => n),
        FromFactDto(dto.ObservedBounds, static b => new MapBounds(FromDto(b.Min), FromDto(b.Max))),
        EquatableArray<Tile>.From(dto.Tiles.Select(FromDto)),
        EquatableArray<Portal>.From(dto.Portals.Select(FromDto)),
        EquatableArray<Polygon>.From(dto.Landmarks.Select(FromDto)),
        dto.Version,
        dto.ObservedAtUtc);

    private static TileDto ToDto(Tile tile) => new(
        ToDto(tile.Coordinate),
        ToFactDto(tile.Traversability, static t => (int)t));

    private static Tile FromDto(TileDto dto) => new(
        FromDto(dto.Coordinate),
        FromFactDto(dto.Traversability, static t => (TileTraversability)t));

    private static PortalDto ToDto(Portal portal) => new(
        portal.Id.Value,
        portal.SourceMap.Value,
        ToFactDto(portal.SourcePosition, static p => ToDto(p)),
        ToFactDto(portal.DestinationMap, static m => m.Value),
        ToFactDto(portal.IsActive, static a => a));

    private static Portal FromDto(PortalDto dto) => new(
        new PortalId(dto.Id),
        new MapId(dto.SourceMap),
        FromFactDto(dto.SourcePosition, static p => FromDto(p)),
        FromFactDto(dto.DestinationMap, static m => new MapId(m)),
        FromFactDto(dto.IsActive, static a => a));

    private static PolygonDto ToDto(Polygon polygon) =>
        new(polygon.Vertices.Select(ToDto).ToList());

    private static Polygon FromDto(PolygonDto dto) =>
        new(EquatableArray<WorldPosition>.From(dto.Vertices.Select(FromDto)));

    private static WorldPositionDto ToDto(WorldPosition position) => new(position.X, position.Y);

    private static WorldPosition FromDto(WorldPositionDto dto) => new(dto.X, dto.Y);

    private static TileCoordinateDto ToDto(TileCoordinate coordinate) => new(coordinate.Column, coordinate.Row);

    private static TileCoordinate FromDto(TileCoordinateDto dto) => new(dto.Column, dto.Row);

    /// <summary>
    /// Converts one <see cref="WorldFact{T}"/> to its DTO shape, carrying
    /// <see cref="WorldFact{T}.Source"/>/<see cref="WorldFact{T}.Confidence"/>/
    /// <see cref="WorldFact{T}.ObservedAtUtc"/>/<see cref="WorldFact{T}.HasObservedValue"/>/
    /// <see cref="WorldFact{T}.Reason"/> through unchanged and mapping only the
    /// value, which is never read when the fact has no observed value (mirrors
    /// <see cref="WorldFact{T}.Unknown"/>'s own <c>default!</c> treatment of an
    /// absent value).
    /// </summary>
    private static WorldFactDto<TDto> ToFactDto<TDomain, TDto>(WorldFact<TDomain> fact, Func<TDomain, TDto> mapValue)
    {
        TDto? value = fact.HasObservedValue ? mapValue(fact.Value) : default;
        return new WorldFactDto<TDto>(value, (int)fact.Source, fact.Confidence, fact.ObservedAtUtc, fact.HasObservedValue, fact.Reason);
    }

    /// <inheritdoc cref="ToFactDto{TDomain, TDto}(WorldFact{TDomain}, Func{TDomain, TDto})"/>
    private static WorldFact<TDomain> FromFactDto<TDomain, TDto>(WorldFactDto<TDto> dto, Func<TDto, TDomain> mapValue)
    {
        TDomain value = dto.HasObservedValue ? mapValue(dto.Value!) : default!;
        return new WorldFact<TDomain>(value, (DataSourceKind)dto.Source, dto.Confidence, dto.ObservedAtUtc, dto.HasObservedValue, dto.Reason);
    }

    private sealed record MapModelDto(
        string Id,
        WorldFactDto<string> Name,
        WorldFactDto<MapBoundsDto> ObservedBounds,
        List<TileDto> Tiles,
        List<PortalDto> Portals,
        List<PolygonDto> Landmarks,
        long Version,
        DateTime ObservedAtUtc);

    private sealed record TileDto(TileCoordinateDto Coordinate, WorldFactDto<int> Traversability);

    private sealed record PortalDto(
        string Id,
        string SourceMap,
        WorldFactDto<WorldPositionDto> SourcePosition,
        WorldFactDto<string> DestinationMap,
        WorldFactDto<bool> IsActive);

    private sealed record PolygonDto(List<WorldPositionDto> Vertices);

    private sealed record WorldPositionDto(float X, float Y);

    private sealed record TileCoordinateDto(int Column, int Row);

    private sealed record MapBoundsDto(TileCoordinateDto Min, TileCoordinateDto Max);

    private sealed record WorldFactDto<T>(T? Value, int Source, double Confidence, DateTime ObservedAtUtc, bool HasObservedValue, string? Reason);
}
