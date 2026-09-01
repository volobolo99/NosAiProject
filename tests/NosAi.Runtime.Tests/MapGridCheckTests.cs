using System.Buffers.Binary;
using NosAi.LiveIntegration;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The standing-cell printout: bytes, not an interpretation of them.
/// </summary>
public sealed class MapGridCheckTests
{
    [Fact]
    public void AnOpenStandingCellPrintsWalkableAndTheRawByte()
    {
        MapGrid grid = Grid(7, 3, 3,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00);

        string report = MapGridCheck.Describe(grid, 1, 1);

        Assert.Contains("map=7 3x3 player=1,1", report, StringComparison.Ordinal);
        Assert.Contains("standing: walkable raw=0x00", report, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockedStandingCellIsNamedBlockedAndTheBytesAreStillPrinted()
    {
        MapGrid grid = Grid(1, 3, 3,
            0x00, 0x00, 0x00,
            0x00, 0x01, 0x00,
            0x00, 0x00, 0x00);

        string report = MapGridCheck.Describe(grid, 1, 1);

        Assert.Contains("standing: blocked raw=0x01", report, StringComparison.Ordinal);
        Assert.Contains("0x00 0x01 0x00", report, StringComparison.Ordinal);
        Assert.DoesNotContain("inverted", report, StringComparison.Ordinal);
        Assert.DoesNotContain("transposed", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNeighbourhoodIsTheRawBytesInScreenOrder()
    {
        MapGrid grid = Grid(2, 3, 3,
            0x10, 0x11, 0x12,
            0x20, 0x21, 0x22,
            0x30, 0x31, 0x32);

        string report = MapGridCheck.Describe(grid, 1, 1);

        Assert.Contains("0x10 0x11 0x12", report, StringComparison.Ordinal);
        Assert.Contains("0x20 0x21 0x22", report, StringComparison.Ordinal);
        Assert.Contains("0x30 0x31 0x32", report, StringComparison.Ordinal);
        int top = report.IndexOf("0x10 0x11 0x12", StringComparison.Ordinal);
        int mid = report.IndexOf("0x20 0x21 0x22", StringComparison.Ordinal);
        int bot = report.IndexOf("0x30 0x31 0x32", StringComparison.Ordinal);
        Assert.True(top < mid && mid < bot);
    }

    [Fact]
    public void ACellOutsideTheGridIsBlockedWithoutInventingAByte()
    {
        MapGrid grid = Grid(4, 1, 1, 0x00);

        string report = MapGridCheck.Describe(grid, 5, 5);

        Assert.Contains("standing: blocked outside", report, StringComparison.Ordinal);
        Assert.Contains("-- -- --", report, StringComparison.Ordinal);
        Assert.DoesNotContain("raw=0x00", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEdgePrintsOutsideNeighboursAsBlanksRatherThanAFallbackByte()
    {
        MapGrid grid = Grid(5, 2, 2,
            0x0A, 0x0B,
            0x0C, 0x0D);

        string report = MapGridCheck.Describe(grid, 0, 0);

        Assert.Contains("standing: walkable raw=0x0A", report, StringComparison.Ordinal);
        Assert.Contains("-- -- --", report, StringComparison.Ordinal);
        Assert.Contains("-- 0x0A 0x0B", report, StringComparison.Ordinal);
        Assert.Contains("-- 0x0C 0x0D", report, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectWritesTheReportAndDoesNotTouchTheGridFile()
    {
        byte[] file = BuildGridFile(2, 1, 0x00, 0x01);
        using TempDir dir = TempDir.Create();
        string path = Path.Combine(dir.Maps, "9.grid");
        File.WriteAllBytes(path, file);
        byte[] before = File.ReadAllBytes(path);

        var previous = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int code;
        try
        {
            code = MapGridCheck.Inspect(dir.Maps, 9, 0, 0);
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal(0, code);
        Assert.Contains("map=9 2x1 player=0,0", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void AMissingGridIsANamedRefusal()
    {
        using TempDir dir = TempDir.Create();
        var previous = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int code;
        try
        {
            code = MapGridCheck.Inspect(dir.Maps, 3, 0, 0);
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal(1, code);
        Assert.Contains("grid_file_not_found:3", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, MapGridCheck.Run());
    }

    [Fact]
    public void TheMapIdOffsetIsPinnedSoAMovedValueIsADeliberateEdit()
        => Assert.Equal(0x30, NosTaleClientLayout.MapIdOffset);

    [Fact]
    public void TheRuntimeWiresTheGridCheckFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--grid-check", program, StringComparison.Ordinal);
        Assert.Contains("MapGridCheck.Run", program, StringComparison.Ordinal);
    }

    private static MapGrid Grid(int mapId, int width, int height, params byte[] cells)
        => new(mapId, width, height, cells);

    private static byte[] BuildGridFile(int width, int height, params byte[] cells)
    {
        var file = new byte[MapGridFormat.HeaderBytes + cells.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)height);
        cells.CopyTo(file.AsSpan(MapGridFormat.HeaderBytes));
        return file;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("NosAi.sln not found above the test output.");
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }
        public string Maps => Path.Combine(Root, "maps");

        private TempDir(string root) => Root = root;

        public static TempDir Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "nosai-grid-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "maps"));
            return new TempDir(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
