// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — The authorised boundary for input injection
// ============================================================================
//
// The Safety Gate used to live inside NosTaleGameAdapter alone: anyone holding
// RuntimeComponents.InputBackend or .Humanizer injected real input with
// LiveInputEnabled false, bypassing the authorisation entirely. The gate now
// sits at the boundary, where it cannot be stepped around (ADR-0003, "the
// runtime is authoritative for safety, authorization and privileged
// execution").

using System;
using System.Threading;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.LowLevel;

/// <summary>Why an injection attempt was refused.</summary>
public sealed record InputRefusal(string Reason, DateTime AtUtc);

/// <summary>
/// Wraps a real backend and refuses every injection unless the policy allows it.
/// </summary>
/// <remarks>
/// <para>
/// Fail closed by construction: the decision is taken here, on every call, from
/// the live policy — not cached, not passed in by the caller. A refusal returns
/// false and is counted, so a runtime that silently does nothing is still
/// diagnosable from <see cref="RefusedCount"/> and <see cref="LastRefusal"/>.
/// </para>
/// <para>
/// Read-only queries (<see cref="TryGetCursorPosition"/>) are always allowed:
/// observing the desktop is not actuating it.
/// </para>
/// <para>
/// <b>The commit point (§ 2.1), and why it cannot be walked around.</b> The policy
/// answers "is live input enabled"; it says nothing about the physical world in the
/// instant of emission. When a <see cref="CommitPointValidator"/> is supplied, this
/// gate additionally requires an open <see cref="ActuationScope"/> for every
/// actuating call, and revalidates the five conditions immediately before each
/// irreversible one. There is no call that reaches the inner backend without passing
/// both, and no way to obtain the inner backend from here: a scope is the only route
/// through, and opening one costs a <see cref="CommitRequest"/>, which costs a
/// geometry stamp taken at authorisation.
/// </para>
/// <para>
/// A gate built <i>without</i> a validator is policy-only, exactly as before, and
/// <see cref="RequiresCommitPoint"/> reports which of the two it is rather than
/// leaving the difference to be inferred. That is a construction-time choice made in
/// one place — the composition root wires the validator for the autonomous path — and
/// it is not a runtime switch, because a runtime switch is what a bypass looks like.
/// The auto-calibrator is the one deliberate exception and states its own reason: it
/// is producing the calibration a commit point would need, under explicit operator
/// arming, with its own foreground check.
/// </para>
/// </remarks>
public sealed class GatedInputBackend : IInputBackend
{
    /// <summary>Reported when an act was attempted with no scope open.</summary>
    public const string CommitScopeRequiredReason = "commit_scope_required";

    /// <summary>Reported when a scope is already open. One act at a time (DOMAIN-17).</summary>
    public const string ScopeAlreadyOpenReason = "commit_scope_already_open";

    /// <summary>Reported when the inner backend cannot be asked to release.</summary>
    public const string ReleaseUnsupportedReason = "release_not_supported_by_backend";

    private readonly IInputBackend _inner;
    private readonly Func<RuntimeSafetyPolicy> _policySource;
    private readonly CommitPointValidator? _commitPoint;
    private readonly object _scopeLock = new();
    private ActuationScope? _scope;
    private long _refusedCount;
    private long _allowedCount;
    private InputRefusal? _lastRefusal;

    /// <summary>Injections refused because the policy did not allow them.</summary>
    public long RefusedCount => Interlocked.Read(ref _refusedCount);

    /// <summary>Injections the policy allowed through to the real backend.</summary>
    public long AllowedCount => Interlocked.Read(ref _allowedCount);

    public InputRefusal? LastRefusal => _lastRefusal;

    /// <summary>
    /// The last commit-point decision, or null when this gate is policy-only or
    /// has never validated. A refused decision is the last commit-point refusal
    /// a halt dump photographs.
    /// </summary>
    public CommitDecision? LastCommitDecision => _commitPoint?.LastDecision;

    /// <summary>True only when the policy currently permits live injection.</summary>
    public bool IsLive => _inner.IsLive && _policySource().LiveInputEnabled;

    public GatedInputBackend(IInputBackend inner, RuntimeSafetyPolicy policy)
        : this(inner, () => policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
    }

    /// <summary>
    /// Takes the policy through a delegate so a runtime that flips the switch at
    /// run time is obeyed immediately, with no stale copy to re-inject through.
    /// </summary>
    public GatedInputBackend(IInputBackend inner, Func<RuntimeSafetyPolicy> policySource)
        : this(inner, policySource, null)
    {
    }

    /// <param name="commitPoint">
    /// When supplied, every actuating call needs an open <see cref="ActuationScope"/>
    /// and every irreversible one is revalidated against the desktop in the instant
    /// before it is emitted. Null keeps the policy-only behaviour and says so through
    /// <see cref="RequiresCommitPoint"/>.
    /// </param>
    public GatedInputBackend(
        IInputBackend inner,
        Func<RuntimeSafetyPolicy> policySource,
        CommitPointValidator? commitPoint)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
        _commitPoint = commitPoint;
    }

    /// <summary>Whether this gate enforces the commit point as well as the policy.</summary>
    public bool RequiresCommitPoint => _commitPoint is not null;

    /// <summary>The scope currently open, or null.</summary>
    public ActuationScope? CurrentScope
    {
        get { lock (_scopeLock) return _scope; }
    }

    /// <summary>
    /// Opens the one act this gate will carry, against the geometry and scale it was
    /// authorised under.
    /// </summary>
    /// <remarks>
    /// One at a time by construction. Two concurrent acts would each be revalidating a
    /// world the other was changing, and DOMAIN-17 allows one irreversible step, not
    /// two interleaved.
    /// </remarks>
    /// <param name="authority">
    /// Under whose authority the act is emitted: a <see cref="Autonomy.SafetyToken"/>
    /// for a planned one, a named command for one a person asked for (ADR-0020 § 2).
    /// There is deliberately <b>no overload without it</b> — a rule enforced by the
    /// order of calls survives only until someone adds a shorter signature, and this is
    /// the one place both entries to the boundary already pass through.
    /// </param>
    public bool TryBeginActuation(
        in CommitRequest request,
        in ActuationAuthority authority,
        out ActuationScope? scope,
        out string? refusalReason)
    {
        // Before the lock, because an unattributable act is refused whether or not
        // anything else is in flight, and the refusal should name the authority rather
        // than whichever scope happened to be open.
        if (!authority.IsUsable(DateTime.UtcNow, out string? authorityRefusal))
        {
            scope = null;
            refusalReason = authorityRefusal;
            Refuse(refusalReason!);
            return false;
        }

        lock (_scopeLock)
        {
            if (_scope is not null)
            {
                scope = null;
                refusalReason = ScopeAlreadyOpenReason;
                Refuse(refusalReason);
                return false;
            }

            scope = new ActuationScope(this, request, authority);
            _scope = scope;
        }

        refusalReason = null;
        return true;
    }

    /// <summary>
    /// Abandons the act in flight, if any, releasing whatever it was holding down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// § 2.3 requires the operator's suspend to stop everything <i>immediately</i>, and
    /// disarming the policy alone does not: the policy is read on the way in, so it
    /// refuses the next call and says nothing about the key the current act already
    /// pressed. Without this, an emergency stop during a program that holds a key would
    /// disarm the runtime and leave the key down — the one state where stopping made
    /// things worse than not stopping.
    /// </para>
    /// <para>
    /// The release goes out through <see cref="ReleaseHeld"/>, which cannot press
    /// anything, so this is a way to let go and not a way back in. Nothing to abort is
    /// success, not an error: an emergency stop has to be callable twice.
    /// </para>
    /// </remarks>
    /// <returns>True when there was an act to abandon.</returns>
    public bool AbortOpenScope(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ActuationScope? open;
        lock (_scopeLock)
            open = _scope;

        // An already-aborted scope stays registered until its owner disposes it, so
        // "there is a scope" is not "there is an act to abandon". Reporting true twice
        // would tell an operator that a second stop caught something.
        if (open is null || open.IsAborted)
            return false;

        open.Abort(reason);
        return true;
    }

    internal void CloseScope(ActuationScope scope)
    {
        lock (_scopeLock)
        {
            if (ReferenceEquals(_scope, scope))
                _scope = null;
        }
    }

    /// <summary>
    /// Releases one press the open scope recorded. Not policy-gated, and cannot press.
    /// </summary>
    /// <remarks>
    /// The single deliberate exception to the gate, and it is bounded by what it can
    /// express rather than by who calls it: <see cref="IInputReleaseBackend"/> has no
    /// method that presses anything, so the worst this path can do is let go of a key.
    /// Gating it would mean that switching the policy off mid-act left a button held
    /// down, which is the failure the exception exists to prevent — a refused release
    /// is worse than an ungated one.
    /// </remarks>
    internal void ReleaseHeld(HeldInput input)
    {
        if (_inner is not IInputReleaseBackend releasable)
        {
            _lastRefusal = new InputRefusal(ReleaseUnsupportedReason, DateTime.UtcNow);
            return;
        }

        if (input.IsKey)
            releasable.ReleaseKey(input.VirtualKey);
        else
            releasable.ReleaseMouseButton(input.Button);
    }

    private bool Allowed()
    {
        RuntimeSafetyPolicy policy = _policySource()
            ?? throw new InvalidOperationException("The safety policy source returned null; refusing to inject input.");
        if (policy.LiveInputEnabled)
        {
            Interlocked.Increment(ref _allowedCount);
            return true;
        }
        Interlocked.Increment(ref _refusedCount);
        _lastRefusal = new InputRefusal("live_input_disabled_by_policy", DateTime.UtcNow);
        return false;
    }

    private void Refuse(string reason)
    {
        Interlocked.Increment(ref _refusedCount);
        _lastRefusal = new InputRefusal(reason, DateTime.UtcNow);
    }

    /// <summary>
    /// The gate for a reversible call: policy, and the scope being open and unaborted.
    /// </summary>
    private bool MayMove(out ActuationScope? scope)
    {
        scope = null;

        if (!Allowed())
            return false;

        if (_commitPoint is null)
            return true;

        lock (_scopeLock)
            scope = _scope;

        if (scope is null)
        {
            Refuse(CommitScopeRequiredReason);
            return false;
        }

        if (!scope.TryEnter(out string? aborted))
        {
            Refuse(aborted!);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The gate for the irreversible step: everything <see cref="MayMove"/> asks, plus
    /// the five conditions re-read now, plus the emission latency measured against them.
    /// </summary>
    /// <remarks>
    /// A refusal here aborts the scope rather than merely returning false, because
    /// § 2.3 requires the act in flight to be abandoned and not retried: whatever
    /// pressed anything gets released on the way out.
    /// </remarks>
    private bool MayCommit(out ActuationScope? scope)
    {
        if (!MayMove(out scope))
            return false;

        if (_commitPoint is null || scope is null)
            return true;

        CommitDecision decision = _commitPoint.Validate(scope.Request);
        if (!decision.IsAuthorised)
        {
            Refuse(decision.RefusalReason!);
            scope.Abort(decision.RefusalReason!);
            return false;
        }

        // The last thing before the irreversible step, so the interval this measures
        // is the one nothing else covers.
        if (!_commitPoint.MayEmit(decision, out string? tooLate, out _))
        {
            Refuse(tooLate!);
            scope.Abort(tooLate!);
            return false;
        }

        return true;
    }

    // Observation, not actuation: never gated.
    public bool TryGetCursorPosition(out int x, out int y) => _inner.TryGetCursorPosition(out x, out y);

    // Moving the cursor is reversible: it is not the step DOMAIN-17 protects, so it
    // asks the policy and the scope and not the five conditions.
    public bool MoveRelative(int dx, int dy) => MayMove(out _) && _inner.MoveRelative(dx, dy);

    public bool MoveAbsolute(int x, int y) => MayMove(out _) && _inner.MoveAbsolute(x, y);

    public bool Click(MouseButton button, int delayBetweenDownUpMs = 45)
    {
        if (!MayCommit(out ActuationScope? scope))
            return false;

        // Recorded before the press and struck off after it returns, so a call that
        // fails between the down and the up leaves the record the abort path reads.
        scope?.RecordButton(button);
        try
        {
            return _inner.Click(button, delayBetweenDownUpMs);
        }
        finally
        {
            scope?.Forget(new HeldInput(false, 0, button));
        }
    }

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
    {
        if (!MayCommit(out ActuationScope? scope))
            return false;

        if (scope is null)
            return _inner.KeyPress(virtualKey, pressDurationMs, modifiers);

        foreach (ushort modifier in modifiers)
            scope.RecordKey(modifier);
        scope.RecordKey(virtualKey);

        try
        {
            return _inner.KeyPress(virtualKey, pressDurationMs, modifiers);
        }
        finally
        {
            scope.Forget(new HeldInput(true, virtualKey, default));
            foreach (ushort modifier in modifiers)
                scope.Forget(new HeldInput(true, modifier, default));
        }
    }

    public bool ScrollWheel(int detents) => MayCommit(out _) && _inner.ScrollWheel(detents);
}

/// <summary>
/// Backend that records requests without touching the OS. It is the honest
/// stand-in for tests and dry runs: nothing reaches the desktop, and
/// <see cref="IsLive"/> says so rather than implying a real actuation happened.
/// </summary>
public sealed class RecordingInputBackend : IInputBackend, IInputReleaseBackend
{
    private readonly List<string> _events = new();
    private int _cursorX;
    private int _cursorY;

    public bool IsLive => false;

    public IReadOnlyList<string> Events => _events;

    public RecordingInputBackend(int cursorX = 0, int cursorY = 0)
    {
        _cursorX = cursorX;
        _cursorY = cursorY;
    }

    public bool TryGetCursorPosition(out int x, out int y)
    {
        x = _cursorX;
        y = _cursorY;
        return true;
    }

    public bool MoveRelative(int dx, int dy)
    {
        _cursorX += dx;
        _cursorY += dy;
        _events.Add($"move-relative:{dx},{dy}");
        return true;
    }

    public bool MoveAbsolute(int x, int y)
    {
        _cursorX = x;
        _cursorY = y;
        _events.Add($"move-absolute:{x},{y}");
        return true;
    }

    public bool Click(MouseButton button, int delayBetweenDownUpMs = 45)
    {
        _events.Add($"click:{button}");
        return true;
    }

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
    {
        string prefix = modifiers.Length == 0 ? string.Empty : string.Join("+", modifiers.ToArray()) + "+";
        _events.Add($"key:{prefix}{virtualKey}");
        return true;
    }

    public bool ScrollWheel(int detents)
    {
        _events.Add($"scroll:{detents}");
        return true;
    }

    public bool ReleaseMouseButton(MouseButton button)
    {
        _events.Add($"release-button:{button}");
        return true;
    }

    public bool ReleaseKey(ushort virtualKey)
    {
        _events.Add($"release-key:{virtualKey}");
        return true;
    }
}
