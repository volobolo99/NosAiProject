using NosAi.Runtime.Safety;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate3;

/// <summary>How an authorised action actually ended.</summary>
public enum ExecutionState : byte
{
    /// <summary>
    /// Nothing was attempted, and the reason says what was missing.
    /// </summary>
    /// <remarks>
    /// Either the safety token was invalid, replayed or bound to another
    /// candidate, or the effector had no way to carry the action out — an
    /// unconfigured keybind, an uncalibrated screen projection, a target with no
    /// known position (<see cref="InputActionEffector"/>). Both are "nothing
    /// happened, and here is what to fix", which is why they share a state and
    /// are told apart by the reason.
    /// </remarks>
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

    /// <param name="token">
    /// The authorisation for <paramref name="candidate"/>, carried all the way to the
    /// boundary that emits (ADR-0020 § 4). Required by the signature on purpose: an
    /// effector that cannot receive one cannot be composed into the pipeline, so
    /// "nothing emits without an authorisation bound to this act" is a property of the
    /// types rather than of the order in which somebody happened to call them.
    /// </param>
    Task<ExecutionResult> ApplyAsync(
        ActionCandidate candidate,
        SafetyToken token,
        CancellationToken cancellationToken = default);
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

    public Task<ExecutionResult> ApplyAsync(
        ActionCandidate candidate,
        SafetyToken token,
        CancellationToken cancellationToken = default)
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

    /// <summary>
    /// The same choice, taken on every action rather than once.
    /// </summary>
    /// <remarks>
    /// The overload above reads the policy at composition time, which is right
    /// for a fixed policy and wrong for the live runtime: the host builds its
    /// orchestrator while everything is still off, so an effector selected then
    /// would stay disabled for the process's whole life and the operator's switch
    /// would do nothing. Taking a source defers the decision to the moment of
    /// acting, which is also what makes turning the switch back off an emergency
    /// stop rather than a request.
    /// </remarks>
    public static IActionEffector ForPolicy(
        Func<RuntimeSafetyPolicy> policySource, IActionEffector? liveEffector = null)
    {
        ArgumentNullException.ThrowIfNull(policySource);

        return liveEffector is null
            ? new DisabledActionEffector("no_live_effector_bound")
            : new PolicyGatedActionEffector(policySource, liveEffector);
    }
}

/// <summary>
/// Defers to a live effector only while the policy allows live input.
/// </summary>
/// <remarks>
/// Fails closed on every call, not once: a policy read at construction cannot
/// express an operator who arms the runtime after it started, or disarms it in
/// the middle of a fight.
/// </remarks>
public sealed class PolicyGatedActionEffector : IActionEffector
{
    private readonly Func<RuntimeSafetyPolicy> _policySource;
    private readonly IActionEffector _inner;

    public PolicyGatedActionEffector(Func<RuntimeSafetyPolicy> policySource, IActionEffector inner)
    {
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    private bool LiveInputAllowed => (_policySource()
        ?? throw new InvalidOperationException("The safety policy source returned null; refusing to act."))
        .LiveInputEnabled;

    /// <inheritdoc />
    public bool CanApply => LiveInputAllowed && _inner.CanApply;

    /// <inheritdoc />
    public string? UnavailableReason => LiveInputAllowed
        ? _inner.UnavailableReason
        : "live_input_disabled_by_policy";

    /// <inheritdoc />
    public Task<ExecutionResult> ApplyAsync(
        ActionCandidate candidate,
        SafetyToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(token);

        return LiveInputAllowed
            ? _inner.ApplyAsync(candidate, token, cancellationToken)
            : Task.FromResult(new ExecutionResult(
                candidate.CandidateId,
                ExecutionState.Disabled,
                ActualDurationMs: 0,
                Reason: "live_input_disabled_by_policy"));
    }
}
