using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NosAi.Navigation.Pathfinding;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// The per-cell bits the client's own map grid carries.
/// </summary>
/// <remarks>
/// <para>
/// The layout is the client's, not ours, and is recorded in
/// <c>docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md</c> § 5. Naming the bits is the
/// whole point: the runtime reads two of them today and carries the other three
/// faithfully, and a byte compared against a literal <c>0x02</c> somewhere in a
/// pathfinder is how a meaning gets quietly reassigned.
/// </para>
/// <para>
/// Bits outside this set are not errors and are not dropped: a client build may
/// use them for something this project has not identified, and a loader that
/// masked them off would make the file it wrote back differ from the file it read.
/// They are preserved in <see cref="MapGrid.RawAt"/> and ignored by every
/// predicate here.
/// </para>
/// </remarks>
[Flags]
public enum MapCellFlags : byte
{
    /// <summary>Open ground as far as static geometry is concerned.</summary>
    None = 0x00,

    /// <summary>Walking onto this cell is forbidden.</summary>
    WalkBlocked = 0x01,

    /// <summary>
    /// Attacks do not cross this cell. This is the line-of-sight datum, and it is
    /// a separate fact from walkability: a chasm blocks the feet and not the arrow,
    /// a low wall the reverse.
    /// </summary>
    AttackBlocked = 0x02,

    /// <summary>A raid-related constraint applies to this cell.</summary>
    RaidConstrained = 0x04,

    /// <summary>Monsters do not acquire targets on this cell.</summary>
    AggroDisabled = 0x08,

    /// <summary>Player-versus-player is disabled on this cell.</summary>
    PvpDisabled = 0x10
}

/// <summary>
/// The client's static map geometry for one map: a rectangle of cells, each a
/// bitmask, read once per client build and true until the build changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> <c>NavigationPathfinding</c> populates its grid by
/// observation, so static geometry — walls, water, the shape of the map — was
/// being discovered at runtime and held as <see cref="TileType.Unobserved"/> until
/// something looked at it. It never needed to be discovered: it is a file the
/// client ships. Reading it turns the walkability guard into an indexed array
/// access, gives line of sight without any visual heuristic, and leaves
/// <see cref="TileType.Unobserved"/> to the dynamic obstacles it can actually
/// speak about. See <see cref="StaticGeometryLayer"/> for the composition rule
/// that keeps those two apart.
/// </para>
/// <para>
/// <b>Classification.</b> A grid is <c>CACHED</c> with provenance "client file",
/// never <c>LIVE</c>. It is true for as long as the build it was extracted from is
/// the build that is running, which is why <see cref="MapGridSetIdentity"/> is part
/// of this contract rather than an operational extra.
/// </para>
/// <para>
/// <b>Outside the rectangle is blocked, not free.</b> Every predicate here answers
/// for a cell that does not exist by naming the consequence that fails closed —
/// which is not the same as a uniform <c>false</c>. Walking and attacking are
/// refused; a protection like <see cref="MapCellFlags.PvpDisabled"/> is reported
/// absent, because assuming an unknown cell protects you is the permissive error
/// and assuming it does not is the safe one. Unknown is not zero, false or empty
/// (DOMAIN-10); it is whichever answer does not authorise.
/// </para>
/// <para>
/// <b>A default instance is a grid that is not loaded, and it blocks everything.</b>
/// This is a struct, so <c>default(MapGrid)</c> can be produced by any caller and
/// by every uninitialised field. It answers exactly as a zero-sized grid does —
/// nothing is walkable, everything blocks attacks — so a missing grid stops
/// planning instead of silently opening the whole map. <see cref="IsLoaded"/> says
/// which case it is, for callers that need to report the difference rather than
/// merely be safe from it.
/// </para>
/// <para>
/// <b>Allocation.</b> Constructing one wraps a buffer the loader already owns; no
/// query allocates, and none copies the buffer. <see cref="HasLineOfSight"/> walks
/// the segment with two integers rather than materialising the cells it crosses.
/// </para>
/// </remarks>
public readonly struct MapGrid
{
    /// <summary>The cells, row-major, one byte each. Null only for the default instance.</summary>
    private readonly byte[]? _cells;

    /// <summary>Cells across. Zero for a grid that is not loaded.</summary>
    public int Width { get; }

    /// <summary>Cells down. Zero for a grid that is not loaded.</summary>
    public int Height { get; }

    /// <summary>The map this grid belongs to.</summary>
    public int MapId { get; }

    /// <summary>
    /// Wraps a buffer of cells. The buffer is taken, not copied: the loader owns it
    /// and must not write to it afterwards.
    /// </summary>
    /// <param name="cells">
    /// At least <paramref name="width"/> × <paramref name="height"/> bytes,
    /// row-major. A longer buffer is accepted so a loader may hand over a pooled or
    /// over-read array without slicing it; the excess is never read.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Dimensions that are negative, or that overflow, or that the buffer cannot
    /// cover. All three are refused at construction rather than caught per access,
    /// which is what lets every read below skip a second bounds check.
    /// </exception>
    public MapGrid(int mapId, int width, int height, byte[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        // Checked so that a width and height which multiply past Int32 cannot wrap
        // to a small positive length that the buffer happens to satisfy.
        long required = (long)width * height;
        if (required > cells.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cells),
                $"A {width}x{height} grid needs {required} cells and was given {cells.Length}.");
        }

        MapId = mapId;
        Width = width;
        Height = height;

        // A zero-area grid keeps a non-null buffer so IsLoaded can tell "the client
        // ships an empty map" apart from "no grid was loaded". Both block; only one
        // of them is a fault worth reporting.
        _cells = cells;
    }

    /// <summary>
    /// Whether a grid was actually loaded, as opposed to a default instance.
    /// </summary>
    /// <remarks>
    /// Both answer every query the same way — blocked — so this is never needed for
    /// safety. It is needed for honesty: "planning stopped because no grid is
    /// loaded" and "planning stopped because the route is walled in" are different
    /// facts and a caller that cannot tell them apart reports the wrong one.
    /// </remarks>
    public bool IsLoaded => _cells is not null;

    /// <summary>Cells in the rectangle. Zero when nothing is loaded.</summary>
    public int CellCount => Width * Height;

    /// <summary>Whether the cell exists in this grid.</summary>
    /// <remarks>
    /// One unsigned comparison per axis: casting to <c>uint</c> folds the negative
    /// test into the upper-bound test, so a negative coordinate becomes a very large
    /// unsigned one and fails the same compare.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int x, int y) =>
        _cells is not null && (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>
    /// The cell's raw byte, including any bit this project has not named.
    /// </summary>
    /// <param name="fallback">
    /// Returned for a cell outside the grid. There is no safe universal default for
    /// a whole byte — the bits fail closed in opposite directions — so the caller
    /// has to say what it means, and the predicates below say it for the two bits
    /// that matter.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte RawAt(int x, int y, byte fallback = 0) =>
        Contains(x, y) ? CellUnchecked(x, y) : fallback;

    /// <summary>The named bits of a cell that exists.</summary>
    /// <returns>False when the cell is outside the grid, with <paramref name="flags"/> unset.</returns>
    /// <remarks>
    /// The try-shape rather than a returned <see cref="MapCellFlags"/>, because a
    /// returned value would have to invent flags for a cell that does not exist, and
    /// every invented flag is wrong for half its readers.
    /// </remarks>
    public bool TryGetFlags(int x, int y, out MapCellFlags flags)
    {
        if (!Contains(x, y))
        {
            flags = MapCellFlags.None;
            return false;
        }

        flags = (MapCellFlags)CellUnchecked(x, y);
        return true;
    }

    /// <summary>Whether static geometry permits standing on this cell.</summary>
    /// <remarks>
    /// False outside the grid. This answers for geometry alone: a cell that is
    /// walkable here may still be occupied by something that moves, which is the
    /// dynamic layer's question and not this one's.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWalkable(int x, int y) =>
        Contains(x, y) && (CellUnchecked(x, y) & (byte)MapCellFlags.WalkBlocked) == 0;

    /// <summary>Whether an attack is stopped by this cell.</summary>
    /// <remarks>
    /// True outside the grid: a shot leaving the map is stopped, not free.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BlocksAttack(int x, int y) =>
        !Contains(x, y) || (CellUnchecked(x, y) & (byte)MapCellFlags.AttackBlocked) != 0;

    /// <summary>Whether a raid constraint applies to this cell.</summary>
    /// <remarks>
    /// True outside the grid. A constraint of unknown presence is treated as present,
    /// because proceeding as though a restriction does not apply is the permissive
    /// error.
    /// </remarks>
    public bool IsRaidConstrained(int x, int y) =>
        !Contains(x, y) || (CellUnchecked(x, y) & (byte)MapCellFlags.RaidConstrained) != 0;

    /// <summary>Whether monsters are prevented from acquiring targets on this cell.</summary>
    /// <remarks>
    /// False outside the grid, and this is the opposite direction from
    /// <see cref="BlocksAttack"/> on purpose. This bit is a <i>protection</i>, so the
    /// answer that does not authorise is the one that assumes the protection is
    /// absent: a planner that believed an unknown cell was aggro-free would walk into
    /// it expecting safety it has no evidence for.
    /// </remarks>
    public bool IsAggroDisabled(int x, int y) =>
        Contains(x, y) && (CellUnchecked(x, y) & (byte)MapCellFlags.AggroDisabled) != 0;

    /// <summary>Whether player-versus-player is disabled on this cell.</summary>
    /// <remarks>False outside the grid, for the reason given on <see cref="IsAggroDisabled"/>.</remarks>
    public bool IsPvpDisabled(int x, int y) =>
        Contains(x, y) && (CellUnchecked(x, y) & (byte)MapCellFlags.PvpDisabled) != 0;

    /// <summary>
    /// Whether an attack can travel from one cell to another, tracing the segment and
    /// denying at the first cell that blocks attacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both endpoints are tested.</b> The contract in
    /// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3 says "trace the segment and
    /// deny at the first <c>BlocksAttack</c>", with no cell excused, and the literal
    /// reading is also the fail-closed one: a caller standing in a cell that stops
    /// attacks has no line out of it, and a target standing in one cannot be reached.
    /// Excusing the origin would be a convenience whose cost is a shot the client will
    /// refuse and the verifier will have to catch afterwards.
    /// </para>
    /// <para>
    /// <b>It is symmetric.</b> The segment is traced from whichever endpoint sorts
    /// first, so <c>a → b</c> and <c>b → a</c> visit the same cells and cannot
    /// disagree. Plain Bresenham does not guarantee that where a line passes exactly
    /// through a corner, and a line of sight that depends on who is asking is a bug
    /// that only ever shows up as an intermittent missed shot.
    /// </para>
    /// <para>
    /// Any segment touching a cell outside the grid is denied, because
    /// <see cref="BlocksAttack"/> is true there.
    /// </para>
    /// </remarks>
    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY)
    {
        // Ordering the endpoints is what makes the trace symmetric; it costs one
        // comparison and removes a whole class of intermittent disagreement.
        if (toY < fromY || (toY == fromY && toX < fromX))
        {
            (fromX, toX) = (toX, fromX);
            (fromY, toY) = (toY, fromY);
        }

        int dx = Math.Abs(toX - fromX);
        int dy = -Math.Abs(toY - fromY);
        int stepX = fromX < toX ? 1 : -1;
        int stepY = fromY < toY ? 1 : -1;
        int error = dx + dy;

        int x = fromX;
        int y = fromY;

        while (true)
        {
            if (BlocksAttack(x, y))
                return false;

            if (x == toX && y == toY)
                return true;

            // Doubled once rather than halved twice: keeps the comparison in integers
            // and the loop free of any division.
            int doubled = error * 2;

            if (doubled >= dy)
            {
                error += dy;
                x += stepX;
            }

            if (doubled <= dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    /// <inheritdoc cref="HasLineOfSight(int,int,int,int)"/>
    public bool HasLineOfSight(GridPoint from, GridPoint to) =>
        HasLineOfSight(from.X, from.Y, to.X, to.Y);

    /// <inheritdoc cref="IsWalkable(int,int)"/>
    public bool IsWalkable(GridPoint point) => IsWalkable(point.X, point.Y);

    /// <inheritdoc cref="BlocksAttack(int,int)"/>
    public bool BlocksAttack(GridPoint point) => BlocksAttack(point.X, point.Y);

    /// <summary>
    /// Reads a cell already known to exist, without a second bounds check.
    /// </summary>
    /// <remarks>
    /// Every caller has just returned from <see cref="Contains"/>, and the
    /// constructor has already established that the buffer covers
    /// <see cref="Width"/> × <see cref="Height"/>, so the index is provably inside
    /// the array and the check the JIT would emit is genuinely redundant rather than
    /// merely inconvenient. The multiplication cannot overflow for the same reason:
    /// the constructor rejected dimensions whose product does not fit.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte CellUnchecked(int x, int y) =>
        Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(_cells!),
            (nint)(uint)((y * Width) + x));
}
