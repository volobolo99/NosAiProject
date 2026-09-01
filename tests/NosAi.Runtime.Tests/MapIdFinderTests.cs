using System.Buffers.Binary;
using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The 777 grids as an oracle for the map id: a word is a candidate only while
/// it names a rectangle that contains the character, and only while that word
/// changes across a map. And what gets written down has to be an offset from a
/// base the runtime resolves again, because an address dies with the process.
/// </summary>
public sealed class MapIdFinderTests
{
    private static readonly MapIdAnchors Anchors = new(
        ModuleBase: 0x400000, ModuleSize: 0x100000, PlayerManager: 0x1000000, PlayerObject: 0x2000000);

    [Fact]
    public void APositionInsideALargeMapAndOutsideASmallOneSelectsOnlyTheLarge()
    {
        MapGridSize[] maps =
        {
            new(0, 49, 51),
            new(1, 160, 180),
            new(2, 150, 150),
            new(3, 180, 220),
        };

        HashSet<int> plausible = MapIdFinder.PlausibleIds(maps, 157, 102);

        Assert.Contains(1, plausible);
        Assert.Contains(3, plausible);
        Assert.DoesNotContain(0, plausible);
        Assert.DoesNotContain(2, plausible);
    }

    [Fact]
    public void ACellOnTheOriginIsInsideEveryPositiveRectangle()
    {
        MapGridSize[] maps = { new(0, 49, 51), new(1, 1, 1) };

        HashSet<int> plausible = MapIdFinder.PlausibleIds(maps, 0, 0);

        Assert.Equal(2, plausible.Count);
    }

    [Fact]
    public void ANegativeCoordinateIsInsideNoMap()
    {
        MapGridSize[] maps = { new(1, 160, 180) };

        Assert.Empty(MapIdFinder.PlausibleIds(maps, -1, 10));
        Assert.Empty(MapIdFinder.PlausibleIds(maps, 10, -1));
    }

    [Fact]
    public void AnAddressInsideTheClientImageIsMeasuredFromTheModule()
    {
        MapIdHit hit = Anchors.Anchor(0x400000 + 0x8ABC, 5);

        Assert.Equal(new MapIdHit(MapIdAnchorKind.Module, 0x8ABC, 5), hit);
        Assert.True(hit.IsDurable);
    }

    [Fact]
    public void AnAddressJustPastAStructBaseIsMeasuredFromThatStruct()
    {
        Assert.Equal(
            new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5),
            Anchors.Anchor(0x1000000 + 0x2A8, 5));
        Assert.Equal(
            new MapIdHit(MapIdAnchorKind.PlayerObject, 0x40, 5),
            Anchors.Anchor(0x2000000 + 0x40, 5));
    }

    [Fact]
    public void TheNearerBaseWinsWhenTwoWindowsOverlap()
    {
        var overlapping = new MapIdAnchors(0, 0, PlayerManager: 0x1000000, PlayerObject: 0x1000100);

        Assert.Equal(
            new MapIdHit(MapIdAnchorKind.PlayerObject, 0x10, 5),
            overlapping.Anchor(0x1000110, 5));
    }

    [Fact]
    public void AnAddressFarFromEveryBaseStaysAnAddressAndIsNotDurable()
    {
        MapIdHit hit = Anchors.Anchor(0x1DB2FF7C, 5);

        Assert.Equal(new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5), hit);
        Assert.False(hit.IsDurable);
        Assert.Equal("heap 0x1DB2FF7C", hit.Describe());
    }

    [Fact]
    public void AnOffsetResolvesAgainstWhereTheBaseIsNow()
    {
        var hit = new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5);
        var moved = new MapIdAnchors(0x400000, 0x100000, PlayerManager: 0x3000000, PlayerObject: 0x4000000);

        Assert.True(moved.TryResolve(hit, out long address));
        Assert.Equal(0x30002A8, address);
    }

    [Fact]
    public void AnOffsetPastTheStructWindowResolvesToNothing()
    {
        var hit = new MapIdHit(MapIdAnchorKind.PlayerManager, MapIdAnchors.StructWindow, 5);

        Assert.False(Anchors.TryResolve(hit, out long address));
        Assert.Equal(0, address);
    }

    [Fact]
    public void NarrowingDropsAnAddressThatDidNotChangeAndKeepsOneThatDid()
    {
        var previous = new List<MapIdHit>
        {
            new(MapIdAnchorKind.Heap, 0x100, 1),
            new(MapIdAnchorKind.Heap, 0x200, 1),
        };
        var now = new Dictionary<long, int>
        {
            [0x100] = 3,
            [0x200] = 1,
        };
        var plausible = new HashSet<int> { 3 };

        List<MapIdHit> survivors = MapIdFinder.Narrow(
            previous, plausible, Anchors,
            address => now.TryGetValue(address, out int value) ? value : null,
            requireChanged: true);

        Assert.Equal(new[] { new MapIdHit(MapIdAnchorKind.Heap, 0x100, 3) }, survivors);
    }

    [Fact]
    public void NarrowingFollowsTheAnchorAfterTheBaseHasMoved()
    {
        // The client replaces the manager on a map change. A candidate tracked by
        // its old address would be read out of whatever now occupies that memory.
        var previous = new[] { new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 1) };
        var moved = new MapIdAnchors(0x400000, 0x100000, PlayerManager: 0x3000000, PlayerObject: 0x4000000);
        var plausible = new HashSet<int> { 7 };

        List<MapIdHit> survivors = MapIdFinder.Narrow(
            previous, plausible, moved,
            address => address == 0x30002A8 ? 7 : 1,
            requireChanged: true);

        Assert.Equal(new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 7), Assert.Single(survivors));
    }

    [Fact]
    public void NarrowingDropsAWordThatIsNoLongerAContainingMap()
    {
        var previous = new[] { new MapIdHit(MapIdAnchorKind.Heap, 0x100, 1) };
        var plausible = new HashSet<int> { 7 };

        List<MapIdHit> survivors = MapIdFinder.Narrow(
            previous, plausible, Anchors, _ => 1, requireChanged: true);

        Assert.Empty(survivors);
    }

    [Fact]
    public void NarrowingDropsAnUnreadableAddress()
    {
        var previous = new[] { new MapIdHit(MapIdAnchorKind.Heap, 0x100, 1) };
        var plausible = new HashSet<int> { 2 };

        List<MapIdHit> survivors = MapIdFinder.Narrow(
            previous, plausible, Anchors, _ => null, requireChanged: true);

        Assert.Empty(survivors);
    }

    [Fact]
    public void OnTheSameProcessABareAddressStillMeansSomething()
    {
        var file = new MapIdCandidates(
            Passes: 1, Restarts: 0, ProcessId: 4242, PlayerX: 79, PlayerY: 110,
            Hits: new[] { new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5) });

        List<MapIdHit> carried = MapIdFinder.Carry(file, processId: 4242, out string note);

        Assert.Single(carried);
        Assert.Contains("Same client process", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AcrossARestartTheAddressesGoAndTheOffsetsStay()
    {
        var file = new MapIdCandidates(
            Passes: 2, Restarts: 0, ProcessId: 4242, PlayerX: 79, PlayerY: 110,
            Hits: new[]
            {
                new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5),
                new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5),
            });

        List<MapIdHit> carried = MapIdFinder.Carry(file, processId: 9999, out string note);

        Assert.Equal(new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5), Assert.Single(carried));
        Assert.Contains("Client restarted", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatDoesNotNameItsProcessIsTreatedAsIgnoranceNotAsTheSameProcess()
    {
        var file = new MapIdCandidates(
            Passes: 1, Restarts: 0, ProcessId: 0, PlayerX: 79, PlayerY: 110,
            Hits: new[] { new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5) });

        List<MapIdHit> carried = MapIdFinder.Carry(file, processId: 4242, out string note);

        Assert.Empty(carried);
        Assert.Contains("does not name the process", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ProofNeedsOneOffsetTwoMapsAndARestart()
    {
        MapIdHit[] anchored = { new(MapIdAnchorKind.PlayerManager, 0x2A8, 5) };
        MapIdHit[] bare = { new(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5) };

        Assert.True(MapIdFinder.Proven(anchored, passes: 2, restarts: 1));
        Assert.False(MapIdFinder.Proven(anchored, passes: 1, restarts: 1));
        Assert.False(MapIdFinder.Proven(anchored, passes: 2, restarts: 0));
        Assert.False(MapIdFinder.Proven(bare, passes: 2, restarts: 1));
        Assert.False(MapIdFinder.Proven(Array.Empty<MapIdHit>(), passes: 2, restarts: 1));
    }

    [Fact]
    public void TheAdviceAsksForTheProofThatIsStillMissing()
    {
        Assert.Contains(
            "restart the client",
            MapIdFinder.Advice(count: 1, durable: 1, passes: 2, restarts: 0, truncated: false),
            StringComparison.Ordinal);
        Assert.Contains(
            "Cross a portal",
            MapIdFinder.Advice(count: 1, durable: 1, passes: 1, restarts: 0, truncated: false),
            StringComparison.Ordinal);
        Assert.Contains(
            "cannot be written",
            MapIdFinder.Advice(count: 1, durable: 0, passes: 2, restarts: 1, truncated: false),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACatalogReadsWidthAndHeightWithoutLoadingCells()
    {
        using TempDir dir = TempDir.Create();
        WriteGrid(dir.Maps, 0, 49, 51);
        WriteGrid(dir.Maps, 1, 160, 180);

        Assert.True(MapGridExtractor.TryLoadCatalog(dir.Maps, out IReadOnlyList<MapGridSize> maps, out string? reason), reason);
        Assert.Equal(2, maps.Count);
        Assert.Equal(new MapGridSize(0, 49, 51), maps[0]);
        Assert.Equal(new MapGridSize(1, 160, 180), maps[1]);
    }

    [Fact]
    public void CandidateFileRoundTripsTheAnchorTheProcessAndTheCounters()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-mapid-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            string text = MapIdFinder.FormatCandidates(
                new[] { new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5) },
                passes: 2, restarts: 1, processId: 4242, playerX: 157, playerY: 102);
            File.WriteAllText(path, text);

            Assert.True(MapIdFinder.TryLoadCandidates(path, out MapIdCandidates? loaded));
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Passes);
            Assert.Equal(1, loaded.Restarts);
            Assert.Equal(4242, loaded.ProcessId);
            Assert.Equal(157, loaded.PlayerX);
            Assert.Equal(102, loaded.PlayerY);
            Assert.Equal(new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5), Assert.Single(loaded.Hits));
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void AFileWrittenBeforeAnchorsExistedReadsAsAddressesAndNamesNoProcess()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-mapid-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "# passes=1\n# player=157,102\n# candidates=1\n1DB2FF7C 5\n");

            Assert.True(MapIdFinder.TryLoadCandidates(path, out MapIdCandidates? loaded));
            Assert.NotNull(loaded);
            Assert.False(loaded!.NamesTheProcess);
            Assert.Equal(new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5), Assert.Single(loaded.Hits));
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void MapIdIsUnmappedUntilFindMapIdEstablishesAnOffset()
    {
        Assert.False(NosTaleClientLayout.TryReadMapId(out int mapId, out string? reason));
        Assert.Equal(0, mapId);
        Assert.Equal(NosTaleClientLayout.MapIdUnmapped, reason);
    }

    [Fact]
    public void TheRuntimeWiresFindMapId()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--find-mapid", program, StringComparison.Ordinal);
        Assert.Contains("MapIdFinder.Run", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapIdOffset = 0x30", File.ReadAllText(
            Path.Combine(root, "src", "NosAi.Runtime", "LiveIntegration", "NosTaleClientLayout.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheClientsOwnWritableImageJoinsThePrivateRegionsInTheScan()
    {
        var inside = new MemoryRegion(new IntPtr(0x401000), 0x1000, Protect: 0x04, Type: 0x1000000);
        var elsewhere = new MemoryRegion(new IntPtr(0x7FF00000), 0x1000, Protect: 0x04, Type: 0x1000000);

        Assert.True(MapIdFinder.InMainModule(inside, Anchors));
        Assert.False(MapIdFinder.InMainModule(elsewhere, Anchors));
        Assert.True(inside.IsWritable);
        Assert.False(new MemoryRegion(new IntPtr(0x401000), 0x1000, Protect: 0x20, Type: 0x1000000).IsWritable);
    }

    [Fact]
    public void RunRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, MapIdFinder.Run());
    }

    private static void WriteGrid(string directory, int mapId, int width, int height)
    {
        var file = new byte[MapGridFormat.HeaderBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)height);
        File.WriteAllBytes(Path.Combine(directory, mapId.ToString(CultureInfo.InvariantCulture) + ".grid"), file);
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
            string root = Path.Combine(Path.GetTempPath(), "nosai-find-mapid-" + Guid.NewGuid().ToString("N"));
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
