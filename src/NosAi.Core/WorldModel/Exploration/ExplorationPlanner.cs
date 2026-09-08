namespace NosAi.Core.WorldModel.Exploration;

/// <summary>
/// Turns a <see cref="MapModel"/> plus player position/entity history into
/// an updated <see cref="ExplorationFootprint"/>, a ranked set of
/// <see cref="FrontierCandidate"/>s and a single-waypoint
/// <see cref="NavigationPlan"/> toward the best one (docs/ROADMAP_ESECUTIVA.md
/// S:AP-04 "Exploration", docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md S:4.4: "The
/// agent chooses frontier regions using information gain, travel cost, risk
/// and mission relevance"). Pure and stateless, mirroring
/// <c>NosAi.Core.WorldModel.Reconstruction.MapReconstructionFusion</c> and
/// <c>NosAi.Core.WorldModel.Temporal.WorldModelTemporalEnricher</c>: no I/O,
/// no clock reads other than the caller-supplied instant, safe to call once
/// per fusion/planning tick.
/// </summary>
/// <remarks>
/// <b>Scope, honestly restricted (docs/agents/phases/AP-04/AP-04_A1_STATUS.md):</b>
/// every <see cref="NavigationPlan"/> this class builds stays on one map --
/// <see cref="BuildNavigationPlan"/>'s <see cref="NavigationWaypoint.UsePortal"/>
/// is always <see langword="null"/>. This class deliberately never chains
/// across a portal to a different map: frontier selection is a same-map
/// concept (a candidate tile's <see cref="FrontierCandidate.MapId"/> is
/// always the map already being explored). A route across more than one
/// map, once real portal-connectivity data exists to route through
/// (docs/agents/EXECUTION_QUEUE.md Q-070/Q-071), is a separate concern --
/// see <see cref="MultiMapRoutePlanner"/>.
/// </remarks>
public static class ExplorationPlanner
{
    /// <summary>
    /// How far (in tiles, Chebyshev box) <see cref="BuildFrontierCandidates"/>
    /// looks around a candidate to estimate its information gain.
    /// </summary>
    public const int InformationGainRadius = 3;

    /// <summary>
    /// How close (in tiles, Euclidean) a hostile, living, positioned mob has
    /// to be to a candidate before it contributes to that candidate's risk.
    /// Contribution decays linearly to zero at this distance.
    /// </summary>
    public const double RiskRadius = 5.0;

    /// <summary>The tile a continuous world position falls inside, by flooring each axis.</summary>
    public static TileCoordinate ToTileCoordinate(WorldPosition position) =>
        new((int)Math.Floor(position.X), (int)Math.Floor(position.Y));

    /// <summary>
    /// Folds the player's current position into <paramref name="previous"/>,
    /// marking its tile visited if it was not already, and recomputes
    /// <see cref="ExplorationFootprint.FullyExplored"/> against
    /// <paramref name="map"/>'s currently known walkable tiles.
    /// </summary>
    /// <param name="previous">The footprint from the prior cycle, for the same map as <paramref name="map"/>.</param>
    /// <param name="map">This cycle's reconstructed map (AP-03).</param>
    /// <param name="playerPosition">
    /// This cycle's player position. <c>Unknown</c> leaves
    /// <paramref name="previous"/>'s visited tiles unchanged -- an
    /// unobserved position is never treated as "still at the last known
    /// tile" here, since that would let a stale position keep re-marking the
    /// same tile visited forever.
    /// </param>
    /// <param name="nowUtc">The instant this cycle runs at.</param>
    /// <exception cref="ArgumentException"><paramref name="map"/> is for a different map than <paramref name="previous"/>.</exception>
    public static ExplorationFootprint UpdateFootprint(
        ExplorationFootprint previous,
        MapModel map,
        WorldFact<WorldPosition> playerPosition,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(map);

        if (!previous.MapId.Equals(map.Id))
        {
            throw new ArgumentException(
                $"Footprint is for map '{previous.MapId}' but the map given is '{map.Id}'. "
                + "Updating it against a different map's tiles would silently attach one map's visited history to another map's identity.",
                nameof(map));
        }

        EquatableArray<TileCoordinate> visited = previous.VisitedTiles;
        if (playerPosition.HasValue)
        {
            TileCoordinate currentTile = ToTileCoordinate(playerPosition.Value);
            if (!ContainsCoordinate(visited, currentTile))
                visited = AppendCoordinate(visited, currentTile);
        }

        WorldFact<bool> fullyExplored = ComputeFullyExplored(map, visited, nowUtc);

        if (visited.Equals(previous.VisitedTiles) && fullyExplored.Equals(previous.FullyExplored))
            return previous;

        return previous with
        {
            VisitedTiles = visited,
            FullyExplored = fullyExplored,
            ObservedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Every currently-walkable, not-yet-visited tile of <paramref name="map"/>,
    /// each carrying the raw scoring inputs <see cref="SelectNextFrontier"/>
    /// needs. Order matches <paramref name="map"/>.Tiles' own order, which is
    /// already deterministic (AP-03's projector emits tiles in a fixed
    /// order) -- so the same inputs always produce the same candidate list.
    /// </summary>
    /// <param name="map">This cycle's reconstructed map.</param>
    /// <param name="footprint">The current footprint for the same map, used to exclude already-visited tiles.</param>
    /// <param name="playerPosition">Where to measure <see cref="FrontierCandidate.TravelCost"/> from.</param>
    /// <param name="mobs">
    /// This cycle's known mobs. Only hostile, alive, positioned mobs
    /// contribute to <see cref="FrontierCandidate.Risk"/>; an entity missing
    /// any of those facts is treated as not contributing risk, never as
    /// confirmed non-hostile/dead by omission.
    /// </param>
    /// <param name="missionFocus">
    /// The current strategic goal's target position, if one is active and
    /// known. <see langword="null"/> yields <see cref="FrontierCandidate.MissionRelevance"/>
    /// of exactly zero for every candidate, per that field's contract.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="map"/> is for a different map than <paramref name="footprint"/>.</exception>
    public static IReadOnlyList<FrontierCandidate> BuildFrontierCandidates(
        MapModel map,
        ExplorationFootprint footprint,
        WorldPosition playerPosition,
        EquatableArray<Mob> mobs,
        WorldPosition? missionFocus = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(footprint);

        if (!map.Id.Equals(footprint.MapId))
        {
            throw new ArgumentException(
                $"Footprint is for map '{footprint.MapId}' but the map given is '{map.Id}'. "
                + "Scoring frontier candidates against a different map's footprint would silently mix two maps' exploration state.",
                nameof(footprint));
        }

        if (map.Tiles.Count == 0)
            return Array.Empty<FrontierCandidate>();

        var tilesByCoordinate = new Dictionary<TileCoordinate, Tile>(map.Tiles.Count);
        foreach (Tile tile in map.Tiles)
            tilesByCoordinate[tile.Coordinate] = tile;

        var visited = new HashSet<TileCoordinate>();
        foreach (TileCoordinate coordinate in footprint.VisitedTiles)
            visited.Add(coordinate);

        var candidates = new List<FrontierCandidate>();
        foreach (Tile tile in map.Tiles)
        {
            if (!IsWalkable(tile) || visited.Contains(tile.Coordinate))
                continue;

            candidates.Add(new FrontierCandidate(
                map.Id,
                tile.Coordinate,
                InformationGain: ComputeInformationGain(tile.Coordinate, tilesByCoordinate, visited),
                TravelCost: ComputeDistance(tile.Coordinate, playerPosition),
                Risk: ComputeRisk(tile.Coordinate, mobs),
                MissionRelevance: missionFocus is { } focus ? -ComputeDistance(tile.Coordinate, focus) : 0d));
        }

        return candidates;
    }

    /// <summary>
    /// The single highest-scoring candidate under <paramref name="weights"/>,
    /// or <see langword="null"/> when <paramref name="candidates"/> is empty
    /// (nothing left to explore, or nothing walkable was ever reconstructed).
    /// Ties keep whichever candidate appears first in
    /// <paramref name="candidates"/> -- combined with
    /// <see cref="BuildFrontierCandidates"/>'s deterministic order, two calls
    /// given the same inputs always pick the same candidate.
    /// </summary>
    public static FrontierCandidate? SelectNextFrontier(
        IReadOnlyList<FrontierCandidate> candidates,
        FrontierScoringWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            return null;

        FrontierScoringWeights effectiveWeights = weights ?? FrontierScoringWeights.Default;

        FrontierCandidate best = candidates[0];
        double bestScore = Score(best, effectiveWeights);

        for (int i = 1; i < candidates.Count; i++)
        {
            FrontierCandidate candidate = candidates[i];
            double score = Score(candidate, effectiveWeights);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// A single-waypoint, same-map <see cref="NavigationPlan"/> toward
    /// <paramref name="selected"/>, or <see cref="NavigationPlan.Unreachable"/>
    /// when <paramref name="selected"/> is <see langword="null"/> (nothing to
    /// explore left on this map right now).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="selected"/> is for a different map than <paramref name="mapId"/>.</exception>
    public static NavigationPlan BuildNavigationPlan(MapId mapId, FrontierCandidate? selected, DateTime nowUtc)
    {
        if (selected is null)
            return NavigationPlan.Unreachable("no_frontier_candidate", nowUtc);

        FrontierCandidate candidate = selected;
        if (!candidate.MapId.Equals(mapId))
        {
            throw new ArgumentException(
                $"Candidate is on map '{candidate.MapId}' but the plan requested is for map '{mapId}'. "
                + "Building a plan from a different map's candidate would silently route toward the wrong map's tile.",
                nameof(selected));
        }

        var waypoint = new NavigationWaypoint(
            mapId,
            new WorldPosition(candidate.Coordinate.Column, candidate.Coordinate.Row),
            UsePortal: null);

        return new NavigationPlan(
            EquatableArray<NavigationWaypoint>.From(new[] { waypoint }),
            WorldFact<bool>.Derived(true, confidence: 1d, nowUtc),
            nowUtc);
    }

    /// <summary>
    /// Why frontier scoring produced nothing, so an empty candidate list is never read as
    /// "this map is finished".
    /// </summary>
    /// <param name="map">The map as currently modelled.</param>
    /// <param name="footprint">Which tiles of that map have been visited.</param>
    /// <returns>The verdict explaining the absence of candidates, or <see cref="ExplorationVerdict.FrontierAvailable"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="footprint"/> is null.</exception>
    /// <exception cref="ArgumentException">The footprint belongs to a different map.</exception>
    public static ExplorationVerdict ExplainFrontier(MapModel map, ExplorationFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(footprint);

        if (!map.Id.Equals(footprint.MapId))
        {
            throw new ArgumentException(
                $"Footprint is for map '{footprint.MapId}' but the map given is '{map.Id}'.",
                nameof(footprint));
        }

        if (map.Tiles.Count == 0)
        {
            return ExplorationVerdict.MapUnknown;
        }

        var visited = new HashSet<TileCoordinate>();
        foreach (TileCoordinate coordinate in footprint.VisitedTiles)
            visited.Add(coordinate);

        bool anyUnvisited = false;
        foreach (Tile tile in map.Tiles)
        {
            if (visited.Contains(tile.Coordinate))
            {
                continue;
            }

            anyUnvisited = true;
            if (IsWalkable(tile))
            {
                return ExplorationVerdict.FrontierAvailable;
            }
        }

        // Unvisited tiles that cannot be walked are not exploration left to do: the map is
        // as covered as it can be, and saying "fully explored" would be a different claim
        // from "what remains is wall".
        return anyUnvisited
            ? ExplorationVerdict.NoReachableFrontier
            : ExplorationVerdict.FullyExplored;
    }

    private static bool IsWalkable(Tile tile) =>
        tile.Traversability.HasValue && tile.Traversability.Value == TileTraversability.Walkable;

    private static double Score(FrontierCandidate candidate, FrontierScoringWeights weights) =>
        weights.InformationGain * candidate.InformationGain
        - weights.TravelCost * candidate.TravelCost
        - weights.Risk * candidate.Risk
        + weights.MissionRelevance * candidate.MissionRelevance;

    private static double ComputeInformationGain(
        TileCoordinate candidate,
        Dictionary<TileCoordinate, Tile> tilesByCoordinate,
        HashSet<TileCoordinate> visited)
    {
        double gain = 0d;
        for (int dRow = -InformationGainRadius; dRow <= InformationGainRadius; dRow++)
        {
            for (int dColumn = -InformationGainRadius; dColumn <= InformationGainRadius; dColumn++)
            {
                if (dRow == 0 && dColumn == 0)
                    continue;

                var neighbor = new TileCoordinate(candidate.Column + dColumn, candidate.Row + dRow);
                if (!tilesByCoordinate.TryGetValue(neighbor, out Tile? neighborTile))
                    continue;

                if (IsWalkable(neighborTile) && !visited.Contains(neighbor))
                    gain += 1d;
            }
        }

        return gain;
    }

    private static double ComputeRisk(TileCoordinate candidate, EquatableArray<Mob> mobs)
    {
        double risk = 0d;
        foreach (Mob mob in mobs)
        {
            if (!mob.IsHostile.HasValue || !mob.IsHostile.Value) continue;
            if (!mob.IsAlive.HasValue || !mob.IsAlive.Value) continue;
            if (!mob.Position.HasValue) continue;

            double distance = ComputeDistance(candidate, mob.Position.Value);
            if (distance < RiskRadius)
                risk += RiskRadius - distance;
        }

        return risk;
    }

    private static double ComputeDistance(TileCoordinate tile, WorldPosition position)
    {
        double dx = tile.Column - position.X;
        double dy = tile.Row - position.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ContainsCoordinate(EquatableArray<TileCoordinate> coordinates, TileCoordinate target)
    {
        foreach (TileCoordinate coordinate in coordinates)
        {
            if (coordinate.Equals(target))
                return true;
        }

        return false;
    }

    private static EquatableArray<TileCoordinate> AppendCoordinate(EquatableArray<TileCoordinate> coordinates, TileCoordinate toAppend)
    {
        var extended = new TileCoordinate[coordinates.Count + 1];
        for (int i = 0; i < coordinates.Count; i++)
            extended[i] = coordinates[i];
        extended[coordinates.Count] = toAppend;
        return EquatableArray<TileCoordinate>.From(extended);
    }

    private static WorldFact<bool> ComputeFullyExplored(MapModel map, EquatableArray<TileCoordinate> visited, DateTime nowUtc)
    {
        if (map.Tiles.Count == 0)
            return WorldFact<bool>.Unknown("map_not_reconstructed", nowUtc);

        var visitedSet = new HashSet<TileCoordinate>();
        foreach (TileCoordinate coordinate in visited)
            visitedSet.Add(coordinate);

        foreach (Tile tile in map.Tiles)
        {
            if (IsWalkable(tile) && !visitedSet.Contains(tile.Coordinate))
                return WorldFact<bool>.Derived(false, confidence: 1d, nowUtc);
        }

        return WorldFact<bool>.Derived(true, confidence: 1d, nowUtc);
    }
}

/// <summary>
/// Linear weights <see cref="ExplorationPlanner.SelectNextFrontier"/> applies
/// to a <see cref="FrontierCandidate"/>'s raw inputs:
/// <c>InformationGain*w1 - TravelCost*w2 - Risk*w3 + MissionRelevance*w4</c>.
/// </summary>
public sealed record FrontierScoringWeights(
    double InformationGain,
    double TravelCost,
    double Risk,
    double MissionRelevance)
{
    /// <summary>
    /// Risk weighted heaviest: a frontier that reveals a lot of new map but
    /// sits next to hostiles is not a good default choice for an autonomous
    /// player with no combat policy consulted at this layer (AP-04 does not
    /// own combat decisions -- AP-05 does).
    /// </summary>
    public static FrontierScoringWeights Default { get; } = new(
        InformationGain: 1.0,
        TravelCost: 0.5,
        Risk: 2.0,
        MissionRelevance: 1.0);
}

/// <summary>
/// Why <see cref="ExplorationPlanner"/> has no frontier to offer. An empty candidate list on
/// its own cannot tell a map never observed from one walked end to end, and the two demand
/// opposite behaviour: look first, or leave.
/// </summary>
public enum ExplorationVerdict
{
    /// <summary>No tile of this map has been observed yet: there is nothing to reason about.</summary>
    MapUnknown = 0,

    /// <summary>At least one walkable tile has not been visited.</summary>
    FrontierAvailable = 1,

    /// <summary>Every observed tile has been visited: this map is done.</summary>
    FullyExplored = 2,

    /// <summary>
    /// Tiles remain unvisited but none of them can be walked. Not the same claim as
    /// <see cref="FullyExplored"/>: what is left is wall, not coverage.
    /// </summary>
    NoReachableFrontier = 3
}
