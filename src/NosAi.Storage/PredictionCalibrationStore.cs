using System.Globalization;
using Microsoft.Data.Sqlite;
using NosAi.Core.WorldModel;

namespace NosAi.Storage;

/// <summary>
/// One context's resolved calibration, exactly as it is persisted: the raw
/// Beta-Binomial evidence and the raw outcome counters. Plain scalars only, so
/// the persistence layer carries no dependency on the runtime's learning types
/// (<c>NosAi.Runtime</c> references <c>NosAi.Storage</c>, never the reverse).
/// </summary>
/// <param name="ContextKey">The context key.</param>
/// <param name="Alpha">Beta-Binomial alpha (successes plus the uniform prior's 1.0).</param>
/// <param name="Beta">Beta-Binomial beta (failures plus the uniform prior's 1.0).</param>
/// <param name="TotalTrials">Learnable (<c>Live</c>) outcomes observed.</param>
/// <param name="Confirmed">Live outcomes within tolerance.</param>
/// <param name="Refuted">Live outcomes outside tolerance.</param>
/// <param name="Ignored">Outcomes seen but not learnable (not Live).</param>
/// <param name="ErrorSum">Sum of absolute errors over learnable outcomes.</param>
public sealed record CalibrationSnapshotEntry(
    string ContextKey,
    double Alpha,
    double Beta,
    int TotalTrials,
    int Confirmed,
    int Refuted,
    int Ignored,
    double ErrorSum);

/// <summary>
/// An immutable photograph of a ledger's resolved calibration state -- the wire
/// format between <c>PredictionLedger</c> and <c>PredictionCalibrationStore</c>.
/// </summary>
/// <remarks>
/// Open predictions are deliberately absent. A prediction is written before the
/// act, never after, and persisting one would be persisting a claim about an
/// instant that no longer exists: re-imported into another process it would be
/// resolved against a different world than the one it was made against, which is
/// the opposite of the honesty <c>PredictionLedger</c>'s remarks require.
/// </remarks>
public sealed record CalibrationSnapshot(EquatableArray<CalibrationSnapshotEntry> Entries)
{
    /// <summary>The canonical empty snapshot.</summary>
    public static CalibrationSnapshot Empty { get; } = new(EquatableArray<CalibrationSnapshotEntry>.Empty);

    /// <summary>Wraps <paramref name="entries"/> into an immutable snapshot.</summary>
    public static CalibrationSnapshot From(IReadOnlyList<CalibrationSnapshotEntry> entries) =>
        new(EquatableArray<CalibrationSnapshotEntry>.From(entries));
}

/// <summary>
/// Durable store for <c>PredictionLedger</c>'s resolved calibration
/// (docs/ROADMAP_ESECUTIVA.md S:AP-09 "Memory / Learning / Simulation"): the
/// persistence that keeps the Beta-Binomial evidence and outcome counters from
/// dying with the process. Same durability discipline as
/// <see cref="ActionOutcomeLedgerStore"/>: WAL, FULL synchronous,
/// busy_timeout=5000, applied and re-verified before the table is touched, in
/// the same <c>nosai.db</c> file (a new table, not a second database).
/// </summary>
/// <remarks>
/// <para>
/// One row per context, keyed on <see cref="CalibrationSnapshotEntry.ContextKey"/>.
/// <see cref="Save"/> replaces the table's contents wholesale: the snapshot is a
/// complete photograph, not a delta, and an upsert would leave a context that was
/// dropped from the ledger lingering in the store as a stale row.
/// </para>
/// <para>
/// The replace-vs-sum choice belongs to the caller, not here. This store only
/// mirrors whatever snapshot it is handed; <c>PredictionLedger.ImportCalibration</c>
/// is where "import twice must not double the counts" is argued and enforced.
/// </para>
/// </remarks>
public sealed class PredictionCalibrationStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public PredictionCalibrationStore(string databasePath, SqliteJournalOptions options)
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
    public static PredictionCalibrationStore OpenFromVolume(SqliteJournalOptions options) =>
        new(VolumeLocator.ResolveDatabasePath(options), options);

    /// <summary>
    /// <see cref="OpenFromVolume"/>, but returns <see langword="null"/> with a
    /// reason instead of throwing when the volume is not attached -- the same
    /// shape <see cref="ActionOutcomeLedgerStore.TryOpenFromVolume"/> uses.
    /// </summary>
    public static PredictionCalibrationStore? TryOpenFromVolume(SqliteJournalOptions options, out string? failureReason)
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
    /// Replaces the stored calibration with <paramref name="snapshot"/>. A
    /// snapshot is a complete photograph: every existing row is cleared first,
    /// so a context removed from the ledger does not survive here as a stale row.
    /// </summary>
    public void Save(CalibrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();

            using (SqliteCommand clear = _connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM prediction_calibration";
                clear.ExecuteNonQuery();
            }

            foreach (CalibrationSnapshotEntry entry in snapshot.Entries)
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO prediction_calibration
                        (context_key, alpha, beta, total_trials, confirmed, refuted, ignored, error_sum)
                    VALUES
                        ($contextKey, $alpha, $beta, $totalTrials, $confirmed, $refuted, $ignored, $errorSum)
                    """;
                command.Parameters.AddWithValue("$contextKey", entry.ContextKey);
                command.Parameters.AddWithValue("$alpha", entry.Alpha);
                command.Parameters.AddWithValue("$beta", entry.Beta);
                command.Parameters.AddWithValue("$totalTrials", entry.TotalTrials);
                command.Parameters.AddWithValue("$confirmed", entry.Confirmed);
                command.Parameters.AddWithValue("$refuted", entry.Refuted);
                command.Parameters.AddWithValue("$ignored", entry.Ignored);
                command.Parameters.AddWithValue("$errorSum", entry.ErrorSum);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>Loads the stored calibration, in ordinal context-key order.</summary>
    public CalibrationSnapshot Load()
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT context_key, alpha, beta, total_trials, confirmed, refuted, ignored, error_sum
                FROM prediction_calibration
                ORDER BY context_key ASC
                """;

            var entries = new List<CalibrationSnapshotEntry>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                entries.Add(ReadEntry(reader));

            return entries.Count == 0
                ? CalibrationSnapshot.Empty
                : new CalibrationSnapshot(EquatableArray<CalibrationSnapshotEntry>.From(entries));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    // Read every column by name via reader.GetOrdinal("..."), never by a
    // positional integer literal -- a column-order slip is exactly the kind of
    // silent bug a hardcoded index would hide.
    private static CalibrationSnapshotEntry ReadEntry(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("context_key")),
        reader.GetDouble(reader.GetOrdinal("alpha")),
        reader.GetDouble(reader.GetOrdinal("beta")),
        reader.GetInt32(reader.GetOrdinal("total_trials")),
        reader.GetInt32(reader.GetOrdinal("confirmed")),
        reader.GetInt32(reader.GetOrdinal("refuted")),
        reader.GetInt32(reader.GetOrdinal("ignored")),
        reader.GetDouble(reader.GetOrdinal("error_sum")));

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS prediction_calibration (
                context_key  TEXT PRIMARY KEY,
                alpha        REAL NOT NULL,
                beta         REAL NOT NULL,
                total_trials INTEGER NOT NULL,
                confirmed    INTEGER NOT NULL,
                refuted      INTEGER NOT NULL,
                ignored      INTEGER NOT NULL,
                error_sum    REAL NOT NULL
            )
            """);
    }

    // ----------------------------------------------------------------------
    // Durability policy -- copied verbatim from ActionOutcomeLedgerStore.cs
    // (which itself copies MapModelStore.cs). This store's durability policy is
    // the exact same one already audited there, not a re-derivation.
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
