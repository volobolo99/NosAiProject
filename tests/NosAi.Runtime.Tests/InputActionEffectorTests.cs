using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The link between a planned action and a real gesture, and every case in which
/// it refuses to make one.
/// </summary>
/// <remarks>
/// <para>
/// F3-1. <see cref="RecordingInputBackend"/> shows which gestures would have gone
/// out without touching the desktop, so the translation can be pinned exactly —
/// and so the test that matters most, that a closed policy lets nothing through,
/// is an assertion about an empty list rather than a hope.
/// </para>
/// <para>
/// The backend is always wrapped in <see cref="GatedInputBackend"/>, because the
/// effector will not accept anything else: taking the gate concretely is how
/// ADR-0003's boundary is made impossible to step around.
/// </para>
/// </remarks>
public sealed class InputActionEffectorTests
{
    private static readonly RuntimeSafetyPolicy Open =
        RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };

    private static readonly RuntimeSafetyPolicy Closed = RuntimeSafetyPolicy.SafeDefault;

    /// <summary>A projection calibrated to shift a map point by a known offset.</summary>
    private sealed class FakeProjection : IScreenProjection
    {
        private readonly int _offsetX;
        private readonly int _offsetY;
        private readonly string? _refuseWith;

        public FakeProjection(int offsetX = 1000, int offsetY = 500, string? refuseWith = null)
        {
            _offsetX = offsetX;
            _offsetY = offsetY;
            _refuseWith = refuseWith;
        }

        /// <summary>A stated scale, so the commit point has something to compare against.</summary>
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            if (_refuseWith is { } reason)
            {
                screenX = 0;
                screenY = 0;
                failureReason = reason;
                return false;
            }

            screenX = mapX + _offsetX;
            screenY = mapY + _offsetY;
            failureReason = null;
            return true;
        }
    }

    /// <summary>A backend that queues nothing, the way SendInput does under load.</summary>
    private sealed class RejectingInputBackend : IInputBackend
    {
        public bool IsLive => true;
        public bool TryGetCursorPosition(out int x, out int y) { x = 0; y = 0; return true; }
        public bool MoveRelative(int dx, int dy) => false;
        public bool MoveAbsolute(int x, int y) => false;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => false;
        public bool KeyPress(ushort key, int ms = 80, ReadOnlySpan<ushort> modifiers = default) => false;
        public bool ScrollWheel(int detents) => false;
    }

    private static KeybindMap Binds(params (string Intent, ushort Key)[] binds)
    {
        string entries = string.Join(",", binds.Select(b =>
            $"\"{b.Intent}\": {{ \"virtualKey\": {b.Key}, \"label\": \"k\" }}"));
        string path = Path.Combine(Path.GetTempPath(), $"nosai-keybinds-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $"{{ \"version\": 1, \"binds\": {{ {entries} }} }}");
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

    // TrustTier is qualified because it is defined in Contracts, Gate3, Gate6 and
    // Host at once, and a file importing two of those namespaces cannot say which
    // it means. That is the shared-boundary debt the roadmap records, met here.
    private static ActionCandidate Candidate(
        ActionType type,
        int skillOrItemId = 0,
        int x = 0,
        int y = 0,
        int slot = 1,
        long entityId = 101) => new(
        Guid.NewGuid(), type, TargetFor(type, x, y, slot, entityId), skillOrItemId,
        NosAi.Runtime.Autonomy.TrustTier.Tier1_Assisted, "test");

    /// <summary>
    /// A target of the shape each action type requires; <c>0,0</c> means the
    /// position was not supplied, which an entity is allowed to say and a
    /// position is not.
    /// </summary>
    private static ActionTarget TargetFor(ActionType type, int x, int y, int slot, long entityId)
    {
        MapPoint? at = x == 0 && y == 0 ? null : new MapPoint(x, y);
        return type switch
        {
            ActionType.UseBasicAttack or ActionType.TargetEntity or ActionType.UseSkill
                => new ActionTarget.Entity(entityId, at),
            ActionType.MoveToPosition or ActionType.EmergencyFlee or ActionType.CollectGroundItem
                => new ActionTarget.Position(at ?? new MapPoint(0, 0)),
            ActionType.UseConsumable => new ActionTarget.InventorySlot(slot),
            _ => ActionTarget.None.Instance,
        };
    }

    private static (InputActionEffector Effector, RecordingInputBackend Backend) Build(
        RuntimeSafetyPolicy policy,
        KeybindMap? keybinds = null,
        IScreenProjection? projection = null)
    {
        var backend = new RecordingInputBackend();
        var gate = new GatedInputBackend(backend, () => policy);
        return (new InputActionEffector(gate, keybinds ?? KeybindMap.Empty, () => policy, projection), backend);
    }

    // ------------------------------------------------------------ the gestures

    [Fact]
    public async Task A_consumable_presses_the_key_the_operator_bound_to_its_slot()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, Binds(("consumable.4", 49)));

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseConsumable, skillOrItemId: 101, slot: 4));

        Assert.Equal(ExecutionState.Completed, result.State);
        Assert.Equal("key:49", Assert.Single(backend.Events));
    }

    [Fact]
    public async Task A_skill_presses_the_key_bound_to_that_skill_slot()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, Binds(("skill.201", 112)));

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseSkill, skillOrItemId: 201));

        Assert.Equal(ExecutionState.Completed, result.State);
        Assert.Equal("key:112", Assert.Single(backend.Events));
    }

    /// <summary>
    /// The cursor has to arrive before the click, or the click lands wherever it
    /// happened to be — a real gesture at the wrong point.
    /// </summary>
    [Theory]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.TargetEntity)]
    [InlineData(ActionType.MoveToPosition)]
    [InlineData(ActionType.EmergencyFlee)]
    public async Task A_click_action_moves_to_the_projected_point_and_then_clicks(ActionType type)
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, projection: new FakeProjection(offsetX: 1000, offsetY: 500));

        ExecutionResult result = await effector.ApplyAsync(Candidate(type, x: 125, y: 85));

        Assert.Equal(ExecutionState.Completed, result.State);
        Assert.Equal(["move-absolute:1125,585", "click:Left"], backend.Events);
    }

    // -------------------------------------------------------- the named refusals

    /// <summary>
    /// No default key. "The potion is on 1" would press some key during a real
    /// fight; the reason names the intent the operator has to configure.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_keybind_is_refused_by_name_and_nothing_is_pressed()
    {
        (InputActionEffector effector, RecordingInputBackend backend) = Build(Open);

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseConsumable, skillOrItemId: 101, slot: 4));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("keybind_not_configured:consumable.4", result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// The default projection until F2-3 exists. A fallback transform would click
    /// somewhere in the window, and the cycle would only find out at verification,
    /// after having acted.
    /// </summary>
    [Fact]
    public async Task Without_a_calibrated_projection_no_click_is_attempted()
    {
        (InputActionEffector effector, RecordingInputBackend backend) = Build(Open);

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseBasicAttack, x: 125, y: 85));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal(UncalibratedScreenProjection.NotCalibratedReason, result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// A point outside the client area is a refusal, not a click on the border.
    /// The projection's own reason is carried through rather than flattened.
    /// </summary>
    [Fact]
    public async Task A_point_the_projection_rejects_carries_its_reason_out()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, projection: new FakeProjection(refuseWith: "point_outside_client_area"));

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.MoveToPosition, x: 9999, y: 9999));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("point_outside_client_area", result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// A target with no position cannot be clicked, and 0,0 is how ActionCandidate
    /// already spells "no position" — the planner builds a consumable with exactly
    /// that. It is read as absent, not as the corner of the map.
    /// </summary>
    [Fact]
    public async Task A_target_without_a_position_is_refused_before_the_projection_is_asked()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, projection: new FakeProjection());

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseBasicAttack, x: 0, y: 0));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("target_position_unknown", result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// The planner knows there is a target and not which one until F2-2 picks the
    /// nearest observed sighting. Clicking where a placeholder pointed is the
    /// mistake the typed target exists to prevent, so it is refused by name.
    /// </summary>
    [Fact]
    public async Task An_entity_nobody_has_identified_is_refused_before_the_projection()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, projection: new FakeProjection());
        var candidate = new ActionCandidate(
            Guid.NewGuid(), ActionType.UseBasicAttack, ActionTarget.Entity.Unidentified, 0,
            NosAi.Runtime.Autonomy.TrustTier.Tier1_Assisted, "test");

        ExecutionResult result = await effector.ApplyAsync(candidate);

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("target_entity_unresolved", result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// An entity seen without a position is a different refusal from one nobody
    /// identified, and both are different from a click at 0,0.
    /// </summary>
    [Fact]
    public async Task An_identified_entity_without_a_position_is_refused_by_its_own_name()
    {
        (InputActionEffector effector, RecordingInputBackend backend) =
            Build(Open, projection: new FakeProjection());

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseBasicAttack, entityId: 313816, x: 0, y: 0));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("target_position_unknown", result.Reason);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// Named, not silently unhandled. An effector that quietly did nothing would
    /// have the cycle reported as executed with nothing behind it.
    /// </summary>
    [Theory]
    [InlineData(ActionType.CollectGroundItem)]
    [InlineData(ActionType.RestAndRecover)]
    public async Task An_action_with_no_gesture_yet_is_refused_by_name(ActionType type)
    {
        (InputActionEffector effector, RecordingInputBackend backend) = Build(Open);

        ExecutionResult result = await effector.ApplyAsync(Candidate(type, x: 10, y: 10));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal($"action_not_implemented:{type}", result.Reason);
        Assert.Empty(backend.Events);
    }

    [Fact]
    public async Task An_action_type_of_none_is_refused_rather_than_ignored()
    {
        (InputActionEffector effector, RecordingInputBackend backend) = Build(Open);

        ExecutionResult result = await effector.ApplyAsync(Candidate(ActionType.None));

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("action_type_not_executable:None", result.Reason);
        Assert.Empty(backend.Events);
    }

    // ------------------------------------------------- completed means executed

    /// <summary>
    /// The defect that made Gate 3 unable to tell the truth was a Completed with
    /// no execution behind it. SendInput reports how many events it queued, the
    /// backend returns false when that is not what was asked for, and this must
    /// stay Failed.
    /// </summary>
    [Fact]
    public async Task A_key_the_backend_did_not_queue_is_failed_and_never_completed()
    {
        var policy = Open;
        var gate = new GatedInputBackend(new RejectingInputBackend(), () => policy);
        var effector = new InputActionEffector(gate, Binds(("consumable.4", 49)), () => policy);

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.UseConsumable, skillOrItemId: 101, slot: 4));

        Assert.Equal(ExecutionState.Failed, result.State);
        Assert.False(result.Completed);
        Assert.Equal("input_not_accepted:consumable.4", result.Reason);
    }

    [Fact]
    public async Task A_click_the_backend_did_not_queue_is_failed_and_never_completed()
    {
        var policy = Open;
        var gate = new GatedInputBackend(new RejectingInputBackend(), () => policy);
        var effector = new InputActionEffector(
            gate, KeybindMap.Empty, () => policy, new FakeProjection());

        ExecutionResult result = await effector.ApplyAsync(
            Candidate(ActionType.MoveToPosition, x: 125, y: 85));

        Assert.Equal(ExecutionState.Failed, result.State);
        Assert.Equal("input_not_accepted:cursor_move", result.Reason);
    }

    // ------------------------------------------------------------- the gate

    /// <summary>
    /// The one that matters most: with the policy closed, nothing reaches the
    /// backend at all, for any action, and the cycle reports as suppressed rather
    /// than as failed or done.
    /// </summary>
    [Theory]
    [InlineData(ActionType.UseConsumable)]
    [InlineData(ActionType.UseSkill)]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.MoveToPosition)]
    [InlineData(ActionType.TargetEntity)]
    [InlineData(ActionType.EmergencyFlee)]
    public async Task With_the_policy_closed_nothing_reaches_the_backend(ActionType type)
    {
        (InputActionEffector effector, RecordingInputBackend backend) = Build(
            Closed,
            Binds(("consumable.1", 49), ("skill.0", 112)),
            new FakeProjection());

        ExecutionResult result = await effector.ApplyAsync(Candidate(type, x: 125, y: 85));

        Assert.Equal(ExecutionState.Disabled, result.State);
        Assert.True(result.SuppressedByPolicy);
        Assert.False(result.Completed);
        Assert.Empty(backend.Events);
    }

    /// <summary>
    /// The policy is read on every call, so an operator who closes the switch
    /// mid-session is obeyed at once rather than after the next restart.
    /// </summary>
    [Fact]
    public async Task Closing_the_policy_mid_session_stops_the_very_next_action()
    {
        RuntimeSafetyPolicy policy = Open;
        var backend = new RecordingInputBackend();
        var gate = new GatedInputBackend(backend, () => policy);
        var effector = new InputActionEffector(
            gate, Binds(("consumable.4", 49)), () => policy);

        Assert.Equal(
            ExecutionState.Completed,
            (await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4))).State);

        policy = Closed;

        ExecutionResult afterClosing =
            await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4));

        Assert.Equal(ExecutionState.Disabled, afterClosing.State);
        Assert.Single(backend.Events);
    }

    /// <summary>
    /// The factory already accepted a live effector; this is the composition the
    /// runtime uses, and it still refuses while the operator's switch is off.
    /// </summary>
    [Fact]
    public void The_factory_selects_the_live_effector_only_when_the_policy_is_open()
    {
        var policy = Open;
        var live = new InputActionEffector(
            new GatedInputBackend(new RecordingInputBackend(), () => policy),
            KeybindMap.Empty,
            () => policy);

        Assert.Same(live, ActionEffectorFactory.ForPolicy(Open, live));
        Assert.IsType<DisabledActionEffector>(ActionEffectorFactory.ForPolicy(Closed, live));
    }

    /// <summary>
    /// The reason the factory grew a policy <i>source</i>. The host composes its
    /// orchestrator while every switch is still off, so an effector chosen from a
    /// policy read once would stay disabled for the life of the process and the
    /// operator's switch would do nothing at all.
    /// </summary>
    [Fact]
    public async Task A_policy_source_lets_the_operator_arm_the_runtime_after_it_started()
    {
        RuntimeSafetyPolicy policy = Closed;
        var backend = new RecordingInputBackend();
        var gate = new GatedInputBackend(backend, () => policy);
        IActionEffector effector = ActionEffectorFactory.ForPolicy(
            () => policy,
            new InputActionEffector(gate, Binds(("consumable.4", 49)), () => policy));

        // Composed while everything is off, exactly as the host does it.
        Assert.False(effector.CanApply);
        ExecutionResult beforeArming =
            await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4));
        Assert.Equal(ExecutionState.Disabled, beforeArming.State);
        Assert.Empty(backend.Events);

        policy = Open;

        ExecutionResult afterArming =
            await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4));

        Assert.Equal(ExecutionState.Completed, afterArming.State);
        Assert.Equal("key:49", Assert.Single(backend.Events));
    }

    /// <summary>
    /// And disarming takes effect on the next action, which is what makes the
    /// switch an emergency stop rather than a request.
    /// </summary>
    [Fact]
    public async Task Disarming_through_the_policy_source_stops_the_next_action()
    {
        RuntimeSafetyPolicy policy = Open;
        var backend = new RecordingInputBackend();
        IActionEffector effector = ActionEffectorFactory.ForPolicy(
            () => policy,
            new InputActionEffector(
                new GatedInputBackend(backend, () => policy),
                Binds(("consumable.4", 49)),
                () => policy));

        await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4));
        policy = Closed;
        ExecutionResult stopped = await effector.ApplyAsync(Candidate(ActionType.UseConsumable, 101, slot: 4));

        Assert.Equal(ExecutionState.Disabled, stopped.State);
        Assert.Single(backend.Events);
    }

    /// <summary>
    /// A source with no effector behind it is still the disabled one: an
    /// incomplete configuration must not become a pipeline that claims to act.
    /// </summary>
    [Fact]
    public void A_policy_source_without_an_effector_is_still_disabled()
        => Assert.IsType<DisabledActionEffector>(
            ActionEffectorFactory.ForPolicy(() => Open, liveEffector: null));
}
