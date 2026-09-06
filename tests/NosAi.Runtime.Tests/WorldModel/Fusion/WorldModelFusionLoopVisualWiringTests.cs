using System.Collections.Immutable;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-02/A4: runtime wiring of a vision-side <see cref="VisualObservation"/>
/// into <see cref="WorldModelFusionLoop"/>, on top of the network-only wiring
/// AP-01/A4 already tests in <c>WorldModelFusionLoopTests.cs</c> (deliberately
/// left untouched -- a new file rather than an edit to that one, per this
/// task's file ownership). Every test here drives <see cref="WorldModelFusionLoop.RunOnce"/>
/// directly with a synthetic <c>visualSource</c> delegate; none constructs a
/// real <see cref="ScreenVitalsCapture"/> or touches Windows/DXGI.
/// </summary>
public sealed class WorldModelFusionLoopVisualWiringTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private sealed class RecordingLogger : IRuntimeLogger
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = new();
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Errors.Add((message, exception));
    }

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

    private static Gate1CanonicalSnapshot Snapshot(GameplayObservation? gameplay) =>
        Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            AttachedClient(),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: gameplay);

    /// <summary>A gameplay observation carrying only HP/MP at the given source/instant, position/map otherwise unpublished.</summary>
    private static GameplayObservation VitalsOnly(
        int hp, int maxHp, int mp, int maxMp, DateTime atUtc,
        Func<int, DateTime, ClassifiedValue<int>> classify) => new(
        Hp: classify(hp, atUtc),
        MaxHp: classify(maxHp, atUtc),
        Mp: classify(mp, atUtc),
        MaxMp: classify(maxMp, atUtc),
        HasTarget: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
        InCombat: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
        EntitiesInView: ClassifiedValue<int>.Unknown("not_published_by_provider"),
        ObservedAtUtc: atUtc);

    private static Resource FindResource(WorldModelSnapshot snapshot, ResourceKind kind) =>
        snapshot.Player.Status.Resources.First(r => r.Kind == kind);

    /// <summary>A synthetic vision-side reading: a real frame was acquired, only vitals are populated (no entities, no target state -- exactly what <see cref="ScreenVitalsCapture"/> itself would report before its target-composer follow-up work).</summary>
    private static VisualObservation ScreenVitals(int hp, int maxHp, int mp, int maxMp, DateTime atUtc) => new(
        Frame: new PerceptionResult(
            FrameIndex: 0,
            Source: NosAi.Runtime.Contracts.DataSourceKind.Live,
            FrameAcquired: true,
            Regions: ImmutableArray<RegionOfInterest>.Empty,
            Entities: ImmutableArray<TrackedEntity>.Empty,
            UnavailableReason: null),
        Vitals: new ScreenVitalObservation(
            HpRoi: new PixelRect(0, 0, 10, 10),
            MpRoi: new PixelRect(0, 0, 10, 10),
            HpBar: new ScreenBarFill(ClassifiedValue<double>.Derived(1.0, atUtc), 0.95, null),
            MpBar: new ScreenBarFill(ClassifiedValue<double>.Derived(1.0, atUtc), 0.95, null),
            Hp: new ScreenVitalPair(ClassifiedValue<int>.Derived(hp, atUtc), ClassifiedValue<int>.Derived(maxHp, atUtc), 0.95, null),
            Mp: new ScreenVitalPair(ClassifiedValue<int>.Derived(mp, atUtc), ClassifiedValue<int>.Derived(maxMp, atUtc), 0.95, null),
            HpGlyphs: 3,
            MpGlyphs: 2,
            TrainedGlyphs: 10),
        HasTarget: ClassifiedValue<bool>.Unknown("target_state_composer_not_wired_in_this_pass"),
        ObservedAtUtc: atUtc);

    // -- no visualSource: identical to AP-01/A4's own network-only behavior --

    [Fact]
    public void NoVisualSource_LeavesResourcesExactlyAsTheNetworkChannelProjectedThem()
    {
        GameplayObservation gameplay = VitalsOnly(80, 100, 40, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger());

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Resource health = FindResource(result, ResourceKind.Health);
        Assert.Equal(80d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, health.Current.Source);
        Resource mana = FindResource(result, ResourceKind.Mana);
        Assert.Equal(40d, mana.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, mana.Current.Source);
    }

    [Fact]
    public void NoVisualSource_ProducesTheSameResultAsBeforeThisParameterExisted()
    {
        // Same scenario run through both an old-style construction (no
        // visualSource argument at all) and one that passes visualSource:
        // null explicitly -- the two constructor calls must be indistinguishable.
        GameplayObservation gameplay = VitalsOnly(100, 100, 50, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));

        var implicitDefault = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger());
        var explicitNull = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), visualSource: null);

        WorldModelSnapshot a = implicitDefault.RunOnce(Snapshot(gameplay), T0);
        WorldModelSnapshot b = explicitNull.RunOnce(Snapshot(gameplay), T0);

        Assert.Equal(a.Player.Status.Resources, b.Player.Status.Resources);
    }

    // -- visualSource supplied, known vitals: the result reflects fusion ------

    [Fact]
    public void VisualSource_WithFresherScreenReading_WinsOverAStaleCachedNetworkReading()
    {
        // Exactly the scenario AP-02/A3's own status report calls out: a
        // Cached (stale, re-published) network reading loses to a fresh
        // Derived screen reading -- the concrete case that makes fusing a
        // second channel useful at all.
        DateTime networkAt = T0.AddSeconds(-2);
        GameplayObservation gameplay = VitalsOnly(50, 100, 20, 50, networkAt,
            (v, t) => ClassifiedValue<int>.Cached(v, t));
        VisualObservation visual = ScreenVitals(80, 100, 35, 50, T0);

        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), visualSource: () => visual);
        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Resource health = FindResource(result, ResourceKind.Health);
        Assert.Equal(80d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, health.Current.Source);
        Resource mana = FindResource(result, ResourceKind.Mana);
        Assert.Equal(35d, mana.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, mana.Current.Source);
    }

    [Fact]
    public void VisualSource_LiveNetworkReading_StillBeatsAFreshScreenReading()
    {
        // The wire stays the primary source when both are genuinely fresh:
        // Live outranks Derived regardless of which is more recent.
        GameplayObservation gameplay = VitalsOnly(100, 100, 50, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));
        VisualObservation visual = ScreenVitals(42, 100, 10, 50, T0);

        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), visualSource: () => visual);
        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Resource health = FindResource(result, ResourceKind.Health);
        Assert.Equal(100d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, health.Current.Source);
    }

    [Fact]
    public void VisualSource_IsInvokedExactlyOncePerRunOnceCall()
    {
        int calls = 0;
        VisualObservation Visual()
        {
            calls++;
            return ScreenVitals(70, 100, 30, 50, T0);
        }

        GameplayObservation gameplay = VitalsOnly(60, 100, 20, 50, T0.AddSeconds(-1),
            (v, t) => ClassifiedValue<int>.Cached(v, t));
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), visualSource: Visual);

        loop.RunOnce(Snapshot(gameplay), T0);
        loop.RunOnce(Snapshot(gameplay), T0.AddSeconds(1));

        Assert.Equal(2, calls);
    }

    // -- visualSource throws: the network cycle must never fail -------------

    [Fact]
    public void VisualSource_ThatThrows_NeverFailsRunOnce_AndKeepsTheNetworkResult()
    {
        GameplayObservation gameplay = VitalsOnly(90, 100, 45, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));
        var logger = new RecordingLogger();
        var loop = new WorldModelFusionLoop(
            () => Snapshot(gameplay),
            logger,
            visualSource: () => throw new InvalidOperationException("capture blew up"));

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Resource health = FindResource(result, ResourceKind.Health);
        Assert.Equal(90d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, health.Current.Source);
        Assert.Equal(1, result.Version);
    }

    [Fact]
    public void VisualSource_ThatThrows_LogsTheFaultWithoutStoppingSubsequentTicks()
    {
        GameplayObservation gameplay = VitalsOnly(90, 100, 45, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));
        var logger = new RecordingLogger();
        int attempts = 0;
        VisualObservation Visual()
        {
            attempts++;
            throw new InvalidOperationException("capture blew up every time");
        }

        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), logger, visualSource: Visual);

        loop.RunOnce(Snapshot(gameplay), T0);
        loop.RunOnce(Snapshot(gameplay), T0.AddSeconds(1));

        Assert.Equal(2, attempts);
        Assert.Equal(2, logger.Errors.Count);
        Assert.All(logger.Errors, e => Assert.IsType<InvalidOperationException>(e.Exception));
    }

    [Fact]
    public void VisualSource_ThatThrows_StillIncrementsVersionAndRaisesSnapshotFused()
    {
        GameplayObservation gameplay = VitalsOnly(90, 100, 45, 50, T0, (v, t) => ClassifiedValue<int>.Live(v, t));
        var loop = new WorldModelFusionLoop(
            () => Snapshot(gameplay),
            new NullRuntimeLogger(),
            visualSource: () => throw new InvalidOperationException("boom"));
        WorldModelSnapshot? raised = null;
        loop.SnapshotFused += s => raised = s;

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Assert.Same(result, loop.Current);
        Assert.Same(result, raised);
        Assert.Equal(1, result.Version);
    }
}
