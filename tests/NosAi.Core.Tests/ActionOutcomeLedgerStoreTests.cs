using Microsoft.Data.Sqlite;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// AP-09/A4: <see cref="ActionOutcomeLedgerStore"/>'s own persistence
/// contract, tested against a real temp-file SQLite database (never
/// <c>:memory:</c> -- the WAL pragma verification below is part of what
/// this file tests), the same pattern <see cref="MapModelStoreTests"/>
/// already establishes for <see cref="MapModelStore"/>.
/// </summary>
public sealed class ActionOutcomeLedgerStoreTests : IDisposable
{
    private readonly string _databasePath;

    public ActionOutcomeLedgerStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-action-outcome-ledger-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static readonly DateTime T0 = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static ActionOutcomeLedgerEntry FullyPopulatedEntry(string context) => new(
        new Guid("11111111-1111-1111-1111-111111111111"),
        new ActionId("action-1"),
        MemoryType.Combat,
        WorldFact<ActionOutcome>.Live(ActionOutcome.Succeeded, 0.9, T0, reason: "resource_gain_confirmed"),
        context,
        T0);

    // -- round-trips ----------------------------------------------------------

    [Fact]
    public void AppendThenLoadByContext_RoundTripsAFullyPopulatedEntry_UnchangedFieldForField()
    {
        const string context = "skill:201";
        ActionOutcomeLedgerEntry original = FullyPopulatedEntry(context);

        using (var store = new ActionOutcomeLedgerStore(_databasePath, Options()))
            store.Append(original);

        using (var reopened = new ActionOutcomeLedgerStore(_databasePath, Options()))
        {
            EquatableArray<ActionOutcomeLedgerEntry> loaded = reopened.LoadByContext(context);

            ActionOutcomeLedgerEntry entry = Assert.Single(loaded);
            Assert.Equal(original.EntryId, entry.EntryId);
            Assert.Equal(original.ActionId, entry.ActionId);
            Assert.Equal(original.Category, entry.Category);
            Assert.Equal(original.Context, entry.Context);
            Assert.Equal(original.RecordedAtUtc, entry.RecordedAtUtc);

            // Outcome: value, source, confidence, observed-at, reason -- the
            // whole flat WorldFact, never a re-derived subset.
            Assert.True(entry.Outcome.HasValue);
            Assert.Equal(original.Outcome.Value, entry.Outcome.Value);
            Assert.Equal(original.Outcome.Source, entry.Outcome.Source);
            Assert.Equal(original.Outcome.Confidence, entry.Outcome.Confidence);
            Assert.Equal(original.Outcome.ObservedAtUtc, entry.Outcome.ObservedAtUtc);
            Assert.Equal(original.Outcome.Reason, entry.Outcome.Reason);
        }
    }

    [Fact]
    public void AppendThenLoadByContext_RoundTripsAnUnknownOutcome_WithoutFabricatingAValue()
    {
        // The exact case the AP-09 contract fix exists for: an outcome that
        // was never actually observed (guard refusal, no verification reading)
        // must come back with HasObservedValue == false and its reason
        // preserved -- never with a fabricated ActionOutcome member.
        const string context = "consumable-slot:2";
        const string reason = "key_press_not_accepted";
        var entry = new ActionOutcomeLedgerEntry(
            new Guid("22222222-2222-2222-2222-222222222222"),
            new ActionId("action-2"),
            MemoryType.Combat,
            WorldFact<ActionOutcome>.Unknown(reason, T0),
            context,
            T0);

        using (var store = new ActionOutcomeLedgerStore(_databasePath, Options()))
            store.Append(entry);

        using (var reopened = new ActionOutcomeLedgerStore(_databasePath, Options()))
        {
            ActionOutcomeLedgerEntry loaded = Assert.Single(reopened.LoadByContext(context));

            Assert.False(loaded.Outcome.HasValue);
            Assert.False(loaded.Outcome.HasObservedValue);
            Assert.Equal(reason, loaded.Outcome.Reason);
            Assert.Equal(T0, loaded.Outcome.ObservedAtUtc);
        }
    }

    // -- ordering / filtering -------------------------------------------------

    [Fact]
    public void LoadByContext_ReturnsEntriesOldestFirst_AndExcludesOtherContexts_WithOrdinalCaseSensitiveMatch()
    {
        var earlier = new ActionOutcomeLedgerEntry(
            new Guid("33333333-3333-3333-3333-333333333333"),
            new ActionId("action-a"),
            MemoryType.Spatial,
            WorldFact<ActionOutcome>.Live(ActionOutcome.Failed, 1d, T0),
            "scout:map-1",
            T0);
        var later = new ActionOutcomeLedgerEntry(
            new Guid("44444444-4444-4444-4444-444444444444"),
            new ActionId("action-b"),
            MemoryType.Spatial,
            WorldFact<ActionOutcome>.Live(ActionOutcome.Succeeded, 1d, T0.AddSeconds(5)),
            "scout:map-1",
            T0.AddSeconds(5));
        var otherContext = new ActionOutcomeLedgerEntry(
            new Guid("55555555-5555-5555-5555-555555555555"),
            new ActionId("action-c"),
            MemoryType.Combat,
            WorldFact<ActionOutcome>.Live(ActionOutcome.Succeeded, 1d, T0),
            "scout:MAP-1", // ordinal match: different case is a different context
            T0);

        using var store = new ActionOutcomeLedgerStore(_databasePath, Options());
        store.Append(otherContext);
        store.Append(later);
        store.Append(earlier);

        EquatableArray<ActionOutcomeLedgerEntry> loaded = store.LoadByContext("scout:map-1");

        Assert.Equal(2, loaded.Count);
        Assert.Equal(earlier.EntryId, loaded[0].EntryId); // oldest first, despite later append order
        Assert.Equal(later.EntryId, loaded[1].EntryId);
    }

    [Fact]
    public void LoadByContext_ForAContextWithNoEntries_ReturnsEmpty_NeverThrows()
    {
        using var store = new ActionOutcomeLedgerStore(_databasePath, Options());

        EquatableArray<ActionOutcomeLedgerEntry> loaded = store.LoadByContext("never-recorded-context");

        Assert.True(loaded.Count == 0);
    }

    // -- durability policy ----------------------------------------------------

    [Fact]
    public void ConstructingTheStore_AppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy()
    {
        // Same reasoning as MapModelStoreTests' own policy test:
        // synchronous/busy_timeout are per-connection pragmas a second
        // connection cannot observe, so constructing without throwing
        // (ApplyPolicyOrThrow already re-reads and verifies both) is itself
        // the evidence for those two. journal_mode=WAL is persisted to the
        // file header, so a fresh, independent connection can confirm it
        // directly.
        using var store = new ActionOutcomeLedgerStore(_databasePath, Options());

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        string journalMode = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;

        Assert.Equal("wal", journalMode);
    }
}
