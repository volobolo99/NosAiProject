// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — A path is admitted once and revalidated every segment (C2-7)
// ============================================================================
//
// docs/PIANO_CAPACITA.md C2-7, docs/CONTROLLO_PERSONAGGIO_ROADMAP.md P5.
//
// A route computed once and then executed blind is the failure P5 exists to
// prevent: between the computation and segment N the world moved. Two checks,
// and they answer different questions. Admission asks whether this path was ever
// walkable at all — a question about the client's own geometry, which does not
// change while we walk. Revalidation asks whether the next cell is clear now — a
// question about things that move, and therefore one that has to be asked again
// every time.

using System.Globalization;
using System.Collections.Immutable;
using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Autonomy;

namespace NosAi.Runtime.Navigation;

/// <summary>Why a whole path was refused before anything was emitted.</summary>
/// <param name="IsAdmitted">True when every cell of the path is walkable static geometry.</param>
/// <param name="RefusalReason">Which cell failed and why. Null when admitted.</param>
/// <param name="FirstBadIndex">Where in the path the refusal is, or -1.</param>
/// <param name="CellsChecked">How many cells were examined before the answer.</param>
public readonly record struct PathAdmission(
    bool IsAdmitted,
    string? RefusalReason,
    int FirstBadIndex,
    int CellsChecked);

/// <summary>Whether the next cell of the path may be stepped onto now.</summary>
/// <param name="IsClear">True when both the static and the dynamic answer allow it.</param>
/// <param name="RefusalReason">Which of the two refused, named. Null when clear.</param>
/// <param name="NeedsReplan">
/// True when the refusal is one a different route could avoid. A blocked or occupied
/// cell is; a stale world is not — replanning against an observation too old to trust
/// produces a different path with the same defect.
/// </param>
public readonly record struct SegmentRevalidation(
    bool IsClear,
    string? RefusalReason,
    bool NeedsReplan);

/// <summary>
/// The two checks a path passes: once as a whole, and again at every segment.
/// </summary>
/// <remarks>
/// <para>
/// <b>A segment is one cell, and that is not a simplification.</b>
/// <see cref="StepGuardChain"/> authorises a step of Chebyshev distance one and refuses
/// anything longer at its first guard, so a smoothed waypoint two cells away is not an
/// act this runtime can emit. Walking the path cell by cell is therefore the only
/// executable reading of it, and it is the safer one anyway: a click on a distant cell
/// would hand the route back to the client's own pathing, which does not know what the
/// runtime observed.
/// </para>
/// <para>
/// <b>Why admission is separate from revalidation.</b> They fail differently and want
/// different answers. Static geometry is a file the client ships: if the route crosses a
/// wall it was never walkable, no amount of waiting changes it, and the correct
/// behaviour is to emit <i>nothing at all</i> — which is the second half of P5's DoD.
/// Occupancy is about a moment: a cell taken now may be free in a second, and refusing
/// the whole route for it would throw away a route that is still good.
/// </para>
/// <para>
/// <b>The freshness discipline is the one <see cref="OccupancyFreshness"/> already
/// states</b>, and it is not restated here — it is called. An absent observation and an
/// expired one are the same answer, and neither of them is "clear".
/// </para>
/// </remarks>
public static class PathRevalidation
{
    /// <summary>Reported when the path has fewer than two cells to walk.</summary>
    public const string EmptyPathReason = "path_empty";

    /// <summary>Reported when no grid is loaded for the map being walked.</summary>
    public const string GridNotLoadedReason = "path_grid_not_loaded";

    /// <summary>Reported when a cell of the path lies outside the grid.</summary>
    public const string CellOffGridPrefix = "path_cell_off_grid";

    /// <summary>Reported when the client's geometry forbids a cell of the path.</summary>
    public const string CellBlockedPrefix = "path_cell_blocked";

    /// <summary>Reported when two consecutive cells are not adjacent.</summary>
    public const string CellsNotAdjacentPrefix = "path_cells_not_adjacent";

    /// <summary>Reported when the character is not where the path says it should be.</summary>
    public const string OffPathPrefix = "path_position_off_path";

    /// <summary>
    /// Whether the whole path is walkable against the client's own geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once, before anything is emitted. P5's DoD in one sentence: <i>no input for
    /// a path that crosses a blocked cell</i> — so the crossing has to be found before the
    /// first step, not discovered at the segment that hits it, by which time the runtime
    /// has already acted several times on a route it should never have accepted.
    /// </para>
    /// <para>
    /// <b>Every cell, not the endpoints.</b> A route is admitted on the cells it occupies;
    /// checking only where it starts and ends is how a path through a wall is accepted.
    /// Adjacency is checked at the same time, because a path with a gap in it is not a
    /// path this runtime can walk — the missing cells were never examined by anybody.
    /// </para>
    /// </remarks>
    public static PathAdmission Admit(in MapGrid grid, IReadOnlyList<MapPoint> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!grid.IsLoaded)
            return new PathAdmission(false, GridNotLoadedReason, -1, 0);

        if (path.Count < 2)
            return new PathAdmission(false, EmptyPathReason, -1, path.Count);

        for (var i = 0; i < path.Count; i++)
        {
            MapPoint cell = path[i];

            if (!grid.Contains(cell.X, cell.Y))
            {
                return new PathAdmission(
                    false,
                    string.Create(CultureInfo.InvariantCulture, $"{CellOffGridPrefix}:{i}@{cell.X},{cell.Y}"),
                    i,
                    i + 1);
            }

            if (!grid.IsWalkable(cell.X, cell.Y))
            {
                return new PathAdmission(
                    false,
                    string.Create(CultureInfo.InvariantCulture, $"{CellBlockedPrefix}:{i}@{cell.X},{cell.Y}"),
                    i,
                    i + 1);
            }

            if (i > 0 && !IsAdjacent(path[i - 1], cell))
            {
                return new PathAdmission(
                    false,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{CellsNotAdjacentPrefix}:{i - 1}->{i}@{path[i - 1].X},{path[i - 1].Y}->{cell.X},{cell.Y}"),
                    i,
                    i + 1);
            }
        }

        return new PathAdmission(true, null, -1, path.Count);
    }

    /// <summary>
    /// Whether the next cell may be stepped onto, given where the character actually is
    /// and what has been observed moving.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The position is checked first, and it is not a formality.</b> A path is a
    /// statement about a sequence of cells starting where the character was; if the
    /// character is somewhere else — knocked back, teleported, or simply drifted — then
    /// the next cell of that path is not adjacent to it, and stepping towards it is
    /// walking a route that no longer describes the world. Naming that as its own
    /// refusal is what lets the caller replan from where it is rather than repeat.
    /// </para>
    /// <para>
    /// <b>Static before dynamic.</b> The same order <see cref="StaticGeometryLayer"/>
    /// composes in: geometry wins outright, and an observation saying a wall is clear is
    /// an observation that is wrong about a wall. It also gives the more useful sentence
    /// first — a cell the client forbids is a permanent fact, and a cell somebody is
    /// standing on is not.
    /// </para>
    /// </remarks>
    /// <param name="grid">The client's static geometry for this map.</param>
    /// <param name="from">Where the character is observed to be.</param>
    /// <param name="to">The next cell of the path.</param>
    /// <param name="view">What has been seen moving, and when the seeing was refreshed.</param>
    /// <param name="nowUtc">The instant ages are measured from.</param>
    /// <param name="maxViewAge">Null takes <see cref="OccupancyFreshness.DefaultMaxViewAge"/>.</param>
    /// <param name="maxSightingAge">Null takes <see cref="OccupancyFreshness.DefaultMaxSightingAge"/>.</param>
    public static SegmentRevalidation Revalidate(
        in MapGrid grid,
        MapPoint from,
        MapPoint to,
        in OccupancyView view,
        DateTime nowUtc,
        TimeSpan? maxViewAge = null,
        TimeSpan? maxSightingAge = null)
    {
        if (!grid.IsLoaded)
            return new SegmentRevalidation(false, GridNotLoadedReason, NeedsReplan: false);

        if (!IsAdjacent(from, to))
        {
            // A different route from where the character actually is can fix this, so it
            // is a replan and not an abandonment.
            return new SegmentRevalidation(
                false,
                string.Create(CultureInfo.InvariantCulture,
                    $"{OffPathPrefix}:{from.X},{from.Y}_not_beside_{to.X},{to.Y}"),
                NeedsReplan: true);
        }

        if (!grid.Contains(to.X, to.Y))
        {
            return new SegmentRevalidation(
                false,
                string.Create(CultureInfo.InvariantCulture, $"{CellOffGridPrefix}:{to.X},{to.Y}"),
                NeedsReplan: true);
        }

        if (!grid.IsWalkable(to.X, to.Y))
        {
            return new SegmentRevalidation(
                false,
                string.Create(CultureInfo.InvariantCulture, $"{CellBlockedPrefix}:{to.X},{to.Y}"),
                NeedsReplan: true);
        }

        OccupancyVerdict occupancy = OccupancyFreshness.Evaluate(
            to, in view, nowUtc, maxViewAge, maxSightingAge);

        if (occupancy.IsClear)
            return new SegmentRevalidation(true, null, NeedsReplan: false);

        // A cell somebody is standing on can be routed around. A world the runtime has
        // stopped hearing from cannot: replanning against an observation too old to act
        // on produces a different path with exactly the same defect, and would do it
        // repeatedly, which is how a blind runtime looks busy.
        bool routable = occupancy.Occupancy is DynamicOccupancy.Occupied or DynamicOccupancy.Suspected
            && occupancy.RefusalReason is { } reason
            && (reason.StartsWith(OccupancyFreshness.DestinationOccupiedPrefix, StringComparison.Ordinal)
                || reason.StartsWith(OccupancyFreshness.DestinationSuspectedPrefix, StringComparison.Ordinal));

        return new SegmentRevalidation(false, occupancy.RefusalReason, routable);
    }

    /// <summary>Chebyshev distance of exactly one: the only step the guard chain authorises.</summary>
    public static bool IsAdjacent(MapPoint a, MapPoint b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return (dx | dy) != 0 && dx <= 1 && dy <= 1;
    }

    /// <summary>Converts a pathfinder result into the vocabulary the guards speak.</summary>
    /// <remarks>
    /// <see cref="AStarPathfinder"/> answers in <see cref="GridPoint"/> and everything
    /// downstream of authorisation speaks <see cref="MapPoint"/>. One conversion, here,
    /// rather than a cast at each call site — a coordinate silently changing type is how
    /// an x and a y end up swapped.
    /// </remarks>
    public static IReadOnlyList<MapPoint> ToCells(ImmutableArray<GridPoint> waypoints)
    {
        if (waypoints.IsDefaultOrEmpty)
            return Array.Empty<MapPoint>();

        var cells = new MapPoint[waypoints.Length];
        for (var i = 0; i < waypoints.Length; i++)
            cells[i] = new MapPoint(waypoints[i].X, waypoints[i].Y);

        return cells;
    }
}
