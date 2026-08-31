using NosAi.Runtime.Contracts;
using NosAi.Runtime.Security;

namespace NosAi.Runtime.Safety;

/// <summary>The individual powers the operator can switch.</summary>
public enum SafetySwitch
{
    Unknown = 0,

    /// <summary>Whether actions may be executed at all.</summary>
    Execution = 1,

    /// <summary>Whether keyboard and mouse input may reach the client.</summary>
    LiveInput = 2,

    /// <summary>Whether packets may be put on the wire toward the game server.</summary>
    PacketInjection = 3,

    /// <summary>Whether an action requires the client to be healthy first.</summary>
    RequireClientHealthy = 4,

    /// <summary>Whether an action requires the paired phone's approval.</summary>
    RequireGuardApproval = 5
}

/// <summary>One recorded change to the safety state.</summary>
/// <remarks>
/// Kept so the operator can see not just what is on, but when it was turned on and
/// by whom. A switch that changed without a trace is one nobody can account for.
/// </remarks>
public sealed record SafetySwitchChange(
    DateTime AtUtc,
    SecurityPrincipal Principal,
    SafetySwitch Switch,
    bool From,
    bool To,
    string Reason);

/// <summary>
/// The live safety state, and the only way to change it.
/// </summary>
/// <remarks>
/// <para>
/// The policy used to be an immutable record fixed at <c>SafeDefault</c>, so
/// execution, live input and packet injection were off with no way to turn them
/// on: the operator had no switch, only a hardcoded refusal. They are switches
/// now, and the operator holds them.
/// </para>
/// <para>
/// What did not become unrestricted is <i>who</i> may flip them. Only
/// <see cref="SecurityPrincipal.Operator"/> can — the paired phone cannot arm the
/// PC, which is the same line ADR-0014 drew for capture and memory: widening what
/// the runtime can do does not widen who may ask. Every change is authorised, and
/// every change is recorded.
/// </para>
/// <para>
/// Defaults stay off. A runtime that started able to act would act before the
/// operator had decided it should; turning it on is one call, and
/// <c>--unlock-execution</c> arms it from boot for anyone who wants that.
/// </para>
/// </remarks>
public sealed class RuntimeSafetyController
{
    private readonly IRuntimeAuthorizationPolicy _authorization;
    private readonly object _sync = new();
    private readonly List<SafetySwitchChange> _history = new();
    private RuntimeSafetyPolicy _policy;

    /// <summary>Raised after every accepted change, for auditing and the UI.</summary>
    public event Action<SafetySwitchChange>? Changed;

    public RuntimeSafetyController(RuntimeSafetyPolicy? initial = null, IRuntimeAuthorizationPolicy? authorization = null)
    {
        _policy = initial ?? RuntimeSafetyPolicy.SafeDefault;
        _authorization = authorization ?? new Gate1AuthorizationPolicy();
    }

    /// <summary>The state in force right now.</summary>
    public RuntimeSafetyPolicy Policy
    {
        get { lock (_sync) return _policy; }
    }

    /// <summary>
    /// Whether anything may execute.
    /// </summary>
    /// <remarks>
    /// Derived, not stored: execution is on when the operator has armed at least one
    /// way to act. A mode that claimed "enabled" while every power behind it was off
    /// would be a label, not a fact.
    /// </remarks>
    public bool ExecutionEnabled
    {
        get { lock (_sync) return _policy.LiveInputEnabled || _policy.PacketInjectionEnabled; }
    }

    /// <summary>The execution mode as the snapshot reports it.</summary>
    public string ExecutionMode => ExecutionEnabled ? "enabled_by_operator" : "disabled_by_operator";

    /// <summary>Every change so far, oldest first.</summary>
    public IReadOnlyList<SafetySwitchChange> History
    {
        get { lock (_sync) return _history.ToArray(); }
    }

    /// <summary>Reads one switch.</summary>
    public bool Read(SafetySwitch which)
    {
        lock (_sync) return ReadLocked(which);
    }

    /// <summary>
    /// Sets one switch, if the caller may.
    /// </summary>
    /// <remarks>
    /// Returns the authorization decision rather than a bare bool so a refusal
    /// carries its reason all the way to the operator's screen.
    /// </remarks>
    public AuthorizationDecision Set(SecurityPrincipal principal, SafetySwitch which, bool value, string reason = "operator_request")
    {
        if (which == SafetySwitch.Unknown || !Enum.IsDefined(which))
            return AuthorizationDecision.Deny(principal, RuntimeCapability.Unknown, "unknown_switch");

        // Arming a power is itself a privileged act: it is authorised as the
        // capability it unlocks, so the phone cannot arm what it may not use.
        var capability = CapabilityFor(which);
        var decision = _authorization.Evaluate(principal, RuntimeCapability.RequestCommand, TrustTier.Tier1, TrustTier.Tier4);
        if (!decision.Allowed)
            return AuthorizationDecision.Deny(principal, capability, decision.Reason);

        // Only the operator may change the safety state, whatever else they hold.
        if (principal != SecurityPrincipal.Operator)
            return AuthorizationDecision.Deny(principal, capability, "safety_switch_operator_only");

        SafetySwitchChange? change = null;
        lock (_sync)
        {
            bool current = ReadLocked(which);
            if (current != value)
            {
                _policy = WriteLocked(which, value);
                change = new SafetySwitchChange(DateTime.UtcNow, principal, which, current, value, reason);
                _history.Add(change);
            }
        }

        if (change is not null)
            Changed?.Invoke(change);

        return AuthorizationDecision.Allow(principal, capability, value ? "switch_enabled" : "switch_disabled");
    }

    /// <summary>Turns every acting power off at once.</summary>
    /// <remarks>
    /// The control an operator needs most and should never have to assemble from
    /// three separate calls under pressure. The guards stay on.
    /// </remarks>
    public void EmergencyStop(string reason = "emergency_stop")
    {
        Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, false, reason);
        Set(SecurityPrincipal.Operator, SafetySwitch.PacketInjection, false, reason);
    }

    private bool ReadLocked(SafetySwitch which) => which switch
    {
        SafetySwitch.Execution => _policy.LiveInputEnabled || _policy.PacketInjectionEnabled,
        SafetySwitch.LiveInput => _policy.LiveInputEnabled,
        SafetySwitch.PacketInjection => _policy.PacketInjectionEnabled,
        SafetySwitch.RequireClientHealthy => _policy.RequireClientHealthy,
        SafetySwitch.RequireGuardApproval => _policy.RequireGuardApproval,
        _ => false
    };

    private RuntimeSafetyPolicy WriteLocked(SafetySwitch which, bool value) => which switch
    {
        // Execution is the pair of acting powers, so setting it sets both.
        SafetySwitch.Execution => _policy with { LiveInputEnabled = value, PacketInjectionEnabled = value },
        SafetySwitch.LiveInput => _policy with { LiveInputEnabled = value },
        SafetySwitch.PacketInjection => _policy with { PacketInjectionEnabled = value },
        SafetySwitch.RequireClientHealthy => _policy with { RequireClientHealthy = value },
        SafetySwitch.RequireGuardApproval => _policy with { RequireGuardApproval = value },
        _ => _policy
    };

    private static RuntimeCapability CapabilityFor(SafetySwitch which) => which switch
    {
        SafetySwitch.LiveInput => RuntimeCapability.SendLiveInput,
        SafetySwitch.PacketInjection => RuntimeCapability.InjectPacket,
        SafetySwitch.Execution => RuntimeCapability.ExecuteGameAction,
        _ => RuntimeCapability.RequestCommand
    };
}
