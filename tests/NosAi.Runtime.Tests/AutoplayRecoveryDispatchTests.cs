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
/// AP-08/A2A4: the <see cref="StrategicGoalKind.Recovery"/> dispatch branch
/// of <see cref="AutoplayCommand.ExecuteOneCycle"/> -- Recovery selected
/// dispatches to <see cref="RecoverCommand.ExecuteOneRound"/> exactly like
/// Survival (both run the configured consumable slot; they differ only in
/// why they fired), and Recovery selected without a configured slot is its
/// own distinguishable outcome (<see cref="AutoplayCommand.AutoplayDispatch.RecoverySkippedNoSlot"/>).
/// Mirrors <c>AutoplayCommandTests</c>'s Survival tests byte-for-byte in
/// shape, with <see cref="StrategicGoalKind.Recovery"/> as the plan kind.
/// </summary>
public sealed class AutoplayRecoveryDispatchTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
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
        string path = Path.Combine(Path.GetTempPath(), "nosai-autoplay-recovery-" + Guid.NewGuid().ToString("N") + ".json");
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
        PlayerVitalsReading? after = null)
    {
        RecordingInput recording = input ?? new RecordingInput();
        var reads = new Queue<PlayerVitalsReading?>(new[] { before, after });
        ExplorationFootprint footprint = FullyExploredFootprint();

        return AutoplayCommand.ExecuteOneCycle(
            plan,
            recoverSlot,
            keybinds ?? ConfirmedConsumableMap(),
            recording,
            readVitals: () => reads.Count > 0 ? reads.Dequeue() : null,
            verificationDelay: () => { }, // no real time in tests
            map: MapModel.Unknown(footprint.MapId, "test", Now),
            footprint,
            playerPosition: new WorldPosition(0f, 0f),
            EquatableArray<Mob>.Empty,
            origin: new MapPoint(0, 0),
            grid: default,
            view: new OccupancyView(null, Now),
            controller: new PathWalkController(),
            chain: ChainRig.NotAuthorizing(),
            executor: ExecutorRig.NeverEmitted(recording),
            readPosition: () => null,
            onEvidence: null,
            in AutoplayAuthority,
            Now,
            playerId: new EntityId("test-player"),
            drops: EquatableArray<Drop>.Empty,
            gameplay: null,
            playerFacts: new Player(
                new EntityId("test-player"),
                WorldFact<WorldPosition>.Unknown("test_player_not_read", Now),
                WorldFact<float>.Unknown("test_player_not_read", Now),
                WorldFact<bool>.Unknown("test_player_not_read", Now),
                WorldFact<MapId>.Unknown("test_player_not_read", Now),
                CombatantStatus.Empty,
                WorldFact<EquatableArray<Skill>>.Unknown("test_player_not_read", Now),
                WorldFact<EquatableArray<Cooldown>>.Unknown("test_player_not_read", Now),
                WorldFact<EquatableArray<InventoryItem>>.Unknown("test_player_not_read", Now),
                WorldFact<EquatableArray<EquipmentItem>>.Unknown("test_player_not_read", Now)),
            resolveSlot: _ => null,
            equipExecutor: new EquipExecutor(
                new GatedInputBackend(recording, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true }),
                () => IntPtr.Zero),
            calibration: BagPanelRoiCalibration.Uncalibrated,
            optimizationGesture: null,
            readLatestEquip: () => null);
    }

    private static StrategicPlan RecoveryPlan() =>
        new(StrategicGoalKind.Recovery, WorldFact<bool>.Derived(true, confidence: 1d, Now), Now);

    // ------------------------------------------------- Recovery, slot configured

    [Fact]
    public void ARecoveryPlanWithARecoverSlot_DispatchesToRecoverCommand_AndReportsTheEvidence()
    {
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(
            RecoveryPlan(),
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

    // ------------------------------------------- Recovery, no slot configured

    [Fact]
    public void ARecoveryPlanWithoutARecoverSlot_IsSkipped_WithItsOwnDistinguishableOutcome()
    {
        RecordingInput input = new();

        AutoplayCommand.AutoplayCycleResult result = RunCycle(RecoveryPlan(), recoverSlot: null, input: input);

        // The exact enum value, not just "not equal": RecoverySkippedNoSlot
        // must stay distinguishable from SurvivalSkippedNoSlot in the
        // printed/audited outcome.
        Assert.Equal(AutoplayCommand.AutoplayDispatch.RecoverySkippedNoSlot, result.Dispatch);
        Assert.NotEqual(AutoplayCommand.AutoplayDispatch.SurvivalSkippedNoSlot, result.Dispatch);
        Assert.Null(result.ScoutRun);
        Assert.Null(result.RecoverEvidence);
        Assert.Empty(input.Presses);
    }

    // Minimal rig stand-ins: the dispatcher must never reach the executor's
    // emission path in these tests, and these fakes make any accidental reach
    // loud instead of silent. (Same shape as AutoplayCommandTests' own.)

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
