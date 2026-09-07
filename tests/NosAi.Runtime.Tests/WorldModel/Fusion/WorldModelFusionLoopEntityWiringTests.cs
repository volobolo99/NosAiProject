using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Observability;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-05: runtime wiring of the optional catalogue classifier into
/// <see cref="WorldModelFusionLoop"/>, on top of the network-only wiring
/// AP-01/A4 tests in <c>WorldModelFusionLoopTests.cs</c>, the vision wiring in
/// <c>WorldModelFusionLoopVisualWiringTests.cs</c> and the map wiring in
/// <c>WorldModelFusionLoopMapWiringTests.cs</c> -- a new file rather than an
/// edit to any of them, the same file-ownership convention those three already
/// follow. What the projection itself produces is
/// <c>GameplayObservationEntityProjectionTests</c>'s job; this file only proves
/// the delegate reaches it through a real fusion cycle.
/// </summary>
public sealed class WorldModelFusionLoopEntityWiringTests
{
    private static readonly DateTime T0 = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static ClientBaselineSnapshot AttachedClient() => new(
        ProcessDetected: true,
        WindowDetected: true,
        ClientAttached: true,
        ProcessId: 4242,
        WindowHandle: (nint)0xABC,
        Source: "live_process_attach",
        ObservedAtUtc: T0,
        Availability: ClientBaselineAvailability.BaselineReady,
        Status: "attached_os_session",
        Warning: null,
        FailureReason: null,
        ProcessName: "NostaleClientX",
        WindowTitle: "NosTale",
        ProcessResponding: true,
        WindowVisible: true);

    private static Gate1CanonicalSnapshot Snapshot(GameplayObservation gameplay) =>
        Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            AttachedClient(),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: gameplay);

    /// <summary>Monster 313816, vnum 36, at 198/310 HP -- this repository's own capture.</summary>
    private static GameplayObservation WithMonsterInView() =>
        GameplayObservation.Unobserved("only_entities_under_test", T0) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
                new[] { new SelectableEntity(313816, new MapPoint(109, 63), 198d / 310d, T0, Vnum: 36) },
                T0)
        };

    [Fact]
    public void NoClassifier_FusesASnapshotWithNoMobs()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(WithMonsterInView()), new NullRuntimeLogger());

        WorldModelSnapshot fused = loop.RunOnce(Snapshot(WithMonsterInView()), T0);

        Assert.Empty(fused.Mobs);
        Assert.Empty(fused.Npcs);
    }

    [Fact]
    public void AClassifier_ReachesTheProjection_AndTheFusedSnapshotHoldsARealMob()
    {
        var loop = new WorldModelFusionLoop(
            () => Snapshot(WithMonsterInView()),
            new NullRuntimeLogger(),
            classifyVnum: _ => CatalogueClass.Monster);

        WorldModelSnapshot fused = loop.RunOnce(Snapshot(WithMonsterInView()), T0);

        Mob mob = Assert.Single(fused.Mobs);
        Assert.Equal("mob-313816", mob.Id.Value);
        Assert.Equal(new WorldPosition(109, 63), mob.Position.Value);

        Resource health = Assert.Single(mob.Status.Resources);
        Assert.Equal(198d / 310d, health.Fraction.Value, precision: 10);
    }

    /// <summary>
    /// The catalogue is asked about a vnum, not about an entity id: the same
    /// vnum seen twice must not be looked up twice, which is what
    /// <see cref="CatalogueClassifier"/> exists to guarantee downstream.
    /// </summary>
    [Fact]
    public void TheClassifierIsAskedOncePerSightedEntity_WithItsVnum()
    {
        var asked = new List<int>();
        var loop = new WorldModelFusionLoop(
            () => Snapshot(WithMonsterInView()),
            new NullRuntimeLogger(),
            classifyVnum: vnum => { asked.Add(vnum); return CatalogueClass.Monster; });

        loop.RunOnce(Snapshot(WithMonsterInView()), T0);

        Assert.Equal(new[] { 36 }, asked);
    }

    /// <summary>
    /// A second cycle over the same entity keeps it a mob and lets the temporal
    /// enricher see the same <see cref="EntityId"/> across cycles -- the id must
    /// be stable, or velocity could never be derived for it.
    /// </summary>
    [Fact]
    public void TheSameEntityKeepsItsIdAcrossCycles()
    {
        var loop = new WorldModelFusionLoop(
            () => Snapshot(WithMonsterInView()),
            new NullRuntimeLogger(),
            classifyVnum: _ => CatalogueClass.Monster);

        WorldModelSnapshot first = loop.RunOnce(Snapshot(WithMonsterInView()), T0);
        WorldModelSnapshot second = loop.RunOnce(Snapshot(WithMonsterInView()), T0.AddSeconds(1));

        Assert.Equal(Assert.Single(first.Mobs).Id, Assert.Single(second.Mobs).Id);
    }
}
