namespace NosAi.Core.WorldModel;

/// <summary>The observed rectangular bounds of a map, in tile coordinates. Not necessarily the map's true full extent -- only what has been observed so far (AP-03 grows this incrementally).</summary>
public readonly record struct MapBounds(TileCoordinate Min, TileCoordinate Max);

/// <summary>
/// A persistent, versioned, incrementally-built map (docs/ROADMAP_ESECUTIVA.md
/// S:AP-03 owns the reconstruction algorithm; this is only the semantic
/// contract AP-01 needs so a partially-explored map can already be carried
/// in the World Model). <see cref="Version"/> increases monotonically each
/// time this map is updated with new observations, mirroring the versioning
/// convention already used by <c>NosAi.Core.WorldState.Version</c>.
/// </summary>
public sealed record MapModel(
    MapId Id,
    WorldFact<string> Name,
    WorldFact<MapBounds> ObservedBounds,
    EquatableArray<Tile> Tiles,
    EquatableArray<Portal> Portals,
    EquatableArray<Polygon> Landmarks,
    long Version,
    DateTime ObservedAtUtc)
{
    /// <summary>An empty map, before any observation of it exists. Never used to fabricate tiles/portals/landmarks that were not actually observed.</summary>
    public static MapModel Unknown(MapId id, string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new MapModel(
            id,
            WorldFact<string>.Unknown(reason, now),
            WorldFact<MapBounds>.Unknown(reason, now),
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 0,
            now);
    }
}
