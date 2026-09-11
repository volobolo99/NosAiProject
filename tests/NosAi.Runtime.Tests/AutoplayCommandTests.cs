using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Strategy;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-08/A4: <see cref="AutoplayCommand.ExecuteOneCycle"/> -- the pure
/// dispatcher that chooses at most one of the two pre-built rounds
/// (<see cref="ScoutCommand.ExecuteOneRound"/> /
/// <see cref="RecoverCommand.ExecuteOneRound"/>) from an already-computed
/// <see cref="StrategicPlan"/>. Covers every
/// <see cref="AutoplayCommand.AutoplayDispatch"/> branch (Idle/Explored/
/// Recovered/SurvivalSkippedNoSlot/NotDispatchable), and proves a
/// non-dispatched or skipped cycle never touches the walk controller or the
/// input backend.
/// </summary>
public sealed class AutoplayCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly ActuationAuthority AutoplayAuthority = ActuationAuthority.Commanded(AutoplayCommand.Flag);
    private const int RecoverSlot = 2;

    // ------------------------------------------------------------- helpers

    private static PlayerVitalsReading Vitals(uint hp) => new(Hp: hp, MaxHp: 100, Mp: 60, MaxMp: 100);

    private static KeybindMap ConfirmedConsumableMap(int slot = RecoverSlot, ushort virtualKey = 112)
    {
        // Built through the same public loader the production path uses, on a
        // small in-memory file (KeybindMap only parses from JSON).
        return MapFromJson($$"""
            {
              "version": 1,
              "binds": {
                "consumable.{{slot}}": { "virtualKey": {{virtualKey}}, "label": "F1", "confirmed": true }
              }
            }
            """);
    }

    private static KeybindMap MapFromJson(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-autoplay-keybinds-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            Assert.True(KeybindMap.TryLoad(path, out KeybindMap map, out string? reason), reason);
            return map;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingInput : IInputBackend
    {
        public List<(ushort VirtualKey, int PressDurationMs)> Presses { get; } = new();

        public bool IsLive => false;
        public bool TryGetCursorPosition(out int x, out int y) { x = 0; y = 0; return true; }
        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => true;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
        {
            Presses.Add((virtualKey, pressDurationMs));
            return Accepted;
        }
        public bool ScrollWheel(int detents) => true;

        public bool Accepted { get; set; } = true;
    }

    /// <summary>
    /// The minimal context both branches share. The scout branch is given a
    /// fully-explored footprint with no walkable tiles, so a dispatched
    /// scout round can never walk or emit (no reachable frontier); the
    /// assertions in the scout-dispatch tests therefore watch the round's
    /// dispatch outcome and plan, not a real walk.
    /// </summary>
    private static ExplorationFootprint FullyExploredFootprint() =>
        new(
            new MapId("map-1"),
            EquatableArray<TileCoordinate>.Empty,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);

    private static AutoplayCommand.AutoplayCycleResult RunCycle(
        StrategicPlan plan,
        int? recoverSlot = RecoverSlot,
        RecordingInput? input = null,
        KeybindMap? keybinds = null,
        PlayerVitalsReading? before = null,
        PlayerVitalsReading? after = null,
        EquatableArray<Mob>? mobs = null)
    {
        ExplorationFootprint footprint = FullyExploredFootprint();
        return RunCycleWithMap(
            plan,
            MapModel.Unknown(footprint.MapId, "test", Now),
            footprint,
            recoverSlot,
            input,
            keybinds,
            before,
            after,
            mobs);
    }

    private static AutoplayCommand.AutoplayCycleResult RunCycleWithMap(
        StrategicPlan plan,
        MapModel map,
        ExplorationFootprint footprint,
        int? recoverSlot = RecoverSlot,
        RecordingInput? input = null,
        KeybindMap? keybinds = null,
        PlayerVitalsReading? before = null,
        PlayerVitalsReading? after = null,
        EquatableArray<Mob>? mobs = null)
    {
        RecordingInput recording = input ?? new RecordingInput();
        var reads = new Queue<PlayerVitalsReading?>(new[] { before, after });

        return AutoplayCommand.ExecuteOneCycle(
            plan,
            recoverSlot,
            keybinds ?? ConfirmedConsumableMap(),
            recording,
            readVitals: () => reads.Count > 0 ? reads.Dequeue() : null,
            verificationDelay: () => { }, // no real time in tests
            map,
            footprint,
            playerPosition: new WorldPosition(0f, 0f),
            mobs ?? EquatableArray<Mob>.Empty,
            origin: new MapPoint(0, 0),
            grid: default,
            view: new OccupancyView(null, Now),
            controller: new PathWalkController(),
            chain: ChainRig.NotAuthorizing(),
            executor: ExecutorRig.NeverEmitted(recording),
            readPosition: () => null,
            onEvidence: null,
            in AutoplayAuthority,
            Now);
    }

    // ------------------------------------------------------------- Idle

    [Fact]
    public void APlanWithNoSelection_IsIdle_AndNeverTouchesInputOrTheWalkController()
    {
        RecordingInput input = new();
        StrategicPlan unselected = StrategicPlan.Unselected("no_strategic_signal_available", Now);

        AutoplayCommand.AutoplayCycleResult result = RunCycle(unselected, input: input);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.Idle, result.Dispatch);
        Assert.Null(result.ScoutRun);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses);
    }

    // ------------------------------------------------------------- Scout branch

    [Fact]
    public void AnExplorationPlan_DispatchesToScoutCommand_UnderAutoplayAuthority()
    {
        StrategicPlan plan = new(
            StrategicGoalKind.Exploration,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);

        // A real, walkable, not-yet-visited tile on the same map as the
        // footprint: the scout round must find a reachable frontier and walk.
        var footprintMapId = new MapId("map-1");
        ExplorationFootprint footprint = new(
            footprintMapId,
            EquatableArray<TileCoordinate>.Empty,
            WorldFact<bool>.Derived(false, confidence: 1d, Now),
            Now);
        MapModel map = new(
            footprintMapId,
            WorldFact<string>.Unknown("test", Now),
            WorldFact<NosAi.Core.WorldModel.MapBounds>.Unknown("test", Now),
            EquatableArray<Tile>.From(new[]
            {
                new Tile(
                    new TileCoordinate(1, 1),
                    WorldFact<TileTraversability>.Live(TileTraversability.Walkable, confidence: 1d, Now))
            }),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 1,
            Now);
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycleWithMap(
            plan, map, footprint, input: input);

        // The plan selected Exploration and ExecuteOneCycle chose the scout
        // branch under the --autoplay authority: the round produced a walk run
        // (this rig's grid is not loaded, so the run honestly reports
        // non-arrival -- what matters here is that the scout branch was taken,
        // not that the walk succeeded). It never produces recover evidence.
        Assert.Equal(AutoplayCommand.AutoplayDispatch.Explored, result.Dispatch);
        Assert.NotNull(result.ScoutRun);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses); // a scout round emits clicks, not key presses
    }

    [Fact]
    public void AnExplorationPlan_CarriesTheUpdatedFootprintForward_NotTheOriginal()
    {
        // Without this, a caller reusing the returned footprint verbatim across
        // cycles would never learn the player had already visited wherever this
        // cycle walked to -- the exact gap AP-08's A5 audit found.
        StrategicPlan plan = new(
            StrategicGoalKind.Exploration,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);

        var footprintMapId = new MapId("map-1");
        ExplorationFootprint originalFootprint = ExplorationFootprint.Empty(footprintMapId, "test", Now);
        MapModel map = new(
            footprintMapId,
            WorldFact<string>.Unknown("test", Now),
            WorldFact<NosAi.Core.WorldModel.MapBounds>.Unknown("test", Now),
            EquatableArray<Tile>.From(new[]
            {
                new Tile(
                    new TileCoordinate(1, 1),
                    WorldFact<TileTraversability>.Live(TileTraversability.Walkable, confidence: 1d, Now))
            }),
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 1,
            Now);

        AutoplayCommand.AutoplayCycleResult result = RunCycleWithMap(plan, map, originalFootprint);

        Assert.NotEqual(originalFootprint, result.UpdatedFootprint);
        Assert.Equal(footprintMapId, result.UpdatedFootprint.MapId);
    }

    [Fact]
    public void AnIdleCycle_PassesTheFootprintThroughUnchanged()
    {
        StrategicPlan unselected = StrategicPlan.Unselected("no_strategic_signal_available", Now);
        ExplorationFootprint footprint = FullyExploredFootprint();

        AutoplayCommand.AutoplayCycleResult result = RunCycleWithMap(
            unselected, MapModel.Unknown(footprint.MapId, "test", Now), footprint);

        Assert.Equal(footprint, result.UpdatedFootprint);
    }

    // ------------------------------------------------------------- Recover branch

    [Fact]
    public void ASurvivalPlanWithARecoverSlot_DispatchesToRecoverCommand_AndReportsTheEvidence()
    {
        StrategicPlan plan = new(
            StrategicGoalKind.Survival,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(
            plan,
            recoverSlot: RecoverSlot,
            input: input,
            before: Vitals(hp: 40),
            after: Vitals(hp: 90));

        Assert.Equal(AutoplayCommand.AutoplayDispatch.Recovered, result.Dispatch);
        Assert.NotNull(result.RecoverEvidence);
        Assert.True(result.RecoverEvidence!.ResourceGainConfirmed);
        Assert.Equal(ResourceKind.Health, result.RecoverEvidence.ResourceObserved);

        // The press went out exactly once, for the confirmed consumable.{slot}
        // key, with the command's press duration.
        (ushort virtualKey, int duration) = Assert.Single(input.Presses);
        Assert.Equal(112, virtualKey);
        Assert.Equal(80, duration);
    }

    // ------------------------------------------------- Survival, no slot configured

    [Fact]
    public void ASurvivalPlanWithoutARecoverSlot_IsSkipped_AndNeverTouchesInputOrTheWalkController()
    {
        RecordingInput input = new();
        StrategicPlan plan = new(
            StrategicGoalKind.Survival,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);

        AutoplayCommand.AutoplayCycleResult result = RunCycle(plan, recoverSlot: null, input: input);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.SurvivalSkippedNoSlot, result.Dispatch);
        Assert.Null(result.ScoutRun);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses);
    }

    // ------------------------------------------------------------- Not dispatchable

    [Fact]
    public void AnyOtherSelectedKind_IsNotDispatchable_AndNeverTouchesInputOrTheWalkController()
    {
        // QuestUrgency is the only kind here with no assessor and no dispatch
        // at all; Progression/Optimization have no assessor either, and
        // Farming (which does, see the Farming branch tests below) is
        // covered separately. Whatever the kind, dispatch must name it --
        // never substitute a different act for the one the plan selected.
        StrategicPlan plan = new(
            StrategicGoalKind.QuestUrgency,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(plan, input: input);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.NotDispatchable, result.Dispatch);
        Assert.Equal(StrategicGoalKind.QuestUrgency, result.Plan.SelectedKind);
        Assert.Null(result.ScoutRun);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses);
    }

    [Theory]
    [InlineData(StrategicGoalKind.Progression)]
    [InlineData(StrategicGoalKind.Optimization)]
    public void EveryUndispatchedGoalKind_IsNotDispatchable_ByName_NeverSubstituted(StrategicGoalKind kind)
    {
        StrategicPlan plan = new(kind, WorldFact<bool>.Derived(true, confidence: 1d, Now), Now);
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(plan, input: input);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.NotDispatchable, result.Dispatch);
        Assert.Equal(kind, result.Plan.SelectedKind);
        Assert.Empty(input.Presses);
    }

    // ------------------------------------------------------------ Farming branch

    private static Mob AttackableMob(string id, float x, float y) => new(
        new EntityId(id),
        WorldFact<WorldPosition>.Live(new WorldPosition(x, y), confidence: 1d, Now),
        WorldFact<string>.Live("test-mob", confidence: 1d, Now),
        WorldFact<bool>.Live(true, confidence: 1d, Now),
        WorldFact<bool>.Live(true, confidence: 1d, Now),
        CombatantStatus.Empty);

    [Fact]
    public void AFarmingPlan_WithNoObservedMobs_IsFarmingSkippedNoTarget_AndNeverTouchesInput()
    {
        StrategicPlan plan = new(
            StrategicGoalKind.Farming,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(plan, input: input);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.FarmingSkippedNoTarget, result.Dispatch);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void AFarmingPlan_WithAnAttackableMobInRange_DispatchesToEngageCommand_UnderAutoplayAuthority()
    {
        // Player is at (0,0) (RunCycle's default); this mob sits well inside
        // TargetSelectionPolicy.Default's 12-tile range and is alive/hostile,
        // so TargetSelector.TrySelect must find it and Engage must fire.
        StrategicPlan plan = new(
            StrategicGoalKind.Farming,
            WorldFact<bool>.Derived(true, confidence: 1d, Now),
            Now);
        RecordingInput input = new();
        EquatableArray<Mob> mobs = new(System.Collections.Immutable.ImmutableArray.Create(AttackableMob("777", x: 1f, y: 1f)));

        AutoplayCommand.AutoplayCycleResult result = RunCycle(plan, input: input, mobs: mobs);

        Assert.Equal(AutoplayCommand.AutoplayDispatch.Engaged, result.Dispatch);
        Assert.NotNull(result.RecoverEvidence);
    }

    // ------------------------------------------------------- Run(...) argument guards

    // Program.cs's dispatch parses --cycles as an integer and passes it straight
    // through; a count above MaxCycles or below one must be refused cleanly,
    // never silently clamped and never an unhandled exception.

    [Fact]
    public void Run_CyclesAboveMax_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = AutoplayCommand.Run(cycles: AutoplayCommand.MaxCycles + 1);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_ZeroCycles_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = AutoplayCommand.Run(cycles: 0);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_NegativeCycles_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = AutoplayCommand.Run(cycles: -1);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    // ------------------------------------------------------------ wiring

    [Fact]
    public void TheRuntimeWiresTheAutoplayFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(AutoplayCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("AutoplayCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + AutoplayCommand.Flag + "\"", program, StringComparison.Ordinal);
        Assert.Contains("--cycles", program, StringComparison.Ordinal);
        Assert.Contains("--recover-slot", program, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }

    // Minimal rig stand-ins: the dispatcher must never reach the executor's
    // emission path in these tests, and these fakes make any accidental reach
    // loud instead of silent.

    private static class ChainRig
    {
        public static StepGuardChain NotAuthorizing() =>
            new(
                () => "test_no_session",
                () => RuntimeSafetyPolicy.SafeDefault,
                new FailingProjection());
    }

    private static class ExecutorRig
    {
        /// <summary>
        /// An executor built over a real GatedInputBackend whose policy is
        /// safe-default (live input disabled), so a step can never be emitted;
        /// the recording backend would catch any press that slipped through
        /// anyway. Built the same way the certification suites build their
        /// gated rig: RuntimeComposition.Create with an explicit policy.
        /// </summary>
        public static SingleStepExecutor NeverEmitted(IInputBackend backend) =>
            new(
                ChainRig.NotAuthorizing(),
                RuntimeComposition.Create(RuntimeSafetyPolicy.SafeDefault, backend).InputBackend as GatedInputBackend
                ?? throw new InvalidOperationException("composed backend was not gated"),
                () => IntPtr.Zero);
    }

    private sealed class FailingProjection : IScreenProjection
    {
        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 0;
            screenY = 0;
            failureReason = "test_projection_never_used";
            return false;
        }

        public GeometryShape Scale => new(0, 0, 0);
    }
}
