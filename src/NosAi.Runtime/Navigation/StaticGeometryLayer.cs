using System;
using NosAi.Navigation.Pathfinding;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// What the runtime knows about something that moves standing on a cell.
/// </summary>
/// <remarks>
/// Deliberately three values and not a boolean. The middle one is the whole reason
/// <see cref="TileType.Unobserved"/> still exists after the static grid arrives, and
/// collapsing it into either neighbour is how the distinction would be lost: folded
/// into <see cref="Clear"/> the runtime walks into a monster it had reason to expect,
/// folded into <see cref="Occupied"/> it treats a guess as a sighting.
/// </remarks>
public enum DynamicOccupancy : byte
{
    /// <summary>
    /// Nothing that moves is known or suspected here.
    /// </summary>
    /// <remarks>
    /// This is the ordinary case and it is <b>not</b> a claim that the cell was
    /// looked at. See the remarks on <see cref="StaticGeometryLayer"/> for why that
    /// is sound, and for what carries the weight instead.
    /// </remarks>
    Clear = 0,

    /// <summary>
    /// A tracked entity may be here, and the reading that said so is too old to act on.
    /// </summary>
    /// <remarks>
    /// Positive suspicion about a named entity, not a general absence of information.
    /// That is what keeps this domain small: it is bounded by the number of entities
    /// being tracked, not by the size of the map.
    /// </remarks>
    Suspected = 1,

    /// <summary>A tracked entity is here, on an observation recent enough to act on.</summary>
    Occupied = 2
}

/// <summary>
/// The rule that joins the client's static geometry to the observed dynamic layer,
/// and the reason <see cref="TileType.Unobserved"/> keeps its meaning after the join.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this exists to solve.</b> <see cref="MapGridData"/> holds one byte
/// per cell and that byte is a <see cref="TileType"/>, so geometry and occupancy
/// share a slot. Before the static grid existed that was harmless, because both were
/// discovered by looking and <see cref="TileType.Unobserved"/> honestly covered
/// both. Writing the client's geometry into the same byte would end that: every cell
/// the file calls open would be stamped <see cref="TileType.Walkable"/>, and the
/// runtime would have said "nothing is standing here" on the authority of a file
/// that cannot possibly know.
/// </para>
/// <para>
/// <b>The rule.</b> Two layers, composed in one direction only.
/// </para>
/// <list type="bullet">
/// <item>
/// The <b>static layer</b> is the <see cref="MapGrid"/>. It is authoritative and it
/// is <i>complete</i>: inside a loaded rectangle every cell has an answer, so there
/// is no such thing as unknown geometry there. Outside the rectangle, and for a grid
/// that is not loaded at all, geometry is unknown and blocks.
/// </item>
/// <item>
/// The <b>dynamic layer</b> is observation. It may only <i>subtract</i> walkability.
/// It can turn open ground into blocked ground; it can never turn blocked ground
/// into open ground, whatever it saw.
/// </item>
/// </list>
/// <para>
/// <b>Where <see cref="TileType.Unobserved"/> goes, and why it is still honest.</b>
/// It stops standing for unread geometry — that was always a placeholder for a file
/// nobody had opened — and keeps exactly two jobs, in both of which it still blocks:
/// </para>
/// <list type="number">
/// <item>
/// <b>No grid is loaded for this map.</b> Geometry is genuinely unknown, planning
/// must stop, and <see cref="Compose"/> answers <see cref="TileType.Unobserved"/> for
/// every cell. This is the case the build-identity check in
/// <see cref="MapGridSetIdentity"/> produces on purpose after a client patch.
/// </item>
/// <item>
/// <b>A tracked entity is suspected here</b> (<see cref="DynamicOccupancy.Suspected"/>).
/// A specific thing that moves might be on this cell and the sighting is stale.
/// </item>
/// </list>
/// <para>
/// <b>The decision worth arguing about: open ground with nothing observed on it is
/// walkable.</b> It would be easy to call that "unknown" and block it, and it would
/// be wrong in a way that matters. The runtime never observes most cells of a map;
/// if the absence of a sighting blocked a cell, every cell would block, planning
/// would return nothing on every map forever, and the pressure to weaken the rule
/// would land on the geometry guarantee — the one that must not move. The two
/// unknowns are different propositions and only one of them is spatial:
/// </para>
/// <list type="bullet">
/// <item>
/// Unknown <i>geometry</i> is a fact about a place. It is answered in space, it
/// blocks, and it is absolute.
/// </item>
/// <item>
/// Unknown <i>occupancy</i> is a fact about a moment. Nothing about a cell can
/// settle it, because whatever is there arrived and will leave. It is answered in
/// <i>time</i> — by the observation-age limit the cycle already enforces before
/// acting, and by revalidating the route before each segment rather than trusting it
/// end to end (<c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3).
/// </item>
/// </list>
/// <para>
/// So <see cref="TileType.Walkable"/> out of <see cref="Compose"/> claims exactly
/// what it can support — <i>the client's geometry permits this cell and nothing
/// observed is on it</i> — and the claim it does not make, that the cell is still
/// clear by the time the character gets there, is the one the revalidation makes
/// again each time it matters. Nothing here authorises an act; it authorises a plan,
/// and the plan is checked before every segment of it.
/// </para>
/// </remarks>
public static class StaticGeometryLayer
{
    /// <summary>
    /// The tile a cell reverts to once nothing dynamic is on it: geometry alone.
    /// </summary>
    /// <remarks>
    /// The baseline exists so that clearing a dynamic obstacle has somewhere correct
    /// to go. Restoring <see cref="TileType.Walkable"/> would open cells the client
    /// walls off, and restoring <see cref="TileType.Unobserved"/> would re-block
    /// ground already read from the file — the two ways a one-byte grid loses the
    /// distinction as soon as anything moves across it.
    /// </remarks>
    public static TileType BaselineFor(in MapGrid grid, int x, int y)
    {
        if (!grid.IsLoaded)
            return TileType.Unobserved;

        return grid.IsWalkable(x, y) ? TileType.Walkable : TileType.BlockedObstacle;
    }

    /// <summary>
    /// The tile a planner should see: static geometry with the dynamic layer
    /// subtracted from it.
    /// </summary>
    /// <remarks>
    /// Subtraction only, and the order is not negotiable: geometry is consulted
    /// first and a blocked cell stays blocked no matter what the dynamic layer
    /// reports. An observation saying a wall is clear is an observation that is
    /// wrong about a wall.
    /// </remarks>
    public static TileType Compose(in MapGrid grid, int x, int y, DynamicOccupancy occupancy)
    {
        TileType baseline = BaselineFor(in grid, x, y);

        // Geometry wins outright. Nothing observed can promote a cell.
        if (baseline is not TileType.Walkable)
            return baseline;

        return occupancy switch
        {
            DynamicOccupancy.Occupied => TileType.BlockedObstacle,
            DynamicOccupancy.Suspected => TileType.Unobserved,
            _ => TileType.Walkable
        };
    }

    /// <inheritdoc cref="Compose(in MapGrid,int,int,DynamicOccupancy)"/>
    public static TileType Compose(in MapGrid grid, GridPoint point, DynamicOccupancy occupancy) =>
        Compose(in grid, point.X, point.Y, occupancy);

    /// <summary>What a projection did, and what it had to overrule.</summary>
    /// <param name="CellsWritten">Cells whose tile the static geometry set.</param>
    /// <param name="SemanticTilesPreserved">
    /// Walkable cells left as they were because they carried a meaning the grid
    /// cannot express — a town, a portal mouth.
    /// </param>
    /// <param name="SemanticTilesOverruled">
    /// Cells the runtime had marked as a town or a portal and the client's geometry
    /// calls solid. Not silently resolved: geometry wins, and the count is reported
    /// because a portal inside a wall means the portal table and the grid disagree,
    /// and that is worth someone's attention rather than a shrug.
    /// </param>
    public readonly record struct ProjectionReport(
        int CellsWritten,
        int SemanticTilesPreserved,
        int SemanticTilesOverruled);

    /// <summary>
    /// Writes the static baseline into an observation grid, leaving the tiles that
    /// carry meaning the client's bits cannot express.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TileType.SafeZoneTown"/> and <see cref="TileType.PortalEntrance"/>
    /// are walkable tiles with a meaning attached, and the grid has no bit for
    /// either, so a projection that stamped every open cell
    /// <see cref="TileType.Walkable"/> would erase the portal table into the
    /// geometry. They are preserved where the geometry agrees they are open and
    /// overruled where it does not.
    /// </para>
    /// <para>
    /// <see cref="TileType.WaterOrChasm"/> is not preserved: it is a
    /// <i>non-walkable</i> guess about terrain, which is precisely the thing the
    /// file now answers, and keeping it would leave an observation standing in front
    /// of the authority that replaced it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The grid is not loaded, or its rectangle does not match the target. A
    /// projection that silently covered part of a map would leave the rest at
    /// whatever it happened to hold, which is the one outcome nobody could diagnose
    /// afterwards.
    /// </exception>
    public static ProjectionReport Project(in MapGrid grid, MapGridData target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!grid.IsLoaded)
        {
            throw new ArgumentException(
                "No grid is loaded, so there is no geometry to project. A map without its grid "
                + "must stop planning, not be projected as open ground.",
                nameof(grid));
        }

        if (grid.Width != target.Width || grid.Height != target.Height)
        {
            throw new ArgumentException(
                $"Grid is {grid.Width}x{grid.Height} and the target map is "
                + $"{target.Width}x{target.Height}. Projecting part of a map leaves the rest "
                + "holding whatever it held before.",
                nameof(grid));
        }

        var written = 0;
        var preserved = 0;
        var overruled = 0;

        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                TileType baseline = grid.IsWalkable(x, y)
                    ? TileType.Walkable
                    : TileType.BlockedObstacle;

                TileType existing = target.GetTileType(x, y);
                bool carriesMeaning = existing is TileType.SafeZoneTown or TileType.PortalEntrance;

                if (carriesMeaning)
                {
                    if (baseline is TileType.Walkable)
                    {
                        preserved++;
                        continue;
                    }

                    overruled++;
                }

                target.SetTileType(x, y, baseline);
                written++;
            }
        }

        return new ProjectionReport(written, preserved, overruled);
    }
}
