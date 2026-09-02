// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — One step, from authorisation to verdict (C-P4)
// ============================================================================
//
// The interface docs/CONTROLLO_PERSONAGGIO_ROADMAP.md P4 says the `--step`
// command must respect. The command prints and emits audit events; it does not
// compose guards, does not choose their order, and has no route to the input
// backend that does not pass through here.

using System.Globalization;
using System.Threading;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.Navigation;

/// <summary>Everything one step did, in the order it did it.</summary>
/// <param name="Authorization">Every guard's outcome, and where the ladder stopped.</param>
/// <param name="Emitted">Whether the irreversible step actually left.</param>
/// <param name="EmissionRefusal">
/// Why nothing was emitted although the guards passed: the gate, the scope, or the
/// commit point refusing in the last instant. Null when the act went out.
/// </param>
/// <param name="Verification">What became of it. <see cref="MovementOutcome.Aborted"/> when nothing was emitted.</param>
/// <param name="EmittedAtUtc">When the irreversible step left, or null.</param>
public sealed record StepReport(
    StepAuthorization Authorization,
    bool Emitted,
    string? EmissionRefusal,
    MovementVerification Verification,
    DateTime? EmittedAtUtc)
{
    /// <summary>The one sentence a caller can print: what happened, named.</summary>
    public string Summary
    {
        get
        {
            if (!Authorization.IsAuthorized)
                return $"refused at {Authorization.RefusedAt}: {Authorization.RefusalReason}";

            if (!Emitted)
                return $"not emitted: {EmissionRefusal}";

            string elapsed = Verification.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            string detail = Verification.Detail is { } named ? $" ({named})" : string.Empty;
            return $"{Verification.Outcome} in {elapsed}ms{detail}";
        }
    }
}

/// <summary>
/// One authorised step against the real client: guards, act, verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists rather than a command doing it.</b> The order the guards are
/// asked in, and the fact that the click is the last thing that happens, are the two
/// properties that make a step safe. Left to a caller they would be re-decided in
/// every caller, which is how an order becomes an accident. The command above this
/// prints a report; it cannot reorder anything, and it has no reference to the input
/// backend at all.
/// </para>
/// <para>
/// <b>One irreversible step, and it is the last</b> (DOMAIN-17). The cursor move is
/// reversible — it can be undone by moving the cursor — and is checked against the
/// policy and the open scope. The click is not, and is revalidated in full by
/// <see cref="CommitPointValidator"/> inside <see cref="GatedInputBackend"/> in the
/// instant before it is emitted. This class never calls that validator itself: a
/// second copy of the commit point here could disagree with the one that counts.
/// </para>
/// <para>
/// <b>The scope is closed whatever happens.</b> Verification runs inside it, so an
/// exception or a cancellation during the wait still releases anything the act
/// pressed, through the release path that cannot press.
/// </para>
/// <para>
/// <b>What it does not do: retry.</b> Not once, not on a stall, not on an abort. A
/// step that failed is a fact for the caller and the recovery breaker to weigh; a
/// retry buried here would be an act nobody authorised, and it would arrive after the
/// world had already contradicted the plan.
/// </para>
/// </remarks>
public sealed class SingleStepExecutor
{
    /// <summary>Reported when the gate would not open a scope for the act.</summary>
    public const string ScopeRefusedPrefix = "step_scope_refused";

    /// <summary>Reported when the cursor could not be placed on the destination.</summary>
    public const string CursorMoveRefusedReason = "step_cursor_move_refused";

    /// <summary>Reported when the click itself was refused — normally by the commit point.</summary>
    public const string ClickRefusedPrefix = "step_click_refused";

    /// <summary>Reported when the session window is not known, so no geometry can be stamped.</summary>
    public const string NoSessionWindowReason = "step_session_window_unknown";

    /// <summary>Reported when the session window's geometry could not be read.</summary>
    public const string GeometryUnknownReason = "step_session_geometry_unknown";

    private readonly StepGuardChain _guards;
    private readonly GatedInputBackend _input;
    private readonly MovementVerifier _verifier;
    private readonly Func<IntPtr> _sessionWindow;
    private readonly Func<IntPtr, GeometryStamp> _readGeometry;
    private readonly TimeProvider _clock;

    /// <param name="guards">The composed ladder. Not optional and not reorderable.</param>
    /// <param name="input">
    /// The gated boundary, concrete for the same reason <see cref="Gate3.InputActionEffector"/>
    /// takes it concrete: an executor built over a raw backend would step around the gate.
    /// </param>
    /// <param name="sessionWindow">The client window the act is aimed at, re-read per step.</param>
    /// <param name="readGeometry">
    /// How the window's geometry is stamped. Defaults to reading the real window, which
    /// is the only thing production ever passes; it is a seam so a test can state a
    /// geometry instead of owning a window, and not a way to supply a fixed one at run
    /// time — a stamp that never changed would agree with the commit point always, and
    /// the first condition would stop being a check.
    /// </param>
    public SingleStepExecutor(
        StepGuardChain guards,
        GatedInputBackend input,
        Func<IntPtr> sessionWindow,
        MovementVerifier? verifier = null,
        TimeProvider? clock = null,
        Func<IntPtr, GeometryStamp>? readGeometry = null)
    {
        _guards = guards ?? throw new ArgumentNullException(nameof(guards));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _sessionWindow = sessionWindow ?? throw new ArgumentNullException(nameof(sessionWindow));
        _verifier = verifier ?? new MovementVerifier();
        _clock = clock ?? TimeProvider.System;
        _readGeometry = readGeometry ?? (window => GeometryStamp.Take(window, _clock));
    }

    /// <summary>The verifier's window and tolerance, for a caller that reports them.</summary>
    public MovementVerifier Verifier => _verifier;

    /// <summary>
    /// Authorises, emits and verifies one step onto an adjacent cell.
    /// </summary>
    /// <param name="request">Where from, where to, and what the world looked like.</param>
    /// <param name="readPosition">
    /// The observed grid position, re-read while the window is open. Only readings
    /// stamped after the emission are testimony — see <see cref="MovementVerifier"/>.
    /// </param>
    /// <param name="authority">
    /// Under whose authority this step is emitted (ADR-0020 § 2): the operator command
    /// that asked for it, or the cycle's <see cref="Autonomy.SafetyToken"/>. Required,
    /// and stated per step rather than held on the executor — an authority captured at
    /// construction would be the same one for every act the process ever emits, which is
    /// not an attribution.
    /// </param>
    public StepReport Step(
        in StepRequest request,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readPosition);

        StepAuthorization authorization = _guards.Authorize(in request);
        if (!authorization.IsAuthorized)
        {
            return new StepReport(
                authorization,
                false,
                authorization.RefusalReason,
                MovementVerification.NotAttempted(authorization.RefusalReason!),
                null);
        }

        IntPtr window = _sessionWindow();
        if (window == IntPtr.Zero)
            return NotEmitted(authorization, NoSessionWindowReason);

        // Taken once, here, and never refreshed while the act is in flight: the commit
        // point needs the geometry as it was *then* or it has nothing to disagree with.
        GeometryStamp stamp = _readGeometry(window);
        if (!stamp.IsKnown)
            return NotEmitted(authorization, GeometryUnknownReason);

        var commit = new CommitRequest(
            stamp,
            authorization.ScreenX,
            authorization.ScreenY,
            authorization.Scale);

        if (!_input.TryBeginActuation(in commit, in authority, out ActuationScope? scope, out string? scopeRefusal) || scope is null)
            return NotEmitted(authorization, $"{ScopeRefusedPrefix}:{scopeRefusal ?? "unknown"}");

        try
        {
            // Reversible: the cursor can be put back, and the gate checks the policy and
            // the open scope but does not revalidate the world for it.
            if (!_input.MoveAbsolute(authorization.ScreenX, authorization.ScreenY))
                return NotEmitted(authorization, CursorMoveRefusedReason);

            // The one irreversible step, and the last thing that happens. The five
            // conditions are re-read inside the gate between this call and the pixels.
            DateTime emittedAt = _clock.GetUtcNow().UtcDateTime;
            if (!_input.Click(MouseButton.Left))
            {
                string reason = _input.LastRefusal?.Reason ?? "unknown";
                return NotEmitted(authorization, $"{ClickRefusedPrefix}:{reason}");
            }

            MovementVerification verification = _verifier.Verify(
                request.From, request.To, emittedAt, readPosition, cancellationToken);

            return new StepReport(authorization, true, null, verification, emittedAt);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static StepReport NotEmitted(StepAuthorization authorization, string reason) =>
        new(authorization, false, reason, MovementVerification.NotAttempted(reason), null);
}
