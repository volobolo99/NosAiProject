// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Gate 2 — Registro eventi durevole: ordine totale, riproduzione deterministica
//          e perdite dichiarate (M075–M076)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.Gate2;

/// <summary>One position in the durable event log: either an event or a loss.</summary>
/// <remarks>
/// Gaps are records, not metadata. A replay that returned only the events it has
/// would present a log with holes as a complete one, which is the failure mode an
/// audit trail exists to prevent — it invites confident conclusions from evidence
/// that is quietly missing.
/// </remarks>
public abstract record EventLogRecord(long Sequence);

/// <summary>An event as it was persisted, with the order it was persisted in.</summary>
public sealed record EventLogEntry(long Sequence, RuntimeEvent Event) : EventLogRecord(Sequence);

/// <summary>
/// A recorded loss: events that existed and did not reach the store.
/// </summary>
/// <param name="Sequence">The last event that <b>did</b> land before the loss.</param>
/// <param name="LostCount">
/// How many were lost. Known exactly when the bus counted them; the count is the
/// claim, not an estimate.
/// </param>
public sealed record EventLogGap(long Sequence, long LostCount, string Reason, DateTime DetectedUtc)
    : EventLogRecord(Sequence);

/// <summary>
/// The result of reading the log back.
/// </summary>
/// <remarks>
/// <see cref="IsComplete"/> is the question a caller actually has, and it is
/// answered rather than implied: a log with a single recorded gap is not a record
/// of what happened, and anything reasoning over it must know that first.
/// </remarks>
public sealed record EventLogReplay(
    IReadOnlyList<EventLogRecord> Records,
    long EventCount,
    long GapCount,
    long LostEventCount)
{
    /// <summary>Whether every event that was published reached the store.</summary>
    public bool IsComplete => GapCount == 0;

    /// <summary>The events only, in order, for a caller that has checked completeness.</summary>
    public IEnumerable<RuntimeEvent> Events
    {
        get
        {
            foreach (var record in Records)
            {
                if (record is EventLogEntry entry)
                    yield return entry.Event;
            }
        }
    }
}

/// <summary>
/// Reads the Gate 2 event store back in the order it was written.
/// </summary>
/// <remarks>
/// <para>
/// The store had no total order before this. Replaying by <c>timestamp_utc</c>
/// leaves ties unresolved, and by <c>frame_index</c> leaves many events sharing a
/// position — so two replays of the same database could differ, and a durable log
/// that cannot be read back the same way twice is not durable in the way that
/// matters. Every row now carries a monotonic <c>seq</c> assigned at insert.
/// </para>
/// <para>
/// Read-only by construction: this opens the same database with the same pragmas
/// and never writes. Replay is an observation, and observing an audit trail must
/// not be able to change it.
/// </para>
/// </remarks>
public static class EventLogReader
{
    /// <summary>
    /// Reads every record, oldest first.
    /// </summary>
    /// <param name="databasePath">The Gate 2 telemetry store.</param>
    /// <param name="sessionId">
    /// Restricts events to one session. Gaps are always included: a loss that
    /// happened while another session was running still means this replay is
    /// incomplete, and filtering it out would hide exactly that.
    /// </param>
    /// <param name="busyTimeoutMs">Matches the writer, so a live flush is waited out.</param>
    public static EventLogReplay Read(string databasePath, string? sessionId = null, int busyTimeoutMs = 5000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var connection = Gate2Sqlite.OpenAligned(databasePath, busyTimeoutMs);

        var events = ReadEvents(connection, sessionId);
        var gaps = ReadGaps(connection);

        return Merge(events, gaps);
    }

    private static List<EventLogEntry> ReadEvents(SqliteConnection connection, string? sessionId)
    {
        var entries = new List<EventLogEntry>();
        if (!Gate2EventSchema.TableExists(connection, Gate2EventSchema.EventsTable))
            return entries;

        using var command = connection.CreateCommand();
        command.CommandText = sessionId is null
            ? $"SELECT seq, event_id, session_id, frame_index, timestamp_utc, source_module, event_type, priority, payload_json FROM {Gate2EventSchema.EventsTable} ORDER BY seq"
            : $"SELECT seq, event_id, session_id, frame_index, timestamp_utc, source_module, event_type, priority, payload_json FROM {Gate2EventSchema.EventsTable} WHERE session_id = $session ORDER BY seq";

        if (sessionId is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$session";
            parameter.Value = sessionId;
            command.Parameters.Add(parameter);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new EventLogEntry(
                reader.GetInt64(0),
                new RuntimeEvent(
                    Guid.ParseExact(reader.GetString(1), "N"),
                    reader.GetString(2),
                    unchecked((ulong)reader.GetInt64(3)),
                    DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetString(5),
                    reader.GetString(6),
                    (EventPriority)reader.GetInt32(7),
                    reader.GetString(8))));
        }

        return entries;
    }

    private static List<EventLogGap> ReadGaps(SqliteConnection connection)
    {
        var gaps = new List<EventLogGap>();
        if (!Gate2EventSchema.TableExists(connection, Gate2EventSchema.GapsTable))
            return gaps;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT after_seq, lost_count, reason, detected_utc FROM {Gate2EventSchema.GapsTable} ORDER BY after_seq, gap_id";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            gaps.Add(new EventLogGap(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return gaps;
    }

    /// <summary>
    /// Interleaves gaps with the events they follow.
    /// </summary>
    /// <remarks>
    /// A gap sits immediately after the last event that landed before it, so a
    /// reader walking the list sees the loss where it happened rather than in a
    /// separate summary nobody reads.
    /// </remarks>
    private static EventLogReplay Merge(List<EventLogEntry> events, List<EventLogGap> gaps)
    {
        var records = new List<EventLogRecord>(events.Count + gaps.Count);
        int gapIndex = 0;

        // Gaps recorded before any event landed come first.
        while (gapIndex < gaps.Count && (events.Count == 0 || gaps[gapIndex].Sequence < events[0].Sequence))
            records.Add(gaps[gapIndex++]);

        foreach (var entry in events)
        {
            records.Add(entry);
            while (gapIndex < gaps.Count && gaps[gapIndex].Sequence <= entry.Sequence)
                records.Add(gaps[gapIndex++]);
        }

        while (gapIndex < gaps.Count)
            records.Add(gaps[gapIndex++]);

        long lost = 0;
        foreach (var gap in gaps)
            lost += gap.LostCount;

        return new EventLogReplay(records, events.Count, gaps.Count, lost);
    }
}

/// <summary>
/// The durable event schema, and the one place that migrates it.
/// </summary>
/// <remarks>
/// Kept apart from the writer so the reader can check for a table without
/// depending on a logger being alive, and so the migration is stated once.
/// </remarks>
internal static class Gate2EventSchema
{
    internal const string EventsTable = "runtime_events";
    internal const string GapsTable = "runtime_event_gaps";

    /// <summary>
    /// Creates or upgrades the event tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original table had <c>event_id</c> as its primary key and no ordering
    /// column. Adding one means rebuilding the table, which is done once here and
    /// preserves every existing row.
    /// </para>
    /// <para>
    /// Rows written before the migration are copied in <c>timestamp_utc, event_id</c>
    /// order. That is the best order recoverable from data that never carried one,
    /// and it is stated rather than presented as the original sequence.
    /// </para>
    /// </remarks>
    internal static void EnsureSchema(SqliteConnection connection)
    {
        if (TableExists(connection, EventsTable) && !ColumnExists(connection, EventsTable, "seq"))
            MigrateEventsTable(connection);

        Gate2Sqlite.Execute(connection, $"""
            CREATE TABLE IF NOT EXISTS {EventsTable} (
                seq           INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id      TEXT NOT NULL UNIQUE,
                session_id    TEXT NOT NULL,
                frame_index   INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                source_module TEXT NOT NULL,
                event_type    TEXT NOT NULL,
                priority      INTEGER NOT NULL,
                payload_json  TEXT NOT NULL
            )
            """);
        Gate2Sqlite.Execute(connection, $"CREATE INDEX IF NOT EXISTS idx_runtime_events_session ON {EventsTable}(session_id)");
        Gate2Sqlite.Execute(connection, $"CREATE INDEX IF NOT EXISTS idx_runtime_events_timestamp ON {EventsTable}(timestamp_utc)");

        // Losses are stored beside the events, not in memory: a drop counter that
        // dies with the process leaves the next replay looking complete.
        Gate2Sqlite.Execute(connection, $"""
            CREATE TABLE IF NOT EXISTS {GapsTable} (
                gap_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                after_seq    INTEGER NOT NULL,
                lost_count   INTEGER NOT NULL,
                reason       TEXT NOT NULL,
                detected_utc TEXT NOT NULL
            )
            """);
    }

    private static void MigrateEventsTable(SqliteConnection connection)
    {
        const string legacy = EventsTable + "_pre_seq";

        Gate2Sqlite.Execute(connection, $"DROP TABLE IF EXISTS {legacy}");
        Gate2Sqlite.Execute(connection, $"ALTER TABLE {EventsTable} RENAME TO {legacy}");
        Gate2Sqlite.Execute(connection, $"""
            CREATE TABLE {EventsTable} (
                seq           INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id      TEXT NOT NULL UNIQUE,
                session_id    TEXT NOT NULL,
                frame_index   INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                source_module TEXT NOT NULL,
                event_type    TEXT NOT NULL,
                priority      INTEGER NOT NULL,
                payload_json  TEXT NOT NULL
            )
            """);
        Gate2Sqlite.Execute(connection, $"""
            INSERT INTO {EventsTable}
                (event_id, session_id, frame_index, timestamp_utc, source_module, event_type, priority, payload_json)
            SELECT event_id, session_id, frame_index, timestamp_utc, source_module, event_type, priority, payload_json
            FROM {legacy}
            ORDER BY timestamp_utc, event_id
            """);
        Gate2Sqlite.Execute(connection, $"DROP TABLE {legacy}");
    }

    internal static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
