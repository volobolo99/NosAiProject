using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The specification every <see cref="IMapGridLoader"/> has to satisfy, written as
/// tests rather than as prose.
/// </summary>
/// <remarks>
/// <para>
/// <b>How to use this.</b> The loader itself is not in this repository yet. When it
/// arrives, it activates this whole file by subclassing it:
/// </para>
/// <code>
/// public sealed class BinaryMapGridLoaderTests : MapGridLoaderContractTests
/// {
///     protected override IMapGridLoader CreateLoader() =&gt; new BinaryMapGridLoader();
/// }
/// </code>
/// <para>
/// Until then this class is abstract and xunit collects nothing from it, which is the
/// intended state: a contract with no implementation should be visibly unimplemented,
/// not quietly green.
/// </para>
/// <para>
/// <b>What it is testing.</b> The format is four bytes of header — <c>uint16</c>
/// little-endian width, then height — followed by exactly width × height cells,
/// row-major, one byte each, with the bit meanings in <see cref="MapCellFlags"/>. The
/// cases below are the ones where a plausible implementation goes wrong quietly:
/// a transposed index, an endianness assumption, a truncated file read as a small
/// map, a corrupted header that becomes a four-billion-cell allocation.
/// </para>
/// <para>
/// <b>Every refusal must leave <c>default(MapGrid)</c>.</b> That is not tidiness. A
/// caller that ignores the return value then holds a grid that blocks the whole map,
/// which stops planning; a half-filled grid would let it plan across cells nobody
/// parsed.
/// </para>
/// </remarks>
public abstract class MapGridLoaderContractTests
{
    /// <summary>The loader under test. One fresh instance per test.</summary>
    protected abstract IMapGridLoader CreateLoader();

    /// <summary>Builds a well-formed file: little-endian header, then the cells.</summary>
    protected static byte[] BuildFile(int width, int height, params byte[] cells)
    {
        var file = new byte[MapGridFormat.HeaderBytes + cells.Length];
        file[0] = (byte)(width & 0xFF);
        file[1] = (byte)((width >> 8) & 0xFF);
        file[2] = (byte)(height & 0xFF);
        file[3] = (byte)((height >> 8) & 0xFF);
        cells.CopyTo(file.AsSpan(MapGridFormat.HeaderBytes));
        return file;
    }

    /// <summary>A file whose header declares a rectangle the payload does not match.</summary>
    protected static byte[] BuildHeaderOnly(int width, int height) =>
        BuildFile(width, height);

    // ----------------------------------------------------------- what must work

    [Fact]
    public void AWellFormedFileLoadsWithItsDimensionsAndMapId()
    {
        byte[] file = BuildFile(3, 2, 0, 1, 2, 3, 4, 5);

        Assert.True(CreateLoader().TryLoad(42, file, out MapGrid grid, out string? reason), reason);

        Assert.Null(reason);
        Assert.True(grid.IsLoaded);
        Assert.Equal(42, grid.MapId);
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(6, grid.CellCount);
    }

    /// <summary>
    /// Row-major, and the test uses a non-square rectangle on purpose: a transposed
    /// index passes every square fixture there is.
    /// </summary>
    [Fact]
    public void CellsAreReadRowMajor()
    {
        // 3 wide, 2 tall. Row 0 is 10,11,12 and row 1 is 20,21,22.
        byte[] file = BuildFile(3, 2, 10, 11, 12, 20, 21, 22);

        Assert.True(CreateLoader().TryLoad(1, file, out MapGrid grid, out _));

        Assert.Equal(10, grid.RawAt(0, 0));
        Assert.Equal(11, grid.RawAt(1, 0));
        Assert.Equal(12, grid.RawAt(2, 0));
        Assert.Equal(20, grid.RawAt(0, 1));
        Assert.Equal(21, grid.RawAt(1, 1));
        Assert.Equal(22, grid.RawAt(2, 1));
    }

    /// <summary>
    /// The header is little-endian. A big-endian read of this file would see a
    /// 512-wide map and refuse it for the wrong reason, so the dimensions are chosen
    /// to be wrong in both directions if the bytes are swapped.
    /// </summary>
    [Fact]
    public void TheHeaderIsLittleEndian()
    {
        // width 258 = 0x0102 -> bytes 02 01;  height 3 = 0x0003 -> bytes 03 00
        var file = new byte[MapGridFormat.HeaderBytes + (258 * 3)];
        file[0] = 0x02;
        file[1] = 0x01;
        file[2] = 0x03;
        file[3] = 0x00;

        Assert.True(CreateLoader().TryLoad(1, file, out MapGrid grid, out string? reason), reason);

        Assert.Equal(258, grid.Width);
        Assert.Equal(3, grid.Height);
    }

    /// <summary>
    /// Bits this project has not identified are carried through untouched. A loader
    /// that masked to the five known bits would make the file it read differ from the
    /// file on disk, and the set hash would then describe something nobody has.
    /// </summary>
    [Fact]
    public void EveryBitIsPreservedIncludingTheOnesNotNamedYet()
    {
        byte[] file = BuildFile(4, 1, 0x00, 0x1F, 0x20, 0xFF);

        Assert.True(CreateLoader().TryLoad(1, file, out MapGrid grid, out _));

        Assert.Equal(0x00, grid.RawAt(0, 0));
        Assert.Equal(0x1F, grid.RawAt(1, 0));
        Assert.Equal(0x20, grid.RawAt(2, 0));
        Assert.Equal(0xFF, grid.RawAt(3, 0));
    }

    /// <summary>The bits keep their meaning through the loader, not just through the struct.</summary>
    [Fact]
    public void TheLoadedGridAnswersTheBitSemantics()
    {
        byte[] file = BuildFile(4, 1, 0x00, 0x01, 0x02, 0x03);

        Assert.True(CreateLoader().TryLoad(1, file, out MapGrid grid, out _));

        Assert.True(grid.IsWalkable(0, 0));
        Assert.False(grid.BlocksAttack(0, 0));

        Assert.False(grid.IsWalkable(1, 0));
        Assert.False(grid.BlocksAttack(1, 0));

        Assert.True(grid.IsWalkable(2, 0));
        Assert.True(grid.BlocksAttack(2, 0));

        Assert.False(grid.IsWalkable(3, 0));
        Assert.True(grid.BlocksAttack(3, 0));
    }

    [Fact]
    public void TheSmallestValidFileIsOneCell()
    {
        byte[] file = BuildFile(1, 1, 0x01);

        Assert.True(CreateLoader().TryLoad(7, file, out MapGrid grid, out string? reason), reason);

        Assert.Equal(1, grid.CellCount);
        Assert.False(grid.IsWalkable(0, 0));
    }

    [Fact]
    public void LoadingIsDeterministic()
    {
        byte[] file = BuildFile(3, 2, 1, 2, 3, 4, 5, 6);
        IMapGridLoader loader = CreateLoader();

        Assert.True(loader.TryLoad(1, file, out MapGrid first, out _));
        Assert.True(loader.TryLoad(1, file, out MapGrid second, out _));

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
                Assert.Equal(first.RawAt(x, y), second.RawAt(x, y));
        }
    }

    /// <summary>
    /// The file is the extractor's, and its hash is the set identity. A loader that
    /// wrote into the buffer it was handed would change the thing the identity
    /// describes.
    /// </summary>
    [Fact]
    public void LoadingDoesNotModifyTheInput()
    {
        byte[] file = BuildFile(3, 2, 1, 2, 3, 4, 5, 6);
        byte[] before = (byte[])file.Clone();

        CreateLoader().TryLoad(1, file, out _, out _);

        Assert.Equal(before, file);
    }

    // -------------------------------------------------------- what must be refused

    [Fact]
    public void AFileTooShortToHoldAHeaderIsRefused()
    {
        IMapGridLoader loader = CreateLoader();

        for (var length = 0; length < MapGridFormat.HeaderBytes; length++)
        {
            Assert.False(
                loader.TryLoad(1, new byte[length], out MapGrid grid, out string? reason),
                $"a {length}-byte file was accepted");

            Assert.Equal(MapGridFormat.HeaderTruncated, reason);
            Assert.False(grid.IsLoaded);
        }
    }

    /// <summary>
    /// The case that must not be read as a smaller map. A truncated file whose
    /// payload is silently used as-is produces a grid the client never had, and every
    /// cell past the truncation reads as open ground.
    /// </summary>
    [Fact]
    public void APayloadShorterThanTheDeclaredRectangleIsRefused()
    {
        byte[] file = BuildFile(4, 4, new byte[15]);

        Assert.False(CreateLoader().TryLoad(1, file, out MapGrid grid, out string? reason));

        Assert.Equal(MapGridFormat.PayloadTruncated, reason);
        Assert.False(grid.IsLoaded);
    }

    [Fact]
    public void AHeaderWithNoPayloadAtAllIsRefused()
    {
        Assert.False(CreateLoader().TryLoad(1, BuildHeaderOnly(2, 2), out MapGrid grid, out string? reason));

        Assert.Equal(MapGridFormat.PayloadTruncated, reason);
        Assert.False(grid.IsLoaded);
    }

    /// <summary>
    /// Trailing bytes mean the parse and the file disagree about the format, and the
    /// explanation that must not be assumed is the convenient one. The struct accepts
    /// an over-long buffer because a pooled array is not a file; a file is a contract.
    /// </summary>
    [Fact]
    public void APayloadLongerThanTheDeclaredRectangleIsRefused()
    {
        byte[] file = BuildFile(2, 2, 1, 2, 3, 4, 5);

        Assert.False(CreateLoader().TryLoad(1, file, out MapGrid grid, out string? reason));

        Assert.Equal(MapGridFormat.PayloadOversized, reason);
        Assert.False(grid.IsLoaded);
    }

    /// <summary>
    /// A rectangle with no cells would load, report <c>IsLoaded</c>, and block every
    /// query — indistinguishable at a glance from a map that is entirely walls. It is
    /// refused so the extractor hears about it.
    /// </summary>
    [Fact]
    public void AZeroDimensionIsRefused()
    {
        IMapGridLoader loader = CreateLoader();

        foreach ((int w, int h) in new[] { (0, 4), (4, 0), (0, 0) })
        {
            Assert.False(
                loader.TryLoad(1, BuildHeaderOnly(w, h), out MapGrid grid, out string? reason),
                $"a {w}x{h} rectangle was accepted");

            Assert.Equal(MapGridFormat.EmptyRectangle, reason);
            Assert.False(grid.IsLoaded);
        }
    }

    /// <summary>
    /// Four corrupted header bytes can declare 65535 × 65535 — over four billion
    /// cells, past <see cref="int.MaxValue"/>. It has to be refused in arithmetic,
    /// before anything tries to allocate it, and without the multiplication wrapping
    /// to something small and plausible.
    /// </summary>
    [Fact]
    public void ARectangleTooLargeToBeAMapIsRefusedInArithmeticNotInAnAllocation()
    {
        byte[] file = BuildHeaderOnly(MapGridFormat.MaxDimension, MapGridFormat.MaxDimension);

        Assert.False(CreateLoader().TryLoad(1, file, out MapGrid grid, out string? reason));

        Assert.Equal(MapGridFormat.RectangleImplausible, reason);
        Assert.False(grid.IsLoaded);
    }

    /// <summary>
    /// The ceiling is checked on the declared rectangle, so it is reached without a
    /// file that large actually existing.
    /// </summary>
    [Fact]
    public void TheCellCeilingIsAppliedToWhatTheHeaderDeclares()
    {
        // 65535 x 2048 is over 134 million cells: past MaxCells, inside Int32.
        byte[] file = BuildHeaderOnly(MapGridFormat.MaxDimension, 2048);

        Assert.False(CreateLoader().TryLoad(1, file, out MapGrid grid, out string? reason));

        Assert.Equal(MapGridFormat.RectangleImplausible, reason);
        Assert.False(grid.IsLoaded);
    }

    /// <summary>
    /// A refusal is a named token the caller can match on, never a bare false and
    /// never an exception: a malformed file is what a client patch produces, and it
    /// has to be distinguishable from a bug.
    /// </summary>
    [Fact]
    public void EveryRefusalCarriesAReasonFromTheFormatVocabulary()
    {
        string[] vocabulary =
        [
            MapGridFormat.HeaderTruncated,
            MapGridFormat.PayloadTruncated,
            MapGridFormat.PayloadOversized,
            MapGridFormat.EmptyRectangle,
            MapGridFormat.RectangleImplausible
        ];

        byte[][] malformed =
        [
            [],
            [0x01],
            BuildHeaderOnly(2, 2),
            BuildFile(2, 2, 1, 2, 3, 4, 5),
            BuildHeaderOnly(0, 0),
            BuildHeaderOnly(MapGridFormat.MaxDimension, MapGridFormat.MaxDimension)
        ];

        IMapGridLoader loader = CreateLoader();

        foreach (byte[] file in malformed)
        {
            Assert.False(loader.TryLoad(1, file, out MapGrid grid, out string? reason));
            Assert.False(grid.IsLoaded);
            Assert.NotNull(reason);
            Assert.Contains(reason, vocabulary);
        }
    }

    /// <summary>
    /// The property that makes ignoring the return value safe rather than merely
    /// untidy: a refused load leaves a grid that blocks the whole map.
    /// </summary>
    [Fact]
    public void ARefusedLoadLeavesAGridThatBlocksEverything()
    {
        CreateLoader().TryLoad(1, BuildHeaderOnly(2, 2), out MapGrid grid, out _);

        Assert.False(grid.IsLoaded);
        Assert.False(grid.IsWalkable(0, 0));
        Assert.True(grid.BlocksAttack(0, 0));
        Assert.False(grid.HasLineOfSight(0, 0, 1, 1));
    }
}
