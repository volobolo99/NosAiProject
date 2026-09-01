using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The decision loop over what the runtime is actually observing.
/// </summary>
/// <remarks>
/// Gate 3 has had a complete pipeline and a passing suite for some time, and
/// nothing in <c>src/</c> ever built one. These tests are about the loop that now
/// does: that it forms a decision from an observed state, that it refuses to form
/// one from an unobserved state, and that it stops short of the client in both
/// cases while the policy keeps live input off.
/// </remarks>
public sealed class Gate3DecisionLoopTests
{
    [Fact]
    public async Task Critical_hp_with_an_unknown_target_reaches_a_decision()
    {
        // Exactly the shape the world channel produces: HP, max HP and MP read
        // from stat, and nothing establishing whether there is a target.
        DateTime now = DateTime.UtcNow;
        var state = new Gate3WorldState(
            ClassifiedValue<int>.Live(200, now),
            ClassifiedValue<int>.Live(5000, now),
            ClassifiedValue<int>.Live(1420, now),
            ClassifiedValue<bool>.Unknown("target_state_not_on_the_wire"),
            ClassifiedValue<bool>.Unknown("combat_state_not_on_the_wire"));

        Gate3LoopCycle cycle = await RunOne(state);

        // Not NoWorldState: the vitals are all there, and ADR-0016 makes the
        // unknown flags skip their own rules rather than block the cycle.
        Assert.Equal(CycleOutcome.ExecutionDisabled, cycle.Outcome);
        // Survival is the branch the vitals alone support.
        Assert.Contains(cycle.SelectedAction, new[] { ActionType.UseConsumable, ActionType.EmergencyFlee });
        Assert.False(cycle.WouldHaveActed);
    }

    [Fact]
    public async Task An_unknown_target_never_sends_the_character_walking()
    {
        // Healthy, so no survival rule applies, and the target state is unknown so
        // neither the attack nor the exploration rule may. The honest answer is
        // that there was nothing to do -- not a waypoint move chosen because
        // "no target" was assumed.
        DateTime now = DateTime.UtcNow;
        var state = new Gate3WorldState(
            ClassifiedValue<int>.Live(7305, now),
            ClassifiedValue<int>.Live(7305, now),
            ClassifiedValue<int>.Live(1420, now),
            ClassifiedValue<bool>.Unknown("target_state_not_on_the_wire"),
            ClassifiedValue<bool>.Unknown("combat_state_not_on_the_wire"));

        Gate3LoopCycle cycle = await RunOne(state);

        Assert.Equal(CycleOutcome.NoCandidate, cycle.Outcome);
        Assert.Equal(ActionType.None, cycle.SelectedAction);
    }

    [Fact]
    public async Task Nothing_observed_refuses_to_plan_and_says_why()
    {
        Gate3LoopCycle cycle = await RunOne(Gate3WorldState.Unobserved("gameplay_provider_not_available"));

        Assert.Equal(CycleOutcome.NoWorldState, cycle.Outcome);
        Assert.Contains("gameplay_provider_not_available", cycle.Summary, StringComparison.Ordinal);
        Assert.Null(cycle.ObservationAge);
    }

    [Fact]
    public async Task A_source_that_throws_becomes_an_unobserved_cycle_not_a_dead_loop()
    {
        await using var loop = new Gate3DecisionLoop(
            new ThrowingWorldStateSource(),
            new Gate3ExecutionOrchestrator(),
            new NullRuntimeLogger());

        Gate3LoopCycle cycle = await loop.RunOnceAsync();

        Assert.Equal(CycleOutcome.NoWorldState, cycle.Outcome);
        Assert.Contains("world_state_source_failed", cycle.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_view_reports_the_age_of_what_it_planned_on()
    {
        DateTime observed = DateTime.UtcNow.AddSeconds(-1.4);
        var state = new Gate3WorldState(
            ClassifiedValue<int>.Cached(4000, observed, "stat_not_resent"),
            ClassifiedValue<int>.Cached(7305, observed, "stat_not_resent"),
            ClassifiedValue<int>.Cached(1420, observed, "stat_not_resent"),
            ClassifiedValue<bool>.Unknown("target_state_not_on_the_wire"),
            ClassifiedValue<bool>.Unknown("combat_state_not_on_the_wire"));

        await using var loop = new Gate3DecisionLoop(
            new FixedWorldStateSource(state), new Gate3ExecutionOrchestrator(), new NullRuntimeLogger());
        await loop.RunOnceAsync();

        Gate3LoopView view = loop.Describe();

        Assert.True(view.LastObservationAgeSeconds.HasValue);
        Assert.InRange(view.LastObservationAgeSeconds.Value, 1.0, 3.0);
        Assert.Equal(1, view.CyclesRun.Value);
        // Nothing is bound that could act, and the view says so rather than
        // leaving the operator to infer it from the outcome.
        Assert.False(view.ActingEnabled.Value);
    }

    [Fact]
    public async Task Outcomes_are_counted_by_kind_rather_than_collapsed()
    {
        DateTime now = DateTime.UtcNow;
        var healthy = new Gate3WorldState(
            ClassifiedValue<int>.Live(7305, now),
            ClassifiedValue<int>.Live(7305, now),
            ClassifiedValue<int>.Live(1420, now),
            ClassifiedValue<bool>.Unknown("target_state_not_on_the_wire"),
            ClassifiedValue<bool>.Unknown("combat_state_not_on_the_wire"));
        var source = new SwitchableWorldStateSource(healthy);

        await using var loop = new Gate3DecisionLoop(
            source, new Gate3ExecutionOrchestrator(), new NullRuntimeLogger());
        await loop.RunOnceAsync();
        source.Current = Gate3WorldState.Unobserved("client_not_attached");
        await loop.RunOnceAsync();
        await loop.RunOnceAsync();

        Gate3LoopView view = loop.Describe();

        Assert.Equal(3, view.CyclesRun.Value);
        Assert.Equal(1, view.OutcomeCounts.Single(c => c.Key == nameof(CycleOutcome.NoCandidate)).Value);
        Assert.Equal(2, view.OutcomeCounts.Single(c => c.Key == nameof(CycleOutcome.NoWorldState)).Value);
    }

    [Fact]
    public async Task The_pump_runs_cycles_and_stops_when_disposed()
    {
        DateTime now = DateTime.UtcNow;
        var source = new FixedWorldStateSource(new Gate3WorldState(
            ClassifiedValue<int>.Live(200, now),
            ClassifiedValue<int>.Live(5000, now),
            ClassifiedValue<int>.Live(1420, now),
            ClassifiedValue<bool>.Unknown("target_state_not_on_the_wire"),
            ClassifiedValue<bool>.Unknown("combat_state_not_on_the_wire")));

        var completed = new TaskCompletionSource();
        var loop = new Gate3DecisionLoop(
            source, new Gate3ExecutionOrchestrator(), new NullRuntimeLogger(), TimeSpan.FromMilliseconds(20));
        loop.CycleCompleted += _ => completed.TrySetResult();

        loop.Start();
        Assert.True(loop.IsRunning);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await loop.DisposeAsync();
        Assert.False(loop.IsRunning);
        Assert.True(loop.Describe().CyclesRun.Value >= 1);
    }

    /// <summary>
    /// The Gate 1 adapter, over a snapshot with no gameplay provider: the state a
    /// runtime started without <c>--observe-game</c> actually produces.
    /// </summary>
    [Fact]
    public async Task Gate1_snapshot_without_a_provider_is_unobserved_with_the_snapshot_reason()
    {
        var host = new Gate1.Gate1BootstrapHost(new Configuration.Gate1HostOptions
        {
            DashboardPort = 0,
            StartDashboard = false,
            EnableDiscovery = false,
            GuardPort = 0
        }, new NullRuntimeLogger());

        await using (host)
        {
            var source = new Gate1SnapshotWorldStateSource(host.Capture);

            Gate3WorldState state = await source.ReadAsync();

            Assert.False(state.IsPlannable);
            Assert.NotNull(state.UnusableReason);
        }
    }

    /// <summary>
    /// The whole chain over the real capture: recorded bytes, framed, decoded,
    /// published by the gameplay provider, planned on by Gate 3.
    /// </summary>
    /// <remarks>
    /// Skipped when the recording is absent, because <c>data/</c> is gitignored
    /// and a clone will not have it. It is not replaced by a synthetic stand-in:
    /// the point of this test is the real bytes.
    /// </remarks>
    [Fact]
    public async Task The_recorded_world_channel_produces_a_real_decision()
    {
        string recording = Path.Combine(RepositoryRoot(), "data", "nostale_combat.noscap");
        if (!File.Exists(recording))
            return;

        using LiveIntegration.Capture.IPacketSource packets =
            LiveIntegration.Capture.CaptureFile.Open(recording);
        var endpoint = new Perception.Network.GameEndpoint(
            packets.ServerAddress.ToString(), packets.ServerPort);
        using Gate1.Gate1ObservationChannel channel =
            Gate1.Gate1ObservationChannel.FromPackets(packets, endpoint, DataSourceKind.Cached);
        Assert.NotNull(channel.Provider);

        await using var loop = new Gate3DecisionLoop(
            new GameplayProviderWorldStateSource(channel.Provider!),
            new Gate3ExecutionOrchestrator(),
            new NullRuntimeLogger());

        Gate3LoopCycle? planned = null;
        for (var i = 0; i < 20 && planned is null; i++)
        {
            Gate3LoopCycle cycle = await loop.RunOnceAsync();
            if (cycle.Outcome != CycleOutcome.NoWorldState)
                planned = cycle;
        }

        Assert.NotNull(planned);
        // The numbers the world channel reported for this session, catalogued in
        // docs/PROTOCOLLO_NOSTALE.md.
        Assert.Equal(7305, planned!.MaxHp.Value);
        Assert.InRange(planned.Hp.Value, 7218, 7305);
        // Recorded, so real and not current -- and therefore never actionable.
        Assert.Equal(DataSourceKind.Cached, planned.Hp.Source);
        Assert.False(planned.WouldHaveActed);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static async Task<Gate3LoopCycle> RunOne(Gate3WorldState state)
    {
        await using var loop = new Gate3DecisionLoop(
            new FixedWorldStateSource(state), new Gate3ExecutionOrchestrator(), new NullRuntimeLogger());
        return await loop.RunOnceAsync();
    }

    private sealed class FixedWorldStateSource(Gate3WorldState state) : IWorldStateSource
    {
        public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(state);
    }

    private sealed class SwitchableWorldStateSource(Gate3WorldState initial) : IWorldStateSource
    {
        public Gate3WorldState Current { get; set; } = initial;

        public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }

    private sealed class ThrowingWorldStateSource : IWorldStateSource
    {
        public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("snapshot unavailable");
    }

    private sealed class NullRuntimeLogger : IRuntimeLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
