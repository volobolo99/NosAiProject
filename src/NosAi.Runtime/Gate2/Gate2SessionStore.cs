// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Store WAL di sessioni e traiettorie con vincolo di integrità
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.Gate2;

/// <summary>
/// Session and world-frame (trajectory) persistence over the canonical WAL store.
/// Mirrors the contract of <c>nosai/persistence/sqlite_logger.py</c> required by
/// docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md: session registration, batched
/// trajectory writes, a session→trajectory integrity constraint and indexes on
/// session and timestamp. Observational component only — never an authorization
/// or execution path.
/// </summary>
public sealed class Gate2SessionStore : IAsyncDisposable
{
    private readonly SqliteStoragePolicy _policy;
    private readonly SqliteConnection _connection;
    private readonly ConcurrentQueue<PendingFrame> _pendingFrames = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushWorker;
    private readonly object _dbLock = new();
    private long _persistedFrameCount;
    private long _failedBatchCount;

    public long PersistedFrameCount => Interlocked.Read(ref _persistedFrameCount);

    /// <summary>Batches that failed to commit: observable instead of silently swallowed.</summary>
    public long FailedBatchCount => Interlocked.Read(ref _failedBatchCount);

    private readonly record struct PendingFrame(
        long SessionRowId, ulong FrameIndex, DateTime TimestampUtc, int MapId,
        Position2D Position, int PlayerHp, int PlayerMp, bool InCombat,
        int EntityCount, float GlobalConfidence, bool Degraded);

    public Gate2SessionStore(SqliteStoragePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _connection = Gate2Sqlite.OpenAligned(policy.DatabasePath, policy.BusyTimeoutMs);
        try
        {
            InitializeSchema();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
        _flushWorker = Task.Run(BatchFlushLoopAsync);
    }

    private void InitializeSchema()
    {
        Gate2Sqlite.Execute(_connection, """
            CREATE TABLE IF NOT EXISTS runtime_sessions (
                session_row_id   INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id       TEXT NOT NULL,
                started_utc      TEXT NOT NULL,
                ended_utc        TEXT NULL,
                frames_persisted INTEGER NOT NULL DEFAULT 0
            )
            """);
        Gate2Sqlite.Execute(_connection, """
            CREATE TABLE IF NOT EXISTS world_frames (
                frame_row_id      INTEGER PRIMARY KEY AUTOINCREMENT,
                session_row_id    INTEGER NOT NULL,
                frame_index       INTEGER NOT NULL,
                timestamp_utc     TEXT NOT NULL,
                map_id            INTEGER NOT NULL,
                pos_x             INTEGER NOT NULL,
                pos_y             INTEGER NOT NULL,
                player_hp         INTEGER NOT NULL,
                player_mp         INTEGER NOT NULL,
                in_combat         INTEGER NOT NULL,
                entity_count      INTEGER NOT NULL,
                global_confidence REAL NOT NULL,
                degraded          INTEGER NOT NULL,
                FOREIGN KEY(session_row_id) REFERENCES runtime_sessions(session_row_id)
            )
            """);
        Gate2Sqlite.Execute(_connection, "CREATE INDEX IF NOT EXISTS idx_world_frames_session ON world_frames(session_row_id)");
        Gate2Sqlite.Execute(_connection, "CREATE INDEX IF NOT EXISTS idx_world_frames_timestamp ON world_frames(timestamp_utc)");
        Gate2Sqlite.Execute(_connection, "CREATE INDEX IF NOT EXISTS idx_runtime_sessions_started ON runtime_sessions(started_utc)");
    }

    /// <summary>Registers a runtime session and returns its row id for trajectory rows.</summary>
    public long OpenSession(string sessionId, DateTime startedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_dbLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO runtime_sessions(session_id, started_utc) VALUES ($id, $started)";
            command.Parameters.AddWithValue("$id", sessionId);
            command.Parameters.AddWithValue("$started", startedUtc.ToString("O"));
            command.ExecuteNonQuery();
            return Gate2Sqlite.ExecuteScalarInt64(_connection, "SELECT last_insert_rowid()");
        }
    }

    /// <summary>
    /// Flushes any pending frames of the session and seals its row with the end time
    /// and the real persisted frame count — a count derived from rows, not claimed.
    /// </summary>
    public void CloseSession(long sessionRowId, DateTime endedUtc)
    {
        lock (_dbLock)
        {
            FlushPendingLocked();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE runtime_sessions
                SET ended_utc = $ended,
                    frames_persisted = (SELECT COUNT(*) FROM world_frames WHERE session_row_id = $id)
                WHERE session_row_id = $id
                """;
            command.Parameters.AddWithValue("$ended", endedUtc.ToString("O"));
            command.Parameters.AddWithValue("$id", sessionRowId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Unknown session row {sessionRowId}.");
        }
    }

    /// <summary>Queues one world frame for asynchronous batched persistence.</summary>
    public void EnqueueFrame(long sessionRowId, WorldStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var player = snapshot.Player;
        _pendingFrames.Enqueue(new PendingFrame(
            sessionRowId, snapshot.FrameIndex, snapshot.TimestampUtc, player.MapId,
            player.Position, player.CurrentHp, player.CurrentMp, player.IsInCombat,
            snapshot.Entities.Count, snapshot.GlobalConfidence, snapshot.IsDegradedState));
    }

    private async Task BatchFlushLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_policy.BatchFlushIntervalMs, _cts.Token).ConfigureAwait(false);
                lock (_dbLock) FlushPendingLocked();
            }
            catch (OperationCanceledException) { break; }
            catch { Interlocked.Increment(ref _failedBatchCount); /* persistence failure must not block runtime */ }
        }
    }

    private void FlushPendingLocked()
    {
        var batch = new List<PendingFrame>(_policy.MaxBatchSize);
        while (batch.Count < _policy.MaxBatchSize && _pendingFrames.TryDequeue(out var frame)) batch.Add(frame);
        if (batch.Count == 0) return;

        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO world_frames
                (session_row_id, frame_index, timestamp_utc, map_id, pos_x, pos_y,
                 player_hp, player_mp, in_combat, entity_count, global_confidence, degraded)
            VALUES ($session, $frame, $ts, $map, $x, $y, $hp, $mp, $combat, $entities, $confidence, $degraded)
            """;
        var session = AddParameter(command, "$session");
        var frameIndex = AddParameter(command, "$frame");
        var ts = AddParameter(command, "$ts");
        var map = AddParameter(command, "$map");
        var x = AddParameter(command, "$x");
        var y = AddParameter(command, "$y");
        var hp = AddParameter(command, "$hp");
        var mp = AddParameter(command, "$mp");
        var combat = AddParameter(command, "$combat");
        var entities = AddParameter(command, "$entities");
        var confidence = AddParameter(command, "$confidence");
        var degraded = AddParameter(command, "$degraded");
        foreach (var frame in batch)
        {
            session.Value = frame.SessionRowId;
            frameIndex.Value = unchecked((long)frame.FrameIndex);
            ts.Value = frame.TimestampUtc.ToString("O");
            map.Value = frame.MapId;
            x.Value = frame.Position.X;
            y.Value = frame.Position.Y;
            hp.Value = frame.PlayerHp;
            mp.Value = frame.PlayerMp;
            combat.Value = frame.InCombat ? 1 : 0;
            entities.Value = frame.EntityCount;
            confidence.Value = frame.GlobalConfidence;
            degraded.Value = frame.Degraded ? 1 : 0;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        Interlocked.Add(ref _persistedFrameCount, batch.Count);
    }

    private static SqliteParameter AddParameter(SqliteCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        command.Parameters.Add(parameter);
        return parameter;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _flushWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        lock (_dbLock)
        {
            try { FlushPendingLocked(); }
            catch { Interlocked.Increment(ref _failedBatchCount); /* fail closed: no fabricated persistence success */ }
            _connection.Dispose();
        }
        _cts.Dispose();
    }
}
