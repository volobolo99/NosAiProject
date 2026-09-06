using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Observability;
using NosAi.Runtime.WorldModel.Fusion;
using NosAi.Storage;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-03/A5 audit item 7 ("exception safety at every new I/O boundary") and
/// a by-test (not by-reading-the-code) recheck of item 2's caching claim on
/// its SQLite side, complementing
/// <c>MapReconstructionSourceTests.Resolve_SecondCallForTheSameMap_UsesTheCache_AndNeverTouchesTheGridOrStoreAgain</c>,
/// which already proves the grid-file half of that claim by deleting the
/// grid directory -- this file proves the SQLite half independently, by
/// watching the database file itself for any write.
/// </summary>
public sealed class MapReconstructionSourceExceptionSafetyTests
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

    private sealed class TempMapsDir : IDisposable
    {
        public string Directory { get; }
        private TempMapsDir(string directory) => Directory = directory;

        public static TempMapsDir Create()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nosai-maprecon-exc-grids-" + Guid.NewGuid().ToString("N"));
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
    /// A real, temp-file-backed <see cref="SqliteJournalOptions"/>, same
    /// probing technique as <c>MapReconstructionSourceTests.TestVolume</c>
    /// (a real, ready, labeled, writable volume is required since
    /// <see cref="MapReconstructionSource"/> always opens its store via
    /// <see cref="MapModelStore.OpenFromVolume"/>).
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
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string label;
                try { label = drive.VolumeLabel; }
                catch (IOException) { continue; }
                if (string.IsNullOrEmpty(label)) continue;
                if (!VolumeLocator.TryResolve(label, out string driveRoot)) continue;

                string relativeDirectory = Path.Combine("nosai-map-reconstruction-exc-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingLogger : IRuntimeLogger
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = new();
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Errors.Add((message, exception));
    }

    [Fact]
    public void Resolve_WhenThePersistedStoreThrowsOnRead_StillCompletesWithoutThrowing_KnownMapReconstructionSourceGap()
    {
        // Setup: a real MapReconstructionSource, one successful Resolve for
        // map-1 so the store/schema genuinely exist on disk (not a store that
        // never opened -- that fail-soft path is already covered by
        // MapReconstructionSourceTests.Resolve_NoStoreAvailable_...).
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        var logger = new RecordingLogger();
        using var source = new MapReconstructionSource(volume.Options, maps.Directory, logger);

        MapModel warmup = source.Resolve(SnapshotFor(new MapId("map-1"), T0), T0);
        Assert.NotNull(warmup);

        // Corrupt the persisted store out from under the *same, still-open*
        // MapReconstructionSource: a second, independent connection to the
        // exact same database file drops the one table MapModelStore reads
        // from and writes to. This is not a hypothetical -- it is the same
        // class of failure MapModelStore's own constructor already treats as
        // possible for MapReconstructionSource's *write* path
        // (PersistIfPossible wraps _store.Save in try/catch, logging and
        // continuing in-memory) -- but MapReconstructionSource's *read* path
        // (LoadPersistedOrUnknown -> MapModelStore.TryLoad) has no equivalent
        // try/catch anywhere between MapModelStore's own unguarded
        // `command.ExecuteScalar()` (MapModelStore.cs, TryLoad) and this
        // call to Resolve.
        string dbPath = VolumeLocator.ResolveDatabasePath(volume.Options);
        using (var corruptor = new SqliteConnection($"Data Source={dbPath}"))
        {
            corruptor.Open();
            using SqliteCommand drop = corruptor.CreateCommand();
            drop.CommandText = "DROP TABLE map_models";
            drop.ExecuteNonQuery();
        }

        // A brand-new map id -- deliberately never resolved before by this
        // source, so the in-memory cache cannot short-circuit the read and
        // LoadPersistedOrUnknown must actually query the (now-corrupted)
        // store. Resolve's own class documentation promises: "Fail-soft at
        // every step ... Resolve never throws." If that promise does not
        // hold, this call throws Microsoft.Data.Sqlite.SqliteException
        // ("no such table: map_models") straight out of this test.
        WorldModelSnapshot snapshotForNewMap = SnapshotFor(new MapId("map-2"), T1);
        MapModel result = source.Resolve(snapshotForNewMap, T1);

        // Expected (per Resolve's documented fail-soft contract): an honest,
        // empty reconstruction for map-2, exactly as if nothing had ever been
        // persisted for it -- never an exception, and the fault logged for
        // operators the same way PersistIfPossible already logs a failed
        // save.
        Assert.NotSame(snapshotForNewMap.Map, result);
        Assert.Empty(result.Tiles);
        Assert.NotEmpty(logger.Errors);
    }

    [Fact]
    public void Resolve_SecondCallForTheSameMap_TrulyNeverWritesToTheSqliteFileOnDisk()
    {
        // Complements MapReconstructionSourceTests's grid-directory-deletion
        // proof of the same caching claim: this checks the SQLite side
        // directly, by watching the database file's own last-write time
        // rather than inferring non-use indirectly.
        using TestVolume volume = TestVolume.Create();
        using TempMapsDir maps = TempMapsDir.Create();
        using var source = new MapReconstructionSource(volume.Options, maps.Directory, new NullRuntimeLogger());

        MapModel first = source.Resolve(SnapshotFor(new MapId("map-1"), T0), T0);
        Assert.NotNull(first);

        string dbPath = VolumeLocator.ResolveDatabasePath(volume.Options);
        Assert.True(File.Exists(dbPath));
        DateTime writeTimeAfterFirstResolve = File.GetLastWriteTimeUtc(dbPath);
        long lengthAfterFirstResolve = new FileInfo(dbPath).Length;

        MapModel second = source.Resolve(SnapshotFor(new MapId("map-1"), T1), T1);

        Assert.Same(first, second);
        Assert.Equal(writeTimeAfterFirstResolve, File.GetLastWriteTimeUtc(dbPath));
        Assert.Equal(lengthAfterFirstResolve, new FileInfo(dbPath).Length);
    }
}
