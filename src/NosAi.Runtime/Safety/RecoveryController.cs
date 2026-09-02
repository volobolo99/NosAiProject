using System.Collections.Generic;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

/// <summary>What the runtime should do after a cycle failed verification.</summary>
public enum RecoveryStrategy : byte
{
    Retry = 0,
    Replan = 1,
    DegradedReplan = 2,
    Cooling = 3,
    HaltAndAlert = 4
}

/// <summary>How the recovery controller is currently treating the runtime.</summary>
/// <remarks>
/// The state is what decides whether an action may be attempted at all. It is
/// separate from <see cref="RuntimeMode"/>, which says what the runtime is doing,
/// and from <see cref="TrustTier"/>, which says what it is allowed to do: a
/// breaker that is cooling down is not the same fact as a runtime that is stopped,
/// and collapsing the two is how the previous version lost the distinction between
/// "recovered" and "the last thing happened to work".
/// </remarks>
public enum RecoveryState : byte
{
    /// <summary>Full speed. Actions flow, outcomes are recorded.</summary>
    Closed = 0,

    /// <summary>Degraded but still acting.</summary>
    Throttled = 1,

    /// <summary>Not acting. Waiting out the cooldown before it will try again.</summary>
    Halted = 2,

    /// <summary>On trial: one action at a time, and any failure halts it again.</summary>
    Probing = 3
}

/// <summary>
/// The photograph taken at the instant the breaker stops trusting itself.
/// </summary>
/// <remarks>
/// Raised once per transition into <see cref="RecoveryState.Halted"/>, never on a
/// failure that arrives while already halted, and never on a timer. Failures
/// already inside the halt are recorded against the window; they are not a new
/// decision to stop.
/// </remarks>
public sealed record RecoveryHaltTransition(
    RecoveryState PreviousState,
    RecoveryState NewState,
    DateTimeOffset TransitionedAtUtc,
    IReadOnlyList<bool> FailureWindow,
    int FailuresInWindow,
    int WindowOccupancy,
    int Halts,
    TimeSpan CurrentCooldown);

/// <summary>
/// Escalates the response to repeated failures, giving up autonomy as it goes, and
/// makes the way back a trial rather than a single lucky outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaced, and why.</b> The escalation used to be driven by a count
/// of <i>consecutive</i> failures, and the way back was
/// <c>ResetFailures()</c> — one success set the count to zero. Both gates then set
/// <c>RuntimeMode.Normal</c> themselves on any confirmed cycle. Together those made
/// the ladder unable to see the failure mode it exists for: a run that fails half
/// the time. Ten successes alternating with nine failures never reached even the
/// first rung, because no two failures were ever adjacent, and after a halt a
/// single success restored full speed with nothing between the halt and the next
/// unrestricted act. <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 6.1 names
/// this as the thing to check; this is the answer to it.
/// </para>
/// <para>
/// <b>The window.</b> Outcomes go into a fixed-length sliding window and the rungs
/// are read off how many of the last <see cref="WindowSize"/> attempts failed. A
/// success no longer erases the history that led to a halt; it only pushes the
/// oldest outcome out of the window. Recent failures therefore have to be *aged
/// out* by a run of clean work, which is the property "one success clears it" was
/// silently substituting for.
/// </para>
/// <para>
/// <b>The trial.</b> Once escalated, the state never improves on its own. After the
/// cooldown expires the breaker allows exactly one action — <see cref="Probing"/> —
/// and admits no second one until that one has been resolved. Full speed returns
/// only after <see cref="ProbeSuccessesToClose"/> consecutive probe successes. One
/// probe failure halts it again with the cooldown doubled, so a runtime that is
/// half broken settles into long waits instead of oscillating at full speed.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It never raises trust.
/// <see cref="TrustBoundary"/> is one-way by construction and stays that way: a
/// component that has been failing does not get to decide it is trustworthy again.
/// So closing the breaker after a halt restores the <see cref="RuntimeMode"/> and
/// nothing else — if the halt also dropped trust to
/// <see cref="TrustTier.Tier0_ReadOnly"/>, the <see cref="ActionTokenIssuer"/> keeps
/// refusing until whoever is watching restores it. That asymmetry is intended and
/// is the reason this class has no method whose name would let it be mistaken for
/// an escalation.
/// </para>
/// </remarks>
public sealed class RecoveryController
{
    /// <summary>How many recent attempts the rungs are read from.</summary>
    /// <remarks>
    /// Long enough that a failure has to be followed by real work to leave the
    /// window rather than by one lucky retry, short enough that a fault fixed an
    /// hour ago is not still being held against the runtime.
    /// </remarks>
    public const int DefaultWindowSize = 20;

    /// <summary>Consecutive probe successes needed before full speed returns.</summary>
    /// <remarks>
    /// More than one, because one is exactly the evidence the counter accepted and
    /// exactly the evidence an intermittent fault supplies about half the time.
    /// </remarks>
    public const int DefaultProbeSuccessesToClose = 3;

    /// <summary>The wait after the first halt. It doubles with each halt after it.</summary>
    public static readonly TimeSpan DefaultBaseCooldown = TimeSpan.FromSeconds(5);

    /// <summary>The ceiling the doubling stops at.</summary>
    /// <remarks>
    /// A cap, not a reset: the wait stops growing but never shrinks on its own. Only
    /// closing the breaker, or an operator reset, puts it back to the base.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxCooldown = TimeSpan.FromMinutes(5);

    private readonly TrustBoundary _trustBoundary;
    private readonly TimeProvider _clock;
    private readonly int _maxRetries;
    private readonly int _windowSize;
    private readonly int _probeSuccessesToClose;
    private readonly TimeSpan _baseCooldown;
    private readonly TimeSpan _maxCooldown;
    private readonly object _lock = new();

    /// <summary>The last outcomes, oldest first. True is a failure.</summary>
    private readonly Queue<bool> _window;

    private int _failuresInWindow;
    private int _consecutiveFailures;
    private RecoveryState _state = RecoveryState.Closed;
    private int _halts;
    private DateTimeOffset _cooldownEndsAtUtc;
    private int _probeSuccesses;
    private bool _probeOutstanding;

    /// <summary>
    /// Raised after a fresh transition into <see cref="RecoveryState.Halted"/>.
    /// Subscribers see the window as it stood at that instant.
    /// </summary>
    public event Action<RecoveryHaltTransition>? Halted;

    /// <param name="maxRetries">
    /// Failures tolerated inside the window before the ladder starts giving up
    /// autonomy. The rungs are unchanged from the consecutive-count version: at
    /// <paramref name="maxRetries"/> + 1 the runtime degrades, above that it halts.
    /// What changed is that they are now counted over the window.
    /// </param>
    /// <param name="clock">
    /// Injected so the cooldown can be exercised without waiting for it. Real time
    /// in a test is a test that either takes minutes or asserts nothing.
    /// </param>
    public RecoveryController(
        TrustBoundary trustBoundary,
        int maxRetries = 2,
        TimeProvider? clock = null,
        int windowSize = DefaultWindowSize,
        int probeSuccessesToClose = DefaultProbeSuccessesToClose,
        TimeSpan? baseCooldown = null,
        TimeSpan? maxCooldown = null)
    {
        ArgumentNullException.ThrowIfNull(trustBoundary);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, maxRetries + 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(probeSuccessesToClose, 1);

        _trustBoundary = trustBoundary;
        _maxRetries = maxRetries;
        _clock = clock ?? TimeProvider.System;
        _windowSize = windowSize;
        _probeSuccessesToClose = probeSuccessesToClose;
        _baseCooldown = baseCooldown ?? DefaultBaseCooldown;
        _maxCooldown = maxCooldown ?? DefaultMaxCooldown;
        _window = new Queue<bool>(windowSize);
    }

    /// <summary>How the breaker is currently treating the runtime.</summary>
    public RecoveryState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>Failures among the last <see cref="WindowSize"/> attempts.</summary>
    public int FailuresInWindow
    {
        get { lock (_lock) return _failuresInWindow; }
    }

    /// <summary>How many attempts the window currently holds.</summary>
    public int WindowOccupancy
    {
        get { lock (_lock) return _window.Count; }
    }

    /// <summary>The window length the rungs are read over.</summary>
    public int WindowSize => _windowSize;

    /// <summary>
    /// The last outcomes, oldest first. True is a failure. A copy: callers cannot
    /// mutate the window the rungs are read from.
    /// </summary>
    public IReadOnlyList<bool> FailureWindow
    {
        get { lock (_lock) return _window.ToArray(); }
    }

    /// <summary>Consecutive probe successes needed before full speed returns.</summary>
    public int ProbeSuccessesToClose => _probeSuccessesToClose;

    /// <summary>The wait after the first halt. It doubles with each halt after it.</summary>
    public TimeSpan BaseCooldown => _baseCooldown;

    /// <summary>The ceiling the doubling stops at.</summary>
    public TimeSpan MaxCooldown => _maxCooldown;

    /// <summary>How many times the breaker has halted since it last closed.</summary>
    public int Halts
    {
        get { lock (_lock) return _halts; }
    }

    /// <summary>
    /// Kept because it is genuinely useful in a diagnostic, and no longer what any
    /// decision is made on.
    /// </summary>
    public int ConsecutiveFailures
    {
        get { lock (_lock) return _consecutiveFailures; }
    }

    /// <summary>The wait currently being served, doubling with each halt.</summary>
    public TimeSpan CurrentCooldown
    {
        get { lock (_lock) return CooldownForHalts(_halts); }
    }

    /// <summary>What is left of the current cooldown, or zero.</summary>
    public TimeSpan CooldownRemaining
    {
        get
        {
            lock (_lock)
            {
                if (_state != RecoveryState.Halted)
                    return TimeSpan.Zero;

                TimeSpan remaining = _cooldownEndsAtUtc - _clock.GetUtcNow();
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// Whether one action may be attempted now, and if not, why not.
    /// </summary>
    /// <remarks>
    /// This is the half the counter never had. Escalation without admission control
    /// only labels the runtime; it does not slow it down, so a degraded runtime went
    /// on acting at full rate and the next failure was never far away. Every caller
    /// that is about to act has to ask, and has to report the refusal rather than
    /// treat it as a failed action — a refusal is the breaker working, not a fault
    /// to be counted against it.
    /// </remarks>
    /// <param name="runtimeMode">
    /// Moved to <see cref="RuntimeMode.Recovery"/> when this call is what opens the
    /// trial, so that the guard which blocks a stopped runtime does not also block
    /// the one action the trial consists of.
    /// </param>
    public bool TryBeginAction(ref RuntimeMode runtimeMode, out string? refusalReason)
    {
        lock (_lock)
        {
            switch (_state)
            {
                case RecoveryState.Closed:
                case RecoveryState.Throttled:
                    refusalReason = null;
                    return true;

                case RecoveryState.Halted:
                    DateTimeOffset now = _clock.GetUtcNow();
                    if (now < _cooldownEndsAtUtc)
                    {
                        refusalReason =
                            $"recovery_halted_cooling_down:{(_cooldownEndsAtUtc - now).TotalSeconds:F1}s_of_"
                            + $"{CooldownForHalts(_halts).TotalSeconds:F1}s";
                        return false;
                    }

                    // The cooldown has run out, so one action may be tried. Not a
                    // return to service: a single probe, and the state says so.
                    _state = RecoveryState.Probing;
                    _probeSuccesses = 0;
                    _probeOutstanding = true;
                    runtimeMode = RuntimeMode.Recovery;
                    refusalReason = null;
                    return true;

                case RecoveryState.Probing:
                    if (_probeOutstanding)
                    {
                        refusalReason = "recovery_probe_in_flight";
                        return false;
                    }

                    _probeOutstanding = true;
                    runtimeMode = RuntimeMode.Recovery;
                    refusalReason = null;
                    return true;

                default:
                    refusalReason = "recovery_state_unknown";
                    return false;
            }
        }
    }

    /// <summary>
    /// Records one failure and returns how the runtime should respond to it.
    /// </summary>
    /// <remarks>
    /// Both original versions took the failing verification result and neither read
    /// it, so it is not a parameter here: a value the method ignores suggests the
    /// decision depends on it when it does not.
    /// </remarks>
    public RecoveryStrategy HandleFailure(ref RuntimeMode runtimeMode)
    {
        RecoveryHaltTransition? transition = null;
        RecoveryStrategy strategy;
        lock (_lock)
        {
            Record(failure: true);
            _probeOutstanding = false;

            // A probe that fails is the strongest evidence available that the fault
            // is still there, so it goes straight back to halted and waits longer.
            if (_state == RecoveryState.Probing)
                strategy = Halt(ref runtimeMode, out transition);
            else if (_failuresInWindow > _maxRetries + 1)
                strategy = Halt(ref runtimeMode, out transition);
            else if (_failuresInWindow == _maxRetries + 1)
            {
                // Never a step back up: once halted, a lighter rung does not apply.
                if (_state == RecoveryState.Halted)
                {
                    strategy = RecoveryStrategy.HaltAndAlert;
                }
                else
                {
                    _state = RecoveryState.Throttled;
                    _trustBoundary.DowngradeTrust(TrustTier.Tier1_Assisted);
                    runtimeMode = RuntimeMode.Degraded;
                    strategy = RecoveryStrategy.DegradedReplan;
                }
            }
            else if (_state == RecoveryState.Halted)
            {
                strategy = RecoveryStrategy.HaltAndAlert;
            }
            else if (_state == RecoveryState.Throttled)
            {
                runtimeMode = RuntimeMode.Degraded;
                strategy = RecoveryStrategy.DegradedReplan;
            }
            else
            {
                runtimeMode = RuntimeMode.Recovery;
                strategy = RecoveryStrategy.Retry;
            }
        }

        // The dump is the photograph of the transition. Raised outside the lock
        // so a subscriber that writes a file cannot stall the next admission.
        if (transition is not null)
            Halted?.Invoke(transition);

        return strategy;
    }

    /// <summary>
    /// Records one success, and returns the state that success leaves the runtime in.
    /// </summary>
    /// <remarks>
    /// <b>The replacement for <c>ResetFailures()</c> on the success path.</b> The
    /// gates used to call the reset directly and then assign
    /// <see cref="RuntimeMode.Normal"/> themselves, which put the decision to return
    /// to full speed in the caller — the one place that cannot see the history it
    /// depends on. A success is evidence, and this records it as evidence; whether
    /// it amounts to a recovery is decided here, from the window and the trial.
    /// </remarks>
    public RecoveryState HandleSuccess(ref RuntimeMode runtimeMode)
    {
        lock (_lock)
        {
            Record(failure: false);
            _probeOutstanding = false;

            switch (_state)
            {
                case RecoveryState.Probing:
                    _probeSuccesses++;
                    if (_probeSuccesses >= _probeSuccessesToClose)
                    {
                        Close(ref runtimeMode);
                        break;
                    }

                    // Still on trial. The next action is another single probe.
                    runtimeMode = RuntimeMode.Recovery;
                    break;

                case RecoveryState.Closed:
                    // Full speed only once the window itself is clean. Recent
                    // failures have to be worked off, not cancelled by one success.
                    runtimeMode = _failuresInWindow == 0
                        ? RuntimeMode.Normal
                        : RuntimeMode.Recovery;
                    break;

                case RecoveryState.Throttled:
                case RecoveryState.Halted:
                    // A success here changes nothing on its own. Getting out of these
                    // is what the trial is for, and the trial is entered on the
                    // cooldown, not on an outcome.
                    break;
            }

            return _state;
        }
    }

    /// <summary>
    /// Clears the history and closes the breaker. The operator's reset, not a
    /// consequence of any outcome.
    /// </summary>
    /// <remarks>
    /// It does not touch trust, and that is the same guarantee it always gave: a
    /// downgrade survives every reset here and is undone only by whoever is
    /// watching.
    /// </remarks>
    public void ResetFailures()
    {
        lock (_lock)
        {
            _window.Clear();
            _failuresInWindow = 0;
            _consecutiveFailures = 0;
            _state = RecoveryState.Closed;
            _halts = 0;
            _probeSuccesses = 0;
            _probeOutstanding = false;
            _cooldownEndsAtUtc = default;
        }
    }

    /// <summary>The name Gate 6 used for <see cref="ResetFailures"/>.</summary>
    public void Reset() => ResetFailures();

    private void Record(bool failure)
    {
        if (_window.Count == _windowSize && _window.Dequeue())
            _failuresInWindow--;

        _window.Enqueue(failure);

        if (failure)
        {
            _failuresInWindow++;
            _consecutiveFailures++;
        }
        else
        {
            _consecutiveFailures = 0;
        }
    }

    private RecoveryStrategy Halt(ref RuntimeMode runtimeMode, out RecoveryHaltTransition? transition)
    {
        transition = null;

        // Only a fresh halt lengthens the wait. Failures arriving while already
        // halted are recorded but do not push the cooldown out again, or a burst of
        // them would compound into a wait nobody chose.
        if (_state != RecoveryState.Halted)
        {
            RecoveryState previous = _state;
            _halts++;
            DateTimeOffset now = _clock.GetUtcNow();
            _cooldownEndsAtUtc = now + CooldownForHalts(_halts);
            _state = RecoveryState.Halted;
            transition = new RecoveryHaltTransition(
                previous,
                RecoveryState.Halted,
                now,
                _window.ToArray(),
                _failuresInWindow,
                _window.Count,
                _halts,
                CooldownForHalts(_halts));
        }

        _probeSuccesses = 0;
        _trustBoundary.DowngradeTrust(TrustTier.Tier0_ReadOnly);
        runtimeMode = RuntimeMode.Stopped;
        return RecoveryStrategy.HaltAndAlert;
    }

    private void Close(ref RuntimeMode runtimeMode)
    {
        // The trial is passed, so the history that led to it is spent. Keeping it
        // would halt the runtime again on the next single failure, which is a
        // different bug in the same family as the one this class was rewritten for.
        _window.Clear();
        _failuresInWindow = 0;
        _consecutiveFailures = 0;
        _state = RecoveryState.Closed;
        _halts = 0;
        _probeSuccesses = 0;
        _probeOutstanding = false;
        _cooldownEndsAtUtc = default;

        // Trust is not restored here, and cannot be. See the class remarks.
        runtimeMode = RuntimeMode.Normal;
    }

    private TimeSpan CooldownForHalts(int halts)
    {
        if (halts <= 0)
            return TimeSpan.Zero;

        // Doubling, computed on the exponent rather than by repeated multiplication
        // so that a long-lived runtime cannot overflow its way to a short wait.
        int doublings = Math.Min(halts - 1, 30);
        double ticks = _baseCooldown.Ticks * Math.Pow(2, doublings);

        return ticks >= _maxCooldown.Ticks
            ? _maxCooldown
            : new TimeSpan((long)ticks);
    }
}
