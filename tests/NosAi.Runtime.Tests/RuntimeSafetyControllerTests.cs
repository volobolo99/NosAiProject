using NosAi.Runtime.Contracts;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Security;
using NosAi.Runtime.Testing;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The operator's switches over execution, live input and packet injection.
/// </summary>
/// <remarks>
/// These powers used to be hardcoded off, so the operator had a refusal rather
/// than a control. They are switches now; what stayed restricted is who may flip
/// them and whether every flip is recorded.
/// </remarks>
public sealed class RuntimeSafetyControllerTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeSafetyControllerTests(ITestOutputHelper output) => _output = output;

    private static RuntimeSafetyController New() => new();

    // ------------------------------------------------------------- defaults

    [Fact]
    public void EverythingActingStartsOff()
    {
        // A runtime that came up armed would act before the operator had decided
        // it should.
        var controller = New();

        // Published to the operator's test page: the point of this check is not
        // that it passed but what the switches actually read at startup.
        Evidence.Live(_output, "liveInputEnabled", controller.Policy.LiveInputEnabled);
        Evidence.Live(_output, "packetInjectionEnabled", controller.Policy.PacketInjectionEnabled);
        Evidence.Live(_output, "executionMode", controller.ExecutionMode);
        Evidence.Live(_output, "requireClientHealthy", controller.Policy.RequireClientHealthy);
        Evidence.Live(_output, "requireGuardApproval", controller.Policy.RequireGuardApproval);

        Assert.False(controller.Policy.LiveInputEnabled);
        Assert.False(controller.Policy.PacketInjectionEnabled);
        Assert.False(controller.ExecutionEnabled);
        Assert.Equal("disabled_by_operator", controller.ExecutionMode);
    }

    [Fact]
    public void TheGuardsStartOn()
    {
        var controller = New();

        Assert.True(controller.Policy.RequireClientHealthy);
        Assert.True(controller.Policy.RequireGuardApproval);
    }

    // ------------------------------------------------------------ the switch

    [Fact]
    public void TheOperatorCanArmAndDisarmLiveInput()
    {
        var controller = New();

        Assert.True(controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true).Allowed);
        Assert.True(controller.Policy.LiveInputEnabled);
        Assert.Equal("enabled_by_operator", controller.ExecutionMode);

        Assert.True(controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, false).Allowed);
        Assert.False(controller.Policy.LiveInputEnabled);
        Assert.Equal("disabled_by_operator", controller.ExecutionMode);
    }

    [Fact]
    public void TheExecutionSwitchMovesBothActingPowers()
    {
        var controller = New();

        controller.Set(SecurityPrincipal.Operator, SafetySwitch.Execution, true);

        Assert.True(controller.Policy.LiveInputEnabled);
        Assert.True(controller.Policy.PacketInjectionEnabled);
    }

    [Fact]
    public void ExecutionModeReflectsTheSwitchesRatherThanALabel()
    {
        // The snapshot used to say "disabled_in_gate1" whatever was armed. A mode
        // that does not track the state is a label, not a fact.
        var controller = New();
        controller.Set(SecurityPrincipal.Operator, SafetySwitch.PacketInjection, true);

        Assert.True(controller.ExecutionEnabled);
        Assert.Equal("enabled_by_operator", controller.ExecutionMode);
    }

    // ---------------------------------------------------------- who may flip

    [Theory]
    [InlineData(SecurityPrincipal.GuardDevice)]
    [InlineData(SecurityPrincipal.AutonomousAgent)]
    [InlineData(SecurityPrincipal.Subsystem)]
    [InlineData(SecurityPrincipal.Unknown)]
    public void OnlyTheOperatorMayChangeTheSafetyState(SecurityPrincipal principal)
    {
        // The phone is an operator's screen. A stolen or spoofed device must not be
        // able to arm the PC — the same line ADR-0014 drew for capture and memory.
        var controller = New();

        var decision = controller.Set(principal, SafetySwitch.LiveInput, true);

        Assert.False(decision.Allowed);
        Assert.False(controller.Policy.LiveInputEnabled);
    }

    [Fact]
    public void ARefusalCarriesItsReason()
    {
        var controller = New();

        var decision = controller.Set(SecurityPrincipal.GuardDevice, SafetySwitch.LiveInput, true);

        Assert.False(decision.Allowed);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact]
    public void AnUnknownSwitchIsRefused()
    {
        var controller = New();

        var decision = controller.Set(SecurityPrincipal.Operator, (SafetySwitch)999, true);

        Assert.False(decision.Allowed);
        Assert.Equal("unknown_switch", decision.Reason);
    }

    // ------------------------------------------------------------- the audit

    [Fact]
    public void EveryChangeIsRecordedWithItsBeforeAndAfter()
    {
        // A switch that changed without a trace is one nobody can account for.
        var controller = New();

        controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true, "test_reason");

        var change = Assert.Single(controller.History);
        Assert.Equal(SafetySwitch.LiveInput, change.Switch);
        Assert.False(change.From);
        Assert.True(change.To);
        Assert.Equal(SecurityPrincipal.Operator, change.Principal);
        Assert.Equal("test_reason", change.Reason);
    }

    [Fact]
    public void SettingAValueItAlreadyHasRecordsNothing()
    {
        // History is a record of changes, not of requests: filling it with no-ops
        // would bury the ones that mattered.
        var controller = New();

        controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, false);

        Assert.Empty(controller.History);
    }

    [Fact]
    public void AChangeRaisesTheEventForAuditing()
    {
        var controller = New();
        SafetySwitchChange? observed = null;
        controller.Changed += c => observed = c;

        controller.Set(SecurityPrincipal.Operator, SafetySwitch.PacketInjection, true);

        Assert.NotNull(observed);
        Assert.Equal(SafetySwitch.PacketInjection, observed!.Switch);
    }

    [Fact]
    public void ARefusedChangeIsNotRecordedAsIfItHappened()
    {
        var controller = New();

        controller.Set(SecurityPrincipal.GuardDevice, SafetySwitch.LiveInput, true);

        Assert.Empty(controller.History);
    }

    // ------------------------------------------------------ emergency stop

    [Fact]
    public void TheEmergencyStopDisarmsEveryActingPower()
    {
        var controller = New();
        controller.Set(SecurityPrincipal.Operator, SafetySwitch.Execution, true);

        controller.EmergencyStop();

        Assert.False(controller.Policy.LiveInputEnabled);
        Assert.False(controller.Policy.PacketInjectionEnabled);
        Assert.Equal("disabled_by_operator", controller.ExecutionMode);
    }

    [Fact]
    public void TheEmergencyStopLeavesTheGuardsOn()
    {
        // Disarming must not also switch off the checks that keep an action from
        // running against an unhealthy client.
        var controller = New();
        controller.EmergencyStop();

        Assert.True(controller.Policy.RequireClientHealthy);
        Assert.True(controller.Policy.RequireGuardApproval);
    }

    // ------------------------------------------ the switch reaches the input

    [Fact]
    public void TheInputBackendFollowsTheSwitchWhileRunning()
    {
        // The point of reading the policy per call: arming takes effect at once,
        // and so does the emergency stop. A backend that captured the policy at
        // construction would keep injecting after the operator pulled the switch.
        var controller = New();
        var recorder = new RecordingInputBackend();
        var gated = new GatedInputBackend(recorder, () => controller.Policy);

        Assert.False(gated.KeyPress(0x0D));
        Assert.Empty(recorder.Keys);
        Assert.Equal("live_input_disabled_by_policy", gated.LastRefusal!.Reason);

        controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true);
        Assert.True(gated.KeyPress(0x0D));
        Assert.Single(recorder.Keys);

        controller.EmergencyStop();
        Assert.False(gated.KeyPress(0x0D));
        Assert.Single(recorder.Keys);
    }

    [Fact]
    public void ObservationIsNeverGatedByTheExecutionSwitch()
    {
        // Reading where the cursor is tells the operator what is happening; it is
        // not an action, and disarming execution must not blind the runtime.
        var controller = New();
        var gated = new GatedInputBackend(new RecordingInputBackend(), () => controller.Policy);

        Assert.True(gated.TryGetCursorPosition(out _, out _));
        Assert.Equal(0, gated.RefusedCount);
    }

    [Fact]
    public void RefusalsAndPassesAreBothCounted()
    {
        var controller = New();
        var gated = new GatedInputBackend(new RecordingInputBackend(), () => controller.Policy);

        gated.MoveAbsolute(10, 10);
        controller.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true);
        gated.MoveAbsolute(20, 20);

        Assert.Equal(1, gated.RefusedCount);
        Assert.Equal(1, gated.AllowedCount);
    }

    [Fact]
    public void TheComposedRuntimeExposesTheLivePolicy()
    {
        // The snapshot reads RuntimeComponents.SafetyPolicy; if that were captured
        // once it would report the state at startup forever.
        var components = RuntimeComposition.CreateSafe();

        Assert.False(components.SafetyPolicy.LiveInputEnabled);
        components.Safety.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true);
        Assert.True(components.SafetyPolicy.LiveInputEnabled);
    }

    [Fact]
    public void TheComposedInputBackendIsGatedNotRaw()
    {
        // Handing out a raw Win32 backend would let any holder of RuntimeComponents
        // inject regardless of the switch.
        var components = RuntimeComposition.CreateSafe();

        Assert.IsType<GatedInputBackend>(components.InputBackend);
        Assert.False(components.InputBackend.IsLive);
        Assert.True(((GatedInputBackend)components.InputBackend).RequiresCommitPoint);
    }

    /// <summary>An input backend that records instead of touching the desktop.</summary>
    private sealed class RecordingInputBackend : IInputBackend
    {
        public List<ushort> Keys { get; } = new();
        public List<(int X, int Y)> Moves { get; } = new();
        public bool IsLive => true;

        public bool TryGetCursorPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            return true;
        }

        public bool MoveRelative(int dx, int dy)
        {
            Moves.Add((dx, dy));
            return true;
        }

        public bool MoveAbsolute(int x, int y)
        {
            Moves.Add((x, y));
            return true;
        }

        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;

        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
        {
            Keys.Add(virtualKey);
            return true;
        }

        public bool ScrollWheel(int detents) => true;
    }
}
