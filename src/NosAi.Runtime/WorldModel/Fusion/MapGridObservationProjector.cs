using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Runtime.Navigation;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Converts the client's own static map geometry (<see cref="MapGrid"/>, read
/// once per client build from its map archive -- see
/// <see cref="MapGridExtractor"/>/<see cref="BinaryMapGridLoader"/>) into the
/// observation-side contract Map Reconstruction (AP-03) merges into the
/// persistent <see cref="MapModel"/> via
/// <c>NosAi.Core.WorldModel.Reconstruction.MapReconstructionFusion</c>. Pure
/// and stateless: given the same grid, map id and instant it always produces
/// the same batch.
/// </summary>
/// <remarks>
/// Every emitted <see cref="Tile"/> is classified
/// <see cref="DataSourceKind.Cached"/>, never <c>Live</c> -- exactly the
/// classification <see cref="MapGrid"/>'s own documentation already assigns
/// it ("A grid is CACHED with provenance 'client file', never LIVE"). No
/// portal is ever emitted here: the grid carries walkability and
/// line-of-sight bits, not portal identity or destination, so fabricating one
/// from grid data alone would be exactly the kind of invented fact this
/// project's Unknown discipline exists to prevent. A future portal source
/// (e.g. a parsed portal table) can add them to a later batch without
/// touching this projector.
/// </remarks>
public static class MapGridObservationProjector
{
    /// <summary>
    /// One <see cref="Tile"/> per cell in <paramref name="grid"/>'s rectangle,
    /// or <see cref="MapObservationBatch.Empty(MapId, string, DateTime?)"/>
    /// when no grid is loaded.
    /// </summary>
    /// <remarks>
    /// Emits every cell of an already fully loaded grid in one pass: the
    /// client's static geometry file has no partial-read concept, unlike a
    /// future dynamic/screen-observed source. Callers on the runtime critical
    /// path (AP-03/A4) are expected to call this once per distinct map id
    /// rather than once per fusion tick -- the grid never changes for the
    /// lifetime of a client build, so re-projecting it on every tick would be
    /// pure waste on a large map. Bounding call frequency is the caller's
    /// responsibility; this projector has no cache of its own because caching
    /// here would hide that decision instead of making it explicit at the
    /// call site.
    /// </remarks>
    public static MapObservationBatch Project(in MapGrid grid, MapId mapId, DateTime observedAtUtc)
    {
        if (!grid.IsLoaded)
            return MapObservationBatch.Empty(mapId, "map_grid_not_loaded", observedAtUtc);

        var tiles = new Tile[grid.CellCount];
        int index = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                TileTraversability traversability = grid.IsWalkable(x, y)
                    ? TileTraversability.Walkable
                    : TileTraversability.Blocked;

                tiles[index++] = new Tile(
                    new TileCoordinate(x, y),
                    WorldFact<TileTraversability>.Cached(traversability, confidence: 1d, observedAtUtc, reason: "client-map-grid"));
            }
        }

        return new MapObservationBatch(
            mapId,
            EquatableArray<Tile>.From(tiles),
            EquatableArray<Portal>.Empty,
            "client-map-grid",
            observedAtUtc);
    }
}
