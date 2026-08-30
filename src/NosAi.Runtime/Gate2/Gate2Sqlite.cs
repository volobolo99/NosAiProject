// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Bootstrap SQLite condiviso: unico punto di applicazione della policy
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.Gate2;

/// <summary>
/// Single application point for the canonical SQLite policy (<see cref="SqliteStoragePolicy"/>,
/// mirrored from <c>nosai/storage/sqlite_policy.py</c>). Every Gate 2 connection goes through
/// here so the pragmas cannot drift between the event store and the session store.
/// </summary>
internal static class Gate2Sqlite
{
    internal static SqliteConnection OpenAligned(string databasePath, int busyTimeoutMs)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bool creatingNewDatabase = !File.Exists(databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            // auto_vacuum is a database-file property: apply it only while creating the
            // file, before any table exists (mirrors nosai/storage/sqlite_policy.py).
            if (creatingNewDatabase) Execute(connection, "PRAGMA auto_vacuum=INCREMENTAL");
            Execute(connection, "PRAGMA foreign_keys=ON");
            Execute(connection, $"PRAGMA busy_timeout={busyTimeoutMs}");
            string journalMode = ExecuteScalarString(connection, $"PRAGMA journal_mode={SqliteStoragePolicy.JournalMode}");
            if (!string.Equals(journalMode, SqliteStoragePolicy.JournalMode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite journal mode mismatch: '{journalMode}'.");
            Execute(connection, $"PRAGMA synchronous={SqliteStoragePolicy.Synchronous}");
            Execute(connection, $"PRAGMA cache_size={-SqliteStoragePolicy.CacheSizeKiB}");
            Execute(connection, $"PRAGMA journal_size_limit={SqliteStoragePolicy.JournalSizeLimitBytes}");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static string ExecuteScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    internal static long ExecuteScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
