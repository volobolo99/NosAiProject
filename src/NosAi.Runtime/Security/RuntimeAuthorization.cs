using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Security;

/// <summary>Who is asking the runtime for something.</summary>
/// <remarks>
/// The runtime had no notion of a caller's identity: everything arrived as an
/// action with a required trust tier, and nothing recorded <i>who</i> wanted it.
/// A tier answers "how dangerous is this"; a principal answers "who may ask", and
/// the two are not the same question.
/// </remarks>
public enum SecurityPrincipal
{
    /// <summary>Not established. Never granted anything.</summary>
    Unknown = 0,

    /// <summary>The person at the machine, through the operator surface.</summary>
    Operator = 1,

    /// <summary>The paired phone, once the Guard channel authenticated it.</summary>
    GuardDevice = 2,

    /// <summary>The runtime's own planning loop, acting without a person.</summary>
    AutonomousAgent = 3,

    /// <summary>An internal subsystem asking on the runtime's behalf.</summary>
    Subsystem = 4
}

/// <summary>What may be asked for.</summary>
/// <remarks>
/// Deliberately finer than "execute": reading the client's traffic and driving the
/// client are different powers with different consequences, and collapsing them
/// into one flag is how a diagnostic read quietly becomes an action.
/// </remarks>
public enum RuntimeCapability
{
    /// <summary>Not established. Never granted.</summary>
    Unknown = 0,

    /// <summary>Read the classified Gate 1 snapshot.</summary>
    ObserveSnapshot = 1,

    /// <summary>Ask for a command. Asking is not doing; the runtime still decides.</summary>
    RequestCommand = 2,

    /// <summary>Act inside the game.</summary>
    ExecuteGameAction = 3,

    /// <summary>Send keyboard or mouse input to the client.</summary>
    SendLiveInput = 4,

    /// <summary>Put packets on the wire toward the game server.</summary>
    InjectPacket = 5,

    /// <summary>Capture and read the client's network traffic (ADR-0014).</summary>
    ReadGameTraffic = 6,

    /// <summary>Read the client process's memory (ADR-0014).</summary>
    ReadProcessMemory = 7
}

/// <summary>
/// An authorization answer, with the reason attached.
/// </summary>
/// <remarks>
/// The reason is the point. <c>SafetyGate</c> used to return a bare <c>false</c>,
/// so "denied because Gate 1 disables execution", "denied because the trust tier
/// is too low" and "denied because this principal was never granted it" were
/// indistinguishable — to the operator and to a test. A refusal that cannot be
/// explained cannot be trusted either.
/// </remarks>
public sealed record AuthorizationDecision(
    bool Allowed,
    string Reason,
    SecurityPrincipal Principal,
    RuntimeCapability Capability)
{
    public static AuthorizationDecision Deny(SecurityPrincipal principal, RuntimeCapability capability, string reason) =>
        new(false, reason, principal, capability);

    public static AuthorizationDecision Allow(SecurityPrincipal principal, RuntimeCapability capability, string reason) =>
        new(true, reason, principal, capability);

    public override string ToString() =>
        $"{(Allowed ? "ALLOW" : "DENY")} {Principal}/{Capability}: {Reason}";
}

/// <summary>Decides what a principal may do. The runtime is the only authority (ADR-0003).</summary>
public interface IRuntimeAuthorizationPolicy
{
    /// <summary>
    /// Evaluates one request.
    /// </summary>
    /// <param name="grantedTier">
    /// The highest trust tier the caller currently holds. A capability can still be
    /// refused above it even when the principal would otherwise be allowed.
    /// </param>
    AuthorizationDecision Evaluate(
        SecurityPrincipal principal,
        RuntimeCapability capability,
        TrustTier requiredTier,
        TrustTier grantedTier);
}

/// <summary>
/// The policy in force while Gate 1 is the operating level.
/// </summary>
/// <remarks>
/// <para>
/// It encodes what the runtime already did — nothing executes — but as a stated
/// policy rather than a hardcoded <c>false</c>, so every refusal names its cause
/// and every rule is testable on its own.
/// </para>
/// <para>
/// <b>Fail-closed by construction.</b> An unknown principal, an unknown capability
/// and anything not explicitly granted are denied. New enum members are therefore
/// denied by default: a capability added without a matching rule cannot be
/// accidentally permitted, which is the failure mode an allow-list exists to stop.
/// </para>
/// <para>
/// <b>The UI is not policy.</b> The operator surface and the phone may <i>ask</i>;
/// this decides. Nothing in a client can widen what a principal holds, and
/// ADR-0014 widened the available data paths without widening this authority.
/// </para>
/// </remarks>
public sealed class Gate1AuthorizationPolicy : IRuntimeAuthorizationPolicy
{
    /// <summary>Reason given whenever an action inside the game is refused.</summary>
    public const string ExecutionDisabledReason = "execution_disabled_in_gate1";

    public AuthorizationDecision Evaluate(
        SecurityPrincipal principal,
        RuntimeCapability capability,
        TrustTier requiredTier,
        TrustTier grantedTier)
    {
        // An unidentified caller gets nothing, before any other rule is consulted.
        if (principal == SecurityPrincipal.Unknown)
            return AuthorizationDecision.Deny(principal, capability, "unknown_principal");

        if (capability == RuntimeCapability.Unknown || !Enum.IsDefined(capability))
            return AuthorizationDecision.Deny(principal, capability, "unknown_capability");

        // Execution is off at this gate, whoever asks and whatever tier they hold.
        // Checked before the grant table so the reason is the real one: an operator
        // refused here must read "Gate 1", not "not granted".
        if (IsExecution(capability))
            return AuthorizationDecision.Deny(principal, capability, ExecutionDisabledReason);

        if (!IsGranted(principal, capability))
            return AuthorizationDecision.Deny(principal, capability, "capability_not_granted");

        // Tier last: the principal may hold the capability and still be below the
        // trust this particular action demands.
        if (grantedTier < requiredTier)
            return AuthorizationDecision.Deny(principal, capability,
                $"trust_tier_insufficient:required_{requiredTier}_granted_{grantedTier}");

        return AuthorizationDecision.Allow(principal, capability, "granted");
    }

    /// <summary>Powers that act on the game rather than observe it.</summary>
    private static bool IsExecution(RuntimeCapability capability) => capability
        is RuntimeCapability.ExecuteGameAction
        or RuntimeCapability.SendLiveInput
        or RuntimeCapability.InjectPacket;

    /// <summary>
    /// The allow-list. Everything absent from it is denied.
    /// </summary>
    /// <remarks>
    /// The phone is deliberately narrow. It is an operator's screen, and a stolen
    /// or spoofed device must not be able to make the PC start capturing traffic or
    /// reading memory — powers that belong to the person at the machine.
    /// </remarks>
    private static bool IsGranted(SecurityPrincipal principal, RuntimeCapability capability) => principal switch
    {
        SecurityPrincipal.Operator => capability
            is RuntimeCapability.ObserveSnapshot
            or RuntimeCapability.RequestCommand
            or RuntimeCapability.ReadGameTraffic
            or RuntimeCapability.ReadProcessMemory,

        SecurityPrincipal.GuardDevice => capability
            is RuntimeCapability.ObserveSnapshot
            or RuntimeCapability.RequestCommand,

        SecurityPrincipal.AutonomousAgent => capability
            is RuntimeCapability.ObserveSnapshot,

        SecurityPrincipal.Subsystem => capability
            is RuntimeCapability.ObserveSnapshot
            or RuntimeCapability.ReadGameTraffic,

        _ => false
    };
}
