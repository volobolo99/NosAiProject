// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Check di certificazione: world model, slimming, delta sync, store
// ============================================================================

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.Gate2;

public static partial class Gate2TestRunner
{
    private static WorldEntity MakeEntity(long id, int x, int y, DateTime observedUtc,
        float confidence = 0.9f, string? name = null, int currentHp = 100, int maxHp = 100) => new(
        id, EntityType.Monster, name ?? $"Mob_{id}", new Position2D(x, y),
        currentHp, maxHp, IsAlive: true, IsTargetable: true,
        DataProvenance.Observed, confidence, observedUtc);

    private static ControlledPlayerState MakePlayer(int mapId = 1, int hp = 800, int mp = 300, Position2D? position = null) => new(
        7, "Hero", 50, 30, hp, 900, mp, 350, position ?? new Position2D(0, 0), mapId, false, 1234, 5);

    // ------------------------------------------------------------------ world model

    private static bool TestInitialStateIsHonestlyUnobserved()
    {
        var initial = WorldStateSnapshot.CreateInitial("S");
        return initial.IsDegradedState
            && initial.GlobalConfidence == 0.0f
            && initial.Player.CharacterId == 0
            && initial.Player.CurrentHp == 0
            && initial.Player.MaxHp == 0;
    }

    private static bool TestObservationFolding()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var initial = WorldStateSnapshot.CreateInitial("S") with { TimestampUtc = t0 };

        var first = WorldModelReducer.Fold(initial, new ObservationBatch(
            t0.AddSeconds(1), MakePlayer(),
            ImmutableArray.Create(MakeEntity(1, 10, 10, t0.AddSeconds(1)), MakeEntity(2, 20, 20, t0.AddSeconds(1))),
            ImmutableArray<long>.Empty, null));
        if (first.FrameIndex != 1 || first.Entities.Count != 2 || first.IsDegradedState) return false;
        if (first.GlobalConfidence is < 0.89f or > 0.91f) return false;

        var second = WorldModelReducer.Fold(first, new ObservationBatch(
            t0.AddSeconds(2), null,
            ImmutableArray.Create(MakeEntity(1, 15, 15, t0.AddSeconds(2))),
            ImmutableArray.Create(2L), null));
        return second.FrameIndex == 2
            && second.Entities.Count == 1
            && second.Entities[1].Position == new Position2D(15, 15)
            && !second.Entities.ContainsKey(2);
    }

    private static bool TestUnobservedFieldsKeepPreviousValues()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var initial = WorldStateSnapshot.CreateInitial("S") with { TimestampUtc = t0 };
        var observedPlayer = MakePlayer(hp: 555);

        var first = WorldModelReducer.Fold(initial, new ObservationBatch(
            t0.AddSeconds(1), observedPlayer, ImmutableArray<WorldEntity>.Empty, ImmutableArray<long>.Empty, null));

        // Player not observed this frame: the previous observation survives — the
        // fold must not zero the vitals or resurrect the UNOBSERVED placeholder.
        var second = WorldModelReducer.Fold(first, new ObservationBatch(
            t0.AddSeconds(2), null,
            ImmutableArray.Create(MakeEntity(1, 5, 5, t0.AddSeconds(2))),
            ImmutableArray<long>.Empty, null));
        return second.Player == observedPlayer && !second.IsDegradedState;
    }

    private static bool TestStaleEntitiesExpire()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var ttl = TimeSpan.FromSeconds(5);
        var initial = WorldStateSnapshot.CreateInitial("S") with { TimestampUtc = t0 };

        var first = WorldModelReducer.Fold(initial, new ObservationBatch(
            t0.AddSeconds(1), MakePlayer(),
            ImmutableArray.Create(MakeEntity(1, 10, 10, t0.AddSeconds(1))),
            ImmutableArray<long>.Empty, null), ttl);

        var withinTtl = WorldModelReducer.Fold(first, new ObservationBatch(
            t0.AddSeconds(4), null, ImmutableArray<WorldEntity>.Empty, ImmutableArray<long>.Empty, null), ttl);
        if (!withinTtl.Entities.ContainsKey(1)) return false;

        var beyondTtl = WorldModelReducer.Fold(withinTtl, new ObservationBatch(
            t0.AddSeconds(7), null, ImmutableArray<WorldEntity>.Empty, ImmutableArray<long>.Empty, null), ttl);
        return !beyondTtl.Entities.ContainsKey(1);
    }

    private static bool TestMapChangeClearsEntities()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var initial = WorldStateSnapshot.CreateInitial("S") with { TimestampUtc = t0 };

        var first = WorldModelReducer.Fold(initial, new ObservationBatch(
            t0.AddSeconds(1), MakePlayer(mapId: 1),
            ImmutableArray.Create(MakeEntity(1, 10, 10, t0.AddSeconds(1)), MakeEntity(2, 20, 20, t0.AddSeconds(1))),
            ImmutableArray<long>.Empty, null));

        var afterPortal = WorldModelReducer.Fold(first, new ObservationBatch(
            t0.AddSeconds(2), null, ImmutableArray<WorldEntity>.Empty, ImmutableArray<long>.Empty, ObservedMapId: 2));
        return afterPortal.Entities.Count == 0 && afterPortal.Player.MapId == 2;
    }

    private static bool TestFoldRejectsTimeRegression()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var initial = WorldStateSnapshot.CreateInitial("S") with { TimestampUtc = t0 };
        try
        {
            WorldModelReducer.Fold(initial, ObservationBatch.Empty(t0.AddSeconds(-1)));
            return false;
        }
        catch (ArgumentException) { return true; }
    }

    // ------------------------------------------------------------------ context slimming

    private static bool TestErrorHistoryCompression()
    {
        var compressor = new ErrorHistoryCompressor(maxErrors: 3, maxMessageChars: 40);

        // Addresses and line numbers are volatile: two occurrences of the same
        // fault must share one signature.
        string sigA = ErrorHistoryCompressor.ComputeSignature("Socket failure 0x1A2B at line 10");
        string sigB = ErrorHistoryCompressor.ComputeSignature("Socket failure 0x9F00 at line 99");
        if (sigA != sigB || sigA.Length != 16) return false;

        var history = Enumerable.Range(1, 10).Select(i => $"transient fault number {i}\nfinal line of fault {i}").ToArray();
        var compressed = compressor.CompressHistory(history);
        if (compressed.Length != 3) return false;
        if (compressed[0].Attempt != 8 || compressed[2].Attempt != 10) return false;
        if (compressed[2].Message != "final line of fault 10") return false;

        var truncated = compressor.CompressError(new string('x', 100));
        return truncated.Message.Length == 40;
    }

    private static bool TestWorldContextSlimming()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var builder = ImmutableDictionary.CreateBuilder<long, WorldEntity>();
        for (int i = 1; i <= 50; i++)
            builder.Add(i, MakeEntity(i, i, 0, t0, name: $"VeryLongEntityName_NumberIs_{i:D3}", currentHp: 50, maxHp: 200));
        var snapshot = WorldStateSnapshot.CreateInitial("S") with
        {
            Player = MakePlayer(position: new Position2D(0, 0)),
            Entities = builder.ToImmutable(),
            IsDegradedState = false,
        };

        var slimmed = WorldContextSlimmer.Slim(snapshot, maxEntities: 8);
        if (slimmed.NearestEntities.Length != 8 || slimmed.TotalEntityCount != 50) return false;
        for (int i = 0; i < 8; i++)
        {
            if (slimmed.NearestEntities[i].EntityId != i + 1) return false;
            if (slimmed.NearestEntities[i].Name.Length > WorldContextSlimmer.MaxEntityNameLength) return false;
        }
        return Math.Abs(slimmed.NearestEntities[0].HpRatio - 0.25f) < 0.001f;
    }

    // ------------------------------------------------------------------ delta sync

    private static bool TestDeltaRoundTripsThroughApply()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var baseEntities = ImmutableDictionary<long, WorldEntity>.Empty
            .Add(1, MakeEntity(1, 10, 10, t0))
            .Add(2, MakeEntity(2, 20, 20, t0))
            .Add(3, MakeEntity(3, 30, 30, t0));
        var baseState = WorldStateSnapshot.CreateInitial("S") with
        {
            FrameIndex = 5, Player = MakePlayer(), Entities = baseEntities,
        };
        var target = baseState with
        {
            FrameIndex = 6,
            Player = baseState.Player with { Position = new Position2D(9, 9), CurrentHp = 777, CurrentMp = 222, IsInCombat = true },
            Entities = baseEntities
                .SetItem(1, baseEntities[1] with { Position = new Position2D(11, 12), CurrentHp = 60 })
                .Remove(2)
                .Add(4, MakeEntity(4, 40, 40, t0)),
        };

        var reconstructed = WorldStateDeltaEngine.ApplyDelta(baseState, WorldStateDeltaEngine.ComputeDelta(baseState, target));
        if (reconstructed.FrameIndex != target.FrameIndex) return false;
        if (reconstructed.Player != target.Player) return false;
        if (!reconstructed.Entities.Keys.Order().SequenceEqual(target.Entities.Keys.Order())) return false;
        foreach (var (id, expected) in target.Entities)
        {
            var actual = reconstructed.Entities[id];
            if (actual.Position != expected.Position || actual.CurrentHp != expected.CurrentHp || actual.IsAlive != expected.IsAlive)
                return false;
        }
        return true;
    }

    private static bool TestBinaryCodecRoundTrip()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var packet = new WorldStateDeltaPacket("S", 5, 6, new Position2D(3, 4), 90, 40, true,
            ImmutableArray.Create(
                new EntityDelta(1, false, new Position2D(7, 8), 55, true, null),
                new EntityDelta(2, true, null, null, null, null),
                new EntityDelta(9, false, new Position2D(40, 40), 100, true, false, MakeEntity(9, 40, 40, t0))));

        byte[] bytes = WorldStateDeltaCodec.Serialize(packet);
        if (!WorldStateDeltaCodec.TryDeserialize(bytes, out var decoded) || decoded is null) return false;
        bool equal = decoded.SessionId == packet.SessionId
            && decoded.BaseFrameIndex == packet.BaseFrameIndex
            && decoded.TargetFrameIndex == packet.TargetFrameIndex
            && decoded.PlayerPosition == packet.PlayerPosition
            && decoded.PlayerHp == packet.PlayerHp
            && decoded.PlayerMp == packet.PlayerMp
            && decoded.PlayerInCombat == packet.PlayerInCombat
            && decoded.MutatedEntities.SequenceEqual(packet.MutatedEntities);
        if (!equal) return false;

        byte[] corrupted = (byte[])bytes.Clone();
        corrupted[0] ^= 0xFF;
        if (WorldStateDeltaCodec.TryDeserialize(corrupted, out _)) return false;

        if (WorldStateDeltaCodec.TryDeserialize(bytes.AsMemory(0, bytes.Length - 3), out _)) return false;

        byte[] padded = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();
        return !WorldStateDeltaCodec.TryDeserialize(padded, out _);
    }

    private static bool TestBinaryDeltaBandwidthSaving()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var builder = ImmutableDictionary.CreateBuilder<long, WorldEntity>();
        for (int i = 1; i <= 100; i++) builder.Add(i, MakeEntity(i, i, i, t0));
        var frame1 = WorldStateSnapshot.CreateInitial("S") with
        {
            FrameIndex = 1, Player = MakePlayer(), Entities = builder.ToImmutable(),
        };
        var frame2 = frame1 with
        {
            FrameIndex = 2,
            Entities = frame1.Entities
                .SetItem(1, frame1.Entities[1] with { Position = new Position2D(99, 99) })
                .SetItem(2, frame1.Entities[2] with { CurrentHp = 42 })
                .SetItem(3, frame1.Entities[3] with { Position = new Position2D(55, 55) }),
        };

        byte[] full = JsonSerializer.SerializeToUtf8Bytes(frame2);
        byte[] compact = WorldStateDeltaCodec.Serialize(WorldStateDeltaEngine.ComputeDelta(frame1, frame2));

        // The >70% bandwidth-saving requirement is measured, never assumed.
        return compact.Length > 0 && (long)compact.Length * 100 <= (long)full.Length * 30;
    }

    private static bool TestDeltaTrackerResync()
    {
        var t0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new DeltaSyncTracker(historyCapacity: 2);
        var snapshot = WorldStateSnapshot.CreateInitial("S") with { FrameIndex = 1, Player = MakePlayer() };
        tracker.TrackFrame(snapshot);
        tracker.RegisterConsumer("phone");

        // Never acknowledged: the first update must be a full resync.
        var initialSync = tracker.ProduceUpdate("phone");
        if (!initialSync.IsFullResync || initialSync.FullSnapshot?.FrameIndex != 1) return false;
        tracker.Acknowledge("phone", 1);

        var frame2 = snapshot with { FrameIndex = 2, TimestampUtc = t0.AddSeconds(1) };
        tracker.TrackFrame(frame2);
        var deltaSync = tracker.ProduceUpdate("phone");
        if (deltaSync.IsFullResync || deltaSync.Delta is null) return false;
        if (deltaSync.Delta.BaseFrameIndex != 1 || deltaSync.Delta.TargetFrameIndex != 2) return false;

        // Two more frames evict frame 1 from the capacity-2 history: the stale
        // consumer must get a resync, not an unreconstructable chain.
        tracker.TrackFrame(snapshot with { FrameIndex = 3, TimestampUtc = t0.AddSeconds(2) });
        tracker.TrackFrame(snapshot with { FrameIndex = 4, TimestampUtc = t0.AddSeconds(3) });
        var evictedSync = tracker.ProduceUpdate("phone");
        return evictedSync.IsFullResync && evictedSync.FullSnapshot?.FrameIndex == 4;
    }

    // ------------------------------------------------------------------ persistence

    private static async Task<bool> TestSessionAndTrajectoryIntegrityAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_store_{Guid.NewGuid():N}.db");
        try
        {
            var basePlayerFrame = WorldStateSnapshot.CreateInitial("SESS") with { Player = MakePlayer() };
            await using (var store = new Gate2SessionStore(new SqliteStoragePolicy(path, batchIntervalMs: 20, maxBatchSize: 50)))
            {
                long sessionRowId = store.OpenSession("SESS", new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc));
                for (int i = 1; i <= 30; i++)
                    store.EnqueueFrame(sessionRowId, basePlayerFrame with { FrameIndex = (ulong)i });

                // CloseSession flushes pending frames synchronously and seals the row.
                store.CloseSession(sessionRowId, new DateTime(2026, 8, 30, 12, 5, 0, DateTimeKind.Utc));
                if (store.PersistedFrameCount != 30 || store.FailedBatchCount != 0) return false;

                using var reader = Gate2Sqlite.OpenAligned(path, busyTimeoutMs: 5000);
                long frames = Gate2Sqlite.ExecuteScalarInt64(reader,
                    $"SELECT COUNT(*) FROM world_frames WHERE session_row_id = {sessionRowId}");
                long sealedFrames = Gate2Sqlite.ExecuteScalarInt64(reader,
                    $"SELECT frames_persisted FROM runtime_sessions WHERE session_row_id = {sessionRowId}");
                string endedUtc = Gate2Sqlite.ExecuteScalarString(reader,
                    $"SELECT ended_utc FROM runtime_sessions WHERE session_row_id = {sessionRowId}");
                if (frames != 30 || sealedFrames != 30 || string.IsNullOrEmpty(endedUtc)) return false;

                // The integrity constraint must reject a trajectory row pointing at a
                // session that does not exist.
                try
                {
                    Gate2Sqlite.Execute(reader, """
                        INSERT INTO world_frames
                            (session_row_id, frame_index, timestamp_utc, map_id, pos_x, pos_y,
                             player_hp, player_mp, in_combat, entity_count, global_confidence, degraded)
                        VALUES (999999, 1, '2026-08-30T12:00:00Z', 1, 0, 0, 1, 1, 0, 0, 1.0, 0)
                        """);
                    return false;
                }
                catch (SqliteException) { /* expected: FOREIGN KEY constraint */ }
            }
            return true;
        }
        finally { CleanupDatabase(path); }
    }

    // ------------------------------------------------------------------ durable event log

    /// <summary>
    /// Drives the real <see cref="Gate2RuntimeEngine"/> composition, closes it, and
    /// reads its store back (M075-M076).
    /// </summary>
    /// <remarks>
    /// The unit tests exercise the logger on its own. This asks the harder
    /// question: does the thing the runtime actually composes leave a log that can
    /// be replayed after the process that wrote it is gone, in the order it was
    /// written, and does it admit what the bus dropped.
    /// </remarks>
    private static async Task<bool> TestDurableEventLogReplaysAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_replay_{Guid.NewGuid():N}.db");
        try
        {
            const int frames = 25;
            await using (var engine = new Gate2RuntimeEngine(path, sessionId: "GATE2_REPLAY"))
            {
                for (int i = 1; i <= frames; i++)
                {
                    int hp = 900 - i;
                    engine.UpdateWorldState(previous => previous with
                    {
                        FrameIndex = (ulong)i,
                        Player = previous.Player with { CurrentHp = hp },
                        IsDegradedState = false
                    });
                }
            }

            var replay = EventLogReader.Read(path, sessionId: "GATE2_REPLAY");

            // Every state update published an audit event, and every one came back.
            if (replay.EventCount != frames) return false;
            if (!replay.IsComplete) return false;

            // In the order they were written: the frames rise, and so do the
            // sequences. Neither timestamp nor frame index could have ordered this
            // on its own, which is why the sequence exists.
            ulong expectedFrame = 1;
            long previousSequence = 0;
            foreach (var record in replay.Records)
            {
                if (record is not EventLogEntry entry) return false;
                if (entry.Event.FrameIndex != expectedFrame++) return false;
                if (entry.Sequence <= previousSequence) return false;
                previousSequence = entry.Sequence;
                if (entry.Event.EventType != "WorldStateUpdated") return false;
            }

            // And a second read of the same store gives the same sequence.
            var again = EventLogReader.Read(path, sessionId: "GATE2_REPLAY");
            if (again.Records.Count != replay.Records.Count) return false;
            for (int i = 0; i < again.Records.Count; i++)
            {
                if (again.Records[i].Sequence != replay.Records[i].Sequence) return false;
            }

            return true;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    // ------------------------------------------------------------------ integrated engine

    private static async Task<bool> TestIntegratedEngineEndToEndAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nosai_gate2_e2e_{Guid.NewGuid():N}.db");
        try
        {
            var t0 = DateTime.UtcNow;
            await using (var engine = new Gate2IntegratedEngine(path, sessionId: "GATE2_E2E"))
            {
                engine.RegisterDeltaConsumer("dashboard");
                for (int i = 1; i <= 3; i++)
                {
                    var snapshot = engine.ObserveFrame(new ObservationBatch(
                        t0.AddSeconds(i), MakePlayer(hp: 800 - i),
                        ImmutableArray.Create(MakeEntity(1, i, i, t0.AddSeconds(i)), MakeEntity(2, 20, 20, t0.AddSeconds(i))),
                        ImmutableArray<long>.Empty, null));
                    if (snapshot.FrameIndex != (ulong)i || snapshot.IsDegradedState) return false;
                }

                var resync = engine.ProduceUpdate("dashboard");
                if (!resync.IsFullResync || resync.FullSnapshot?.FrameIndex != 3) return false;
                engine.AcknowledgeUpdate("dashboard", 3);

                engine.ObserveFrame(new ObservationBatch(
                    t0.AddSeconds(4), null,
                    ImmutableArray.Create(MakeEntity(1, 9, 9, t0.AddSeconds(4))),
                    ImmutableArray<long>.Empty, null));
                var delta = engine.ProduceUpdate("dashboard");
                if (delta.IsFullResync || delta.Delta is null) return false;
                if (delta.Delta.BaseFrameIndex != 3 || delta.Delta.TargetFrameIndex != 4) return false;

                var context = engine.SlimCurrentContext(maxEntities: 1);
                if (context.NearestEntities.Length != 1 || context.TotalEntityCount != 2) return false;
            }

            // After disposal every store is sealed: the session row carries the real
            // frame count and the audit events reached the WAL store.
            using var reader = Gate2Sqlite.OpenAligned(path, busyTimeoutMs: 5000);
            long sealedFrames = Gate2Sqlite.ExecuteScalarInt64(reader,
                "SELECT frames_persisted FROM runtime_sessions WHERE session_id = 'GATE2_E2E'");
            string endedUtc = Gate2Sqlite.ExecuteScalarString(reader,
                "SELECT ended_utc FROM runtime_sessions WHERE session_id = 'GATE2_E2E'");
            long auditEvents = Gate2Sqlite.ExecuteScalarInt64(reader,
                "SELECT COUNT(*) FROM runtime_events WHERE event_type = 'WorldStateUpdated'");
            return sealedFrames == 4 && !string.IsNullOrEmpty(endedUtc) && auditEvents == 4;
        }
        finally { CleanupDatabase(path); }
    }
}
