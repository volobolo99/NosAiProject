using System.Globalization;
using Microsoft.Data.Sqlite;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;

namespace NosAi.Storage;

/// <summary>
/// Durable, append-only store for <see cref="ActionOutcomeLedgerEntry"/>
/// (docs/ROADMAP_ESECUTIVA.md S:AP-09 "Action-outcome ledger") -- the
/// persistence <see cref="LocalOutcomeSimulator"/>'s own remarks name as
/// "a future runtime-wiring concern". Same durability discipline as
/// <see cref="MapModelStore"/>/<see cref="SqliteEventJournal"/>: WAL,
/// FULL synchronous, busy_timeout=5000, applied and independently
/// re-verified before any table is touched.
/// </summary>
/// <remarks>
/// <para>
/// One row per <see cref="ActionOutcomeLedgerEntry"/>, in the same
/// <c>nosai.db</c> file <see cref="MapModelStore"/> and
/// <see cref="SqliteEventJournal"/> already use (a new table in the same
/// file, not a second database). No JSON/DTO layer is needed, unlike
/// <see cref="MapModelStore"/>: <see cref="ActionOutcomeLedgerEntry"/> has
/// no nested <see cref="EquatableArray{T}"/> -- every field is a scalar or
/// one flat <see cref="WorldFact{ActionOutcome}"/>, so every field maps to
/// its own plain SQL column.
/// </para>
/// <para>
/// The ledger is <b>append-only</b>. A second <see cref="Append"/> with the
/// same <see cref="ActionOutcomeLedgerEntry.EntryId"/> is a caller bug and
/// is rejected by the primary key, never silently overwritten -- unlike
/// <see cref="MapModelStore.Save"/>'s intentional upsert.
/// </para>
/// </remarks>
public sealed class ActionOutcomeLedgerStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public ActionOutcomeLedgerStore(string databasePath, SqliteJournalOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        try
        {
            _connection.Open();
            ApplyPolicyOrThrow(options);
            EnsureSchema();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>Opens the store at the path resolved from <paramref name="options"/>'s labeled volume.</summary>
    /// <exception cref="InvalidOperationException">The labeled volume is not attached (see <see cref="VolumeLocator.ResolveDatabasePath"/>).</exception>
    public static ActionOutcomeLedgerStore OpenFromVolume(SqliteJournalOptions options) =>
        new(VolumeLocator.ResolveDatabasePath(options), options);

    /// <summary>
    /// <see cref="OpenFromVolume"/>, but returns <see langword="null"/>
    /// with a reason instead of throwing when the volume is not attached --
    /// the same "Try, return null with a reason" shape
    /// <c>NosAi.Runtime.Navigation.CollectCommand.LiveScope.TryOpen</c>
    /// already uses for an equally optional live resource. Ledger recording
    /// is opportunistic history, never a gate: callers treat a
    /// <see langword="null"/> store as "record nothing", never as a reason
    /// to refuse the command whose act already ran.
    /// </summary>
    public static ActionOutcomeLedgerStore? TryOpenFromVolume(SqliteJournalOptions options, out string? failureReason)
    {
        try
        {
            failureReason = null;
            return OpenFromVolume(options);
        }
        catch (InvalidOperationException ex)
        {
            failureReason = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Appends <paramref name="entry"/>. The ledger is append-only --
    /// unlike <see cref="MapModelStore.Save"/>'s intentional upsert, a
    /// second call with the same <see cref="ActionOutcomeLedgerEntry.EntryId"/>
    /// is a caller bug and is rejected by the primary key, never silently
    /// overwritten.
    /// </summary>
    public void Append(ActionOutcomeLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO action_outcome_ledger
                    (entry_id, action_id, category, context,
                     outcome_has_observed_value, outcome_value, outcome_source,
                     outcome_confidence, outcome_observed_at_unix_millis, outcome_reason,
                     recorded_at_unix_millis)
                VALUES
                    ($entryId, $actionId, $category, $context,
                     $outcomeHasObservedValue, $outcomeValue, $outcomeSource,
                     $outcomeConfidence, $outcomeObservedAt, $outcomeReason,
                     $recordedAt)
                """;
            command.Parameters.AddWithValue("$entryId", entry.EntryId.ToString());
            command.Parameters.AddWithValue("$actionId", entry.ActionId.Value);
            command.Parameters.AddWithValue("$category", (int)entry.Category);
            command.Parameters.AddWithValue("$context", entry.Context);
            command.Parameters.AddWithValue("$outcomeHasObservedValue", entry.Outcome.HasObservedValue ? 1 : 0);
            command.Parameters.AddWithValue("$outcomeValue", entry.Outcome.HasObservedValue ? (int)entry.Outcome.Value : DBNull.Value);
            command.Parameters.AddWithValue("$outcomeSource", (int)entry.Outcome.Source);
            command.Parameters.AddWithValue("$outcomeConfidence", entry.Outcome.Confidence);
            command.Parameters.AddWithValue("$outcomeObservedAt", ToUnixMillis(entry.Outcome.ObservedAtUtc));
            command.Parameters.AddWithValue("$outcomeReason", (object?)entry.Outcome.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$recordedAt", ToUnixMillis(entry.RecordedAtUtc));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Loads every entry recorded for <paramref name="context"/> (ordinal
    /// match, same comparison <see cref="LocalOutcomeSimulator.Predict"/>
    /// already uses), oldest first. Not called by any command in this
    /// task -- see the task's own "no read path" scope note.
    /// </summary>
    public EquatableArray<ActionOutcomeLedgerEntry> LoadByContext(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT entry_id, action_id, category, context,
                       outcome_has_observed_value, outcome_value, outcome_source,
                       outcome_confidence, outcome_observed_at_unix_millis, outcome_reason,
                       recorded_at_unix_millis
                FROM action_outcome_ledger
                WHERE context = $context
                ORDER BY recorded_at_unix_millis ASC
                """;
            command.Parameters.AddWithValue("$context", context);

            var entries = new List<ActionOutcomeLedgerEntry>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                entries.Add(ReadEntry(reader));

            return EquatableArray<ActionOutcomeLedgerEntry>.From(entries);
        }
    }

    /// <summary>
    /// Every context that has at least one row, in ordinal ascending order,
    /// with no duplicates. A context with no rows is not returned -- the
    /// ledger is append-only and has no separate context registry, so a
    /// context "exists" only by virtue of a recorded entry naming it.
    /// </summary>
    public EquatableArray<string> Contexts()
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT context
                FROM action_outcome_ledger
                ORDER BY context ASC
                """;

            var contexts = new List<string>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                contexts.Add(reader.GetString(reader.GetOrdinal("context")));

            return EquatableArray<string>.From(contexts);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    // Read every column by name via reader.GetOrdinal("..."), never by a
    // positional integer literal -- a column-order slip is exactly the kind
    // of silent bug a hardcoded index would hide.
    private static ActionOutcomeLedgerEntry ReadEntry(SqliteDataReader reader)
    {
        bool hasObservedValue = reader.GetInt32(reader.GetOrdinal("outcome_has_observed_value")) != 0;
        var outcome = new WorldFact<ActionOutcome>(
            hasObservedValue ? (ActionOutcome)reader.GetInt32(reader.GetOrdinal("outcome_value")) : default,
            (DataSourceKind)reader.GetInt32(reader.GetOrdinal("outcome_source")),
            reader.GetDouble(reader.GetOrdinal("outcome_confidence")),
            FromUnixMillis(reader.GetInt64(reader.GetOrdinal("outcome_observed_at_unix_millis"))),
            hasObservedValue,
            reader.IsDBNull(reader.GetOrdinal("outcome_reason")) ? null : reader.GetString(reader.GetOrdinal("outcome_reason")));

        return new ActionOutcomeLedgerEntry(
            Guid.Parse(reader.GetString(reader.GetOrdinal("entry_id"))),
            new ActionId(reader.GetString(reader.GetOrdinal("action_id"))),
            (MemoryType)reader.GetInt32(reader.GetOrdinal("category")),
            outcome,
            reader.GetString(reader.GetOrdinal("context")),
            FromUnixMillis(reader.GetInt64(reader.GetOrdinal("recorded_at_unix_millis"))));
    }

    private static long ToUnixMillis(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime FromUnixMillis(long unixMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMillis).UtcDateTime;

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS action_outcome_ledger (
                entry_id                       TEXT PRIMARY KEY,
                action_id                      TEXT NOT NULL,
                category                       INTEGER NOT NULL,
                context                        TEXT NOT NULL,
                outcome_has_observed_value     INTEGER NOT NULL,
                outcome_value                  INTEGER NULL,
                outcome_source                 INTEGER NOT NULL,
                outcome_confidence             REAL NOT NULL,
                outcome_observed_at_unix_millis INTEGER NOT NULL,
                outcome_reason                 TEXT NULL,
                recorded_at_unix_millis        INTEGER NOT NULL
            )
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_action_outcome_ledger_context ON action_outcome_ledger(context)");
    }

    // ----------------------------------------------------------------------
    // Durability policy -- copied verbatim from MapModelStore.cs (private,
    // byte-for-byte identical bodies). This store's durability policy is the
    // exact same one already audited there, not a re-derivation.
    // ----------------------------------------------------------------------

    private void ApplyPolicyOrThrow(SqliteJournalOptions options)
    {
        string journalMode = ExecuteScalarString($"PRAGMA journal_mode={options.JournalMode}");
        if (!string.Equals(journalMode, options.JournalMode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite journal_mode mismatch: expected '{options.JournalMode}', got '{journalMode}'.");

        Execute($"PRAGMA synchronous={options.Synchronous}");
        long synchronous = ExecuteScalarInt64("PRAGMA synchronous");
        const long fullSynchronous = 2;
        if (synchronous != fullSynchronous)
            throw new InvalidOperationException($"SQLite synchronous mismatch: expected FULL(2), got {synchronous}.");

        Execute($"PRAGMA busy_timeout={options.BusyTimeoutMs}");
        long busyTimeout = ExecuteScalarInt64("PRAGMA busy_timeout");
        if (busyTimeout != options.BusyTimeoutMs)
            throw new InvalidOperationException($"SQLite busy_timeout mismatch: expected {options.BusyTimeoutMs}, got {busyTimeout}.");
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string ExecuteScalarString(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private long ExecuteScalarInt64(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
