using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NosAi.Runtime.Gate2;

/// <summary>One event, flattened for a diagnostic report.</summary>
public sealed record EventLogTailEntry(
    long Sequence, string SessionId, ulong FrameIndex, DateTime TimestampUtc,
    string SourceModule, string EventType, string Priority);

/// <summary>A recorded loss, flattened for a report.</summary>
public sealed record EventLogGapReport(long AfterSequence, long LostCount, string Reason, DateTime DetectedUtc);

/// <summary>
/// The health of the durable event log, for something outside the runtime to read.
/// </summary>
/// <remarks>
/// <see cref="IsComplete"/> is the question a consumer actually has, answered
/// rather than implied. A dashboard that showed the events without it would
/// present a log with holes as a full record — the exact failure the gap tracking
/// exists to prevent.
/// </remarks>
public sealed record EventLogHealth(
    string DatabasePath,
    bool Exists,
    long EventCount,
    long GapCount,
    long LostEventCount,
    long? FirstSequence,
    long? LastSequence,
    DateTime? FirstEventUtc,
    DateTime? LastEventUtc,
    IReadOnlyList<EventLogGapReport> Gaps,
    IReadOnlyList<EventLogTailEntry> Tail,
    string? FailureReason)
{
    /// <summary>Whether every published event reached the store.</summary>
    public bool IsComplete => GapCount == 0;

    /// <summary>Whether the report could be produced at all.</summary>
    public bool Readable => FailureReason is null;

    /// <summary>A record that could not be produced, with the reason.</summary>
    public static EventLogHealth Failed(string path, bool exists, string reason) =>
        new(path, exists, 0, 0, 0, null, null, null, null,
            Array.Empty<EventLogGapReport>(), Array.Empty<EventLogTailEntry>(), reason);
}

/// <summary>
/// Reads the durable event log and reports its health, for a UI or a CLI.
/// </summary>
/// <remarks>
/// <para>
/// The event store had no reader outside the runtime; this is it. Read-only, and
/// tolerant of a store that is not there yet — a fresh runtime has written no
/// events, and "no file" is a normal answer, not a failure.
/// </para>
/// <para>
/// Wraps <see cref="EventLogReader"/> rather than reimplementing the read, so the
/// diagnostic and the replay agree on ordering and on what a gap is. The
/// difference is only in shape: this flattens the result into records a JSON
/// serialiser and a table renderer can both consume without knowing the reader's
/// types.
/// </para>
/// </remarks>
public static class EventLogDiagnostics
{
    /// <summary>Default store, matching <c>Gate2RuntimeEngine</c>.</summary>
    public const string DefaultDatabasePath = "data/nosai_telemetry.db";

    /// <summary>Events included in the tail, newest last.</summary>
    public const int DefaultTailCount = 20;

    /// <summary>Inspects the store at <paramref name="databasePath"/>.</summary>
    /// <param name="tailCount">How many of the most recent events to include.</param>
    public static EventLogHealth Inspect(string? databasePath = null, int tailCount = DefaultTailCount)
    {
        string path = string.IsNullOrWhiteSpace(databasePath) ? DefaultDatabasePath : databasePath;
        bool exists = File.Exists(path);

        // A store that was never created is not a fault: the runtime simply has not
        // written an event yet. Report an empty, complete log rather than an error.
        if (!exists)
        {
            return new EventLogHealth(path, false, 0, 0, 0, null, null, null, null,
                Array.Empty<EventLogGapReport>(), Array.Empty<EventLogTailEntry>(), null);
        }

        EventLogReplay replay;
        try
        {
            replay = EventLogReader.Read(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return EventLogHealth.Failed(path, true, $"event_log_unreadable:{ex.GetType().Name}");
        }

        var entries = replay.Records.OfType<EventLogEntry>().ToList();
        var gaps = replay.Records.OfType<EventLogGap>()
            .Select(g => new EventLogGapReport(g.Sequence, g.LostCount, g.Reason, g.DetectedUtc))
            .ToList();

        int take = tailCount < 0 ? 0 : tailCount;
        var tail = entries
            .Skip(Math.Max(0, entries.Count - take))
            .Select(e => new EventLogTailEntry(
                e.Sequence, e.Event.SessionId, e.Event.FrameIndex, e.Event.TimestampUtc,
                e.Event.SourceModule, e.Event.EventType, e.Event.Priority.ToString()))
            .ToList();

        return new EventLogHealth(
            path,
            true,
            replay.EventCount,
            replay.GapCount,
            replay.LostEventCount,
            entries.Count > 0 ? entries[0].Sequence : null,
            entries.Count > 0 ? entries[^1].Sequence : null,
            entries.Count > 0 ? entries[0].Event.TimestampUtc : null,
            entries.Count > 0 ? entries[^1].Event.TimestampUtc : null,
            gaps,
            tail,
            null);
    }

    /// <summary>A short human summary, for the CLI report.</summary>
    public static string Describe(EventLogHealth health)
    {
        if (!health.Readable)
            return $"Registro eventi: NON LEGGIBILE ({health.FailureReason}) — {health.DatabasePath}";

        if (!health.Exists)
            return $"Registro eventi: nessuno store ancora ({health.DatabasePath}). Vuoto e completo.";

        var lines = new List<string>
        {
            $"Registro eventi: {health.DatabasePath}",
            $"  eventi     : {health.EventCount} (seq {health.FirstSequence}..{health.LastSequence})",
            $"  completo   : {(health.IsComplete ? "SI" : $"NO — {health.GapCount} interruzioni, {health.LostEventCount} eventi persi")}",
        };

        foreach (var gap in health.Gaps)
            lines.Add($"    gap dopo seq {gap.AfterSequence}: {gap.LostCount} persi ({gap.Reason}) alle {gap.DetectedUtc:HH:mm:ss}");

        if (health.Tail.Count > 0)
        {
            lines.Add($"  ultimi {health.Tail.Count}:");
            foreach (var e in health.Tail)
                lines.Add($"    #{e.Sequence} {e.TimestampUtc:HH:mm:ss} {e.SourceModule}/{e.EventType} [{e.Priority}]");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
