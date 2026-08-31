// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — World Model Canonico, Bounded EventBus, Context Slimmer,
//         Persistenza SQLite WAL e Delta-Encoding
// ============================================================================

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.Gate2
{
    public enum DataProvenance : byte { Observed = 0, Estimated = 1, Decision = 2 }
    public enum EntityType : byte { Player = 0, Monster = 1, Npc = 2, GroundItem = 3, Portal = 4, PetPartner = 5 }

    public readonly record struct Position2D(int X, int Y)
    {
        public double DistanceTo(Position2D other)
        {
            long dx = X - other.X;
            long dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public sealed record WorldEntity(
        long EntityId, EntityType Type, string Name, Position2D Position,
        int CurrentHp, int MaxHp, bool IsAlive, bool IsTargetable,
        DataProvenance Provenance, float ConfidenceScore, DateTime LastObservedUtc);

    public sealed record ControlledPlayerState(
        long CharacterId, string CharacterName, int Level, int JobLevel,
        int CurrentHp, int MaxHp, int CurrentMp, int MaxMp, Position2D Position,
        int MapId, bool IsInCombat, long Gold, int SpPoints);

    public sealed record WorldStateSnapshot(
        string SessionId, ulong FrameIndex, DateTime TimestampUtc,
        ControlledPlayerState Player,
        ImmutableDictionary<long, WorldEntity> Entities,
        float GlobalConfidence, bool IsDegradedState)
    {
        /// <summary>
        /// The pre-observation state. Nothing has been observed yet, so it must not
        /// look healthy: the player placeholder is explicitly unobserved
        /// (CharacterId 0, zero vitals), confidence is zero and the state is
        /// degraded until the first real observation is folded in. UNKNOWN is not
        /// a full HP bar.
        /// </summary>
        public static WorldStateSnapshot CreateInitial(string sessionId) => new(
            sessionId, 0, DateTime.UtcNow,
            new ControlledPlayerState(0, "UNOBSERVED", 0, 0, 0, 0, 0, 0,
                new Position2D(0, 0), 0, false, 0, 0),
            ImmutableDictionary<long, WorldEntity>.Empty, 0.0f, true);
    }

    public enum EventPriority : byte { LowTelemetry = 0, NormalAudit = 1, CriticalSecurity = 2 }

    public sealed record RuntimeEvent(
        Guid EventId, string SessionId, ulong FrameIndex, DateTime TimestampUtc,
        string SourceModule, string EventType, EventPriority Priority, string PayloadJson);

    public static class VRAMContextSlimmer
    {
        private const int MaxDiagnosticLength = 256;
        private static readonly ConcurrentDictionary<string, string> NormalizedCache = new();

        public static string SlimException(Exception? ex)
        {
            if (ex is null) return "UnknownException";
            string normalized = System.Text.RegularExpressions.Regex.Replace(
                $"{ex.GetType().Name}:{ex.Message}",
                @"0x[0-9a-fA-F]+|\d+", "<ID>");
            if (normalized.Length > MaxDiagnosticLength)
                normalized = normalized[..MaxDiagnosticLength] + "...[TRUNCATED]";
            return NormalizedCache.GetOrAdd(normalized, static value => value);
        }

        public static string? SlimJsonPayload(string? rawJson, int maxLen = 512)
        {
            if (rawJson is null || rawJson.Length <= maxLen) return rawJson;
            return rawJson[..maxLen] + "...[SLIMMED]";
        }
    }

    public sealed class BoundedEventBus : IAsyncDisposable
    {
        private readonly Channel<RuntimeEvent> _channel;
        private readonly ConcurrentBag<Action<RuntimeEvent>> _subscribers = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _dispatchTask;
        private long _droppedEventsCounter;
        private long _publishedEventsCounter;

        public long DroppedEventsCount => Interlocked.Read(ref _droppedEventsCounter);
        public long PublishedEventsCount => Interlocked.Read(ref _publishedEventsCounter);

        public BoundedEventBus(int capacity = 5000)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _channel = Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _dispatchTask = Task.Run(DispatchLoopAsync);
        }

        public bool TryPublish(RuntimeEvent runtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(runtimeEvent);
            Interlocked.Increment(ref _publishedEventsCounter);

            if (_channel.Writer.TryWrite(runtimeEvent)) return true;

            if (runtimeEvent.Priority == EventPriority.LowTelemetry)
            {
                Interlocked.Increment(ref _droppedEventsCounter);
                return false;
            }

            if (runtimeEvent.Priority == EventPriority.CriticalSecurity)
            {
                _ = PublishCriticalAsync(runtimeEvent);
                return true;
            }

            Interlocked.Increment(ref _droppedEventsCounter);
            return false;
        }

        private async Task PublishCriticalAsync(RuntimeEvent runtimeEvent)
        {
            try { await _channel.Writer.WriteAsync(runtimeEvent, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Interlocked.Increment(ref _droppedEventsCounter); }
            catch (ChannelClosedException) { Interlocked.Increment(ref _droppedEventsCounter); }
        }

        public void Subscribe(Action<RuntimeEvent> subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            _subscribers.Add(subscriber);
        }

        private async Task DispatchLoopAsync()
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out RuntimeEvent? ev))
                    {
                        foreach (var subscriber in _subscribers)
                        {
                            try { subscriber(ev); } catch { /* observer isolation */ }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            // Complete the writer first and let the dispatcher drain what was already
            // accepted: an accepted audit event must not be lost to disposal timing.
            // Cancellation only afterwards, as a backstop for a stuck subscriber path.
            _channel.Writer.TryComplete();
            try { await _dispatchTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Canonical SQLite policy for the Gate 2 telemetry store. The pragma values mirror
    /// the centralized policy in <c>nosai/storage/sqlite_policy.py</c> (WAL, synchronous=FULL,
    /// busy timeout, cache size, WAL size limit, incremental vacuum) so the C# runtime and
    /// the Python tooling cannot drift apart.
    /// </summary>
    public sealed class SqliteStoragePolicy
    {
        public const string JournalMode = "WAL";
        public const string Synchronous = "FULL";
        public const int CacheSizeKiB = 65536;
        public const long JournalSizeLimitBytes = 64L * 1024 * 1024;

        public string DatabasePath { get; }
        public int BusyTimeoutMs { get; }
        public int BatchFlushIntervalMs { get; }
        public int MaxBatchSize { get; }

        public SqliteStoragePolicy(string databasePath, int busyTimeoutMs = 5000,
            int batchIntervalMs = 250, int maxBatchSize = 500)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Database path is required.", nameof(databasePath));
            if (busyTimeoutMs < 0 || batchIntervalMs <= 0 || maxBatchSize <= 0)
                throw new ArgumentOutOfRangeException("Invalid SQLite storage policy.");
            DatabasePath = databasePath;
            BusyTimeoutMs = busyTimeoutMs;
            BatchFlushIntervalMs = batchIntervalMs;
            MaxBatchSize = maxBatchSize;
        }

        public string BuildConnectionString() => new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
    }

    /// <summary>
    /// Asynchronous batch logger persisting runtime events into the Gate 2 SQLite WAL store.
    /// Observational component only: it must never become an authorization or execution path
    /// (docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md). The single long-lived connection is used
    /// exclusively by the flush worker, then by disposal after the worker has stopped.
    /// </summary>
    public sealed class NosAiSqliteBatchLogger : IAsyncDisposable
    {
        private readonly SqliteStoragePolicy _policy;
        private readonly ConcurrentQueue<RuntimeEvent> _pendingEvents = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _flushWorker;
        private readonly SqliteConnection _connection;

        /// <summary>
        /// Where to read the count of events that never reached this logger.
        /// </summary>
        /// <remarks>
        /// The bus drops low-priority events when it is full and counts them in
        /// memory. That counter dies with the process, so the next replay of the
        /// store would look complete when it is not. Polling it here turns an
        /// in-memory number into a durable gap record.
        /// </remarks>
        private readonly Func<long>? _upstreamDropCount;

        private long _persistedCount;
        private long _failedBatchCount;
        private long _recordedUpstreamDrops;

        public long PersistedCount => Interlocked.Read(ref _persistedCount);

        /// <summary>Batches that failed to commit: observable instead of silently swallowed.</summary>
        public long FailedBatchCount => Interlocked.Read(ref _failedBatchCount);

        /// <param name="upstreamDropCount">
        /// A monotonic count of events lost before reaching this logger, typically
        /// <see cref="BoundedEventBus.DroppedEventsCount"/>. Optional, and when it
        /// is absent the store simply records no upstream gaps — it never invents
        /// completeness it cannot vouch for.
        /// </param>
        public NosAiSqliteBatchLogger(SqliteStoragePolicy policy, Func<long>? upstreamDropCount = null)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _upstreamDropCount = upstreamDropCount;
            _connection = InitializeDatabase();
            _flushWorker = Task.Run(BatchFlushLoopAsync);
        }

        private SqliteConnection InitializeDatabase()
        {
            // Canonical pragmas are applied in one place (Gate2Sqlite) so the event
            // store and the session store can never drift apart.
            var connection = Gate2Sqlite.OpenAligned(_policy.DatabasePath, _policy.BusyTimeoutMs);
            try
            {
                // The schema, and the migration onto the ordered one, live in
                // Gate2EventSchema so the writer and the replay reader cannot
                // disagree about what the table looks like.
                Gate2EventSchema.EnsureSchema(connection);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public void EnqueueEvent(RuntimeEvent ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            _pendingEvents.Enqueue(ev);
        }

        private async Task BatchFlushLoopAsync()
        {
            var batch = new List<RuntimeEvent>(_policy.MaxBatchSize);
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_policy.BatchFlushIntervalMs, _cts.Token).ConfigureAwait(false);
                    batch.Clear();
                    while (batch.Count < _policy.MaxBatchSize && _pendingEvents.TryDequeue(out var ev)) batch.Add(ev);
                    if (batch.Count > 0) PersistBatch(batch);
                    RecordUpstreamLosses();
                }
                catch (OperationCanceledException) { break; }
                catch { Interlocked.Increment(ref _failedBatchCount); /* persistence failure must not block runtime */ }
            }
        }

        private void PersistBatch(IReadOnlyList<RuntimeEvent> batch)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            // seq is assigned by SQLite on insert, which is what gives the log a
            // total order: timestamps tie and frame indexes repeat, so neither can
            // be replayed the same way twice.
            command.CommandText = """
                INSERT OR IGNORE INTO runtime_events
                    (event_id, session_id, frame_index, timestamp_utc, source_module, event_type, priority, payload_json)
                VALUES ($id, $session, $frame, $ts, $source, $type, $priority, $payload)
                """;
            var id = AddParameter(command, "$id");
            var session = AddParameter(command, "$session");
            var frame = AddParameter(command, "$frame");
            var ts = AddParameter(command, "$ts");
            var source = AddParameter(command, "$source");
            var type = AddParameter(command, "$type");
            var priority = AddParameter(command, "$priority");
            var payload = AddParameter(command, "$payload");
            foreach (var ev in batch)
            {
                id.Value = ev.EventId.ToString("N");
                session.Value = ev.SessionId;
                frame.Value = unchecked((long)ev.FrameIndex);
                ts.Value = ev.TimestampUtc.ToString("O");
                source.Value = ev.SourceModule;
                type.Value = ev.EventType;
                priority.Value = (int)ev.Priority;
                payload.Value = ev.PayloadJson;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            Interlocked.Add(ref _persistedCount, batch.Count);
        }

        /// <summary>
        /// Writes a durable marker for events that never arrived.
        /// </summary>
        /// <remarks>
        /// Best effort by necessity — if the store is unreachable the marker cannot
        /// be written either — but the failure is counted rather than swallowed, and
        /// a replay of a store that lost its markers is short rather than falsely
        /// complete.
        /// </remarks>
        public void RecordGap(long lostCount, string reason)
        {
            if (lostCount <= 0) return;
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO runtime_event_gaps (after_seq, lost_count, reason, detected_utc)
                    VALUES (COALESCE((SELECT MAX(seq) FROM runtime_events), 0), $lost, $reason, $at)
                    """;
                AddParameter(command, "$lost").Value = lostCount;
                AddParameter(command, "$reason").Value = reason;
                AddParameter(command, "$at").Value = DateTime.UtcNow.ToString("O");
                command.ExecuteNonQuery();
            }
            catch
            {
                Interlocked.Increment(ref _failedBatchCount);
            }
        }

        /// <summary>Turns the bus's in-memory drop counter into durable gap records.</summary>
        private void RecordUpstreamLosses()
        {
            if (_upstreamDropCount is null) return;

            long total = _upstreamDropCount();
            long alreadyRecorded = Interlocked.Read(ref _recordedUpstreamDrops);
            long newlyLost = total - alreadyRecorded;
            if (newlyLost <= 0) return;

            RecordGap(newlyLost, "event_bus_full");
            Interlocked.Add(ref _recordedUpstreamDrops, newlyLost);
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
            var finalBatch = new List<RuntimeEvent>();
            while (_pendingEvents.TryDequeue(out var ev)) finalBatch.Add(ev);
            if (finalBatch.Count > 0)
            {
                try { PersistBatch(finalBatch); }
                catch
                {
                    Interlocked.Increment(ref _failedBatchCount);
                    // Fail closed: the events are gone, so the log says so rather
                    // than ending as if nothing had been in flight.
                    RecordGap(finalBatch.Count, "final_batch_failed");
                }
            }

            // One last look at the bus, so events dropped just before shutdown are
            // recorded instead of dying with the counter that held them.
            try { RecordUpstreamLosses(); } catch { Interlocked.Increment(ref _failedBatchCount); }

            _connection.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// One entity mutation. <see cref="NewEntity"/> carries the full entity for
    /// additions, because a patch of nullable fields cannot introduce an entity the
    /// receiver has never seen; for updates and removals it stays null.
    /// </summary>
    public sealed record EntityDelta(long EntityId, bool IsRemoved, Position2D? NewPosition,
        int? NewHp, bool? NewIsAlive, bool? NewIsCombat, WorldEntity? NewEntity = null);

    public sealed record WorldStateDeltaPacket(string SessionId, ulong BaseFrameIndex,
        ulong TargetFrameIndex, Position2D PlayerPosition, int PlayerHp, int PlayerMp,
        bool PlayerInCombat, ImmutableArray<EntityDelta> MutatedEntities);

    public static class WorldStateDeltaEngine
    {
        public static WorldStateDeltaPacket ComputeDelta(WorldStateSnapshot baseState, WorldStateSnapshot targetState)
        {
            ArgumentNullException.ThrowIfNull(baseState);
            ArgumentNullException.ThrowIfNull(targetState);
            if (!string.Equals(baseState.SessionId, targetState.SessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Delta requires snapshots from the same session.");
            if (targetState.FrameIndex < baseState.FrameIndex)
                throw new ArgumentException("Target frame must not precede base frame.", nameof(targetState));

            var deltas = new List<EntityDelta>();
            foreach (var (id, current) in targetState.Entities)
            {
                if (!baseState.Entities.TryGetValue(id, out var previous))
                {
                    deltas.Add(new EntityDelta(id, false, current.Position, current.CurrentHp, current.IsAlive, false, current));
                    continue;
                }
                bool posChanged = current.Position != previous.Position;
                bool hpChanged = current.CurrentHp != previous.CurrentHp;
                bool aliveChanged = current.IsAlive != previous.IsAlive;
                if (posChanged || hpChanged || aliveChanged)
                    deltas.Add(new EntityDelta(id, false,
                        posChanged ? current.Position : null,
                        hpChanged ? current.CurrentHp : null,
                        aliveChanged ? current.IsAlive : null, null));
            }
            foreach (var id in baseState.Entities.Keys)
                if (!targetState.Entities.ContainsKey(id)) deltas.Add(new EntityDelta(id, true, null, null, null, null));

            return new WorldStateDeltaPacket(targetState.SessionId, baseState.FrameIndex, targetState.FrameIndex,
                targetState.Player.Position, targetState.Player.CurrentHp, targetState.Player.CurrentMp,
                targetState.Player.IsInCombat, deltas.ToImmutableArray());
        }

        public static byte[] SerializeDelta(WorldStateDeltaPacket delta) => JsonSerializer.SerializeToUtf8Bytes(delta);

        /// <summary>
        /// Reconstructs the target snapshot from a base snapshot plus its delta.
        /// The chain is strict: the delta must have been computed exactly against
        /// this base frame, and a patch referencing an entity the base does not
        /// contain fails closed instead of inventing one.
        /// </summary>
        public static WorldStateSnapshot ApplyDelta(WorldStateSnapshot baseState, WorldStateDeltaPacket delta)
        {
            ArgumentNullException.ThrowIfNull(baseState);
            ArgumentNullException.ThrowIfNull(delta);
            if (!string.Equals(baseState.SessionId, delta.SessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Delta application requires the same session.");
            if (delta.BaseFrameIndex != baseState.FrameIndex)
                throw new InvalidOperationException(
                    $"Delta base frame {delta.BaseFrameIndex} does not match snapshot frame {baseState.FrameIndex}.");

            var builder = baseState.Entities.ToBuilder();
            var mutations = delta.MutatedEntities.IsDefault ? ImmutableArray<EntityDelta>.Empty : delta.MutatedEntities;
            foreach (var mutation in mutations)
            {
                if (mutation.IsRemoved)
                {
                    builder.Remove(mutation.EntityId);
                    continue;
                }
                if (mutation.NewEntity is { } added)
                {
                    builder[mutation.EntityId] = added;
                    continue;
                }
                if (!builder.TryGetValue(mutation.EntityId, out var existing))
                    throw new InvalidOperationException($"Delta patches unknown entity {mutation.EntityId}.");
                builder[mutation.EntityId] = existing with
                {
                    Position = mutation.NewPosition ?? existing.Position,
                    CurrentHp = mutation.NewHp ?? existing.CurrentHp,
                    IsAlive = mutation.NewIsAlive ?? existing.IsAlive,
                };
            }

            return baseState with
            {
                FrameIndex = delta.TargetFrameIndex,
                Player = baseState.Player with
                {
                    Position = delta.PlayerPosition,
                    CurrentHp = delta.PlayerHp,
                    CurrentMp = delta.PlayerMp,
                    IsInCombat = delta.PlayerInCombat,
                },
                Entities = builder.ToImmutable(),
            };
        }
    }

    public sealed class Gate2RuntimeEngine : IAsyncDisposable
    {
        private readonly BoundedEventBus _eventBus;
        private readonly NosAiSqliteBatchLogger _sqliteLogger;
        private WorldStateSnapshot _currentState;
        private readonly object _stateLock = new();

        public WorldStateSnapshot CurrentState { get { lock (_stateLock) return _currentState; } }
        public BoundedEventBus EventBus => _eventBus;
        public NosAiSqliteBatchLogger Logger => _sqliteLogger;

        public Gate2RuntimeEngine(string dbPath = "data/nosai_telemetry.db", string sessionId = "GATE2_ACTIVE_SESSION")
        {
            _eventBus = new BoundedEventBus(2000);
            // The logger polls the bus's drop counter so a full bus leaves a mark in
            // the store instead of an invisible hole in the audit trail.
            _sqliteLogger = new NosAiSqliteBatchLogger(
                new SqliteStoragePolicy(dbPath, batchIntervalMs: 50, maxBatchSize: 100),
                upstreamDropCount: () => _eventBus.DroppedEventsCount);
            _currentState = WorldStateSnapshot.CreateInitial(sessionId);
            _eventBus.Subscribe(_sqliteLogger.EnqueueEvent);
        }

        public WorldStateSnapshot UpdateWorldState(Func<WorldStateSnapshot, WorldStateSnapshot> stateTransformer)
        {
            ArgumentNullException.ThrowIfNull(stateTransformer);
            lock (_stateLock)
            {
                var previous = _currentState;
                var next = stateTransformer(previous) ?? throw new InvalidOperationException("State transformer returned null.");
                if (next.FrameIndex < previous.FrameIndex) throw new InvalidOperationException("World frame regression detected.");
                if (!string.Equals(next.SessionId, previous.SessionId, StringComparison.Ordinal)) throw new InvalidOperationException("World session mutation detected.");
                _currentState = next;
                _eventBus.TryPublish(new RuntimeEvent(Guid.NewGuid(), next.SessionId, next.FrameIndex,
                    DateTime.UtcNow, "WorldModelEngine", "WorldStateUpdated", EventPriority.NormalAudit,
                    JsonSerializer.Serialize(new { entitiesCount = next.Entities.Count, playerHp = next.Player.CurrentHp })));
                return next;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _eventBus.DisposeAsync().ConfigureAwait(false);
            await _sqliteLogger.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static partial class Gate2TestRunner
    {
        /// <summary>
        /// Runs every Gate 2 check and reports each one by name.
        /// </summary>
        /// <remarks>
        /// Same contract as the Gate 1 runner: results accumulate with <c>&amp;=</c> so a
        /// failing check never hides the ones after it, and a throwing check becomes a
        /// named failure carrying the exception message instead of a silent one.
        /// The world-model, delta-sync, session-store and context-slimming checks live
        /// in <c>Gate2TestRunnerChecks.cs</c>.
        /// </remarks>
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Gate 2 checks ===");

            bool allPassed = true;
            allPassed &= Run("WorldState snapshots are immutable", TestWorldStateImmutability);
            allPassed &= Run("Initial state is honestly unobserved", TestInitialStateIsHonestlyUnobserved);
            allPassed &= Run("Observation folding upserts and removes entities", TestObservationFolding);
            allPassed &= Run("Unobserved fields keep their previous values", TestUnobservedFieldsKeepPreviousValues);
            allPassed &= Run("Stale entities expire deterministically", TestStaleEntitiesExpire);
            allPassed &= Run("Map change clears the previous population", TestMapChangeClearsEntities);
            allPassed &= Run("Fold rejects time regression", TestFoldRejectsTimeRegression);
            allPassed &= Run("Context slimmer strips volatile identifiers", TestContextSlimmer);
            allPassed &= Run("Error history compression is bounded and stable", TestErrorHistoryCompression);
            allPassed &= Run("World context slimming fits the entity budget", TestWorldContextSlimming);
            allPassed &= Run("Delta encoding is smaller than a full snapshot", TestDeltaEncoding);
            allPassed &= Run("Delta rejects mixed sessions and frame regression", TestDeltaRejectsIncoherentInput);
            allPassed &= Run("Delta round-trips through apply", TestDeltaRoundTripsThroughApply);
            allPassed &= Run("Binary codec round-trips and rejects malformed frames", TestBinaryCodecRoundTrip);
            allPassed &= Run("Binary delta saves at least 70 percent of bandwidth", TestBinaryDeltaBandwidthSaving);
            allPassed &= Run("Delta tracker resyncs when the base frame is gone", TestDeltaTrackerResync);
            allPassed &= Run("EventBus exposes no execution surface", TestEventBusSecurityInvariant);
            allPassed &= await RunAsync("Bounded EventBus drops only low-priority overflow", TestEventBusDroppingAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("SQLite WAL store persists and reads back events", TestSqliteWalPersistenceAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("Session and trajectory rows enforce integrity", TestSessionAndTrajectoryIntegrityAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("World engine rejects frame regression and session mutation", TestWorldEngineInvariantsAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("Integrated engine observes, persists and serves deltas", TestIntegratedEngineEndToEndAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("Runtime engine store replays in order and admits losses", TestDurableEventLogReplaysAsync).ConfigureAwait(false);

            Console.WriteLine(allPassed
                ? "=== Gate 2 checks passed. Local only: this is not real-environment verification. ==="
                : "=== Gate 2 checks FAILED. See the lines marked FAIL above. ===");
            return allPassed;
        }

        private static bool Run(string name, Func<bool> check)
        {
            try { return Report(name, check(), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
        {
            try { return Report(name, await check().ConfigureAwait(false), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static bool Report(string name, bool passed, string? error)
        {
            var detail = error is null ? string.Empty : $" [{error}]";
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
            return passed;
        }

        private static bool TestWorldStateImmutability()
        {
            var initial = WorldStateSnapshot.CreateInitial("TEST");
            var entity = new WorldEntity(101, EntityType.Monster, "Dander", new Position2D(15, 20), 80, 80, true, true, DataProvenance.Observed, .98f, DateTime.UtcNow);
            var updated = initial with { FrameIndex = 1, Entities = initial.Entities.Add(entity.EntityId, entity) };
            return initial.Entities.Count == 0 && updated.Entities.Count == 1 && updated.Entities.ContainsKey(101);
        }

        private static bool TestContextSlimmer()
        {
            string result = VRAMContextSlimmer.SlimException(new InvalidOperationException("Socket failure IP 192.168.1.100 port 6100 handle 0x7FFEABCD12"));
            return !result.Contains("192.168.1.100", StringComparison.Ordinal) && !result.Contains("0x7FFEABCD12", StringComparison.Ordinal) && result.Contains("<ID>", StringComparison.Ordinal);
        }

        private static async Task<bool> TestEventBusDroppingAsync()
        {
            await using var bus = new BoundedEventBus(10);
            using var dispatcherStall = new ManualResetEventSlim(false);
            bus.Subscribe(_ => dispatcherStall.Wait(TimeSpan.FromSeconds(5)));

            static RuntimeEvent Make(EventPriority priority, ulong frame) => new(
                Guid.NewGuid(), "S", frame, DateTime.UtcNow, "Test", "Tick", priority, "{}");

            // The first event parks the dispatcher inside the subscriber, so the channel
            // fills deterministically instead of racing against the drain loop.
            bus.TryPublish(Make(EventPriority.LowTelemetry, 0));
            bool sawLowPriorityDrop = false;
            for (ulong i = 1; i <= 30; i++) sawLowPriorityDrop |= !bus.TryPublish(Make(EventPriority.LowTelemetry, i));

            // A critical event must be accepted even while the bus is saturated.
            bool criticalAccepted = bus.TryPublish(Make(EventPriority.CriticalSecurity, 999));
            dispatcherStall.Set();
            await Task.Delay(50).ConfigureAwait(false);
            return sawLowPriorityDrop && criticalAccepted && bus.DroppedEventsCount > 0;
        }

        private static async Task<bool> TestSqliteWalPersistenceAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_{Guid.NewGuid():N}.db");
            try
            {
                await using (var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(path, batchIntervalMs: 20, maxBatchSize: 50)))
                {
                    for (int i = 0; i < 25; i++)
                        logger.EnqueueEvent(new RuntimeEvent(Guid.NewGuid(), "GATE2_TEST", (ulong)i, DateTime.UtcNow, "Test", "Audit", EventPriority.NormalAudit, "{}"));

                    // Bounded polling instead of one fixed sleep: fail honestly on timeout.
                    for (int attempt = 0; attempt < 200 && logger.PersistedCount < 25; attempt++)
                        await Task.Delay(10).ConfigureAwait(false);
                    if (logger.PersistedCount < 25 || logger.FailedBatchCount != 0) return false;
                }

                // Read back through an independent connection: persistence must be real
                // SQLite in WAL mode, not a write-path artifact.
                await using var reader = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString());
                await reader.OpenAsync().ConfigureAwait(false);
                using var journalCommand = reader.CreateCommand();
                journalCommand.CommandText = "PRAGMA journal_mode";
                string mode = Convert.ToString(journalCommand.ExecuteScalar())?.ToLowerInvariant() ?? string.Empty;
                using var countCommand = reader.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM runtime_events WHERE session_id = 'GATE2_TEST'";
                long rows = Convert.ToInt64(countCommand.ExecuteScalar());
                return mode == "wal" && rows == 25;
            }
            finally { CleanupDatabase(path); }
        }

        private static async Task<bool> TestWorldEngineInvariantsAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_engine_{Guid.NewGuid():N}.db");
            try
            {
                await using var engine = new Gate2RuntimeEngine(path);
                engine.UpdateWorldState(s => s with { FrameIndex = s.FrameIndex + 1 });

                bool regressionRejected = false;
                try { engine.UpdateWorldState(s => s with { FrameIndex = s.FrameIndex - 1 }); }
                catch (InvalidOperationException) { regressionRejected = true; }

                bool sessionMutationRejected = false;
                try { engine.UpdateWorldState(s => s with { SessionId = "HIJACKED" }); }
                catch (InvalidOperationException) { sessionMutationRejected = true; }

                return regressionRejected && sessionMutationRejected && engine.CurrentState.FrameIndex == 1;
            }
            finally { CleanupDatabase(path); }
        }

        private static bool TestDeltaRejectsIncoherentInput()
        {
            var sessionA = WorldStateSnapshot.CreateInitial("A");
            var sessionB = WorldStateSnapshot.CreateInitial("B");
            bool mixedSessionsRejected = false;
            try { WorldStateDeltaEngine.ComputeDelta(sessionA, sessionB); }
            catch (InvalidOperationException) { mixedSessionsRejected = true; }

            var laterFrame = sessionA with { FrameIndex = 5 };
            bool frameRegressionRejected = false;
            try { WorldStateDeltaEngine.ComputeDelta(laterFrame, sessionA); }
            catch (ArgumentException) { frameRegressionRejected = true; }

            return mixedSessionsRejected && frameRegressionRejected;
        }

        private static void CleanupDatabase(string path)
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { /* best-effort temp cleanup */ }
            }
        }

        private static bool TestDeltaEncoding()
        {
            var initial = WorldStateSnapshot.CreateInitial("S");
            var builder = ImmutableDictionary.CreateBuilder<long, WorldEntity>();
            for (int i = 0; i < 20; i++) builder.Add(i, new WorldEntity(i, EntityType.Monster, $"Fox_{i}", new Position2D(10 + i, 10 + i), 100, 100, true, true, DataProvenance.Observed, .95f, DateTime.UtcNow));
            var frame1 = initial with { FrameIndex = 1, Entities = builder.ToImmutable() };
            var frame2 = frame1 with { FrameIndex = 2, Entities = frame1.Entities.SetItem(0, frame1.Entities[0] with { Position = new Position2D(99, 99) }) };
            var delta = WorldStateDeltaEngine.ComputeDelta(frame1, frame2);
            var full = JsonSerializer.SerializeToUtf8Bytes(frame2);
            var compact = WorldStateDeltaEngine.SerializeDelta(delta);
            return delta.MutatedEntities.Length == 1 && full.Length > 0 && compact.Length < full.Length;
        }

        private static bool TestEventBusSecurityInvariant()
        {
            var methods = typeof(BoundedEventBus).GetMethods().Select(m => m.Name.ToLowerInvariant());
            return !methods.Any(m => m.Contains("execute") || m.Contains("authorize") || m.Contains("grant"));
        }
    }
}
