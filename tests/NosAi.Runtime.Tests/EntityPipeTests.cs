using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// C1-1: the entity pipe. Sightings reach <c>Gate3WorldState.Entities</c> as
/// selectable entities with an instant each, and the character's own position
/// reaches <c>PlayerPosition</c> as a classified value that stays UNKNOWN, with
/// its reason, until a reader is bound.
/// </summary>
/// <remarks>
/// <para>
/// Two knots this file pins. <see cref="EntitySighting"/> had no observation
/// instant and <see cref="SelectableEntity"/> requires one, because
/// <see cref="TargetSelector"/> compares it with a sighting age. The instant
/// comes from the packet that stated the position — never from the poll's clock,
/// which would make a replayed recording look current (ADR-0016) — and for a
/// sighting merged from two packets each half keeps the instant of its own.
/// </para>
/// <para>
/// The position comes from the client's memory and no reader is bound to the
/// running host. It is UNKNOWN with its own reason: not the map origin, not the
/// last known square, and the selector refuses on it by name rather than
/// measuring distances from (0, 0).
/// </para>
/// </remarks>
public sealed class EntityPipeTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);
    private const string Stat = "stat 7305 7305 1420 1420 0 1184";

    /// <summary>A spawn as the capture recorded it: vnum 36, at (109, 63), full health.</summary>
    private const string Spawn = "in 3 36 313826 109 63 2 100 100 0 0 0 -1 1 0 -1 - 0 -1 0 0 0 0 0 0 0 0 0 0 0 0 0";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    // ------------------------------------------------- the instant, at the decoder

    [Fact]
    public void A_spawn_carries_both_instants_and_the_vnum()
    {
        EntitySighting sighting = Assert.Single(
            new NosTaleWorldProtocolDecoder().Decode(Packet(Spawn, At)).Sightings);

        Assert.Equal(At, sighting.PositionObservedAtUtc);
        Assert.Equal(At, sighting.HpObservedAtUtc);
        Assert.Equal(36, sighting.Vnum);
        Assert.Equal(1.0, sighting.HpRatio);
        Assert.Equal(DataSourceKind.Live, sighting.Source);
    }

    /// <summary>
    /// The move is fresh, the health is remembered, and the sighting says so twice:
    /// CACHED as a label, and an older instant on the half that is older.
    /// </summary>
    [Fact]
    public void A_move_after_a_spawn_keeps_the_spawns_health_instant_and_vnum()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Spawn, At));

        EntitySighting moved = Assert.Single(
            decoder.Decode(Packet("mv 3 313826 110 64 5", At.AddSeconds(3))).Sightings);

        Assert.Equal(At.AddSeconds(3), moved.PositionObservedAtUtc);
        Assert.Equal(At, moved.HpObservedAtUtc);
        Assert.Equal(36, moved.Vnum);
        Assert.Equal(DataSourceKind.Cached, moved.Source);
    }

    /// <summary>The mirror case: fresh health on a remembered position.</summary>
    [Fact]
    public void A_vitals_update_keeps_the_positions_older_instant()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Spawn, At));

        EntitySighting updated = Assert.Single(
            decoder.Decode(Packet("st 3 313826 8 0 66 100 198 52 310 52 0", At.AddSeconds(3))).Sightings);

        Assert.Equal(At, updated.PositionObservedAtUtc);
        Assert.Equal(At.AddSeconds(3), updated.HpObservedAtUtc);
        Assert.Equal(36, updated.Vnum);
        Assert.Equal(198.0 / 310.0, updated.HpRatio!.Value, 9);
    }

    [Fact]
    public void A_move_of_an_unseen_entity_has_no_health_instant_and_no_vnum()
    {
        EntitySighting moved = Assert.Single(
            new NosTaleWorldProtocolDecoder().Decode(Packet("mv 3 3194 121 110 5", At)).Sightings);

        Assert.Null(moved.HpRatio);
        Assert.Null(moved.HpObservedAtUtc);
        Assert.Null(moved.Vnum);
        Assert.Equal(At, moved.PositionObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, moved.Source);
    }

    [Theory]
    [InlineData("in 3 x 313826 109 63 2 100 100")]
    [InlineData("in 3 0 313826 109 63 2 100 100")]
    [InlineData("in 3 -36 313826 109 63 2 100 100")]
    public void A_spawn_with_a_malformed_vnum_is_refused_whole(string line)
    {
        Assert.True(new NosTaleWorldProtocolDecoder().Decode(Packet(line)).IsEmpty);
    }

    // ------------------------------------------------- the pipe, at the provider

    [Fact]
    public void Entities_reach_the_observation_as_selectable_entities_with_the_positions_instant()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);
        wire.Send("mv 3 3194 121 110 5", At.AddSeconds(2));

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.Entities.HasValue);
        Assert.Equal(DataSourceKind.Live, observation.Entities.Source);
        Assert.Equal(At.AddSeconds(2), observation.Entities.ObservedAtUtc);

        SelectableEntity spawned = Assert.Single(observation.Entities.Value, e => e.EntityId == 313826);
        Assert.Equal(new MapPoint(109, 63), spawned.At);
        Assert.Equal(At, spawned.ObservedAtUtc);
        Assert.Equal(1.0, spawned.HpRatio);
        Assert.Equal(36, spawned.Vnum);

        SelectableEntity moved = Assert.Single(observation.Entities.Value, e => e.EntityId == 3194);
        Assert.Equal(new MapPoint(121, 110), moved.At);
        Assert.Equal(At.AddSeconds(2), moved.ObservedAtUtc);
    }

    /// <summary>
    /// The definition of done, verbatim: a sighting without a life does not become
    /// a full life and does not become zero. It stays null, and the selector still
    /// takes it, because unknown health is not death.
    /// </summary>
    [Fact]
    public void A_sighting_without_a_life_does_not_become_a_full_life_nor_a_zero()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send("mv 3 3194 121 110 5", At);

        GameplayObservation observation = provider.Observe();

        SelectableEntity entity = Assert.Single(observation.Entities.Value);
        Assert.Null(entity.HpRatio);
        Assert.NotEqual(1.0, entity.HpRatio);
        Assert.NotEqual(0.0, entity.HpRatio);

        bool selected = TargetSelector.TrySelect(
            observation.Entities.Value,
            ClassifiedValue<MapPoint>.Live(new MapPoint(120, 110), At),
            At,
            TargetSelectionPolicy.Default,
            out TargetChoice? choice,
            out string reason);

        Assert.True(selected, reason);
        Assert.Equal(3194, choice!.Entity.EntityId);
        Assert.Null(choice.Entity.HpRatio);
    }

    [Fact]
    public void A_dead_entity_leaves_the_table()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);
        wire.Send("die 3 313826 3 313826", At.AddSeconds(1));

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.Entities.HasValue);
        Assert.Equal("no_entity_retained", observation.Entities.FailureReason);
    }

    [Fact]
    public void Before_any_sighting_the_list_says_nothing_was_observed_yet()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);

        Assert.Equal("no_entities_observed_yet", provider.Observe().Entities.FailureReason);
    }

    /// <summary>
    /// The vitals' rule, applied to the table: what this poll stated keeps its
    /// provenance, what is remembered is CACHED with the instant it was really
    /// observed, and what the wire has not mentioned within the retention bound is
    /// forgotten — which is a statement about the table, not about the map.
    /// </summary>
    [Fact]
    public void An_entity_is_remembered_across_polls_as_cached_and_forgotten_past_retention()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);
        GameplayObservation first = provider.Observe();
        Assert.Equal(DataSourceKind.Live, first.Entities.Source);

        clock.Advance(TimeSpan.FromSeconds(1));
        GameplayObservation second = provider.Observe();
        Assert.True(second.Entities.HasValue);
        Assert.Equal(DataSourceKind.Cached, second.Entities.Source);
        Assert.Equal(At, Assert.Single(second.Entities.Value).ObservedAtUtc);

        clock.Advance(provider.MaxEntityRetention + TimeSpan.FromSeconds(1));
        GameplayObservation third = provider.Observe();
        Assert.False(third.Entities.HasValue);
        Assert.Equal("no_entity_retained", third.Entities.FailureReason);
    }

    /// <summary>A list is never stronger than its weakest member.</summary>
    [Fact]
    public void The_entity_list_is_as_weak_as_its_weakest_member()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);
        // A move of the spawned entity merges its remembered health: CACHED at the decoder.
        wire.Send("mv 3 313826 110 64 5", At.AddSeconds(1));

        GameplayObservation observation = provider.Observe();

        Assert.Equal(DataSourceKind.Cached, observation.Entities.Source);
    }

    /// <summary>
    /// A decoder that does not stamp its sightings cannot feed the selector: an
    /// instant invented at the poll would make its positions look current. The
    /// sighting is left out, and the list says so instead of silently shrinking.
    /// </summary>
    [Fact]
    public void A_sighting_without_an_instant_is_not_published_and_the_list_says_so()
    {
        var wire = new ScriptedSource(() => At);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new UnstampedDecoder()));
        var provider = new NetworkGameplayProvider(feed, new StubClock());
        wire.Send("anything");

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.Entities.HasValue);
        Assert.Contains("no observation instant", observation.Entities.Warning, StringComparison.Ordinal);
    }

    // ------------------------------------------------- the position

    /// <summary>The definition of done, verbatim: an unknown position does not become a coordinate.</summary>
    [Fact]
    public async Task PlayerPosition_stays_unknown_with_its_reason_and_never_becomes_a_coordinate()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);

        GameplayObservation observation = provider.Observe();
        Assert.False(observation.PlayerPosition.HasValue);
        Assert.Equal(NetworkGameplayProvider.PlayerPositionNotOnWireReason, observation.PlayerPosition.FailureReason);

        wire.Send(Spawn, At);
        Gate3WorldState state = await new GameplayProviderWorldStateSource(provider).ReadAsync();
        ClassifiedValue<MapPoint> position = Assert.IsType<ClassifiedValue<MapPoint>>(state.PlayerPosition);
        Assert.False(position.HasValue);
        Assert.Equal(NetworkGameplayProvider.PlayerPositionNotOnWireReason, position.FailureReason);
        Assert.NotNull(state.Entities);

        bool selected = TargetSelector.TrySelect(
            state.Entities!, position, At, TargetSelectionPolicy.Default, out TargetChoice? choice, out string reason);

        Assert.False(selected);
        Assert.Null(choice);
        Assert.Equal(
            $"{TargetSelector.PlayerPositionUnknownReason}:{NetworkGameplayProvider.PlayerPositionNotOnWireReason}",
            reason);
    }

    [Fact]
    public async Task The_position_decorator_fills_the_gap_with_the_readers_classification()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider inner) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);
        var reader = new FixedPositionReader(() => ClassifiedValue<MapPoint>.Live(new MapPoint(108, 63), At));
        var provider = new PositionAwareGameplayProvider(inner, reader);

        Gate3WorldState state = await new GameplayProviderWorldStateSource(provider).ReadAsync();

        Assert.Equal("network_observation+player-position", provider.Name);
        Assert.True(state.PlayerPosition!.HasValue);
        Assert.Equal(new MapPoint(108, 63), state.PlayerPosition.Value);
        Assert.Equal(DataSourceKind.Live, state.PlayerPosition.Source);

        Assert.True(TargetSelector.TrySelect(
            state.Entities!, state.PlayerPosition, At, TargetSelectionPolicy.Default,
            out TargetChoice? choice, out string reason), reason);
        Assert.Equal(313826, choice!.Entity.EntityId);
        Assert.Equal(1.0, choice.DistanceTiles, 6);
    }

    [Fact]
    public void The_position_decorator_keeps_the_readers_refusal()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider inner) = Chain();
        wire.Send(Stat);
        var reader = new FixedPositionReader(
            () => ClassifiedValue<MapPoint>.Unknown("character_id_mismatch:1_not_3443217"));

        GameplayObservation observation = new PositionAwareGameplayProvider(inner, reader).Observe();

        Assert.False(observation.PlayerPosition.HasValue);
        Assert.Equal("character_id_mismatch:1_not_3443217", observation.PlayerPosition.FailureReason);
        Assert.True(observation.HasVitals);
    }

    /// <summary>A broken pointer chain costs the position, not the vitals.</summary>
    [Fact]
    public void A_throwing_reader_costs_the_position_not_the_vitals()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider inner) = Chain();
        wire.Send(Stat);
        var reader = new FixedPositionReader(() => throw new InvalidOperationException("chain broke"));

        GameplayObservation observation = new PositionAwareGameplayProvider(inner, reader).Observe();

        Assert.True(observation.HasVitals);
        Assert.False(observation.PlayerPosition.HasValue);
        Assert.Equal("player_position_reader_failed:InvalidOperationException", observation.PlayerPosition.FailureReason);
    }

    [Fact]
    public void An_established_position_is_not_overridden_by_the_decorator()
    {
        GameplayObservation established = Vitals() with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Derived(new MapPoint(1, 1), At),
        };
        var reader = new FixedPositionReader(() => ClassifiedValue<MapPoint>.Live(new MapPoint(50, 50), At));

        GameplayObservation observation = new PositionAwareGameplayProvider(new FixedProvider(established), reader).Observe();

        Assert.Equal(new MapPoint(1, 1), observation.PlayerPosition.Value);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public void The_position_ages_the_state_and_a_simulated_one_blocks_acting()
    {
        DateTime now = At.AddSeconds(10);
        Gate3WorldState stale = Gate3WorldState.Live(7000, 7305, 1400, false, false, now) with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(5, 5), now.AddSeconds(-5)),
        };
        Gate3WorldState simulated = Gate3WorldState.Live(7000, 7305, 1400, false, false, now) with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Simulated(new MapPoint(5, 5), now),
        };

        Assert.Equal(now.AddSeconds(-5), stale.ObservedAtUtc);
        Assert.False(stale.IsActionable(now, TimeSpan.FromSeconds(2)));
        Assert.True(simulated.IsSimulated);
    }

    // ------------------------------------------------- both sources fill the state

    [Fact]
    public async Task Both_world_state_sources_populate_entities_and_position()
    {
        var entities = new List<SelectableEntity> { new(313826, new MapPoint(109, 63), 1.0, At, 36) };
        GameplayObservation observation = Vitals() with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(entities, At),
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(108, 63), At),
        };

        Gate3WorldState fromProvider = await new GameplayProviderWorldStateSource(new FixedProvider(observation)).ReadAsync();
        Gate3WorldState fromSnapshot = await new Gate1SnapshotWorldStateSource(() => Snapshot(observation)).ReadAsync();

        foreach (Gate3WorldState state in new[] { fromProvider, fromSnapshot })
        {
            SelectableEntity entity = Assert.Single(state.Entities!);
            Assert.Equal(313826, entity.EntityId);
            Assert.Equal(36, entity.Vnum);
            Assert.Equal(At, entity.ObservedAtUtc);
            Assert.Equal(new MapPoint(108, 63), state.PlayerPosition!.Value);
        }
    }

    [Fact]
    public async Task An_unknown_entity_list_reaches_the_state_as_null_and_the_reason_stays_on_the_observation()
    {
        GameplayObservation observation = Vitals();

        Gate3WorldState state = await new GameplayProviderWorldStateSource(new FixedProvider(observation)).ReadAsync();

        Assert.Null(state.Entities);
        Assert.Equal(GameplayObservation.NotPublishedReason, observation.Entities.FailureReason);
        Assert.Equal(GameplayObservation.PlayerPositionNotReadReason, state.PlayerPosition!.FailureReason);
    }

    // ------------------------------------------------- the wire

    [Fact]
    public void The_wire_publishes_entities_position_and_aggressor_additively()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(Spawn, At);

        JsonElement json = JsonDocument.Parse(JsonSerializer.Serialize(provider.Observe().ToWire())).RootElement;

        foreach (string existing in new[] { "hp", "maxHp", "mp", "maxMp", "hasTarget", "inCombat", "entitiesInView", "observedAtUtc" })
            Assert.True(json.TryGetProperty(existing, out _), existing);

        JsonElement entities = json.GetProperty("entities");
        Assert.Equal("LIVE", entities.GetProperty("source").GetString());
        JsonElement entity = Assert.Single(entities.GetProperty("value").EnumerateArray());
        Assert.Equal(313826, entity.GetProperty("entityId").GetInt64());
        Assert.Equal(109, entity.GetProperty("x").GetInt32());
        Assert.Equal(63, entity.GetProperty("y").GetInt32());
        Assert.Equal(36, entity.GetProperty("vnum").GetInt32());
        Assert.Equal(1.0, entity.GetProperty("hpRatio").GetDouble());
        Assert.Equal("2026-09-01T10:00:00.0000000Z", entity.GetProperty("observedAtUtc").GetString());

        JsonElement position = json.GetProperty("playerPosition");
        Assert.Equal("UNKNOWN", position.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, position.GetProperty("value").ValueKind);
        Assert.Equal(NetworkGameplayProvider.PlayerPositionNotOnWireReason, position.GetProperty("failureReason").GetString());

        JsonElement mapId = json.GetProperty("mapId");
        Assert.Equal("UNKNOWN", mapId.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, mapId.GetProperty("value").ValueKind);
        Assert.Equal(NetworkGameplayProvider.MapIdNotOnWireReason, mapId.GetProperty("failureReason").GetString());

        JsonElement standing = json.GetProperty("standingCell");
        Assert.Equal("UNKNOWN", standing.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, standing.GetProperty("value").ValueKind);
        Assert.Equal(NetworkGameplayProvider.StandingCellNotOnWireReason, standing.GetProperty("failureReason").GetString());

        Assert.Equal("UNKNOWN", json.GetProperty("hitBy").GetProperty("source").GetString());
    }

    // ------------------------------------------------- harness

    private static GameplayObservation Vitals() => new(
        Hp: ClassifiedValue<int>.Live(7305, At),
        MaxHp: ClassifiedValue<int>.Live(7305, At),
        Mp: ClassifiedValue<int>.Live(1420, At),
        MaxMp: ClassifiedValue<int>.Live(1420, At),
        HasTarget: ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
        InCombat: ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
        EntitiesInView: ClassifiedValue<int>.Unknown("no_entities_reported"),
        ObservedAtUtc: At);

    private static Gate1CanonicalSnapshot Snapshot(GameplayObservation gameplay) =>
        Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            new ClientBaselineSnapshot(
                ProcessDetected: true,
                WindowDetected: true,
                ClientAttached: true,
                ProcessId: 4242,
                WindowHandle: (nint)0xABC,
                Source: "live_process_attach",
                ObservedAtUtc: At,
                Availability: ClientBaselineAvailability.BaselineReady,
                Status: "attached_os_session",
                Warning: null,
                FailureReason: null,
                ProcessName: "NostaleClientX",
                WindowTitle: "NosTale",
                ProcessResponding: true,
                WindowVisible: true),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: gameplay);

    private static (ScriptedSource Wire, StubClock Clock, NetworkGameplayProvider Provider) Chain()
    {
        var clock = new StubClock();
        var wire = new ScriptedSource(() => clock.GetUtcNow().UtcDateTime);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        return (wire, clock, new NetworkGameplayProvider(feed, clock));
    }

    private sealed class StubClock : TimeProvider
    {
        private DateTimeOffset _now = At;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        private readonly Func<DateTime> _clock;
        public ScriptedSource(Func<DateTime> clock) => _clock = clock;
        public DataSourceKind Source => DataSourceKind.Live;

        public void Send(string line, DateTime? capturedUtc = null)
            => _packets.Enqueue(Packet(line, capturedUtc ?? _clock()));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    private sealed class FixedProvider : IGameplayProvider
    {
        private readonly GameplayObservation _observation;
        public FixedProvider(GameplayObservation observation) => _observation = observation;
        public string Name => "fixed";
        public GameplayObservation Observe() => _observation;
    }

    private sealed class FixedPositionReader : IPlayerPositionProvider
    {
        private readonly Func<ClassifiedValue<MapPoint>> _read;
        public FixedPositionReader(Func<ClassifiedValue<MapPoint>> read) => _read = read;
        public int Reads { get; private set; }

        public ClassifiedValue<MapPoint> ReadPosition()
        {
            Reads++;
            return _read();
        }
    }

    /// <summary>A decoder that sights an entity and does not say when.</summary>
    private sealed class UnstampedDecoder : IGamePacketDecoder
    {
        public string ProtocolName => "unstamped";
        public bool ReadsPlayerVitals => false;
        public bool CanDecode(ObservedPacket packet) => true;

        public DecodedObservations Decode(ObservedPacket packet) => new(
            ImmutableArray.Create(new EntitySighting(77, "Monster", 10, 10, null, DataSourceKind.Live)),
            ImmutableArray<GameEvent>.Empty);
    }
}
