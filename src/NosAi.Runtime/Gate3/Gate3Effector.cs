using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate3;

/// <summary>How an authorised action actually ended.</summary>
public enum ExecutionState : byte
{
    /// <summary>The token was invalid, replayed or not for this candidate. Nothing was attempted.</summary>
    Refused = 0,

    /// <summary>
    /// Policy forbids live input, so nothing was attempted. Not a failure and not
    /// a success: the expected state while <see cref="RuntimeSafetyPolicy.LiveInputEnabled"/> is false.
    /// </summary>
    Disabled = 1,

    /// <summary>The effector reported the action as applied to the real client.</summary>
    Completed = 2,

    /// <summary>The effector attempted the action and it failed.</summary>
    Failed = 3
}

/// <summary>
/// Applies an authorised action to the outside world.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists so that "nothing is executed" is a real implementation with a
/// name, rather than a delay that reports completion. Gate 3 previously slept
/// 50 ms and returned success: the pipeline reported that actions had been
/// carried out when nothing had touched the client, which is exactly the
/// simulated-labelled-as-real failure the project forbids.
/// </para>
/// <para>
/// An implementation must never report <see cref="ExecutionState.Completed"/>
/// unless the action really was applied.
/// </para>
/// </remarks>
public interface IActionEffector
{
    /// <summary>Whether this effector can act at all, given the current policy.</summary>
    bool CanApply { get; }

    /// <summary>Why it cannot act; null when it can.</summary>
    string? UnavailableReason { get; }

    Task<ExecutionResult> ApplyAsync(ActionCandidate candidate, CancellationToken cancellationToken = default);
}

/// <summary>
/// The effector used whenever live input is disabled: it refuses, and says so.
/// </summary>
/// <remarks>
/// This is the default for Gate 3, because <see cref="RuntimeSafetyPolicy.SafeDefault"/>
/// keeps live input and packet injection off. Selecting it is not a limitation to
/// work around — it is the safety posture the gate is built on. What it must not
/// do is pretend, so it reports <see cref="ExecutionState.Disabled"/> and the
/// pipeline treats the cycle as not executed rather than as done.
/// </remarks>
public sealed class DisabledActionEffector : IActionEffector
{
    private readonly string _reason;

    public DisabledActionEffector(string reason = "live_input_disabled_by_policy") => _reason = reason;

    public bool CanApply => false;

    public string? UnavailableReason => _reason;

    public Task<ExecutionResult> ApplyAsync(ActionCandidate candidate, CancellationToken cancellationToken = default)
        => Task.FromResult(new ExecutionResult(
            candidate.CandidateId,
            ExecutionState.Disabled,
            ActualDurationMs: 0,
            Reason: _reason));
}

/// <summary>
/// Chooses the effector a policy permits.
/// </summary>
/// <remarks>
/// Deliberately fails closed: a policy that allows live input but supplies no
/// effector yields the disabled one rather than an ad-hoc stand-in, so an
/// incomplete configuration can never become a pipeline that claims to act.
/// </remarks>
public static class ActionEffectorFactory
{
    public static IActionEffector ForPolicy(RuntimeSafetyPolicy policy, IActionEffector? liveEffector = null)
    {
        if (!policy.LiveInputEnabled)
            return new DisabledActionEffector("live_input_disabled_by_policy");

        return liveEffector ?? new DisabledActionEffector("no_live_effector_bound");
    }
}
