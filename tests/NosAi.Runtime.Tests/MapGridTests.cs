using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The client's static map geometry: the bit semantics, and every place where "not
/// represented" has to mean blocked rather than free.
/// </summary>
/// <remarks>
/// <c>docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md</c> § 5 and
/// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3. The grid is the one part of
/// navigation that can be tested completely offline with no client running, which is
/// most of the reason for reading it from a file instead of discovering it.
/// </remarks>
public sealed class MapGridTests
{
    private const byte Open = 0x00;
    private const byte Wall = 0x01;   // WalkBlocked
    private const byte Glass = 0x02;  // AttackBlocked but walkable
    private const byte Solid = 0x03;  // both

    /// <summary>A grid from a picture, so a test reads as the map it describes.</summary>
    /// <remarks>
    /// Row zero is the first string. '.' open, '#' solid, 'o' walkable but stops
    /// attacks, 'x' walls the feet and lets the arrow through.
    /// </remarks>
    private static MapGrid Grid(params string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var cells = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            Assert.Equal(width, rows[y].Length);
            for (var x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] switch
                {
                    '.' => Open,
                    '#' => Solid,
                    'o' => Glass,
                    'x' => Wall,
                    _ => throw new ArgumentException($"Unknown cell '{rows[y][x]}'.")
                };
            }
        }

        return new MapGrid(mapId: 1, width, height, cells);
    }

    // ------------------------------------------------------------ the bits

    [Fact]
    public void EachBitMeansWhatTheClientFormatSaysItMeans()
    {
        var cells = new byte[]
        {
            0x00,  // nothing set
            0x01,  // walk blocked
            0x02,  // attack blocked
            0x04,  // raid
            0x08,  // aggro disabled
            0x10   // pvp disabled
        };

        var grid = new MapGrid(1, cells.Length, 1, cells);

        Assert.True(grid.IsWalkable(0, 0));
        Assert.False(grid.BlocksAttack(0, 0));

        Assert.False(grid.IsWalkable(1, 0));
        Assert.False(grid.BlocksAttack(1, 0));

        Assert.True(grid.IsWalkable(2, 0));
        Assert.True(grid.BlocksAttack(2, 0));

        Assert.True(grid.IsRaidConstrained(3, 0));
        Assert.True(grid.IsAggroDisabled(4, 0));
        Assert.True(grid.IsPvpDisabled(5, 0));

        // And no bit leaks into a neighbour's meaning.
        Assert.False(grid.IsAggroDisabled(3, 0));
        Assert.False(grid.IsPvpDisabled(4, 0));
        Assert.False(grid.IsRaidConstrained(5, 0));
    }

    /// <summary>
    /// Walkability and line of sight are different facts and the format keeps them
    /// apart: a chasm stops the feet and not the arrow, a pane of glass the reverse.
    /// </summary>
    [Fact]
    public void WalkabilityAndAttackBlockingAreIndependent()
    {
        MapGrid grid = Grid("xo");

        Assert.False(grid.IsWalkable(0, 0));
        Assert.False(grid.BlocksAttack(0, 0));

        Assert.True(grid.IsWalkable(1, 0));
        Assert.True(grid.BlocksAttack(1, 0));
    }

    [Fact]
    public void UnnamedBitsArePreservedRatherThanMaskedOff()
    {
        // 0x20 is not a bit this project has identified. It must survive a read.
        var grid = new MapGrid(1, 1, 1, new byte[] { 0x20 | 0x01 });

        Assert.Equal(0x21, grid.RawAt(0, 0));
        Assert.False(grid.IsWalkable(0, 0));
    }

    // -------------------------------------------------- outside the rectangle

    /// <summary>
    /// The rule the whole contract turns on. Out of grid is blocked, not free
    /// (DOMAIN-10).
    /// </summary>
    [Fact]
    public void OutsideTheGridNothingIsWalkableAndEverythingStopsAnAttack()
    {
        MapGrid grid = Grid(
            "..",
            "..");

        foreach ((int x, int y) in new[] { (-1, 0), (0, -1), (2, 0), (0, 2), (-1, -1), (99, 99) })
        {
            Assert.False(grid.IsWalkable(x, y));
            Assert.True(grid.BlocksAttack(x, y));
            Assert.False(grid.Contains(x, y));
            Assert.False(grid.TryGetFlags(x, y, out _));
        }
    }

    /// <summary>
    /// Fail-closed is about the consequence, not about a uniform default. A
    /// restriction of unknown presence is assumed present; a protection of unknown
    /// presence is assumed absent. Both are the answer that does not authorise.
    /// </summary>
    [Fact]
    public void OutsideTheGridProtectionsAreAbsentAndRestrictionsApply()
    {
        MapGrid grid = Grid("..");

        // Restrictions: assumed to apply.
        Assert.True(grid.BlocksAttack(5, 5));
        Assert.True(grid.IsRaidConstrained(5, 5));

        // Protections: assumed absent. Believing an unknown cell is aggro-free or
        // PvP-free is the permissive error.
        Assert.False(grid.IsAggroDisabled(5, 5));
        Assert.False(grid.IsPvpDisabled(5, 5));
    }

    /// <summary>
    /// A struct can always be defaulted, so the default has to be the safe case:
    /// a grid nobody loaded blocks the map instead of opening it.
    /// </summary>
    [Fact]
    public void ADefaultGridIsNotLoadedAndBlocksEverything()
    {
        MapGrid grid = default;

        Assert.False(grid.IsLoaded);
        Assert.Equal(0, grid.CellCount);
        Assert.False(grid.IsWalkable(0, 0));
        Assert.True(grid.BlocksAttack(0, 0));
        Assert.False(grid.HasLineOfSight(0, 0, 0, 0));
        Assert.False(grid.Contains(0, 0));
    }

    /// <summary>
    /// A zero-area grid blocks exactly as a default one does, and still reports that
    /// it was loaded — "the client ships an empty map" and "no grid was loaded" are
    /// different faults and only one of them is the extractor's.
    /// </summary>
    [Fact]
    public void AnEmptyRectangleIsLoadedAndStillBlocksEverything()
    {
        var grid = new MapGrid(1, 0, 0, Array.Empty<byte>());

        Assert.True(grid.IsLoaded);
        Assert.False(grid.IsWalkable(0, 0));
        Assert.True(grid.BlocksAttack(0, 0));
    }

    [Fact]
    public void ABufferTooSmallForTheRectangleIsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(1, 4, 4, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(1, -1, 4, new byte[16]));
        Assert.Throws<ArgumentNullException>(() => new MapGrid(1, 1, 1, null!));
    }

    /// <summary>
    /// Dimensions whose product overflows Int32 must be caught in arithmetic, not
    /// wrap to a small positive length that a short buffer happens to satisfy.
    /// </summary>
    [Fact]
    public void DimensionsThatOverflowAreRefusedRatherThanWrapped()
    {
        // 65536 * 65536 wraps to 0 in Int32 arithmetic.
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(1, 65536, 65536, new byte[16]));
    }

    /// <summary>A longer buffer is accepted so a loader may pass a pooled array.</summary>
    [Fact]
    public void ABufferLongerThanTheRectangleIsAcceptedAndTheExcessIsNeverRead()
    {
        var cells = new byte[16];
        cells[4] = Wall;       // inside a 2x2 rectangle this is past the end
        var grid = new MapGrid(1, 2, 2, cells);

        Assert.Equal(4, grid.CellCount);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
                Assert.True(grid.IsWalkable(x, y));
        }
    }

    // ------------------------------------------------------ line of sight

    [Fact]
    public void AClearSegmentHasLineOfSight()
    {
        MapGrid grid = Grid(
            ".....",
            ".....",
            ".....");

        Assert.True(grid.HasLineOfSight(0, 0, 4, 2));
        Assert.True(grid.HasLineOfSight(0, 1, 4, 1));
    }

    [Fact]
    public void ACellThatStopsAttacksBreaksTheSegment()
    {
        MapGrid grid = Grid(
            ".....",
            "..o..",
            ".....");

        Assert.False(grid.HasLineOfSight(0, 1, 4, 1));

        // And the same wall does not stop the feet: 0x02 alone is not 0x01.
        Assert.True(grid.IsWalkable(2, 1));
    }

    /// <summary>
    /// A cell that blocks walking but not attacks does not break the line. This is
    /// the case a single "is this tile solid" boolean would get wrong.
    /// </summary>
    [Fact]
    public void ACellThatOnlyBlocksWalkingDoesNotBreakTheSegment()
    {
        MapGrid grid = Grid(
            ".....",
            "..x..",
            ".....");

        Assert.True(grid.HasLineOfSight(0, 1, 4, 1));
        Assert.False(grid.IsWalkable(2, 1));
    }

    /// <summary>Both endpoints are traced, which is the literal contract and the fail-closed reading.</summary>
    [Fact]
    public void BothEndpointsAreTestedNotJustTheCellsBetween()
    {
        MapGrid grid = Grid(
            "o...",
            "....",
            "...o");

        // Standing in a cell that stops attacks: no line out of it.
        Assert.False(grid.HasLineOfSight(0, 0, 2, 1));

        // Aiming into one: no line into it either.
        Assert.False(grid.HasLineOfSight(0, 1, 3, 2));

        // The same segment between two open cells is clear.
        Assert.True(grid.HasLineOfSight(0, 1, 2, 1));
    }

    [Fact]
    public void ASegmentThatLeavesTheGridIsDenied()
    {
        MapGrid grid = Grid(
            "...",
            "...",
            "...");

        Assert.False(grid.HasLineOfSight(1, 1, 5, 1));
        Assert.False(grid.HasLineOfSight(-1, 1, 1, 1));
    }

    [Fact]
    public void ACellHasLineOfSightToItselfUnlessItBlocksAttacks()
    {
        MapGrid grid = Grid("o.");

        Assert.True(grid.HasLineOfSight(1, 0, 1, 0));
        Assert.False(grid.HasLineOfSight(0, 0, 0, 0));
    }

    /// <summary>
    /// A line of sight that depends on who is asking is a bug that shows up only as
    /// an intermittent missed shot, so the trace is made symmetric on purpose.
    /// </summary>
    [Fact]
    public void LineOfSightIsSymmetricInEveryDirectionOverAScatteredGrid()
    {
        MapGrid grid = Grid(
            ".....o..",
            "..o.....",
            "....o...",
            ".o......",
            "......o.",
            "...o....");

        for (var ay = 0; ay < 6; ay++)
        {
            for (var ax = 0; ax < 8; ax++)
            {
                for (var by = 0; by < 6; by++)
                {
                    for (var bx = 0; bx < 8; bx++)
                    {
                        Assert.Equal(
                            grid.HasLineOfSight(ax, ay, bx, by),
                            grid.HasLineOfSight(bx, by, ax, ay));
                    }
                }
            }
        }
    }

    [Fact]
    public void TheGridPointOverloadsAgreeWithTheIntegerOnes()
    {
        MapGrid grid = Grid(
            "..o",
            "x..");

        Assert.Equal(grid.IsWalkable(0, 1), grid.IsWalkable(new GridPoint(0, 1)));
        Assert.Equal(grid.BlocksAttack(2, 0), grid.BlocksAttack(new GridPoint(2, 0)));
        Assert.Equal(
            grid.HasLineOfSight(0, 0, 2, 1),
            grid.HasLineOfSight(new GridPoint(0, 0), new GridPoint(2, 1)));
    }

    /// <summary>
    /// Zero allocations on the query path. The buffer is the loader's; nothing here
    /// copies it, and the segment trace walks two integers rather than building the
    /// cells it crosses.
    /// </summary>
    [Fact]
    public void QueryingAllocatesNothing()
    {
        MapGrid grid = Grid(
            "........",
            "..o..x..",
            "........",
            "....o...");

        // Warm up: first touch of a code path can allocate for reasons that are not
        // the code's, and the measurement is about the steady state.
        for (var i = 0; i < 100; i++)
        {
            _ = grid.IsWalkable(i % 8, i % 4);
            _ = grid.BlocksAttack(i % 8, i % 4);
            _ = grid.HasLineOfSight(0, 0, 7, 3);
            _ = grid.TryGetFlags(i % 8, i % 4, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
        {
            _ = grid.IsWalkable(i % 8, i % 4);
            _ = grid.BlocksAttack(i % 8, i % 4);
            _ = grid.HasLineOfSight(0, 0, 7, 3);
            _ = grid.TryGetFlags(i % 8, i % 4, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
