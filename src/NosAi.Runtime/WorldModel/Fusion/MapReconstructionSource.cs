using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Observability;
using NosAi.Storage;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// AP-03/A4: the runtime glue that turns the client's own static map grid
/// into the persistent, incrementally-built <see cref="MapModel"/> the World
/// Model carries, once per distinct map instead of never -- and, once a
/// map's reconstruction is known, without repeating the work on every
/// fusion tick. Owns the expensive path: resolving the maps directory,
/// loading and projecting a <see cref="MapGrid"/>
/// (<see cref="MapGridObservationProjector"/>, AP-03/A2), merging it into
/// whatever was already known (<see cref="MapReconstructionFusion"/>,
/// AP-03/A3) and persisting the result (<see cref="MapModelStore"/>,
/// AP-03/A4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Caching is not an optimization here, it is the design.</b>
/// <see cref="WorldModel.Fusion.WorldModelFusionLoop"/> ticks roughly every
/// 500ms (<see cref="WorldModel.Fusion.WorldModelFusionLoop.DefaultInterval"/>)
/// for the lifetime of a session on one map, and the client's static grid
/// never changes mid-session. <see cref="Resolve"/> reconstructs a map's
/// evidence exactly once per distinct <see cref="MapId"/>: the first call for
/// a given map runs the full pipeline (grid lookup, projection, merge,
/// persist) and every subsequent call for the *same* map id returns the
/// cached in-memory result unchanged, touching neither the grid file nor the
/// SQLite store again, until the map id itself changes. Re-projecting a
/// several-thousand-cell grid and touching SQLite twice a second forever
/// would be exactly the kind of unbounded per-tick cost
/// docs/NOSAI_ARCHITECTURE_BASELINE.md S:9 rules out.
/// </para>
/// <para>
/// <b>The map id convention.</b> The World Model's <see cref="MapId"/> is
/// produced by <c>GameplayObservationProjector.Project</c> as literally
/// <c>$"map-{numericId}"</c>, or the sentinel <c>"unknown-map"</c> when no map
/// is observed yet. <see cref="MapGridExtractor"/>/<see cref="MapGridObservationProjector"/>
/// need the client's raw numeric id, so <see cref="Resolve"/> parses it back
/// out of that exact prefix and never guesses a numeric id for anything that
/// does not match -- including the unknown-map sentinel, which is treated
/// identically to "no real map known this cycle".
/// </para>
/// <para>
/// <b>Fail-soft at every step, same discipline as <c>ScreenVitalsCapture</c>
/// (AP-02/A4) -- <see cref="Resolve"/> never throws.</b> No dedicated maps
/// volume, no extracted grid for this map id, or no persistent store (missing
/// volume, or the database could not be opened) all degrade to the next best
/// available answer -- an empty observation batch merged against whatever was
/// already known, or an in-memory-only session -- rather than failing the
/// fusion cycle. A map id that does not parse returns the caller's own
/// <see cref="WorldModelSnapshot.Map"/> unchanged and touches neither the
/// store nor the cache.
/// </para>
/// <para>
/// <b>No lock.</b> Like <see cref="WorldModel.Fusion.WorldModelFusionLoop"/>'s
/// own <c>_visualSource</c>, <see cref="Resolve"/> is called strictly
/// sequentially by that loop's single pump; this type does not protect
/// against concurrent calls from multiple threads, which nothing in this
/// runtime does.
/// </para>
/// </remarks>
public sealed class MapReconstructionSource : IDisposable
{
    /// <summary>The <see cref="SqliteJournalOptions.FileName"/> used when the caller does not supply <see cref="SqliteJournalOptions"/> of their own.</summary>
    public const string DefaultDatabaseFileName = "nosai-maps.db";

    /// <summary>Reason recorded when the dedicated maps volume/directory is not available this cycle.</summary>
    public const string MapsDirectoryNotAvailableReason = "maps_directory_not_available";

    /// <summary>Reason recorded when the current map's <c>.grid</c> file could not be loaded (not extracted, corrupt).</summary>
    public const string MapGridNotAvailableReason = "map_grid_not_available";

    /// <summary>Reason recorded when nothing has ever been persisted for a map id yet.</summary>
    public const string MapNotPersistedReason = "map_not_persisted";

    private const string MapIdPrefix = "map-";

    private readonly string? _mapsDirectoryOverride;
    private readonly IRuntimeLogger? _logger;
    private readonly MapModelStore? _store;

    private MapId? _cachedMapId;
    private MapModel? _cachedResult;
    private bool _disposed;

    /// <param name="storeOptions">
    /// Options for the persistent map store. Defaults to
    /// <see cref="SqliteJournalOptions"/> with <see cref="DefaultDatabaseFileName"/>
    /// (same <c>NOSAI-SSD</c> volume label the Gate 1 journal uses, a
    /// different file). Opened via <see cref="MapModelStore.OpenFromVolume"/>;
    /// if that throws (missing volume, database cannot be opened), this
    /// constructor catches it and continues with no store -- reconstruction
    /// still works for the lifetime of this process, it just cannot survive a
    /// restart this run.
    /// </param>
    /// <param name="mapsDirectoryOverride">
    /// Overrides <see cref="MapGridExtractor.TryResolveDedicatedMapsDirectory"/>.
    /// Production leaves this <see langword="null"/>; tests supply a temp
    /// directory of their own.
    /// </param>
    /// <param name="logger">Logs the store-open failure above, if any, and any later persist failure. Optional.</param>
    public MapReconstructionSource(
        SqliteJournalOptions? storeOptions = null,
        string? mapsDirectoryOverride = null,
        IRuntimeLogger? logger = null)
    {
        _mapsDirectoryOverride = mapsDirectoryOverride;
        _logger = logger;

        SqliteJournalOptions effectiveOptions = storeOptions ?? new SqliteJournalOptions(FileName: DefaultDatabaseFileName);
        try
        {
            _store = MapModelStore.OpenFromVolume(effectiveOptions);
        }
        catch (Exception ex)
        {
            _store = null;
            _logger?.Error(
                "MapReconstructionSource could not open its persistent map store; reconstruction will run in-memory only for this process.",
                ex);
        }
    }

    /// <summary>
    /// Reads the current map id off <paramref name="networkSnapshot"/>.Map.Id
    /// and returns the best available <see cref="MapModel"/> for it: the
    /// cached in-memory result if this is the same map as the last call;
    /// otherwise loads any persisted <see cref="MapModel"/>, projects and
    /// merges fresh grid evidence, persists and caches the result. Returns
    /// <paramref name="networkSnapshot"/>.Map unchanged -- never throws, never
    /// fabricates -- when no real map id is known this cycle.
    /// </summary>
    /// <param name="networkSnapshot">This cycle's in-progress fused snapshot, already carrying its own network-projected <see cref="WorldModelSnapshot.Map"/>.Id.</param>
    /// <param name="nowUtc">The instant this cycle runs at, used for every fact this call produces or defaults.</param>
    public MapModel Resolve(WorldModelSnapshot networkSnapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(networkSnapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        MapId mapId = networkSnapshot.Map.Id;
        if (!TryParseNumericMapId(mapId, out int numericMapId))
            return networkSnapshot.Map;

        if (_cachedMapId is { } cachedId && cachedId.Equals(mapId) && _cachedResult is { } cached)
            return cached;

        MapModel baseline = LoadPersistedOrUnknown(mapId, nowUtc);
        MapObservationBatch batch = ProjectGridEvidence(mapId, numericMapId, nowUtc);
        MapModel reconstructed = MapReconstructionFusion.Merge(baseline, batch, nowUtc);

        PersistIfPossible(reconstructed);

        _cachedMapId = mapId;
        _cachedResult = reconstructed;
        return reconstructed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store?.Dispose();
    }

    private MapModel LoadPersistedOrUnknown(MapId mapId, DateTime nowUtc)
    {
        if (_store is null)
            return MapModel.Unknown(mapId, MapNotPersistedReason, nowUtc);

        try
        {
            if (_store.TryLoad(mapId, out MapModel persisted))
                return persisted;
        }
        catch (Exception ex)
        {
            // Mirrors PersistIfPossible's treatment of the write path: a
            // store that cannot be read this cycle is a missed read, not a
            // reason for Resolve to throw. The reconstruction pipeline
            // still runs against an honest Unknown baseline below.
            _logger?.Error("MapReconstructionSource failed to load a persisted map; reconstructing from an Unknown baseline for this cycle.", ex);
        }

        return MapModel.Unknown(mapId, MapNotPersistedReason, nowUtc);
    }

    private MapObservationBatch ProjectGridEvidence(MapId mapId, int numericMapId, DateTime nowUtc)
    {
        string mapsDirectory;
        if (_mapsDirectoryOverride is not null)
        {
            mapsDirectory = _mapsDirectoryOverride;
        }
        else if (!MapGridExtractor.TryResolveDedicatedMapsDirectory(out mapsDirectory, out string? volumeReason))
        {
            return MapObservationBatch.Empty(mapId, volumeReason ?? MapsDirectoryNotAvailableReason, nowUtc);
        }

        if (!MapGridExtractor.TryInfo(mapsDirectory, numericMapId, out MapGrid grid, out _, out string? failureReason))
            return MapObservationBatch.Empty(mapId, failureReason ?? MapGridNotAvailableReason, nowUtc);

        return MapGridObservationProjector.Project(in grid, mapId, nowUtc);
    }

    private void PersistIfPossible(MapModel map)
    {
        if (_store is null)
            return;

        try
        {
            _store.Save(map);
        }
        catch (Exception ex)
        {
            _logger?.Error("MapReconstructionSource failed to persist a reconstructed map; continuing in-memory for this session.", ex);
        }
    }

    /// <summary>
    /// Parses the client's raw numeric map id out of the <c>"map-{numericId}"</c>
    /// convention <c>GameplayObservationProjector.Project</c> produces.
    /// Anything that does not match this exact prefix -- including the
    /// <c>"unknown-map"</c> sentinel -- is "no real map known this cycle",
    /// never a numeric id to guess at.
    /// </summary>
    private static bool TryParseNumericMapId(MapId mapId, out int numericMapId)
    {
        numericMapId = 0;
        string value = mapId.Value;
        if (!value.StartsWith(MapIdPrefix, StringComparison.Ordinal))
            return false;

        return int.TryParse(
            value.AsSpan(MapIdPrefix.Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out numericMapId);
    }
}
