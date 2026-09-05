using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Perception.Fusion;
using NosAi.Runtime.WorldModel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-01 / A4: existing snapshots reach the World Model without inventing values
/// and without acquiring execution authority.
/// </summary>
public sealed class WorldModelRuntimeWiringTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc);

    private static ClassifiedValue<T> Live<T>(T value, DateTime? at = null) =>
        ClassifiedValue<T>.Live(value, at ?? T0);

    private static ClassifiedValue<T> Simulated<T>(T value, DateTime? at = null) =>
        ClassifiedValue<T>.Simulated(value, at ?? T0);

    private static ClassifiedValue<T> Unknown<T>(string reason) =>
        ClassifiedValue<T>.Unknown(reason);

    private static GameplayObservation NetworkVitals(
        int hp,
        int maxHp,
        int mp,
        IReadOnlyList<SelectableEntity>? entities = null,
        bool? hasTarget = null,
        int? mapId = null)
    {
        return new GameplayObservation(
            Live(hp),
            Live(maxHp),
            Live(mp),
            Live(1420),
            hasTarget is { } target ? Live(target) : Unknown<bool>("target_flag_not_mapped"),
            Unknown<bool>("combat_flag_not_mapped"),
            Live(entities?.Count ?? 0),
            T0)
        {
            Entities = entities is null
                ? Live<IReadOnlyList<SelectableEntity>>(Array.Empty<SelectableEntity>())
                : Live(entities),
            MapId = mapId is { } id
                ? Live(id)
                : Unknown<int>(NetworkGameplayProvider.MapIdNotOnWireReason),
            StandingCell = Unknown<MapPoint>(NetworkGameplayProvider.StandingCellNotOnWireReason)
        };
    }

    private static ClientBaselineSnapshot Client(bool attached, string? failure = null) => new(
        ProcessDetected: attached,
        WindowDetected: attached,
        ClientAttached: attached,
        ProcessId: attached ? 4242 : null,
        WindowHandle: attached ? (nint)0xABC : IntPtr.Zero,
        Source: "live_process_attach",
        ObservedAtUtc: T0,
        Availability: attached ? ClientBaselineAvailability.BaselineReady : ClientBaselineAvailability.Unavailable,
        Status: attached ? "attached_os_session" : "client_unavailable",
        Warning: null,
        FailureReason: attached ? null : failure ?? "process_not_attached",
        ProcessName: attached ? "NostaleClientX" : null,
        WindowTitle: attached ? "NosTale" : null,
        ProcessResponding: attached,
        WindowVisible: attached);

    private static Gate1CanonicalSnapshot Snapshot(ClientBaselineSnapshot client, GameplayObservation? gameplay)
    {
        Gate1CanonicalSnapshot snapshot = Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "ap01-a4",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            client,
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: gameplay);
        return snapshot with { CapturedAtUtc = T0 };
    }

    private static WorldModelWorldStateSource Source(
        Gate1CanonicalSnapshot snapshot,
        IWorldModel? coarse = null,
        Func<MapWorldObservation>? mapWorld = null)
    {
        var classified = new ClassifiedWorldModel();
        return new WorldModelWorldStateSource(() => snapshot, classified, coarse, mapWorld);
    }

    [Fact]
    public async Task Detached_client_stays_unknown_and_does_not_write_the_coarse_model()
    {
        var coarse = new NosAi.Runtime.WorldModel.WorldModel();
        WorldState before = coarse.Current;
        var source = Source(Snapshot(Client(attached: false), NetworkVitals(100, 100, 50)), coarse);

        Gate3WorldState planning = await source.ReadAsync();

        Assert.False(planning.HasVitals);
        Assert.Equal(WorldModelWorldStateSource.ClientNotAttachedReason, planning.Hp.FailureReason);
        Assert.False(source.Classified.CoarsePublished);
        Assert.Equal(before.Tick, coarse.Current.Tick);
        Assert.True(before.PlayerAlive);
        Assert.Equal(1.0, before.PlayerHpRatio);
        Assert.Equal(before.Tick, coarse.Current.Tick);
        Assert.Equal(before.PlayerHpRatio, coarse.Current.PlayerHpRatio);
    }

    [Fact]
    public async Task Missing_gameplay_provider_stays_unknown_with_the_snapshot_reason()
    {
        var source = Source(Snapshot(Client(attached: true), gameplay: null));

        Gate3WorldState planning = await source.ReadAsync();

        Assert.False(planning.HasVitals);
        Assert.Equal("gameplay_provider_not_available", planning.Hp.FailureReason);
        Assert.False(source.Classified.CoarsePublished);
        Assert.Null(source.Classified.LastFusion);
    }

    [Fact]
    public async Task Live_vitals_reach_planning_state_and_the_coarse_model()
    {
        var coarse = new NosAi.Runtime.WorldModel.WorldModel();
        var entities = new[] { new SelectableEntity(7, new MapPoint(10, 4), 0.5, T0, Vnum: 3) };
        var source = Source(Snapshot(Client(attached: true), NetworkVitals(7200, 7305, 1400, entities)), coarse);

        Gate3WorldState planning = await source.ReadAsync();

        Assert.True(planning.HasVitals);
        Assert.Equal(7200, planning.Hp.Value);
        Assert.Equal(7305, planning.MaxHp.Value);
        Assert.Equal(1400, planning.Mp.Value);
        Assert.Equal(DataSourceKind.Live, planning.Hp.Source);
        Assert.False(planning.HasTarget.HasValue);
        Assert.True(source.Classified.CoarsePublished);
        Assert.Null(source.Classified.CoarseRefusalReason);
        Assert.Equal(7200 / 7305.0, coarse.Current.PlayerHpRatio);
        Assert.True(coarse.Current.PlayerAlive);
        Assert.Single(coarse.Current.Entities);
        Assert.Equal("7", coarse.Current.Entities[0].Id);
        Assert.NotNull(source.Classified.LastFusion);
    }

    [Fact]
    public async Task Unknown_hp_is_not_written_as_zero_into_the_coarse_model()
    {
        var coarse = new NosAi.Runtime.WorldModel.WorldModel();
        WorldState before = coarse.Current;
        var unknownHp = NetworkVitals(1, 1, 1) with
        {
            Hp = Unknown<int>("hp_not_on_wire"),
            MaxHp = Unknown<int>("max_hp_not_on_wire")
        };
        var source = Source(Snapshot(Client(attached: true), unknownHp), coarse);

        Gate3WorldState planning = await source.ReadAsync();

        Assert.False(planning.Hp.HasValue);
        Assert.Equal("hp_not_on_wire", planning.Hp.FailureReason);
        Assert.False(source.Classified.CoarsePublished);
        Assert.Equal(before.PlayerHpRatio, coarse.Current.PlayerHpRatio);
        Assert.Equal(before.Tick, coarse.Current.Tick);
        Assert.NotEqual(0.0, coarse.Current.PlayerHpRatio);
    }

    [Fact]
    public async Task Simulated_vitals_may_be_planned_and_never_update_the_coarse_model()
    {
        var coarse = new NosAi.Runtime.WorldModel.WorldModel();
        WorldState before = coarse.Current;
        var simulated = new GameplayObservation(
            Simulated(10),
            Simulated(100),
            Simulated(20),
            Simulated(50),
            Unknown<bool>("target_flag_not_mapped"),
            Unknown<bool>("combat_flag_not_mapped"),
            Live(0),
            T0)
        {
            Entities = Live<IReadOnlyList<SelectableEntity>>(Array.Empty<SelectableEntity>())
        };
        var source = Source(Snapshot(Client(attached: true), simulated), coarse);

        Gate3WorldState planning = await source.ReadAsync();

        Assert.True(planning.HasVitals);
        Assert.True(planning.IsSimulated);
        Assert.False(source.Classified.CoarsePublished);
        Assert.Equal(SensorFusionWorldAdapter.SimulatedNotProjectedReason, source.Classified.CoarseRefusalReason);
        Assert.Equal(before.Tick, coarse.Current.Tick);
    }

    [Fact]
    public async Task Memory_map_id_is_fused_without_overwriting_unknown_network_map()
    {
        var map = new MapWorldObservation(Live(42), Live(new MapPoint(8, 3)));
        var source = Source(
            Snapshot(Client(attached: true), NetworkVitals(100, 100, 40)),
            mapWorld: () => map);

        Gate3WorldState planning = await source.ReadAsync();
        FusedWorldObservation? fused = source.Classified.LastFusion;

        Assert.True(planning.HasVitals);
        Assert.NotNull(fused);
        Assert.True(fused!.MapId.HasValue);
        Assert.Equal(42, fused.MapId.Value.Value);
        Assert.Equal(SensorKind.Memory, fused.MapId.Winner);
        Assert.True(fused.StandingCell.HasValue);
        Assert.Equal(new MapPoint(8, 3), fused.StandingCell.Value.Value);
    }

    [Fact]
    public async Task Composed_has_target_is_not_established_by_the_network_label()
    {
        var source = Source(
            Snapshot(Client(attached: true), NetworkVitals(100, 100, 40, hasTarget: true)));

        Gate3WorldState planning = await source.ReadAsync();
        FusedWorldObservation? fused = source.Classified.LastFusion;

        Assert.True(planning.HasTarget.HasValue);
        Assert.True(planning.HasTarget.Value);
        Assert.NotNull(fused);
        Assert.Equal(SensorKind.Memory, fused!.HasTarget.Winner);
        Assert.NotEqual(SensorKind.Network, fused.HasTarget.Winner);
    }

    [Fact]
    public async Task Snapshot_throw_is_unknown_not_a_fabricated_world()
    {
        var classified = new ClassifiedWorldModel();
        var source = new WorldModelWorldStateSource(
            () => throw new InvalidOperationException("boom"),
            classified);

        Gate3WorldState planning = await source.ReadAsync();

        Assert.StartsWith(WorldModelWorldStateSource.SnapshotFailedPrefix, planning.Hp.FailureReason);
        Assert.False(classified.CoarsePublished);
    }

    [Fact]
    public async Task FromFused_unknown_entities_refuse_the_coarse_projection()
    {
        var coarse = new NosAi.Runtime.WorldModel.WorldModel();
        WorldState before = coarse.Current;
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(
                NetworkVitals(500, 500, 80) with
                {
                    Entities = Unknown<IReadOnlyList<SelectableEntity>>("entities_unread")
                },
                memory: null,
                screen: null,
                local: null),
            T0);

        var source = WorldModelWorldStateSource.FromFused(() => fused, new ClassifiedWorldModel(), coarse);
        Gate3WorldState planning = await source.ReadAsync();

        Assert.True(planning.HasVitals);
        Assert.Equal(500, planning.Hp.Value);
        Assert.False(source.Classified.CoarsePublished);
        Assert.Equal(before.Tick, coarse.Current.Tick);
    }

    [Fact]
    public async Task Wiring_does_not_enable_execution()
    {
        var source = Source(Snapshot(Client(attached: true), NetworkVitals(100, 100, 40)));
        Gate3WorldState planning = await source.ReadAsync();

        Assert.True(planning.HasVitals);
        Assert.False(NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault.LiveInputEnabled);
        Assert.False(NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault.PacketInjectionEnabled);
    }
}
