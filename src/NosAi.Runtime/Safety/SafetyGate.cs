using NosAi.Runtime.Contracts;
using NosAi.Runtime.Security;

namespace NosAi.Runtime.Safety;

/// <summary>
/// The runtime's authority over whether an action may happen (ADR-0003).
/// </summary>
/// <remarks>
/// <para>
/// The outcome is unchanged: nothing executes at Gate 1. What changed is that the
/// refusal is now <i>reasoned</i>. This used to be a bare <c>return false</c> with
/// a comment, so an operator — and a test — could not tell "Gate 1 disables
/// execution" from "the guard refused" from "the trust tier is too low". A gate
/// whose refusals are indistinguishable cannot be audited, and a gate that cannot
/// be audited is trusted on faith.
/// </para>
/// <para>
/// The guard's own verdict still comes first: a candidate the guard rejected is
/// refused without consulting the policy, because the policy answers a different
/// question and cannot overturn a rejection.
/// </para>
/// </remarks>
public sealed class SafetyGate : ISafetyGate
{
    private readonly IRuntimeAuthorizationPolicy _policy;
    private readonly SecurityPrincipal _principal;
    private readonly TrustTier _grantedTier;

    /// <summary>The gate as the runtime composes it: Gate 1 policy, autonomous caller.</summary>
    /// <remarks>
    /// The default principal is the planning loop, not the operator: an action
    /// arriving through the orchestrator has no person behind it, and assuming one
    /// would grant it more than it should have.
    /// </remarks>
    public SafetyGate()
        : this(new Gate1AuthorizationPolicy(), SecurityPrincipal.AutonomousAgent, TrustTier.Tier1)
    {
    }

    public SafetyGate(IRuntimeAuthorizationPolicy policy, SecurityPrincipal principal, TrustTier grantedTier)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _principal = principal;
        _grantedTier = grantedTier;
    }

    /// <summary>The last decision made, for logging and for the operator surface.</summary>
    /// <remarks>
    /// Exposed so a caller that only gets a bool can still report why. It is a
    /// diagnostic, never an input: nothing reads this to decide anything.
    /// </remarks>
    public AuthorizationDecision? LastDecision { get; private set; }

    public bool Authorize(CandidateAction action, GuardDecision guardDecision)
        => Evaluate(action, guardDecision).Allowed;

    /// <summary>Authorizes and returns the reason with the answer.</summary>
    public AuthorizationDecision Evaluate(CandidateAction action, GuardDecision guardDecision)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(guardDecision);

        AuthorizationDecision decision;

        if (!guardDecision.Allowed)
        {
            decision = AuthorizationDecision.Deny(
                _principal, CapabilityFor(action.Kind), $"guard_refused:{guardDecision.Reason}");
        }
        else
        {
            decision = _policy.Evaluate(_principal, CapabilityFor(action.Kind), action.RequiredTrustTier, _grantedTier);
        }

        LastDecision = decision;
        return decision;
    }

    /// <summary>
    /// The power an action needs. Every action kind needs execution.
    /// </summary>
    /// <remarks>
    /// Including <see cref="ActionKind.NoOp"/>, deliberately. This gate authorises
    /// <i>actions to be performed</i>, and at Gate 1 none are — mapping the no-op
    /// to observation instead would have turned a gate that always refused into one
    /// that sometimes permits, which is a loosening of a safety boundary smuggled in
    /// behind a refactor. Observation does not pass through here at all.
    /// </remarks>
    private static RuntimeCapability CapabilityFor(ActionKind kind) => RuntimeCapability.ExecuteGameAction;
}
