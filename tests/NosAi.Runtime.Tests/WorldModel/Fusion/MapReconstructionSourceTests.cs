using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Observability;
using NosAi.Runtime.WorldModel.Fusion;
using NosAi.Storage;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-03/A4: <see cref="MapReconstructionSource.Resolve"/>'s runtime-glue
/// contract, exercised against a real temp <c>.grid</c> file (same on-disk
/// format <see cref="MapGridExtractorTests"/> already builds by hand) and a
/// real temp-file <see cref="MapModelStore"/> (found via a real, ready,
/// labeled volume, the same precondition <c>VolumeLocatorTests</c> already
/// requires of the test host -- see <see cref="TestVolume"/>).
/// </summary>
public sealed class MapReconstructionSourceTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddSeconds(1);

    private static WorldModelSnapshot SnapshotFor(MapId mapId, DateTime nowUtc)
    {
        WorldModelSnapshot baseline = WorldModelSnapshot.Unknown("test_baseline", nowUtc);
        var map = new MapModel(
            mapId,
            WorldFact<string>.Unknown("map_name_catalog_not_available", nowUtc),
            WorldFact<MapBounds>.Unknown("map_bounds_not_yet_reconstructed", nowUtc),
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 0,
            nowUtc);
        return baseline with { Map = map };
    }

    private static void WriteGridFile(string mapsDirectory, int mapId, int width, int height, params byte[] cells)
    {
        Directory.CreateDirectory(mapsDirectory);
        var file = new byte[MapGridFormat.HeaderBytes + cells.Length];
        file[0] = (byte)(width & 0xFF);
        file[1] = (byte)((width >> 8) & 0xFF);
        file[2] = (byte)(height & 0xFF);
        file[3] = (byte)((height >> 8) & 0xFF);
        cells.CopyTo(file.AsSpan(MapGridFormat.HeaderBytes));
        string path = Path.Combine(mapsDirectory, mapId.ToString(CultureInfo.InvariantCulture) + ".grid");
        File.WriteAllBytes(path, file);
    }

    /// <summary>A fresh, empty temp directory for <c>.grid</c> files, deleted on dispose.</summary>
    private sealed class TempMapsDir : IDisposable
    {
        public string Directory { get; }

        private TempMapsDir(string directory) => Directory = directory;

        public static TempMapsDir Create()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nosai-maprecon-grids-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            return new TempMapsDir(dir);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// A real, temp-file-backed <see cref="SqliteJournalOptions"/> pointing at
    /// a fresh, uniquely-named subdirectory under a real, ready, labeled
    /// volume on the test host -- resolved the same way
    /// <c>NosAi.Core.Tests.VolumeLocatorTests.ResolvesARealAttachedVolumeByItsActualLabel</c>
    /// already assumes one exists, since <see cref="MapReconstructionSource"/>'s
    /// own constructor only accepts <see cref="SqliteJournalOptions"/> (it
    /// always opens its store via <see cref="MapModelStore.OpenFromVolume"/>,
    /// never a raw path) and there is no dedicated "NOSAI-SSD" volume on this
    /// test host. The created subtree is deleted on dispose.
    /// </summary>
    private sealed class TestVolume : IDisposable
    {
        public SqliteJournalOptions Options { get; }
        private readonly string _createdDirectory;

        private TestVolume(SqliteJournalOptions options, string createdDirectory)
        {
            Options = options;
            _createdDirectory = createdDirectory;
        }

        public static TestVolume Create()
        {
            // A test host can report several ready, labeled "volumes" that are
            // not actually writable by this process (on Linux, DriveInfo
            // enumerates pseudo filesystems too -- /proc, /sys, cgroup, ... --
            // each with its own fstype-as-label). Rather than assume the first
            // labeled match is usable the way NosAi.Core.Tests.VolumeLocatorTests
            // does for its own narrower assertion, this probes each candidate
            // with a real write and uses the first one that actually succeeds,
            // so the test is robust across hosts instead of tied to one label.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string label;
                try { label = drive.VolumeLabel; }
                catch (IOException) { continue; }
                if (string.IsNullOrEmpty(label)) continue;
                if (!VolumeLocator.TryResolve(label, out string driveRoot)) continue;

                string relativeDirectory = Path.Combine("nosai-map-reconstruction-tests", Guid.NewGuid().ToString("N"));
                string candidateDirectory = Path.Combine(driveRoot, relativeDirectory);
                if (!TryProbeWritable(candidateDirectory)) continue;

                string fileName = Path.Combine(relativeDirectory, "nosai-maps.db");
                var options = new SqliteJournalOptions(VolumeLabel: label, FileName: fileName);
                return new TestVolume(options, candidateDirectory);
            }

            throw new InvalidOperationException(
                "Test host has no ready, labeled, writable volume -- MapReconstructionSource always opens its store " +
                "via MapModelStore.OpenFromVolume, so this test needs at least one to exist.");
        }

        private static bool TryProbeWritable(string candidateDirectory)
        {
            try
            {
                System.IO.Directory.CreateDirectory(candidateDirectory);
                string probe = Path.Combine(candidateDirectory, "probe.tmp");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_createdDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Resolve_FirstCallForAKnownMap_ProducesRealTilesAndPersistsThem()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 3, width: 2, height: 2, cells: new byte[] { 0x00, 0x01, 0x00, 0x00 });
        var mapId = new MapId("map-3");
        WorldModelSnapshot snapshot = SnapshotFor(mapId, T0);

        MapModel result;
        using (var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger()))
            result = source.Resolve(snapshot, T0);

        Assert.Equal(4, result.Tiles.Count);
        Assert.True(result.Version > 0);
        Assert.All(result.Tiles, t => Assert.Equal(DataSourceKind.Cached, t.Traversability.Source));

        string dbPath = VolumeLocator.ResolveDatabasePath(volume.Options);
        using var store = new MapModelStore(dbPath, volume.Options);
        Assert.True(store.TryLoad(mapId, out MapModel persisted));
        Assert.Equal(result.Tiles.Count, persisted.Tiles.Count);
        Assert.Equal(result.Version, persisted.Version);
    }

    [Fact]
    public void Resolve_SecondCallForTheSameMap_UsesTheCache_AndNeverTouchesTheGridOrStoreAgain()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 7, width: 2, height: 1, cells: new byte[] { 0x00, 0x01 });
        var mapId = new MapId("map-7");
        WorldModelSnapshot snapshot = SnapshotFor(mapId, T0);

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel first = source.Resolve(snapshot, T0);
        Assert.Equal(2, first.Tiles.Count);

        // Delete the maps directory entirely: if the second Resolve call
        // touched the grid again, this would degrade to an empty-batch merge
        // (still 2 tiles, since Merge is a no-op against an empty batch) or,
        // if it also re-read the store, would still return an equal value --
        // so the strong assertion is reference identity: the second call
        // must return the exact same MapModel instance the first one cached,
        // never a freshly reconstructed (even if equal) one.
        Directory.Delete(maps.Directory, recursive: true);

        MapModel second = source.Resolve(SnapshotFor(mapId, T1), T1);

        Assert.Same(first, second);
    }

    [Fact]
    public void Resolve_MapChange_ReRunsThePipelineForTheNewMap()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 10, width: 2, height: 1, cells: new byte[] { 0x00, 0x00 });
        WriteGridFile(maps.Directory, mapId: 11, width: 3, height: 1, cells: new byte[] { 0x00, 0x00, 0x00 });

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel first = source.Resolve(SnapshotFor(new MapId("map-10"), T0), T0);
        MapModel second = source.Resolve(SnapshotFor(new MapId("map-11"), T1), T1);

        Assert.Equal(2, first.Tiles.Count);
        Assert.Equal(3, second.Tiles.Count);
        Assert.Equal("map-10", first.Id.Value);
        Assert.Equal("map-11", second.Id.Value);
    }

    [Fact]
    public void Resolve_AfterRestart_RecoversThePersistedMapWithoutTheGridFilePresentAgain()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 42, width: 2, height: 2, cells: new byte[] { 0x00, 0x01, 0x00, 0x00 });
        var mapId = new MapId("map-42");

        MapModel firstResult;
        using (var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger()))
            firstResult = source.Resolve(SnapshotFor(mapId, T0), T0);

        // The grid file is gone before the "restart" -- recovery must come
        // entirely from the persisted MapModel, not from re-reading the grid.
        Directory.Delete(maps.Directory, recursive: true);

        using var restarted = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel secondResult = restarted.Resolve(SnapshotFor(mapId, T1), T1);

        Assert.Equal(firstResult.Tiles.Count, secondResult.Tiles.Count);
        Assert.Equal(firstResult.Version, secondResult.Version);
        Assert.Equal(firstResult.Id, secondResult.Id);
    }

    [Fact]
    public void Resolve_NoRealMapIdKnown_ReturnsTheSnapshotsOwnMapUnchanged_AndLeavesTheCacheUntouched()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 9, width: 1, height: 1, cells: new byte[] { 0x00 });
        var realMapId = new MapId("map-9");

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel cached = source.Resolve(SnapshotFor(realMapId, T0), T0);

        WorldModelSnapshot unknownSnapshot = SnapshotFor(new MapId("unknown-map"), T1);
        MapModel unknownResult = source.Resolve(unknownSnapshot, T1);

        Assert.Same(unknownSnapshot.Map, unknownResult);

        // The interleaved "unknown-map" call must not have disturbed the
        // cache for the real map: resolving it again must still return the
        // exact object the first call produced.
        MapModel again = source.Resolve(SnapshotFor(realMapId, T0.AddSeconds(2)), T0.AddSeconds(2));
        Assert.Same(cached, again);
    }

    [Fact]
    public void Resolve_AMapIdThatDoesNotParse_ReturnsTheSnapshotsOwnMapUnchanged()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        WorldModelSnapshot snapshot = SnapshotFor(new MapId("map-not-a-number"), T0);

        MapModel result = source.Resolve(snapshot, T0);

        Assert.Same(snapshot.Map, result);
    }

    [Fact]
    public void Resolve_NoStoreAvailable_StillReconstructsInMemoryForTheSession()
    {
        // No SqliteJournalOptions override is passed, so the constructor
        // tries MapModelStore.OpenFromVolume against the default "NOSAI-SSD"
        // label, which is not attached on this test host -- exercising the
        // fail-soft "continue with a null store" path.
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 1, width: 1, height: 1, cells: new byte[] { 0x00 });
        var logger = new RecordingLogger();

        using var source = new MapReconstructionSource(mapsDirectoryOverride: maps.Directory, logger: logger);
        MapModel result = source.Resolve(SnapshotFor(new MapId("map-1"), T0), T0);

        Assert.Single(result.Tiles);
        Assert.Single(logger.Errors);
    }

    private sealed class RecordingLogger : IRuntimeLogger
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = new();
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Errors.Add((message, exception));
    }

    // ----------------------------------------------- RecordPortalCrossing

    private static Portal CrossingPortal(MapId sourceMap, MapId destinationMap, DateTime observedAtUtc, float x = 10f, float y = 20f)
    {
        // The id is derived the same way PortalCrossingDetector derives it --
        // from the position rounded to PositionRoundingUnits -- so two portals
        // built from nearby positions (as repeated crossings of one physical
        // portal would produce) carry the same id and MergeByKey refines them.
        double round(float value) =>
            Math.Round(value / PortalCrossingDetector.PositionRoundingUnits, MidpointRounding.AwayFromZero)
            * PortalCrossingDetector.PositionRoundingUnits;

        return new Portal(
            new PortalId($"observed:{sourceMap.Value}:{round(x).ToString("R", CultureInfo.InvariantCulture)}:{round(y).ToString("R", CultureInfo.InvariantCulture)}"),
            sourceMap,
            WorldFact<WorldPosition>.Live(new WorldPosition(x, y), 1d, observedAtUtc),
            WorldFact<MapId>.Live(destinationMap, 1d, observedAtUtc),
            WorldFact<bool>.Live(true, 1d, observedAtUtc));
    }

    [Fact]
    public void RecordPortalCrossing_ForANeverResolvedMap_PersistsThePortal()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        var sourceMap = new MapId("map-5");
        var destinationMap = new MapId("map-6");

        // A fresh source with no prior Resolve call for either map: the portal
        // for the map just left must still be recorded against the persisted
        // baseline (Unknown -> merge -> persist), not lost.
        using (var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger()))
            source.RecordPortalCrossing(CrossingPortal(sourceMap, destinationMap, T0), T0);

        // A second, independent source against the same volume sees the row.
        using var reopened = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel result = reopened.Resolve(SnapshotFor(sourceMap, T1), T1);

        Portal portal = Assert.Single(result.Portals);
        Assert.Equal(sourceMap, portal.SourceMap);
        Assert.Equal(destinationMap, portal.DestinationMap.Value);
    }

    [Fact]
    public void RecordPortalCrossing_ForTheCurrentlyCachedMap_UpdatesTheInMemoryCache()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 12, width: 2, height: 1, cells: new byte[] { 0x00, 0x00 });
        var mapA = new MapId("map-12");
        var destinationMap = new MapId("map-13");

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel first = source.Resolve(SnapshotFor(mapA, T0), T0);
        Assert.Empty(first.Portals);

        source.RecordPortalCrossing(CrossingPortal(mapA, destinationMap, T1), T1);

        // The grid file is gone: a cache hit (not a re-read of the grid or the
        // store) is the only way the portal still shows up.
        Directory.Delete(maps.Directory, recursive: true);
        MapModel second = source.Resolve(SnapshotFor(mapA, T1.AddSeconds(1)), T1.AddSeconds(1));

        Portal portal = Assert.Single(second.Portals);
        Assert.Equal(destinationMap, portal.DestinationMap.Value);
        Assert.True(second.Version > first.Version);
    }

    [Fact]
    public void RecordPortalCrossing_ForANonCachedMap_LeavesTheCurrentCacheUntouched()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        WriteGridFile(maps.Directory, mapId: 21, width: 1, height: 1, cells: new byte[] { 0x00 });
        var mapB = new MapId("map-21");     // the map currently cached
        var mapA = new MapId("map-20");     // a different map, the crossing's source

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        MapModel cached = source.Resolve(SnapshotFor(mapB, T0), T0);

        source.RecordPortalCrossing(CrossingPortal(mapA, mapB, T1), T1);

        // Resolve for B again must return the exact cached instance -- the
        // recording for A must not have disturbed B's cache.
        MapModel again = source.Resolve(SnapshotFor(mapB, T1.AddSeconds(1)), T1.AddSeconds(1));
        Assert.Same(cached, again);
    }

    [Fact]
    public void RecordPortalCrossing_TwoCrossingsOfTheSamePortal_MergeIntoOneRow()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        var sourceMap = new MapId("map-30");
        var destinationMap = new MapId("map-31");

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());

        // Two crossings of the same physical portal: same rounded position,
        // hence the same derived Portal.Id (the detector rounds, so float
        // jitter between polls does not split one portal into several ids).
        source.RecordPortalCrossing(CrossingPortal(sourceMap, destinationMap, T0, x: 10.1f, y: 20.9f), T0);
        source.RecordPortalCrossing(CrossingPortal(sourceMap, destinationMap, T1, x: 10.2f, y: 20.8f), T1);

        // The in-memory cache for the source map now carries the merged portal.
        MapModel result = source.Resolve(SnapshotFor(sourceMap, T1.AddSeconds(1)), T1.AddSeconds(1));

        Portal portal = Assert.Single(result.Portals); // merged, not duplicated
        Assert.Equal(destinationMap, portal.DestinationMap.Value);
        Assert.Equal(T1, portal.DestinationMap.ObservedAtUtc); // second crossing refined it
    }

    // ------------------------------------------------ LoadAllKnownMaps

    [Fact]
    public void LoadAllKnownMaps_WithNothingEverPersisted_ReturnsAnEmptyDictionary()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());

        Assert.Empty(source.LoadAllKnownMaps());
    }

    [Fact]
    public void LoadAllKnownMaps_AfterPersistingTwoMaps_ReturnsBothWithTheirCorrectContent()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        var mapA = new MapId("map-40");
        var mapB = new MapId("map-41");

        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        source.RecordPortalCrossing(CrossingPortal(mapA, mapB, T0), T0);
        source.RecordPortalCrossing(CrossingPortal(mapB, mapA, T0.AddSeconds(2)), T0.AddSeconds(2));

        IReadOnlyDictionary<MapId, MapModel> known = source.LoadAllKnownMaps();

        Assert.Equal(2, known.Count);
        Assert.True(known.ContainsKey(mapA));
        Assert.True(known.ContainsKey(mapB));
        // Assert on the portal content, not full-object equality, to stay
        // resilient to unrelated MapModel field changes.
        Assert.Single(known[mapA].Portals);
        Assert.Equal(mapB, known[mapA].Portals[0].DestinationMap.Value);
        Assert.Single(known[mapB].Portals);
        Assert.Equal(mapA, known[mapB].Portals[0].DestinationMap.Value);
    }

    [Fact]
    public void LoadAllKnownMaps_FromAnIndependentSource_SeesMapsPersistedByAnother()
    {
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        var mapA = new MapId("map-50");
        var mapB = new MapId("map-51");

        using (var first = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger()))
        {
            first.RecordPortalCrossing(CrossingPortal(mapA, mapB, T0), T0);
            first.RecordPortalCrossing(CrossingPortal(mapB, mapA, T0.AddSeconds(2)), T0.AddSeconds(2));
        }

        // A second, independent source against the same volume reads the
        // shared store, not some per-instance in-memory cache.
        using var second = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());
        IReadOnlyDictionary<MapId, MapModel> known = second.LoadAllKnownMaps();

        Assert.Equal(2, known.Count);
        Assert.True(known.ContainsKey(mapA));
        Assert.True(known.ContainsKey(mapB));
        Assert.Single(known[mapA].Portals);
        Assert.Single(known[mapB].Portals);
    }
}
