using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Security;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The operator halt disarms, then aborts, and a second halt is not an error.
/// </summary>
public sealed class ImmediateHaltTests
{
    [Fact]
    public void DisarmHappensBeforeAbort()
    {
        var target = new RecordingHaltTarget();

        ImmediateHaltResult result = ImmediateHalt.Execute(SecurityPrincipal.Operator, target);

        Assert.True(result.Allowed);
        Assert.Equal(ImmediateHalt.AcceptedReason, result.Reason);
        Assert.True(result.ActAborted);
        Assert.Equal(["disarm:" + ImmediateHalt.Reason, "abort:" + ImmediateHalt.Reason], target.Calls);
    }

    [Fact]
    public void ASecondHaltIsNotAnError()
    {
        var target = new RecordingHaltTarget();

        ImmediateHaltResult first = ImmediateHalt.Execute(SecurityPrincipal.Operator, target);
        ImmediateHaltResult second = ImmediateHalt.Execute(SecurityPrincipal.Operator, target);

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.Equal(4, target.Calls.Count);
        Assert.Equal("disarm:" + ImmediateHalt.Reason, target.Calls[2]);
        Assert.Equal("abort:" + ImmediateHalt.Reason, target.Calls[3]);
    }

    [Fact]
    public void OnlyTheOperatorMayHalt()
    {
        var target = new RecordingHaltTarget();

        ImmediateHaltResult result = ImmediateHalt.Execute(SecurityPrincipal.GuardDevice, target);

        Assert.False(result.Allowed);
        Assert.Equal(ImmediateHalt.OperatorOnlyReason, result.Reason);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void TheLiveTargetDisarmsThenAbortsAnOpenScope()
    {
        var safety = new RuntimeSafetyController();
        safety.Set(SecurityPrincipal.Operator, SafetySwitch.Execution, true);
        var inner = new RecordingReleaseBackend();
        var gate = new GatedInputBackend(inner, () => safety.Policy);

        Assert.True(gate.TryBeginActuation(default, ActuationAuthority.Commanded("test"), out ActuationScope? scope, out _), "scope should open without a commit point");
        scope!.RecordKey(0x41);

        ImmediateHaltResult result = ImmediateHalt.Execute(SecurityPrincipal.Operator, safety, gate);

        Assert.True(result.Allowed);
        Assert.True(result.ActAborted);
        Assert.False(safety.Policy.LiveInputEnabled);
        Assert.False(safety.Policy.PacketInjectionEnabled);
        Assert.Contains("release-key:65", inner.Events);

        ImmediateHaltResult again = ImmediateHalt.Execute(SecurityPrincipal.Operator, safety, gate);
        Assert.True(again.Allowed);
        Assert.False(again.ActAborted);
    }

    private sealed class RecordingHaltTarget : IImmediateHaltTarget
    {
        public List<string> Calls { get; } = new();

        public void DisarmActingPowers(string reason) => Calls.Add("disarm:" + reason);

        public bool AbortOpenAct(string reason)
        {
            Calls.Add("abort:" + reason);
            return true;
        }
    }

    private sealed class RecordingReleaseBackend : IInputBackend, IInputReleaseBackend
    {
        public List<string> Events { get; } = new();
        public bool IsLive => true;

        public bool TryGetCursorPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            return true;
        }

        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => true;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default) => true;
        public bool ScrollWheel(int detents) => true;

        public bool ReleaseKey(ushort virtualKey)
        {
            Events.Add($"release-key:{virtualKey}");
            return true;
        }

        public bool ReleaseMouseButton(MouseButton button)
        {
            Events.Add($"release-button:{button}");
            return true;
        }
    }
}
