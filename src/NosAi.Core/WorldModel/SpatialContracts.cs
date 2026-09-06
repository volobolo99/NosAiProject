namespace NosAi.Core.WorldModel;

/// <summary>A position in one map's own 2D coordinate space (docs/NOSAI_ARCHITECTURE_BASELINE.md S:3 "Spatial Model").</summary>
public readonly record struct WorldPosition(float X, float Y);

/// <summary>
/// An entity's estimated rate of movement, in world units per second along
/// each axis. Always a <see cref="DataSourceKind.Derived"/> fact -- nothing
/// observes velocity directly, it is computed from two positions and the
/// time between them (see <c>NosAi.Core.WorldModel.Temporal.TemporalBelief.EstimateVelocity</c>).
/// </summary>
public readonly record struct WorldVelocity(float DxPerSecond, float DyPerSecond);

/// <summary>A discrete tile/grid coordinate within one map, distinct from the continuous <see cref="WorldPosition"/> entities move through.</summary>
public readonly record struct TileCoordinate(int Column, int Row);

/// <summary>
/// Whether a tile can be traversed. Has no "Unknown" member by design
/// (mirrors <c>NosAi.Core.Hardware.HardwareThrottleState</c>'s convention):
/// absence of a trustworthy reading is expressed by wrapping this enum in
/// <see cref="WorldFact{T}.Unknown"/> instead, so "we have not observed
/// this tile" can never be confused with "this tile is confirmed walkable".
/// </summary>
public enum TileTraversability
{
    /// <summary>Observed and confirmed traversable by the player.</summary>
    Walkable = 0,

    /// <summary>Observed and confirmed blocked (wall, obstacle, out of bounds).</summary>
    Blocked = 1,

    /// <summary>Observed as traversable but with a known hazard (trap, damage zone, hostile spawn density).</summary>
    Hazardous = 2
}

/// <summary>One reconstructed map tile. Map reconstruction itself (AP-03) decides how tiles are produced; this is only the shape a tile is recorded in.</summary>
public sealed record Tile(
    TileCoordinate Coordinate,
    WorldFact<TileTraversability> Traversability);

/// <summary>
/// A closed region of the map expressed as an ordered vertex loop (a
/// landmark boundary, a hazard zone, an explored-area outline, ...). The
/// loop is not required to be observed in full: an unfinished exploration
/// still yields a valid (possibly open) sequence of observed vertices.
/// </summary>
public sealed record Polygon(EquatableArray<WorldPosition> Vertices);

/// <summary>A transition between two maps.</summary>
public sealed record Portal(
    PortalId Id,
    MapId SourceMap,
    WorldFact<WorldPosition> SourcePosition,
    WorldFact<MapId> DestinationMap,
    WorldFact<bool> IsActive);
