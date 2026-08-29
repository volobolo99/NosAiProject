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
        public static WorldStateSnapshot CreateInitial(string sessionId) => new(
            sessionId, 0, DateTime.UtcNow,
            new ControlledPlayerState(1, "Player_01", 1, 1, 100, 100, 50, 50,
                new Position2D(0, 0), 1, false, 0, 10000),
            ImmutableDictionary<long, WorldEntity>.Empty, 1.0f, false);
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
            _channel.Writer.TryComplete();
            _cts.Cancel();
            try { await _dispatchTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _cts.Dispose();
        }
    }

    public sealed class SqliteStoragePolicy
    {
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
    }

    public sealed class NosAiSqliteBatchLogger : IAsyncDisposable
    {
        private readonly SqliteStoragePolicy _policy;
        private readonly ConcurrentQueue<RuntimeEvent> _pendingEvents = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _flushWorker;
        private long _persistedCount;

        public long PersistedCount => Interlocked.Read(ref _persistedCount);

        public NosAiSqliteBatchLogger(SqliteStoragePolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            InitializeDatabase();
            _flushWorker = Task.Run(BatchFlushLoopAsync);
        }

        private void InitializeDatabase()
        {
            string? directory = Path.GetDirectoryName(_policy.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Gate 2 source is retained as a self-contained runtime component.
            // Real SQLite provider integration is intentionally not fabricated here.
            if (!File.Exists(_policy.DatabasePath)) File.WriteAllText(_policy.DatabasePath, "NOSAI_SQLITE_WAL_HEADER_V1\n");
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
                    if (batch.Count > 0)
                    {
                        await FlushBatchToFileAsync(batch).ConfigureAwait(false);
                        Interlocked.Add(ref _persistedCount, batch.Count);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* persistence failure must not block runtime */ }
            }
        }

        private async Task FlushBatchToFileAsync(IReadOnlyCollection<RuntimeEvent> batch)
        {
            var sb = new StringBuilder();
            foreach (var ev in batch) sb.AppendLine(JsonSerializer.Serialize(ev));
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await using var stream = new FileStream(_policy.DatabasePath, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _flushWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
            var finalBatch = new List<RuntimeEvent>();
            while (_pendingEvents.TryDequeue(out var ev)) finalBatch.Add(ev);
            if (finalBatch.Count > 0)
            {
                try
                {
                    await FlushBatchToFileAsync(finalBatch).ConfigureAwait(false);
                    Interlocked.Add(ref _persistedCount, finalBatch.Count);
                }
                catch { /* fail closed: no fabricated persistence success */ }
            }
            _cts.Dispose();
        }
    }

    public sealed record EntityDelta(long EntityId, bool IsRemoved, Position2D? NewPosition,
        int? NewHp, bool? NewIsAlive, bool? NewIsCombat);

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
                    deltas.Add(new EntityDelta(id, false, current.Position, current.CurrentHp, current.IsAlive, false));
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

        public Gate2RuntimeEngine(string dbPath = "data/nosai_telemetry.db")
        {
            _eventBus = new BoundedEventBus(2000);
            _sqliteLogger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(dbPath, batchIntervalMs: 50, maxBatchSize: 100));
            _currentState = WorldStateSnapshot.CreateInitial("GATE2_ACTIVE_SESSION");
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

    public static class Gate2TestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            bool allPassed = true;
            allPassed &= RunTest("WorldState immutability", TestWorldStateImmutability);
            allPassed &= RunTest("Context slimmer", TestContextSlimmer);
            allPassed &= await RunTestAsync("Bounded EventBus", TestEventBusDroppingAsync);
            allPassed &= await RunTestAsync("Batch persistence", TestBatchPersistenceAsync);
            allPassed &= RunTest("Delta encoding", TestDeltaEncoding);
            allPassed &= RunTest("EventBus security invariant", TestEventBusSecurityInvariant);
            return allPassed;
        }

        private static bool RunTest(string _, Func<bool> test) { try { return test(); } catch { return false; } }
        private static async Task<bool> RunTestAsync(string _, Func<Task<bool>> test) { try { return await test(); } catch { return false; } }

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
            for (int i = 0; i < 50; i++) bus.TryPublish(new RuntimeEvent(Guid.NewGuid(), "S", (ulong)i, DateTime.UtcNow, "Test", "Tick", EventPriority.LowTelemetry, "{}"));
            await Task.Delay(50);
            return bus.PublishedEventsCount == 50 && bus.DroppedEventsCount > 0;
        }

        private static async Task<bool> TestBatchPersistenceAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_{Guid.NewGuid():N}.db");
            try
            {
                await using (var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(path, batchIntervalMs: 20, maxBatchSize: 50)))
                {
                    for (int i = 0; i < 25; i++) logger.EnqueueEvent(new RuntimeEvent(Guid.NewGuid(), "S", (ulong)i, DateTime.UtcNow, "Test", "Audit", EventPriority.NormalAudit, "{}"));
                    await Task.Delay(150);
                    if (logger.PersistedCount < 25) return false;
                }
                return File.Exists(path);
            }
            finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
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
