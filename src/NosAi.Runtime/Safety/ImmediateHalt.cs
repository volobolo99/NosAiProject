using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Security;

namespace NosAi.Runtime.Safety;

/// <summary>The two steps of an operator halt, in the order they must happen.</summary>
public interface IImmediateHaltTarget
{
    /// <summary>Turns every acting power off. Must run before the abort.</summary>
    void DisarmActingPowers(string reason);

    /// <summary>
    /// Abandons the act in flight, if any. Must run after the disarm: aborting
    /// first would leave the switches on, which is the dangerous half.
    /// </summary>
    bool AbortOpenAct(string reason);
}

/// <summary>The production target: the live safety switches and the gated input.</summary>
public sealed class RuntimeImmediateHaltTarget : IImmediateHaltTarget
{
    private readonly RuntimeSafetyController _safety;
    private readonly GatedInputBackend? _input;

    public RuntimeImmediateHaltTarget(RuntimeSafetyController safety, GatedInputBackend? input)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _input = input;
    }

    public void DisarmActingPowers(string reason)
    {
        _safety.Set(SecurityPrincipal.Operator, SafetySwitch.Execution, false, reason);
        _safety.EmergencyStop(reason);
    }

    public bool AbortOpenAct(string reason) => _input is not null && _input.AbortOpenScope(reason);
}

/// <summary>The outcome of one operator halt request.</summary>
public sealed record ImmediateHaltResult(bool Allowed, string Reason, bool ActAborted)
{
    public static ImmediateHaltResult Denied(string reason) => new(false, reason, false);
    public static ImmediateHaltResult Accepted(bool actAborted) => new(true, ImmediateHalt.AcceptedReason, actAborted);
}

/// <summary>
/// The operator's immediate halt: disarm, then abort the open act.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from the recovery breaker's own halt. This is the person at the
/// machine pulling the brake. The breaker halt is the runtime deciding it no
/// longer trusts itself; this is the operator deciding it must stop acting now.
/// </para>
/// <para>
/// Order is load-bearing. Disarming first refuses any new act; aborting second
/// releases whatever the current act already pressed. Aborting first would leave
/// the switches on between the two calls — the dangerous half still armed.
/// </para>
/// <para>
/// Idempotent: a second halt while already disarmed and with nothing in flight
/// is success, not an error. An emergency stop that could not be pressed twice
/// would be the one the operator needed most.
/// </para>
/// <para>
/// Only <see cref="SecurityPrincipal.Operator"/> may. The phone asks; it does
/// not pull this brake.
/// </para>
/// </remarks>
public static class ImmediateHalt
{
    /// <summary>The command name on the wire and in the CLI.</summary>
    public const string CommandName = "HALT";

    /// <summary>Recorded on every switch change this halt makes.</summary>
    public const string Reason = "operator_immediate_halt";

    public const string OperatorOnlyReason = "safety_switch_operator_only";
    public const string AcceptedReason = "halt_accepted";

    public static ImmediateHaltResult Execute(SecurityPrincipal principal, IImmediateHaltTarget target, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (principal != SecurityPrincipal.Operator)
            return ImmediateHaltResult.Denied(OperatorOnlyReason);

        string why = string.IsNullOrWhiteSpace(reason) ? Reason : reason;
        target.DisarmActingPowers(why);
        bool aborted = target.AbortOpenAct(why);
        return ImmediateHaltResult.Accepted(aborted);
    }

    public static ImmediateHaltResult Execute(
        SecurityPrincipal principal,
        RuntimeSafetyController safety,
        GatedInputBackend? input,
        string? reason = null)
        => Execute(principal, new RuntimeImmediateHaltTarget(safety, input), reason);
}
