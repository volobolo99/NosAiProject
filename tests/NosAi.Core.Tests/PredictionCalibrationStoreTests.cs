using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// AP-09/A2+A4: <see cref="PredictionCalibrationStore"/>'s own persistence
/// contract, tested against a real temp-file SQLite database (never
/// <c>:memory:</c> -- the WAL pragma verification below is part of what this
/// file tests), the same pattern <see cref="ActionOutcomeLedgerStoreTests"/>
/// establishes for <see cref="ActionOutcomeLedgerStore"/>.
/// </summary>
public sealed class PredictionCalibrationStoreTests : IDisposable
{
    private readonly string _databasePath;

    public PredictionCalibrationStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-prediction-calibration-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static CalibrationSnapshotEntry Entry(string context) =>
        new(context, Alpha: 3.0, Beta: 2.0, TotalTrials: 4, Confirmed: 3, Refuted: 1, Ignored: 2, ErrorSum: 12.5);

    private static CalibrationSnapshot Snapshot(params CalibrationSnapshotEntry[] entries) =>
        new(EquatableArray<CalibrationSnapshotEntry>.From(entries));

    // -- round-trip -----------------------------------------------------------

    [Fact]
    public void SaveThenLoad_RoundTripsEveryFieldUnchanged()
    {
        CalibrationSnapshot original = Snapshot(Entry("skill:201"), Entry("walk:map-1"));

        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
            store.Save(original);

        using (var reopened = new PredictionCalibrationStore(_databasePath, Options()))
            Assert.Equal(original, reopened.Load());
    }

    [Fact]
    public void SaveThenLoad_WithAwkwardContextKeys_ReturnsThemIdentical()
    {
        string[] awkward =
        {
            "spazio con  spazi",
            "apostrofo:l'étoile",
            "unicode:火-狐狸",
            "virgolette:\"doppia\"",
        };

        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
            store.Save(Snapshot(awkward.Select(Entry).ToArray()));

        using (var reopened = new PredictionCalibrationStore(_databasePath, Options()))
        {
            CalibrationSnapshot loaded = reopened.Load();
            Assert.Equal(awkward.Length, loaded.Entries.Count);
            foreach (string key in awkward)
                Assert.Contains(loaded.Entries, e => e.ContextKey == key);
        }
    }

    [Fact]
    public void Load_OnAnEmptyStore_ReturnsEmptyNeverThrows()
    {
        using var store = new PredictionCalibrationStore(_databasePath, Options());

        Assert.Empty(store.Load().Entries);
    }

    // -- replace semantics ----------------------------------------------------

    [Fact]
    public void Save_ReplacesTheWholeContents_LeavingNoStaleContexts()
    {
        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
            store.Save(Snapshot(Entry("keep"), Entry("stale")));

        using (var store = new PredictionCalibrationStore(_databasePath, Options()))
        {
            store.Save(Snapshot(Entry("keep")));

            CalibrationSnapshotEntry only = Assert.Single(store.Load().Entries);
            Assert.Equal("keep", only.ContextKey);
        }
    }

    // -- durability policy ----------------------------------------------------

    [Fact]
    public void ConstructingTheStore_AppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy()
    {
        // Same reasoning as ActionOutcomeLedgerStoreTests' own policy test:
        // synchronous/busy_timeout are per-connection pragmas a second
        // connection cannot observe, so constructing without throwing
        // (ApplyPolicyOrThrow already re-reads and verifies both) is itself the
        // evidence for those two. journal_mode=WAL is persisted to the file
        // header, so a fresh, independent connection can confirm it directly.
        using var store = new PredictionCalibrationStore(_databasePath, Options());

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        string journalMode = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;

        Assert.Equal("wal", journalMode);
    }

    // -- the trap a read-only report must avoid -------------------------------

    [Fact]
    public void OpeningTheStoreCreatesTheFile_SoAReaderMustCheckExistenceFirst()
    {
        // The exact danger LearningReportCommand's File.Exists guard exists for:
        // constructing the store is what writes the file. A report that opened
        // the store unconditionally would turn "never written" into "written and
        // empty" on its first run.
        Assert.False(File.Exists(_databasePath));

        using (new PredictionCalibrationStore(_databasePath, Options()))
        {
        }

        Assert.True(File.Exists(_databasePath));
    }
}
