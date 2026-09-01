using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NosAi.Core;

namespace NosAi.Storage;

/// <summary>
/// SQLite-backed, hash-chained <see cref="IEventJournal"/>
/// (docs/ROADMAP_ESECUTIVA.md S:2.2-2.3). WAL + synchronous=FULL +
/// busy_timeout are applied and verified immediately after opening, in that
/// order, before any table is touched: a journal that could not confirm its
/// own durability policy has no business claiming records are durable.
/// </summary>
public sealed class SqliteEventJournal : IEventJournal
{
    private readonly SqliteConnection _connection;
    private readonly object _appendLock = new();
    private long _lastSequence;
    private byte[] _lastChainHash;

    public SqliteEventJournal(string databasePath, SqliteJournalOptions options, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

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
            (_lastSequence, _lastChainHash) = LoadOrCreateGenesis(sessionId);
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>Opens the journal at the path resolved from <paramref name="options"/>'s labeled volume.</summary>
    public static SqliteEventJournal OpenFromVolume(SqliteJournalOptions options, string sessionId) =>
        new(VolumeLocator.ResolveDatabasePath(options), options, sessionId);

    public long Append(in JournalRecord record)
    {
        byte[] payload = record.Payload.ToArray();

        lock (_appendLock)
        {
            long sequence = _lastSequence + 1;
            byte[] chainHash = ComputeChainHash(_lastChainHash, sequence, record.UnixMillis, record.Stage, payload);

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO journal_records (sequence, unix_millis, stage, payload, chain_hash)
                VALUES ($sequence, $unixMillis, $stage, $payload, $chainHash)
                """;
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$unixMillis", record.UnixMillis);
            command.Parameters.AddWithValue("$stage", (byte)record.Stage);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$chainHash", chainHash);
            command.ExecuteNonQuery();

            _lastSequence = sequence;
            _lastChainHash = chainHash;
            return sequence;
        }
    }

    public async IAsyncEnumerable<JournalRecord> ReplayAsync(long fromSequence, [EnumeratorCancellation] CancellationToken ct)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, unix_millis, stage, payload, chain_hash
            FROM journal_records
            WHERE sequence >= $from
            ORDER BY sequence
            """;
        command.Parameters.AddWithValue("$from", fromSequence);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return ReadRecord(reader);
        }
    }

    public bool VerifyChain(long fromSequence, out long firstBrokenSequence)
    {
        byte[] previousHash = LoadSeedHash(fromSequence);

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, unix_millis, stage, payload, chain_hash
            FROM journal_records
            WHERE sequence >= $from
            ORDER BY sequence
            """;
        command.Parameters.AddWithValue("$from", fromSequence);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            JournalRecord record = ReadRecord(reader);
            byte[] payload = record.Payload.ToArray();
            byte[] expected = ComputeChainHash(previousHash, record.Sequence, record.UnixMillis, record.Stage, payload);
            byte[] stored = record.ChainHash.ToArray();

            if (!CryptographicOperations.FixedTimeEquals(expected, stored))
            {
                firstBrokenSequence = record.Sequence;
                return false;
            }

            previousHash = stored;
        }

        firstBrokenSequence = -1;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static JournalRecord ReadRecord(SqliteDataReader reader)
    {
        long sequence = reader.GetInt64(0);
        long unixMillis = reader.GetInt64(1);
        var stage = (PipelineStage)reader.GetByte(2);
        byte[] payload = (byte[])reader[3];
        byte[] chainHash = (byte[])reader[4];
        return new JournalRecord(sequence, unixMillis, stage, payload, chainHash);
    }

    private byte[] LoadSeedHash(long fromSequence)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT chain_hash FROM journal_records WHERE sequence < $from ORDER BY sequence DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$from", fromSequence);

        if (command.ExecuteScalar() is byte[] priorHash)
            return priorHash;

        using SqliteCommand genesis = _connection.CreateCommand();
        genesis.CommandText = "SELECT chain_hash FROM journal_genesis LIMIT 1";
        return (byte[])genesis.ExecuteScalar()!;
    }

    private static byte[] ComputeChainHash(byte[] previousHash, long sequence, long unixMillis, PipelineStage stage, byte[] payload)
    {
        Span<byte> header = stackalloc byte[32 + 8 + 8 + 1];
        previousHash.CopyTo(header);
        BinaryPrimitives.WriteInt64BigEndian(header.Slice(32, 8), sequence);
        BinaryPrimitives.WriteInt64BigEndian(header.Slice(40, 8), unixMillis);
        header[48] = (byte)stage;

        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(header);
        sha256.AppendData(payload);
        return sha256.GetHashAndReset();
    }

    private (long lastSequence, byte[] lastChainHash) LoadOrCreateGenesis(string sessionId)
    {
        using SqliteCommand select = _connection.CreateCommand();
        select.CommandText = "SELECT chain_hash FROM journal_genesis LIMIT 1";

        byte[] genesisHash;
        if (select.ExecuteScalar() is byte[] existing)
        {
            genesisHash = existing;
        }
        else
        {
            genesisHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
            using SqliteCommand insert = _connection.CreateCommand();
            insert.CommandText = "INSERT INTO journal_genesis (session_id, chain_hash) VALUES ($session, $hash)";
            insert.Parameters.AddWithValue("$session", sessionId);
            insert.Parameters.AddWithValue("$hash", genesisHash);
            insert.ExecuteNonQuery();
        }

        using SqliteCommand last = _connection.CreateCommand();
        last.CommandText = "SELECT sequence, chain_hash FROM journal_records ORDER BY sequence DESC LIMIT 1";
        using SqliteDataReader reader = last.ExecuteReader();
        if (reader.Read())
            return (reader.GetInt64(0), (byte[])reader[1]);

        return (-1, genesisHash);
    }

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS journal_records (
                sequence    INTEGER PRIMARY KEY,
                unix_millis INTEGER NOT NULL,
                stage       INTEGER NOT NULL,
                payload     BLOB NOT NULL,
                chain_hash  BLOB NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS journal_genesis (
                session_id TEXT NOT NULL,
                chain_hash BLOB NOT NULL
            )
            """);
    }

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
