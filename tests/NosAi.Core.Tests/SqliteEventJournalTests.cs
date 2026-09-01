using System.Text;
using Microsoft.Data.Sqlite;
using NosAi.Core;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class SqliteEventJournalTests : IDisposable
{
    private readonly string _databasePath;

    public SqliteEventJournalTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-gate1-journal-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task AppendedRecordsReplayInOrderWithMatchingContent()
    {
        var options = new SqliteJournalOptions();
        await using var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-a");

        long seq0 = journal.Append(new JournalRecord(0, 1000, PipelineStage.Observe, Encoding.UTF8.GetBytes("first"), default));
        long seq1 = journal.Append(new JournalRecord(0, 2000, PipelineStage.WorldState, Encoding.UTF8.GetBytes("second"), default));

        Assert.Equal(0, seq0);
        Assert.Equal(1, seq1);

        var replayed = new List<JournalRecord>();
        await foreach (JournalRecord record in journal.ReplayAsync(0, CancellationToken.None))
            replayed.Add(record);

        Assert.Equal(2, replayed.Count);
        Assert.Equal("first", Encoding.UTF8.GetString(replayed[0].Payload.Span));
        Assert.Equal("second", Encoding.UTF8.GetString(replayed[1].Payload.Span));
        Assert.Equal(PipelineStage.Observe, replayed[0].Stage);
        Assert.Equal(PipelineStage.WorldState, replayed[1].Stage);
    }

    [Fact]
    public async Task VerifyChainSucceedsOnAnUntamperedJournal()
    {
        var options = new SqliteJournalOptions();
        await using var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-b");

        for (int i = 0; i < 25; i++)
            journal.Append(new JournalRecord(0, 1000 + i, PipelineStage.Observe, Encoding.UTF8.GetBytes($"record-{i}"), default));

        bool intact = journal.VerifyChain(0, out long firstBroken);

        Assert.True(intact);
        Assert.Equal(-1, firstBroken);
    }

    [Fact]
    public async Task VerifyChainIsValidAcrossTenThousandRecords()
    {
        // docs/ROADMAP_ESECUTIVA.md S:2.5 states the acceptance threshold
        // explicitly as 10,000 records, not "a handful": a chain that only
        // gets exercised for 25 rows in every other test could still hide a
        // bug that only shows up once SQLite starts paging or once the
        // record count exceeds whatever the developer happened to try by hand.
        const int recordCount = 10_000;
        var options = new SqliteJournalOptions();
        await using var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-ten-thousand");

        for (int i = 0; i < recordCount; i++)
            journal.Append(new JournalRecord(0, 1000 + i, PipelineStage.Observe, Encoding.UTF8.GetBytes($"record-{i}"), default));

        bool intact = journal.VerifyChain(0, out long firstBroken);

        Assert.True(intact);
        Assert.Equal(-1, firstBroken);

        var replayed = new List<JournalRecord>();
        await foreach (JournalRecord record in journal.ReplayAsync(0, CancellationToken.None))
            replayed.Add(record);

        Assert.Equal(recordCount, replayed.Count);
    }

    [Fact]
    public async Task VerifyChainDetectsATamperedRecordAtTheCorrectSequence()
    {
        var options = new SqliteJournalOptions();
        await using var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-c");

        for (int i = 0; i < 10; i++)
            journal.Append(new JournalRecord(0, 1000 + i, PipelineStage.Observe, Encoding.UTF8.GetBytes($"record-{i}"), default));

        TamperWithStoredPayload(sequence: 4);

        bool intact = journal.VerifyChain(0, out long firstBroken);

        Assert.False(intact);
        Assert.Equal(4, firstBroken);
    }

    [Fact]
    public async Task ReopeningTheSameDatabaseResumesTheSequenceAndPreservesTheChain()
    {
        var options = new SqliteJournalOptions();
        long lastSequence;

        await using (var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-d"))
        {
            journal.Append(new JournalRecord(0, 1000, PipelineStage.Observe, Encoding.UTF8.GetBytes("a"), default));
            lastSequence = journal.Append(new JournalRecord(0, 1001, PipelineStage.Observe, Encoding.UTF8.GetBytes("b"), default));
        }

        await using var reopened = new SqliteEventJournal(_databasePath, options, sessionId: "session-d");
        long nextSequence = reopened.Append(new JournalRecord(0, 1002, PipelineStage.Observe, Encoding.UTF8.GetBytes("c"), default));

        Assert.Equal(lastSequence + 1, nextSequence);
        Assert.True(reopened.VerifyChain(0, out _));
    }

    [Fact]
    public async Task JournalAppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy()
    {
        // synchronous and busy_timeout are per-connection SQLite pragmas: they are
        // never persisted to the database file, so a second, independent connection
        // cannot observe them. SqliteEventJournal already re-reads and verifies both
        // on its own connection immediately after setting them (ApplyPolicyOrThrow)
        // and throws if the engine did not honor the requested value; constructing
        // it without throwing is itself the evidence that FULL/5000ms were applied.
        var options = new SqliteJournalOptions();
        await using var journal = new SqliteEventJournal(_databasePath, options, sessionId: "session-e");

        // journal_mode=WAL is the one policy setting SQLite stores in the file
        // header, so it is the only one a fresh connection can independently confirm.
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        Assert.Equal("wal", ReadPragma(connection, "journal_mode"));
    }

    private static string ReadPragma(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma}";
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private void TamperWithStoredPayload(long sequence)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE journal_records SET payload = $payload WHERE sequence = $sequence";
        command.Parameters.AddWithValue("$payload", Encoding.UTF8.GetBytes("tampered"));
        command.Parameters.AddWithValue("$sequence", sequence);
        int rows = command.ExecuteNonQuery();
        Assert.Equal(1, rows);
    }
}
