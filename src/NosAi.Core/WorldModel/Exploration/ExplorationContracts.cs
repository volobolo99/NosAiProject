namespace NosAi.Core.WorldModel.Exploration;

/// <summary>
/// Which tiles of a map the player has actually visited
/// (docs/ROADMAP_ESECUTIVA.md S:AP-04 "Exploration"). Distinct from
/// <see cref="MapModel.Tiles"/>: AP-03's one real tile source (the client's
/// own static map grid) reveals a map's full walkability in a single pass,
/// so "which tiles are geometrically unknown" is not a meaningful frontier
/// signal here. "Which tiles has the player actually been near" is the
/// signal exploration needs instead -- an unvisited walkable tile is a
/// frontier candidate, a visited one is not, regardless of whether its
/// geometry has been known since the map's first reconstruction.
/// </summary>
/// <param name="MapId">Which map this footprint is about.</param>
/// <param name="VisitedTiles">
/// Tiles the player has been observed on or adjacent to, at least once.
/// Deriving this from player position history is AP-04/A3's job; this
/// contract only defines the shape.
/// </param>
/// <param name="FullyExplored">
/// Whether every walkable tile in the map's currently known bounds has been
/// visited. <c>Unknown</c> until an exploration algorithm actually evaluates
/// it -- never assumed true by omission.
/// </param>
/// <param name="ObservedAtUtc">When this footprint was last updated.</param>
public sealed record ExplorationFootprint(
    MapId MapId,
    EquatableArray<TileCoordinate> VisitedTiles,
    WorldFact<bool> FullyExplored,
    DateTime ObservedAtUtc)
{
    /// <summary>No tiles visited yet -- before the player has ever been observed on this map.</summary>
    public static ExplorationFootprint Empty(MapId mapId, string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new ExplorationFootprint(
            mapId,
            EquatableArray<TileCoordinate>.Empty,
            WorldFact<bool>.Unknown(reason, now),
            now);
    }
}

/// <summary>
/// One candidate region worth exploring next, with the raw inputs a ranking
/// algorithm needs (docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md S:4.4:
/// "The agent chooses frontier regions using information gain, travel cost,
/// risk and mission relevance"). This contract carries only those inputs --
/// scoring and selecting among candidates is AP-04/A3's algorithm, not
/// something this type computes itself.
/// </summary>
/// <param name="MapId">Which map the candidate is on.</param>
/// <param name="Coordinate">The candidate tile.</param>
/// <param name="InformationGain">
/// A non-negative proxy for how much unvisited area this candidate is
/// expected to reveal (e.g. count of nearby unvisited walkable tiles). Not
/// normalized to [0, 1]: callers compare candidates from the same ranking
/// pass against each other, not against a fixed scale.
/// </param>
/// <param name="TravelCost">A non-negative proxy for how far/expensive reaching this candidate is.</param>
/// <param name="Risk">A non-negative proxy for expected danger near this candidate (e.g. nearby hostile density).</param>
/// <param name="MissionRelevance">A signed proxy for how much this candidate serves the current strategic goal; zero when exploration has no active mission tie-in.</param>
public sealed record FrontierCandidate(
    MapId MapId,
    TileCoordinate Coordinate,
    double InformationGain,
    double TravelCost,
    double Risk,
    double MissionRelevance);

/// <summary>
/// One leg of a hierarchical route: move to <see cref="Position"/> on
/// <see cref="MapId"/>, then optionally use <see cref="UsePortal"/> to leave
/// this map for the next waypoint's.
/// </summary>
/// <remarks>
/// This is deliberately coarse -- a handful of waypoints per map, not a
/// cell-by-cell path. Per-cell walking, stall/displacement detection,
/// replanning and step verification are not re-specified here: they are
/// already real, tested and Gate-1-verified in
/// <c>NosAi.Runtime.Navigation.PathWalkController</c>/<c>MovementVerifier</c>/
/// <c>StepGuardChain</c>. A <see cref="NavigationPlan"/> says where the
/// strategic layer wants to go and in what order; that existing machinery
/// is what actually gets the character from one waypoint to the next.
/// </remarks>
/// <param name="MapId">The map this waypoint is on.</param>
/// <param name="Position">Where on that map to move to.</param>
/// <param name="UsePortal">
/// The portal to take after reaching <see cref="Position"/>, when this
/// waypoint is not the route's final one and requires a map transition.
/// <see langword="null"/> for the final waypoint, or for an intermediate
/// waypoint that stays on the same map as the next one.
/// </param>
public sealed record NavigationWaypoint(
    MapId MapId,
    WorldPosition Position,
    PortalId? UsePortal);

/// <summary>
/// A coarse, possibly multi-map route from the strategic layer down to
/// execution (docs/NOSAI_ARCHITECTURE_BASELINE.md S:8 navigation policy:
/// "global graph/navmesh -&gt; local path corridor"). <see cref="Waypoints"/>
/// is the global/coarse half; the local half is
/// <c>NosAi.Runtime.Navigation.PathWalkController</c>, already real.
/// </summary>
/// <param name="Waypoints">
/// The route, in travel order. Empty exactly when <see cref="IsReachable"/>
/// does not have a value or is <see langword="false"/>.
/// </param>
/// <param name="IsReachable">
/// Whether a route was actually found. <c>Unknown</c>/<see langword="false"/>
/// both mean "do not attempt this plan" -- callers must check
/// <see cref="WorldFact{T}.HasValue"/> and <see cref="WorldFact{T}.Value"/>
/// together, not assume an empty <see cref="Waypoints"/> alone means
/// unreachable (an <c>Unknown</c> plan and an established-unreachable plan
/// are different facts with the same empty waypoint list).
/// </param>
/// <param name="ObservedAtUtc">When this plan was produced.</param>
public sealed record NavigationPlan(
    EquatableArray<NavigationWaypoint> Waypoints,
    WorldFact<bool> IsReachable,
    DateTime ObservedAtUtc)
{
    /// <summary>No route could be found, or none has been attempted yet.</summary>
    public static NavigationPlan Unreachable(string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new NavigationPlan(
            EquatableArray<NavigationWaypoint>.Empty,
            WorldFact<bool>.Unknown(reason, now),
            now);
    }
}
