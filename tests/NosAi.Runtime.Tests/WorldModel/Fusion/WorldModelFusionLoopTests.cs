using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Observability;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-01/A4: runtime wiring of an already-captured <see cref="Gate1CanonicalSnapshot"/>
/// into the Unified World Model via <see cref="WorldModelFusionLoop"/>. These
/// tests exercise <see cref="WorldModelFusionLoop.RunOnce"/> directly, never a
/// real <see cref="Gate1BootstrapHost"/> -- exactly the testability the type
/// was written for.
/// </summary>
public sealed class WorldModelFusionLoopTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private sealed class RecordingLogger : IRuntimeLogger
    {
        public List<string> Errors { get; } = new();
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Errors.Add(message);
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

    private static GameplayObservation ObservationAt(int x, int y, DateTime atUtc) => new(
        Hp: ClassifiedValue<int>.Live(100, atUtc),
        MaxHp: ClassifiedValue<int>.Live(100, atUtc),
        Mp: ClassifiedValue<int>.Live(50, atUtc),
        MaxMp: ClassifiedValue<int>.Live(50, atUtc),
        HasTarget: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
        InCombat: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
        EntitiesInView: ClassifiedValue<int>.Unknown("not_published_by_provider"),
        ObservedAtUtc: atUtc)
    {
        PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(x, y), atUtc),
        MapId = ClassifiedValue<int>.Live(5, atUtc)
    };

    // -- construction guards --------------------------------------------------

    [Fact]
    public void Constructor_RejectsNullSourceOrLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WorldModelFusionLoop(null!, new RecordingLogger()));
        Assert.Throws<ArgumentNullException>(() =>
            new WorldModelFusionLoop(() => Snapshot(null), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInterval(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger(), interval: TimeSpan.FromMilliseconds(milliseconds)));
    }

    // -- RunOnce: first cycle, no prior sighting -------------------------------

    [Fact]
    public void FirstTick_WithNoKnownPosition_ProducesUnknownPositionAndVelocity_NeverAFabricatedDefault()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());
        GameplayObservation noPosition = GameplayObservation.Unobserved("player_position_not_read", T0);

        WorldModelSnapshot result = loop.RunOnce(Snapshot(noPosition), T0);

        Assert.False(result.Player.Position.HasValue);
        Assert.False(result.Player.Velocity.HasValue);
        Assert.Equal(1, result.Version);
    }

    // -- RunOnce: velocity derivation across two ticks -------------------------

    [Fact]
    public void TwoConsecutiveTicks_WithDifferentPositions_DeriveNonZeroVelocity()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());

        DateTime t1 = T0;
        DateTime t2 = T0.AddSeconds(1);

        loop.RunOnce(Snapshot(ObservationAt(0, 0, t1)), t1);
        WorldModelSnapshot second = loop.RunOnce(Snapshot(ObservationAt(10, 0, t2)), t2);

        Assert.True(second.Player.Velocity.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, second.Player.Velocity.Source);
        Assert.Equal(10f, second.Player.Velocity.Value.DxPerSecond, 3);
        Assert.Equal(0f, second.Player.Velocity.Value.DyPerSecond, 3);
    }

    [Fact]
    public void TwoConsecutiveTicks_WithTheSamePosition_DeriveZeroVelocity_NotUnknown()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());

        DateTime t1 = T0;
        DateTime t2 = T0.AddSeconds(1);

        loop.RunOnce(Snapshot(ObservationAt(5, 5, t1)), t1);
        WorldModelSnapshot second = loop.RunOnce(Snapshot(ObservationAt(5, 5, t2)), t2);

        Assert.True(second.Player.Velocity.HasValue);
        Assert.Equal(0f, second.Player.Velocity.Value.DxPerSecond, 3);
        Assert.Equal(0f, second.Player.Velocity.Value.DyPerSecond, 3);
    }

    // -- RunOnce: absent gameplay provider falls back honestly -----------------

    [Fact]
    public void AbsentGameplayProvider_FallsBackToUnobserved_NeverFabricatesAPlayerState()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());

        WorldModelSnapshot result = loop.RunOnce(Snapshot(null), T0);

        Assert.False(result.Player.Position.HasValue);
        Assert.False(result.Player.IsAlive.HasValue);
        Assert.False(result.Player.CurrentMap.HasValue);
        Assert.Equal(GameplayObservationProjector.UnknownMapSentinelId, result.Map.Id.Value);
        Assert.Equal("gameplay_provider_not_available", result.Player.Position.Reason);
    }

    [Fact]
    public void AbsentGameplayProvider_PrefersTheSnapshotsOwnReasonOverTheLiteral()
    {
        // Same pattern as Gate3WorldState's Gate1SnapshotWorldStateSource
        // (src/NosAi.Runtime/Gate3/Gate3WorldState.cs:401): when Client.Gameplay
        // is absent but the baseline carries a more specific diagnostic reason
        // than the generic fallback literal, that reason -- not the literal --
        // must reach the fused snapshot. Built with a `with` expression rather
        // than through Gate1SnapshotFactory, which always pairs a null gameplay
        // provider with exactly the generic literal and so could never exercise
        // this branch on its own.
        Gate1CanonicalSnapshot baseline = Snapshot(null);
        Gate1CanonicalSnapshot snapshot = baseline with
        {
            Client = baseline.Client with
            {
                GameplayBaseline = ClassifiedValue<object>.Unknown("gameplay_memory_reader_crashed")
            }
        };
        Assert.Null(snapshot.Client.Gameplay);

        var loop = new WorldModelFusionLoop(() => snapshot, new RecordingLogger());
        WorldModelSnapshot result = loop.RunOnce(snapshot, T0);

        Assert.Equal("gameplay_memory_reader_crashed", result.Player.Position.Reason);
    }

    // -- version and Current/event plumbing ------------------------------------

    [Fact]
    public void Version_GrowsMonotonically_AcrossTicks()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());
        GameplayObservation observation = ObservationAt(1, 1, T0);

        WorldModelSnapshot first = loop.RunOnce(Snapshot(observation), T0);
        WorldModelSnapshot second = loop.RunOnce(Snapshot(observation), T0.AddSeconds(1));
        WorldModelSnapshot third = loop.RunOnce(Snapshot(observation), T0.AddSeconds(2));

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(3, third.Version);
    }

    [Fact]
    public void Current_StartsAtUnknown_BeforeAnyTick()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());

        Assert.Equal(0, loop.Current.Version);
        Assert.False(loop.Current.Player.Position.HasValue);
    }

    [Fact]
    public void RunOnce_UpdatesCurrent_AndRaisesSnapshotFused_WithTheSameResult()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());
        WorldModelSnapshot? raised = null;
        loop.SnapshotFused += s => raised = s;

        WorldModelSnapshot result = loop.RunOnce(Snapshot(ObservationAt(3, 4, T0)), T0);

        Assert.Same(result, loop.Current);
        Assert.NotNull(raised);
        Assert.Same(result, raised);
    }

    [Fact]
    public void RunOnce_RejectsNullSnapshot()
    {
        var loop = new WorldModelFusionLoop(() => Snapshot(null), new RecordingLogger());
        Assert.Throws<ArgumentNullException>(() => loop.RunOnce(null!, T0));
    }

    // -- Start/pump lifecycle ---------------------------------------------------

    [Fact]
    public async Task Start_TicksThroughTheSuppliedSource_AndStopsCleanlyOnDispose()
    {
        var logger = new RecordingLogger();
        int calls = 0;
        Gate1CanonicalSnapshot Source()
        {
            calls++;
            return Snapshot(ObservationAt(calls, 0, DateTime.UtcNow));
        }

        var loop = new WorldModelFusionLoop(Source, logger, interval: TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource();
        loop.Start(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (calls < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(calls >= 2, "The pump should have called the snapshot source at least twice.");
        Assert.True(loop.IsRunning);

        await loop.DisposeAsync();
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Start_ASourceThatThrows_SkipsTheCycle_AndKeepsPumping()
    {
        var logger = new RecordingLogger();
        int calls = 0;
        Gate1CanonicalSnapshot Source()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("boom");
            return Snapshot(ObservationAt(1, 1, DateTime.UtcNow));
        }

        var loop = new WorldModelFusionLoop(Source, logger, interval: TimeSpan.FromMilliseconds(20));
        loop.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (calls < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(calls >= 2);
        Assert.Contains(logger.Errors, m => m.Contains("skipping this cycle", StringComparison.Ordinal));

        await loop.DisposeAsync();
    }
}
