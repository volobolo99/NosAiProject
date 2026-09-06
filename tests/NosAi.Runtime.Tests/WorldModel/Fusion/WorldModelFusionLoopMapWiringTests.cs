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
/// AP-03/A4: runtime wiring of an optional map-reconstruction source into
/// <see cref="WorldModelFusionLoop"/>, on top of the network-only wiring
/// AP-01/A4 already tests in <c>WorldModelFusionLoopTests.cs</c> and the
/// vision-side wiring AP-02/A4 tests in
/// <c>WorldModelFusionLoopVisualWiringTests.cs</c> (both deliberately left
/// untouched -- a new file rather than an edit to either, per this task's
/// file ownership). Every test here drives <see cref="WorldModelFusionLoop.RunOnce"/>
/// directly with a synthetic <c>mapSource</c> delegate; none constructs a
/// real <see cref="MapReconstructionSource"/> or touches a grid file/SQLite
/// (that is <c>MapReconstructionSourceTests.cs</c>'s job).
/// </summary>
public sealed class WorldModelFusionLoopMapWiringTests
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

    /// <summary>A gameplay observation carrying only a map id (and, when given, vitals) at the given instant; everything else is honestly unpublished.</summary>
    private static GameplayObservation GameplayFor(DateTime atUtc, int? mapId = null, int? hp = null, int? maxHp = null)
    {
        var observation = new GameplayObservation(
            Hp: hp is int h ? ClassifiedValue<int>.Cached(h, atUtc) : ClassifiedValue<int>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            MaxHp: maxHp is int mh ? ClassifiedValue<int>.Cached(mh, atUtc) : ClassifiedValue<int>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            Mp: ClassifiedValue<int>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            MaxMp: ClassifiedValue<int>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            HasTarget: ClassifiedValue<bool>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            InCombat: ClassifiedValue<bool>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            EntitiesInView: ClassifiedValue<int>.Unknown("not_published_by_provider", observedAtUtc: atUtc),
            ObservedAtUtc: atUtc);

        if (mapId is int id)
            observation = observation with { MapId = ClassifiedValue<int>.Live(id, atUtc) };

        return observation;
    }

    /// <summary>A synthetic vision-side reading carrying only a fresh, Derived HP -- enough to prove ordering against <c>mapSource</c> without needing a real screen capture.</summary>
    private static VisualObservation ScreenVitals(int hp, int maxHp, DateTime atUtc) => new(
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
            Mp: new ScreenVitalPair(ClassifiedValue<int>.Derived(0, atUtc), ClassifiedValue<int>.Derived(0, atUtc), 0.95, null),
            HpGlyphs: 3,
            MpGlyphs: 2,
            TrainedGlyphs: 10),
        HasTarget: ClassifiedValue<bool>.Unknown("target_state_composer_not_wired_in_this_pass"),
        HasDialogWindow: ClassifiedValue<bool>.Unknown("dialog_window_composer_not_wired_in_this_pass"),
        ObservedAtUtc: atUtc);

    // -- no mapSource: identical to AP-01/A4 and AP-02/A4's own behavior --

    [Fact]
    public void NoMapSource_LeavesMapExactlyAsTheNetworkChannelProjectedIt()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 12);
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger());

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Assert.Equal("map-12", result.Map.Id.Value);
        Assert.Empty(result.Map.Tiles);
    }

    [Fact]
    public void NoMapSource_ProducesTheSameResultAsBeforeThisParameterExisted()
    {
        // Same scenario run through both an old-style construction (no
        // mapSource argument at all) and one that passes mapSource: null
        // explicitly -- the two constructor calls must be indistinguishable.
        GameplayObservation gameplay = GameplayFor(T0, mapId: 9);

        var implicitDefault = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger());
        var explicitNull = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), mapSource: null);

        WorldModelSnapshot a = implicitDefault.RunOnce(Snapshot(gameplay), T0);
        WorldModelSnapshot b = explicitNull.RunOnce(Snapshot(gameplay), T0);

        Assert.Equal(a.Map, b.Map);
    }

    // -- mapSource supplied: the result reflects its return value -----------

    [Fact]
    public void MapSource_Supplied_ReplacesResultMapWithItsReturnValue()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 5);
        MapModel replacement = MapModel.Unknown(new MapId("map-5"), "synthetic_replacement", T0) with { Version = 99 };
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(), mapSource: _ => replacement);

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Assert.Same(replacement, result.Map);
    }

    [Fact]
    public void MapSource_ReceivesThisCyclesFusedSnapshotAsItsArgument()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 7);
        WorldModelSnapshot? received = null;
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(),
            mapSource: snapshot =>
            {
                received = snapshot;
                return snapshot.Map;
            });

        loop.RunOnce(Snapshot(gameplay), T0);

        Assert.NotNull(received);
        Assert.Equal("map-7", received!.Map.Id.Value);
        Assert.Equal(T0, received.ObservedAtUtc);
    }

    [Fact]
    public void MapSource_IsCalledAfterVisualSource_SeeingItsFusedVitals()
    {
        // Exactly the ordering AP-03/A4's own spec requires: mapSource runs
        // last, against the same `result` local AP-02/A4's vitals fusion
        // already produced -- so a mapSource delegate observing a fresher
        // Derived screen HP (which wins over a stale Cached network HP)
        // proves it saw the post-visual-fusion snapshot, not the network-only one.
        GameplayObservation gameplay = GameplayFor(T0.AddSeconds(-2), mapId: 3, hp: 50, maxHp: 100);
        VisualObservation visual = ScreenVitals(80, 100, T0);
        WorldModelSnapshot? seenByMapSource = null;

        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(),
            visualSource: () => visual,
            mapSource: snapshot =>
            {
                seenByMapSource = snapshot;
                return snapshot.Map;
            });

        loop.RunOnce(Snapshot(gameplay), T0);

        Assert.NotNull(seenByMapSource);
        Resource health = seenByMapSource!.Player.Status.Resources.First(r => r.Kind == ResourceKind.Health);
        Assert.Equal(80d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, health.Current.Source);
    }

    [Fact]
    public void MapSource_IsInvokedExactlyOncePerRunOnceCall()
    {
        int calls = 0;
        GameplayObservation gameplay = GameplayFor(T0, mapId: 4);
        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), new NullRuntimeLogger(),
            mapSource: snapshot =>
            {
                calls++;
                return snapshot.Map;
            });

        loop.RunOnce(Snapshot(gameplay), T0);
        loop.RunOnce(Snapshot(gameplay), T0.AddSeconds(1));

        Assert.Equal(2, calls);
    }

    // -- mapSource throws: the network cycle must never fail ----------------

    [Fact]
    public void MapSource_ThatThrows_NeverFailsRunOnce_AndKeepsTheNetworkProjectedMap()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 3);
        var logger = new RecordingLogger();
        var loop = new WorldModelFusionLoop(
            () => Snapshot(gameplay),
            logger,
            mapSource: _ => throw new InvalidOperationException("map source blew up"));

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Assert.Equal("map-3", result.Map.Id.Value);
        Assert.Equal(1, result.Version);
        Assert.Single(logger.Errors);
        Assert.IsType<InvalidOperationException>(logger.Errors[0].Exception);
    }

    [Fact]
    public void MapSource_ThatThrows_LogsTheFaultWithoutStoppingSubsequentTicks()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 3);
        var logger = new RecordingLogger();
        int attempts = 0;
        MapModel MapSource(WorldModelSnapshot snapshot)
        {
            attempts++;
            throw new InvalidOperationException("map source blew up every time");
        }

        var loop = new WorldModelFusionLoop(() => Snapshot(gameplay), logger, mapSource: MapSource);

        loop.RunOnce(Snapshot(gameplay), T0);
        loop.RunOnce(Snapshot(gameplay), T0.AddSeconds(1));

        Assert.Equal(2, attempts);
        Assert.Equal(2, logger.Errors.Count);
        Assert.All(logger.Errors, e => Assert.IsType<InvalidOperationException>(e.Exception));
    }

    [Fact]
    public void MapSource_ThatThrows_StillIncrementsVersionAndRaisesSnapshotFused()
    {
        GameplayObservation gameplay = GameplayFor(T0, mapId: 3);
        var loop = new WorldModelFusionLoop(
            () => Snapshot(gameplay),
            new NullRuntimeLogger(),
            mapSource: _ => throw new InvalidOperationException("boom"));
        WorldModelSnapshot? raised = null;
        loop.SnapshotFused += s => raised = s;

        WorldModelSnapshot result = loop.RunOnce(Snapshot(gameplay), T0);

        Assert.Same(result, loop.Current);
        Assert.Same(result, raised);
        Assert.Equal(1, result.Version);
    }
}
