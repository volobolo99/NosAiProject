namespace NosAi.Core.WorldModel.Reconstruction;

/// <summary>
/// Merges one <see cref="MapObservationBatch"/> into an existing
/// <see cref="MapModel"/> (docs/ROADMAP_ESECUTIVA.md S:AP-03 "Map
/// Reconstruction"). Pure and stateless, mirroring
/// <c>NosAi.Runtime.WorldModel.Fusion.FactFusion</c> and
/// <c>NosAi.Core.WorldModel.Temporal.WorldModelTemporalEnricher</c>: no I/O,
/// no clock reads other than the caller-supplied instant, safe to call once
/// per fusion tick.
/// </summary>
public static class MapReconstructionFusion
{
    /// <summary>
    /// Folds <paramref name="batch"/> into <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never loses history.</b> A tile or portal already in
    /// <paramref name="current"/> and not re-observed by <paramref name="batch"/>
    /// is carried forward unchanged -- this is what lets a partially explored
    /// map be resumed across sessions (AP-03 Definition of Done) instead of
    /// being reset by every new, smaller observation pass.
    /// </para>
    /// <para>
    /// <b>Newer evidence wins per coordinate/id.</b> When the same
    /// <see cref="TileCoordinate"/> or <see cref="PortalId"/> appears in both
    /// <paramref name="current"/> and <paramref name="batch"/>, the batch's
    /// value replaces the existing one. For today's one real source (the
    /// client's own static map file, via
    /// <c>NosAi.Runtime.WorldModel.Fusion.MapGridObservationProjector</c>) this
    /// is not a real conflict -- the same file always decodes the same way --
    /// but the rule is source-agnostic so a future dynamic source (screen
    /// observation, AP-04 exploration) can correct a stale reading without a
    /// second merge function.
    /// </para>
    /// <para>
    /// <b>Bounds only grow.</b> <see cref="MapModel.ObservedBounds"/> is
    /// recomputed as the union of every tile coordinate in the merged set
    /// (which already includes every previously observed tile) with whatever
    /// bounds <paramref name="current"/> already declared. It can never shrink
    /// from one merge to the next.
    /// </para>
    /// <para>
    /// <b>Idempotent.</b> Merging a batch that changes nothing (already-known
    /// tiles/portals, or an empty batch) returns <paramref name="current"/>
    /// unchanged -- same reference, same <see cref="MapModel.Version"/>. This
    /// matters because this runs once per fusion tick against a map whose
    /// static geometry is typically fully known after the first successful
    /// merge: without this, <see cref="MapModel.Version"/> would grow without
    /// bound on an unchanging map.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="batch"/> is for a different map than <paramref name="current"/>.</exception>
    public static MapModel Merge(MapModel current, MapObservationBatch batch, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(batch);

        if (!current.Id.Equals(batch.MapId))
        {
            throw new ArgumentException(
                $"Batch is evidence for map '{batch.MapId}' but the map being merged into is '{current.Id}'. "
                + "Merging it in would silently attach one map's tiles to another map's identity.",
                nameof(batch));
        }

        if (batch.Tiles.Count == 0 && batch.Portals.Count == 0)
            return current;

        EquatableArray<Tile> mergedTiles = MergeByKey(current.Tiles, batch.Tiles, static tile => tile.Coordinate);
        EquatableArray<Portal> mergedPortals = MergeByKey(current.Portals, batch.Portals, static portal => portal.Id);

        bool tilesChanged = !mergedTiles.Equals(current.Tiles);
        bool portalsChanged = !mergedPortals.Equals(current.Portals);
        if (!tilesChanged && !portalsChanged)
            return current;

        WorldFact<MapBounds> mergedBounds = MergeBounds(current.ObservedBounds, mergedTiles, nowUtc);

        return current with
        {
            Tiles = mergedTiles,
            Portals = mergedPortals,
            ObservedBounds = mergedBounds,
            Version = current.Version + 1,
            ObservedAtUtc = nowUtc,
        };
    }

    /// <summary>
    /// Last-write-wins union keyed by <paramref name="keyOf"/>: every element
    /// of <paramref name="existing"/> is kept unless <paramref name="incoming"/>
    /// re-observes the same key, in which case the incoming element replaces
    /// it. Order is the first-seen order across <paramref name="existing"/>
    /// then <paramref name="incoming"/>, so two merges given the same two
    /// inputs always produce the same sequence.
    /// </summary>
    private static EquatableArray<T> MergeByKey<T, TKey>(
        EquatableArray<T> existing,
        EquatableArray<T> incoming,
        Func<T, TKey> keyOf)
        where TKey : notnull
    {
        var byKey = new Dictionary<TKey, T>(existing.Count + incoming.Count);
        var order = new List<TKey>(existing.Count + incoming.Count);

        foreach (T item in existing)
        {
            TKey key = keyOf(item);
            if (!byKey.ContainsKey(key))
                order.Add(key);
            byKey[key] = item;
        }

        foreach (T item in incoming)
        {
            TKey key = keyOf(item);
            if (!byKey.ContainsKey(key))
                order.Add(key);
            byKey[key] = item;
        }

        var merged = new T[order.Count];
        for (int i = 0; i < order.Count; i++)
            merged[i] = byKey[order[i]];

        return EquatableArray<T>.From(merged);
    }

    /// <summary>
    /// The smallest rectangle covering every tile in <paramref name="mergedTiles"/>,
    /// unioned with whatever <paramref name="currentBounds"/> already declared
    /// so bounds can never shrink even if a caller's <see cref="MapModel"/>
    /// declared a wider rectangle than its tile list currently covers.
    /// </summary>
    private static WorldFact<MapBounds> MergeBounds(
        WorldFact<MapBounds> currentBounds,
        EquatableArray<Tile> mergedTiles,
        DateTime nowUtc)
    {
        if (mergedTiles.Count == 0)
            return currentBounds;

        int minColumn = int.MaxValue, minRow = int.MaxValue;
        int maxColumn = int.MinValue, maxRow = int.MinValue;

        foreach (Tile tile in mergedTiles)
        {
            TileCoordinate coordinate = tile.Coordinate;
            if (coordinate.Column < minColumn) minColumn = coordinate.Column;
            if (coordinate.Row < minRow) minRow = coordinate.Row;
            if (coordinate.Column > maxColumn) maxColumn = coordinate.Column;
            if (coordinate.Row > maxRow) maxRow = coordinate.Row;
        }

        if (currentBounds.HasValue)
        {
            MapBounds prior = currentBounds.Value;
            minColumn = Math.Min(minColumn, prior.Min.Column);
            minRow = Math.Min(minRow, prior.Min.Row);
            maxColumn = Math.Max(maxColumn, prior.Max.Column);
            maxRow = Math.Max(maxRow, prior.Max.Row);
        }

        var bounds = new MapBounds(new TileCoordinate(minColumn, minRow), new TileCoordinate(maxColumn, maxRow));

        // Derived, not Cached/Live: the bounds are computed from tiles that
        // already carry their own provenance, not an independent observation
        // of the rectangle itself.
        return WorldFact<MapBounds>.Derived(bounds, confidence: 1d, nowUtc);
    }
}
