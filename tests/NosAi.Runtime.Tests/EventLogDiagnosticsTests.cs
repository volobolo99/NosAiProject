using System.Text.Json;
using Microsoft.Data.Sqlite;
using NosAi.Runtime.Gate2;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Reading the durable event log's health from outside the runtime.
/// </summary>
/// <remarks>
/// The store had no reader beyond the runtime that wrote it; this is the seam a
/// dashboard or a CLI reads through. The property that matters most is that a log
/// with a recorded gap reports itself incomplete — a health check that hid losses
/// would defeat the whole point of recording them.
/// </remarks>
public sealed class EventLogDiagnosticsTests : IDisposable
{
    private readonly string _directory;
    private readonly string _database;

    public EventLogDiagnosticsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nosai-eventlog-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = Path.Combine(_directory, "telemetry.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static RuntimeEvent Event(ulong frame) => new(
        Guid.NewGuid(), "S-diag", frame, DateTime.UtcNow, "TestModule", "Tick",
        EventPriority.NormalAudit, JsonSerializer.Serialize(new { frame }));

    private async Task WriteAsync(int count)
    {
        await using var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(_database, batchIntervalMs: 20, maxBatchSize: 50));
        for (var i = 0; i < count; i++)
            logger.EnqueueEvent(Event((ulong)i));
    }

    [Fact]
    public void AStoreThatWasNeverCreatedIsEmptyAndComplete()
    {
        // A fresh runtime has written nothing; that is a normal answer, not a
        // failure the operator should chase.
        var health = EventLogDiagnostics.Inspect(Path.Combine(_directory, "absent.db"));

        Assert.True(health.Readable);
        Assert.False(health.Exists);
        Assert.True(health.IsComplete);
        Assert.Equal(0, health.EventCount);
    }

    [Fact]
    public async Task ItCountsEventsAndBoundsTheSequence()
    {
        await WriteAsync(12);

        var health = EventLogDiagnostics.Inspect(_database);

        Assert.True(health.Readable);
        Assert.True(health.Exists);
        Assert.Equal(12, health.EventCount);
        Assert.True(health.IsComplete);
        Assert.Equal(1, health.FirstSequence);
        Assert.Equal(12, health.LastSequence);
        Assert.NotNull(health.FirstEventUtc);
    }

    [Fact]
    public async Task ARecordedGapMakesTheHealthIncomplete()
    {
        await using (var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(_database, batchIntervalMs: 20, maxBatchSize: 50)))
        {
            logger.EnqueueEvent(Event(1));
            await Task.Delay(60);
            logger.RecordGap(9, "event_bus_full");
        }

        var health = EventLogDiagnostics.Inspect(_database);

        Assert.False(health.IsComplete);
        Assert.Equal(1, health.GapCount);
        Assert.Equal(9, health.LostEventCount);
        var gap = Assert.Single(health.Gaps);
        Assert.Equal("event_bus_full", gap.Reason);
        Assert.Equal(9, gap.LostCount);
    }

    [Fact]
    public async Task TheTailHoldsTheMostRecentEventsInOrder()
    {
        await WriteAsync(50);

        var health = EventLogDiagnostics.Inspect(_database, tailCount: 5);

        Assert.Equal(5, health.Tail.Count);
        // Newest last, and contiguous at the end of the log.
        Assert.Equal(46, health.Tail[0].Sequence);
        Assert.Equal(50, health.Tail[^1].Sequence);
        Assert.Equal(health.Tail.Select(t => t.Sequence).OrderBy(s => s), health.Tail.Select(t => t.Sequence));
    }

    [Fact]
    public async Task ATailLargerThanTheLogReturnsEverythingNotAnError()
    {
        await WriteAsync(3);

        var health = EventLogDiagnostics.Inspect(_database, tailCount: 100);

        Assert.Equal(3, health.Tail.Count);
    }

    [Fact]
    public async Task TheHealthSerialisesToJsonForTransport()
    {
        // The point of flattening the reader's types into records: a dashboard on
        // the other side of HTTP reads plain JSON, gaps and all.
        await WriteAsync(4);
        var health = EventLogDiagnostics.Inspect(_database);

        string json = JsonSerializer.Serialize(health);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(4, root.GetProperty("EventCount").GetInt64());
        Assert.True(root.GetProperty("IsComplete").GetBoolean());
        Assert.Equal(4, root.GetProperty("Tail").GetArrayLength());
    }

    [Fact]
    public async Task TheSummaryNamesCompletenessAndLosses()
    {
        await using (var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(_database, batchIntervalMs: 20, maxBatchSize: 50)))
        {
            logger.EnqueueEvent(Event(1));
            await Task.Delay(60);
            logger.RecordGap(3, "final_batch_failed");
        }

        string summary = EventLogDiagnostics.Describe(EventLogDiagnostics.Inspect(_database));

        Assert.Contains("NO", summary);
        Assert.Contains("3 eventi persi", summary);
        Assert.Contains("final_batch_failed", summary);
    }

    [Fact]
    public void ADamagedStoreIsReportedAsUnreadableNotCrashing()
    {
        // A corrupt file must degrade to a reason, not take down the caller: the
        // operator needs to see "unreadable", not a stack trace.
        File.WriteAllText(_database, "this is not a sqlite database");

        var health = EventLogDiagnostics.Inspect(_database);

        Assert.False(health.Readable);
        Assert.True(health.Exists);
        Assert.StartsWith("event_log_unreadable", health.FailureReason);
    }

    [Fact]
    public void TheDefaultPathMatchesWhereTheRuntimeWrites()
    {
        // A drift here would point the diagnostic at an empty path while the real
        // log filled elsewhere, reporting a healthy-looking void.
        Assert.Equal("data/nosai_telemetry.db", EventLogDiagnostics.DefaultDatabasePath);
    }
}
