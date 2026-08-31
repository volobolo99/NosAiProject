using System.Text.Json;
using Microsoft.Data.Sqlite;
using NosAi.Runtime.Gate2;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The durable event log: it must survive a restart, read back the same way
/// twice, and admit what it lost (M075–M076).
/// </summary>
/// <remarks>
/// The last of those is the one that decides whether the other two are worth
/// anything. A log that quietly omits what it dropped presents holes as a
/// complete record, and anything reasoning over it draws confident conclusions
/// from evidence that is not there.
/// </remarks>
public sealed class EventLogReplayTests : IDisposable
{
    private readonly string _directory;
    private readonly string _database;

    public EventLogReplayTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nosai-eventlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = Path.Combine(_directory, "telemetry.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static RuntimeEvent Event(string session, ulong frame, string type = "Test") => new(
        Guid.NewGuid(), session, frame, DateTime.UtcNow, "TestModule", type,
        EventPriority.NormalAudit, JsonSerializer.Serialize(new { frame }));

    private SqliteStoragePolicy Policy() => new(_database, batchIntervalMs: 20, maxBatchSize: 50);

    private async Task WriteAsync(params RuntimeEvent[] events)
    {
        await using var logger = new NosAiSqliteBatchLogger(Policy());
        foreach (var runtimeEvent in events)
            logger.EnqueueEvent(runtimeEvent);
        // Disposal drains what is queued, so the events are committed by the time
        // this returns rather than at the mercy of the flush interval.
    }

    // -------------------------------------------------------------- durability

    [Fact]
    public async Task EventsSurviveTheProcessThatWroteThem()
    {
        // "Durable" has to mean readable after the writer is gone. Before this the
        // store was write-only: nothing ever read it back.
        var first = Event("S1", 1);
        var second = Event("S1", 2);
        await WriteAsync(first, second);

        var replay = EventLogReader.Read(_database);

        Assert.Equal(2, replay.EventCount);
        Assert.True(replay.IsComplete);
        Assert.Equal(new[] { first.EventId, second.EventId }, replay.Events.Select(e => e.EventId));
    }

    [Fact]
    public async Task EveryFieldComesBackAsItWentIn()
    {
        var original = new RuntimeEvent(
            Guid.NewGuid(), "S-fields", 4242, new DateTime(2026, 8, 31, 10, 30, 0, DateTimeKind.Utc),
            "WorldModelEngine", "WorldStateUpdated", EventPriority.CriticalSecurity, """{"hp":41}""");

        await WriteAsync(original);
        var restored = Assert.Single(EventLogReader.Read(_database).Events);

        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.SessionId, restored.SessionId);
        Assert.Equal(original.FrameIndex, restored.FrameIndex);
        Assert.Equal(original.TimestampUtc, restored.TimestampUtc);
        Assert.Equal(original.SourceModule, restored.SourceModule);
        Assert.Equal(original.EventType, restored.EventType);
        Assert.Equal(original.Priority, restored.Priority);
        Assert.Equal(original.PayloadJson, restored.PayloadJson);
    }

    // ------------------------------------------------------------------- order

    [Fact]
    public async Task TheOrderIsTheOrderTheyWereWrittenIn()
    {
        // Timestamps tie and frame indexes repeat, so neither can order a replay.
        // Every event written inside one tick shares a timestamp here on purpose.
        var sameFrame = new List<RuntimeEvent>();
        for (var i = 0; i < 20; i++)
            sameFrame.Add(new RuntimeEvent(Guid.NewGuid(), "S-order", 7, new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
                "M", $"E{i}", EventPriority.NormalAudit, "{}"));

        await WriteAsync(sameFrame.ToArray());

        var replay = EventLogReader.Read(_database);
        Assert.Equal(sameFrame.Select(e => e.EventId), replay.Events.Select(e => e.EventId));
    }

    [Fact]
    public async Task TwoReplaysOfTheSameStoreAreIdentical()
    {
        // A log that cannot be read back the same way twice is not durable in the
        // way an audit trail needs.
        await WriteAsync(Enumerable.Range(0, 30).Select(i => Event("S-stable", (ulong)i)).ToArray());

        var first = EventLogReader.Read(_database);
        var second = EventLogReader.Read(_database);

        Assert.Equal(first.Records.Count, second.Records.Count);
        Assert.Equal(
            first.Records.Select(r => r.Sequence),
            second.Records.Select(r => r.Sequence));
    }

    [Fact]
    public async Task SequencesRiseAndNeverRepeat()
    {
        await WriteAsync(Enumerable.Range(0, 10).Select(i => Event("S-seq", (ulong)i)).ToArray());
        await WriteAsync(Enumerable.Range(10, 10).Select(i => Event("S-seq", (ulong)i)).ToArray());

        var sequences = EventLogReader.Read(_database).Records.Select(r => r.Sequence).ToList();

        Assert.Equal(20, sequences.Count);
        Assert.Equal(sequences.OrderBy(s => s), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
    }

    [Fact]
    public async Task ASessionCanBeReplayedOnItsOwn()
    {
        await WriteAsync(Event("A", 1), Event("B", 1), Event("A", 2));

        var replay = EventLogReader.Read(_database, sessionId: "A");

        Assert.Equal(2, replay.EventCount);
        Assert.All(replay.Events, e => Assert.Equal("A", e.SessionId));
    }

    [Fact]
    public async Task TheSameEventWrittenTwiceAppearsOnce()
    {
        // Re-persisting a batch after a partial failure must not duplicate history.
        var duplicate = Event("S-dup", 1);
        await WriteAsync(duplicate);
        await WriteAsync(duplicate);

        Assert.Equal(1, EventLogReader.Read(_database).EventCount);
    }

    // ---------------------------------------------------------------- the gaps

    [Fact]
    public async Task ALossIsRecordedAndTheReplayStopsClaimingToBeComplete()
    {
        await using (var logger = new NosAiSqliteBatchLogger(Policy()))
        {
            logger.EnqueueEvent(Event("S-gap", 1));
            await Task.Delay(80);
            logger.RecordGap(7, "event_bus_full");
            logger.EnqueueEvent(Event("S-gap", 2));
        }

        var replay = EventLogReader.Read(_database);

        Assert.False(replay.IsComplete);
        Assert.Equal(1, replay.GapCount);
        Assert.Equal(7, replay.LostEventCount);
        Assert.Equal(2, replay.EventCount);
    }

    [Fact]
    public async Task AGapSitsWhereTheLossHappened()
    {
        // In the record, not in a summary at the end: a reader walking the log has
        // to meet the hole at the point it was made.
        await using (var logger = new NosAiSqliteBatchLogger(Policy()))
        {
            logger.EnqueueEvent(Event("S-pos", 1));
            await Task.Delay(80);
            logger.RecordGap(3, "event_bus_full");
            logger.EnqueueEvent(Event("S-pos", 2));
            await Task.Delay(80);
        }

        var records = EventLogReader.Read(_database).Records;

        Assert.Collection(records,
            first => Assert.IsType<EventLogEntry>(first),
            gap => Assert.Equal(3, Assert.IsType<EventLogGap>(gap).LostCount),
            last => Assert.IsType<EventLogEntry>(last));
    }

    [Fact]
    public async Task AGapSurvivesTheProcessThatRecordedIt()
    {
        // The point of storing it: an in-memory drop counter dies with the runtime
        // and the next replay would look complete.
        await using (var logger = new NosAiSqliteBatchLogger(Policy()))
            logger.RecordGap(12, "final_batch_failed");

        var replay = EventLogReader.Read(_database);

        Assert.False(replay.IsComplete);
        Assert.Equal(12, replay.LostEventCount);
        Assert.Equal("final_batch_failed", Assert.IsType<EventLogGap>(Assert.Single(replay.Records)).Reason);
    }

    [Fact]
    public async Task AGapIsVisibleEvenWhenReplayingOneSession()
    {
        // A loss that happened while another session ran still means this replay is
        // missing something. Filtering it out would hide exactly that.
        await using (var logger = new NosAiSqliteBatchLogger(Policy()))
        {
            logger.EnqueueEvent(Event("A", 1));
            await Task.Delay(80);
            logger.RecordGap(2, "event_bus_full");
        }

        Assert.False(EventLogReader.Read(_database, sessionId: "A").IsComplete);
    }

    [Fact]
    public async Task NothingIsRecordedForZeroLosses()
    {
        await using (var logger = new NosAiSqliteBatchLogger(Policy()))
        {
            logger.EnqueueEvent(Event("S-none", 1));
            logger.RecordGap(0, "nothing_lost");
        }

        Assert.True(EventLogReader.Read(_database).IsComplete);
    }

    [Fact]
    public async Task AFullBusLeavesAMarkInTheStore()
    {
        // End to end: the bus drops, the logger notices, the store remembers.
        await using var bus = new BoundedEventBus(capacity: 1);
        await using (var logger = new NosAiSqliteBatchLogger(Policy(), upstreamDropCount: () => bus.DroppedEventsCount))
        {
            bus.Subscribe(logger.EnqueueEvent);
            for (var i = 0; i < 400; i++)
            {
                bus.TryPublish(new RuntimeEvent(Guid.NewGuid(), "S-flood", (ulong)i, DateTime.UtcNow,
                    "M", "Flood", EventPriority.LowTelemetry, "{}"));
            }

            Assert.True(bus.DroppedEventsCount > 0, "the bus should have dropped low-priority events at capacity 1");
            await Task.Delay(120);
        }

        var replay = EventLogReader.Read(_database);

        Assert.False(replay.IsComplete);
        Assert.True(replay.LostEventCount > 0);
        Assert.Contains(replay.Records.OfType<EventLogGap>(), gap => gap.Reason == "event_bus_full");
    }

    // ------------------------------------------------------------- empty store

    [Fact]
    public void AnUntouchedStoreReadsAsEmptyAndComplete()
    {
        // Nothing lost and nothing recorded: an empty log is complete, and saying
        // otherwise would make every fresh runtime look like it had already failed.
        var replay = EventLogReader.Read(Path.Combine(_directory, "fresh.db"));

        Assert.Empty(replay.Records);
        Assert.True(replay.IsComplete);
        Assert.Equal(0, replay.EventCount);
    }

    // -------------------------------------------------------------- migration

    [Fact]
    public async Task AStoreWrittenBeforeTheOrderingColumnIsMigratedWithItsRows()
    {
        // The table shipped without an ordering column. Upgrading must keep the
        // history rather than start a new one.
        using (var connection = new SqliteConnection($"Data Source={_database}"))
        {
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE runtime_events (
                    event_id      TEXT PRIMARY KEY,
                    session_id    TEXT NOT NULL,
                    frame_index   INTEGER NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    source_module TEXT NOT NULL,
                    event_type    TEXT NOT NULL,
                    priority      INTEGER NOT NULL,
                    payload_json  TEXT NOT NULL
                )
                """;
            create.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO runtime_events VALUES
                    ('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','S-old',1,'2026-08-30T10:00:00.0000000Z','M','Old',1,'{}'),
                    ('bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb','S-old',2,'2026-08-30T10:00:01.0000000Z','M','Old',1,'{}')
                """;
            insert.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Opening the logger migrates the schema.
        await WriteAsync(Event("S-new", 3));

        var replay = EventLogReader.Read(_database);

        Assert.Equal(3, replay.EventCount);
        Assert.Equal(new[] { "Old", "Old", "Test" }, replay.Events.Select(e => e.EventType));
        // And the migrated rows got an order, oldest first.
        Assert.Equal(replay.Records.Select(r => r.Sequence).OrderBy(s => s), replay.Records.Select(r => r.Sequence));
    }
}
