namespace NosAi.Core.WorldModel.Exploration;

/// <summary>
/// Plans a route across possibly more than one map, using only portals
/// this repository has actually observed a real crossing of.
/// <see cref="ExplorationPlanner"/>'s own remarks declared this out of
/// scope because "no real portal-connectivity data source exists
/// anywhere in this repository yet" -- <c>PortalCrossingDetector</c> and
/// <c>MapReconstructionSource.RecordPortalCrossing</c> (AP-04,
/// docs/agents/EXECUTION_QUEUE.md Q-070/Q-071) now give
/// <see cref="MapModel.Portals"/> real, non-empty content once an
/// operator has actually crossed a boundary during
/// <c>--scout</c>/<c>--autoplay</c>. This type is the algorithm that
/// consumes that data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never fabricates connectivity.</b> A map pair with no portal ever
/// observed between them -- directly or through a chain -- is reported
/// <see cref="NavigationPlan.Unreachable"/>, never guessed at. A portal
/// whose own facts are not confidently known (<see cref="Portal.IsActive"/>
/// not confirmed true, or <see cref="Portal.SourcePosition"/>/
/// <see cref="Portal.DestinationMap"/> not yet observed) is not used as an
/// edge: an <c>Unknown</c> <see cref="Portal.IsActive"/> is not treated as
/// "probably usable" (docs/ROADMAP_ESECUTIVA.md invariant "Unknown is not
/// zero, false or empty").
/// </para>
/// <para>
/// <b>Directed, not assumed bidirectional.</b> <see cref="PortalCrossingDetector"/>
/// records only the direction actually crossed -- the return trip is a
/// different <see cref="Portal.Id"/> (derived from the other map's own
/// rounded position) recorded independently, if and when it is itself
/// observed. This planner never synthesizes a reverse edge from a
/// forward one: some NosTale transitions are genuinely one-way, and
/// assuming otherwise would be exactly the kind of fabricated data this
/// project's anti-mock discipline forbids.
/// </para>
/// <para>
/// Pure and stateless, like <see cref="ExplorationPlanner"/> and
/// <c>NosAi.Core.WorldModel.Reconstruction.MapReconstructionFusion</c>:
/// <paramref name="knownMaps" /> (see <see cref="PlanRoute"/>) is a
/// caller-supplied snapshot, never loaded or cached by this type itself.
/// </para>
/// </remarks>
public static class MultiMapRoutePlanner
{
    /// <summary>No chain of confirmed-usable portals connects the two maps in <paramref name="knownMaps"/>.</summary>
    public const string NoRouteKnownReason = "no_portal_route_known";

    /// <summary>
    /// The shortest (fewest map transitions) route from
    /// <paramref name="startPosition"/> on <paramref name="startMap"/> to
    /// <paramref name="destinationPosition"/> on <paramref name="destinationMap"/>,
    /// using only portals already observed in <paramref name="knownMaps"/>.
    /// </summary>
    /// <param name="knownMaps">
    /// Every map's currently reconstructed <see cref="MapModel"/> the caller
    /// has resolved this cycle, keyed by <see cref="MapModel.Id"/>. A map
    /// with no entry here contributes no outgoing edges, even if a portal
    /// from it was observed in an earlier session and persisted elsewhere --
    /// this method only ever sees what the caller hands it.
    /// </param>
    /// <param name="startMap">The map the route starts on.</param>
    /// <param name="startPosition">Where on <paramref name="startMap"/> the route starts.</param>
    /// <param name="destinationMap">The map the route ends on.</param>
    /// <param name="destinationPosition">Where on <paramref name="destinationMap"/> the route ends.</param>
    /// <param name="nowUtc">The instant this plan is produced at.</param>
    /// <returns>
    /// A same-map <see cref="NavigationPlan"/> when <paramref name="startMap"/>
    /// equals <paramref name="destinationMap"/> (no portal needed); a plan
    /// with one waypoint per map transition when a route exists (the last
    /// portal-using waypoint's <see cref="NavigationWaypoint.UsePortal"/>
    /// leaves the caller on <paramref name="destinationMap"/>, where a
    /// final waypoint at <paramref name="destinationPosition"/> with no
    /// portal closes the plan); otherwise
    /// <see cref="NavigationPlan.Unreachable"/> with <see cref="NoRouteKnownReason"/>.
    /// </returns>
    public static NavigationPlan PlanRoute(
        IReadOnlyDictionary<MapId, MapModel> knownMaps,
        MapId startMap,
        WorldPosition startPosition,
        MapId destinationMap,
        WorldPosition destinationPosition,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(knownMaps);

        if (startMap.Equals(destinationMap))
        {
            var sameMapWaypoint = new NavigationWaypoint(startMap, destinationPosition, UsePortal: null);
            return new NavigationPlan(
                EquatableArray<NavigationWaypoint>.From(new[] { sameMapWaypoint }),
                WorldFact<bool>.Derived(true, confidence: 1d, nowUtc),
                nowUtc);
        }

        List<Portal>? chain = FindShortestPortalChain(knownMaps, startMap, destinationMap);
        if (chain is null)
            return NavigationPlan.Unreachable(NoRouteKnownReason, nowUtc);

        var waypoints = new NavigationWaypoint[chain.Count + 1];
        for (int i = 0; i < chain.Count; i++)
        {
            Portal portal = chain[i];
            waypoints[i] = new NavigationWaypoint(portal.SourceMap, portal.SourcePosition.Value, portal.Id);
        }
        waypoints[chain.Count] = new NavigationWaypoint(destinationMap, destinationPosition, UsePortal: null);

        return new NavigationPlan(
            EquatableArray<NavigationWaypoint>.From(waypoints),
            WorldFact<bool>.Derived(true, confidence: 1d, nowUtc),
            nowUtc);
    }

    /// <summary>
    /// Breadth-first search over observed portal edges: every edge has the
    /// same (unit) cost, so BFS already finds the fewest-transition route,
    /// no separate distance table needed. Deterministic: <paramref name="knownMaps"/>'
    /// own <see cref="EquatableArray{T}"/> order decides which edge wins a
    /// tie, so the same inputs always produce the same chain.
    /// </summary>
    private static List<Portal>? FindShortestPortalChain(
        IReadOnlyDictionary<MapId, MapModel> knownMaps,
        MapId startMap,
        MapId destinationMap)
    {
        var visited = new HashSet<MapId> { startMap };
        var cameFrom = new Dictionary<MapId, Portal>();
        var queue = new Queue<MapId>();
        queue.Enqueue(startMap);

        while (queue.Count > 0)
        {
            MapId current = queue.Dequeue();
            if (current.Equals(destinationMap))
                return ReconstructChain(cameFrom, startMap, destinationMap);

            if (!knownMaps.TryGetValue(current, out MapModel? map))
                continue;

            foreach (Portal portal in map.Portals)
            {
                if (!portal.SourceMap.Equals(current) || !IsUsableEdge(portal))
                    continue;

                MapId neighbor = portal.DestinationMap.Value;
                if (!visited.Add(neighbor))
                    continue;

                cameFrom[neighbor] = portal;
                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    private static List<Portal> ReconstructChain(Dictionary<MapId, Portal> cameFrom, MapId startMap, MapId destinationMap)
    {
        var chain = new List<Portal>();
        MapId current = destinationMap;
        while (!current.Equals(startMap))
        {
            Portal portal = cameFrom[current];
            chain.Add(portal);
            current = portal.SourceMap;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// A portal is only usable as a routing edge once every fact it needs to
    /// be followed is actually confirmed -- an <c>Unknown</c>
    /// <see cref="Portal.IsActive"/> (a crossing observed, but never
    /// confirmed still open) is not treated as "probably still usable".
    /// </summary>
    private static bool IsUsableEdge(Portal portal) =>
        portal.SourcePosition.HasValue
        && portal.DestinationMap.HasValue
        && portal.IsActive.HasValue
        && portal.IsActive.Value;
}
