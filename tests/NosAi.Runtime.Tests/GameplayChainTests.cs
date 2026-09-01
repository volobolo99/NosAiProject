using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// From the gameplay provider through the Gate 1 snapshot to the Gate 3 planner.
/// </summary>
/// <remarks>
/// The chain the project has been blocked on: with nothing reading the game,
/// Gate 2 has no real input, Gate 3 plans over an unobserved state, and Gates 4-6
/// can only demonstrate themselves. These tests use a stub provider rather than a
/// real capture — what is under test here is the wiring and the classification it
/// carries, not the decoding, which <see cref="GameplayProviderTests"/> covers
/// end to end.
/// </remarks>
public sealed class GameplayChainTests
{
    private sealed class StubProvider : IGameplayProvider
    {
        private readonly GameplayObservation _observation;
        public StubProvider(GameplayObservation observation) => _observation = observation;
        public string Name => "stub";
        public GameplayObservation Observe() => _observation;
    }

    private sealed class ThrowingProvider : IGameplayProvider
    {
        public string Name => "throwing";
        public GameplayObservation Observe() => throw new InvalidOperationException("boom");
    }

    private static GameplayObservation Read(int hp, int maxHp, int mp, bool hasTarget, bool inCombat)
    {
        DateTime at = DateTime.UtcNow;
        return new GameplayObservation(
            ClassifiedValue<int>.Derived(hp, at),
            ClassifiedValue<int>.Derived(maxHp, at),
            ClassifiedValue<int>.Derived(mp, at),
            ClassifiedValue<int>.Derived(1420, at),
            ClassifiedValue<bool>.Derived(hasTarget, at),
            ClassifiedValue<bool>.Derived(inCombat, at),
            ClassifiedValue<int>.Derived(3, at),
            at);
    }

    private static ClientBaselineSnapshot AttachedClient() => new(
        ProcessDetected: true,
        WindowDetected: true,
        ClientAttached: true,
        ProcessId: 4242,
        WindowHandle: (nint)0xABC,
        Source: "live_process_attach",
        ObservedAtUtc: DateTime.UtcNow,
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

    // -- the snapshot --------------------------------------------------------

    /// <summary>
    /// The behaviour that must not change for anyone who has not attached a
    /// provider: the same key, the same UNKNOWN, the same reason.
    /// </summary>
    [Fact]
    public void With_no_provider_the_snapshot_reports_exactly_what_it_always_did()
    {
        Gate1CanonicalSnapshot snapshot = Snapshot(null);

        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        Assert.Equal("gameplay_provider_not_available", snapshot.Client.GameplayBaseline.FailureReason);
        Assert.Null(snapshot.Client.Gameplay);
    }

    [Fact]
    public void An_attached_provider_publishes_its_reading_with_its_own_classification()
    {
        Gate1CanonicalSnapshot snapshot = Snapshot(Read(4200, 5000, 900, true, true));

        Assert.Equal(DataSourceKind.Derived, snapshot.Client.GameplayBaseline.Source);
        Assert.True(snapshot.Client.GameplayBaseline.HasValue);
        Assert.Equal(4200, snapshot.Client.Gameplay!.Hp.Value);
    }

    /// <summary>
    /// An attached provider that cannot read is not the same as no provider, and
    /// the snapshot says which: the reason comes from the provider, not a literal.
    /// </summary>
    [Fact]
    public void An_attached_provider_that_read_nothing_says_so_in_its_own_words()
    {
        Gate1CanonicalSnapshot snapshot = Snapshot(
            GameplayObservation.Unobserved("player_vitals_not_mapped"));

        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        Assert.Equal("player_vitals_not_mapped", snapshot.Client.GameplayBaseline.FailureReason);
        Assert.NotNull(snapshot.Client.Gameplay);
    }

    // -- Gate 3 --------------------------------------------------------------

    /// <summary>
    /// The line Gate 3 has been sitting behind. With a complete reading the state
    /// is plannable, and the planner stops refusing on a missing world.
    /// </summary>
    [Fact]
    public async Task A_complete_reading_makes_the_gate3_state_plannable()
    {
        var source = new Gate1SnapshotWorldStateSource(() => Snapshot(Read(4200, 5000, 900, true, true)));

        Gate3WorldState state = await source.ReadAsync();

        Assert.True(state.IsPlannable);
        Assert.Null(state.UnusableReason);
        Assert.Equal(4200, state.Hp.Value);
        Assert.Equal(5000, state.MaxHp.Value);
    }

    /// <summary>
    /// And it is still not <c>IsFullyObserved</c>, because that means LIVE. A
    /// reading through the operator's map is DERIVED, so a rule that requires a
    /// live observation before acting on the real game still refuses.
    /// </summary>
    [Fact]
    public async Task A_derived_reading_is_plannable_but_not_fully_observed()
    {
        var source = new Gate1SnapshotWorldStateSource(() => Snapshot(Read(4200, 5000, 900, true, true)));

        Gate3WorldState state = await source.ReadAsync();

        Assert.True(state.IsPlannable);
        Assert.False(state.IsFullyObserved);
    }

    /// <summary>
    /// The partially read case, carried all the way to the planner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert that the whole state was unplannable, which is what the
    /// runtime did before ADR-0016: three fields read, two unknown, refuse
    /// everything. The property that mattered was never the blanket refusal — it
    /// was that an unknown fact must not be defaulted into a decision — and the
    /// blanket refusal also threw away the decisions that only needed the fields
    /// that <i>were</i> read.
    /// </para>
    /// <para>
    /// So the assertion moves to where the property lives: the state plans, the
    /// unknown flag stays unknown, and no candidate that depends on it is produced.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_partial_reading_plans_what_it_can_and_defaults_nothing()
    {
        DateTime at = DateTime.UtcNow;
        var partial = new GameplayObservation(
            ClassifiedValue<int>.Derived(1200, at),      // 24% of max: survival applies
            ClassifiedValue<int>.Derived(5000, at),
            ClassifiedValue<int>.Derived(900, at),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Derived(0, at),
            at);
        var source = new Gate1SnapshotWorldStateSource(() => Snapshot(partial));

        Gate3WorldState state = await source.ReadAsync();

        Assert.True(state.IsPlannable);
        Assert.Null(state.UnusableReason);
        Assert.Equal(1200, state.Hp.Value);

        // Nothing was invented to get there.
        Assert.False(state.HasTarget.HasValue);
        Assert.Equal("target_flag_not_mapped", state.HasTarget.FailureReason);
        Assert.False(state.InCombat.HasValue);

        List<ActionCandidate> candidates = new ActionPlanner().PlanCandidates(state);

        // The rules that read only the vitals apply.
        Assert.Contains(candidates, c => c.Type == ActionType.UseConsumable);
        // The rules that read the unknown flag do not — in either direction. The
        // waypoint move is the dangerous one: it is what "no target" means, and
        // "nobody knows" is not that.
        Assert.DoesNotContain(candidates, c => c.Type == ActionType.UseSkill);
        Assert.DoesNotContain(candidates, c => c.Type == ActionType.UseBasicAttack);
        Assert.DoesNotContain(candidates, c => c.Type == ActionType.MoveToPosition);
    }

    /// <summary>
    /// The same partial reading with healthy HP: nothing applies, and the honest
    /// answer is that there was no candidate — not that the world was unknown.
    /// </summary>
    [Fact]
    public async Task A_partial_reading_with_nothing_to_do_yields_no_candidate()
    {
        DateTime at = DateTime.UtcNow;
        var partial = new GameplayObservation(
            ClassifiedValue<int>.Derived(4800, at),      // 96% of max: no survival rule
            ClassifiedValue<int>.Derived(5000, at),
            ClassifiedValue<int>.Derived(900, at),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Derived(0, at),
            at);
        var source = new Gate1SnapshotWorldStateSource(() => Snapshot(partial));

        Gate3WorldState state = await source.ReadAsync();

        Assert.True(state.IsPlannable);
        Assert.Empty(new ActionPlanner().PlanCandidates(state));
    }

    [Fact]
    public async Task With_no_provider_gate3_refuses_exactly_as_before()
    {
        var source = new Gate1SnapshotWorldStateSource(() => Snapshot(null));

        Gate3WorldState state = await source.ReadAsync();

        Assert.False(state.IsPlannable);
        Assert.Equal("gameplay_provider_not_available", state.UnusableReason);
    }

    // -- failure -------------------------------------------------------------

    /// <summary>
    /// The snapshot is how the operator finds out something is wrong, so it has to
    /// survive the thing that went wrong. A provider that throws must not take it
    /// down; it must appear in it.
    /// </summary>
    [Fact]
    public async Task A_provider_that_throws_is_reported_not_propagated()
    {
        var runtime = NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = System.Security.Cryptography.RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(
            runtime, world, channel, gameplay: new ThrowingProvider());

        Gate1CanonicalSnapshot snapshot = provider.Capture();

        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        Assert.Contains("gameplay_provider_failed", snapshot.Client.GameplayBaseline.FailureReason!);
    }

    /// <summary>The provider reaches the snapshot through the runtime's own path.</summary>
    [Fact]
    public async Task The_runtime_snapshot_provider_carries_an_attached_provider_through()
    {
        var runtime = NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = System.Security.Cryptography.RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(
            runtime, world, channel,
            gameplay: new StubProvider(Read(4200, 5000, 900, true, false)));

        Gate1CanonicalSnapshot snapshot = provider.Capture();

        Assert.Equal(4200, snapshot.Client.Gameplay!.Hp.Value);
    }

    /// <summary>
    /// The wire form keeps every field's own classification. A consumer that
    /// flattened them could not tell an unread field from a zero.
    /// </summary>
    [Fact]
    public void The_wire_form_classifies_each_field_separately()
    {
        DateTime at = DateTime.UtcNow;
        var partial = GameplayObservation.Unobserved("player_vitals_not_mapped", at) with
        {
            EntitiesInView = ClassifiedValue<int>.Derived(2, at),
        };

        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(partial.ToWire()));

        System.Text.Json.JsonElement root = document.RootElement;
        Assert.Equal(
            DataSourceKind.Unknown.ToWire(),
            root.GetProperty("hp").GetProperty("source").GetString());
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            root.GetProperty("hp").GetProperty("value").ValueKind);
        Assert.Equal(2, root.GetProperty("entitiesInView").GetProperty("value").GetInt32());
    }
}
