namespace NosAi.Core.WorldModel.Reconstruction;

/// <summary>
/// Raw tile/portal evidence for one map, produced by a single extraction or
/// scan pass, before it is merged into the persistent <see cref="MapModel"/>
/// (docs/ROADMAP_ESECUTIVA.md S:AP-03 "Map Reconstruction"). This is the
/// observation-side counterpart to <see cref="MapModel"/>: the same
/// relationship <c>GameplayObservation</c>/<c>VisualObservation</c> have to
/// <see cref="WorldModelSnapshot"/>. <c>NosAi.Core.WorldModel.Reconstruction.MapReconstructionFusion</c>
/// (AP-03/A3) merges a batch into an existing map; it does not replace it.
/// </summary>
/// <param name="MapId">Which map this evidence is about.</param>
/// <param name="Tiles">
/// Tiles observed in this pass. Each already carries its own
/// <see cref="WorldFact{T}"/> classification (e.g. <c>Cached</c> for
/// client-file geometry) -- this batch adds no classification of its own.
/// </param>
/// <param name="Portals">Portals observed in this pass, same rule as <see cref="Tiles"/>.</param>
/// <param name="SourceDescription">
/// A short human-readable provenance tag for this pass (e.g. "client-map-grid").
/// Not a <see cref="WorldFact{T}"/> itself -- it describes where the batch as a
/// whole came from, while the per-tile/per-portal facts carry their own provenance.
/// </param>
/// <param name="ObservedAtUtc">When this pass ran.</param>
public sealed record MapObservationBatch(
    MapId MapId,
    EquatableArray<Tile> Tiles,
    EquatableArray<Portal> Portals,
    string SourceDescription,
    DateTime ObservedAtUtc)
{
    /// <summary>
    /// A scan pass that produced no evidence (grid not loaded, map
    /// unreachable, ...). Never used to fabricate tiles/portals that were not
    /// actually observed.
    /// </summary>
    public static MapObservationBatch Empty(MapId mapId, string reason, DateTime? observedAtUtc = null) =>
        new(
            mapId,
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            reason,
            observedAtUtc ?? DateTime.UtcNow);
}
