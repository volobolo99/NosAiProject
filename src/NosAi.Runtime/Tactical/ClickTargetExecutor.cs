// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Tactical — One click on an established target, from assessment to wire verdict
// ============================================================================
//
// The runtime already clicks to walk (SingleStepExecutor). Nothing clicked on an
// entity. This closes that gap the way every actuating command in this project
// must: assess, project, confine to the window, open one actuation scope, click,
// and then ask the wire -- not a belief -- whether the click landed.

using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Tactical;

/// <summary>What the wire said became of a target-selection click.</summary>
/// <remarks>
/// <para>
/// Three outcomes, kept apart because they call for three different things from
/// the operator. <see cref="Confirmed"/> says the click produced a selection of
/// <i>that</i> id, from the server, in milliseconds. <see cref="DifferentTarget"/>
/// says the wire named a different id — a click that landed on the wrong entity,
/// which is the defect this task exists to make visible. <see cref="NotConfirmed"/>
/// says no <c>ct</c> arrived within the window, which may be latency or a click
/// that did nothing; it is <b>not</b> the same fact as "the wrong id", and folding
/// the two together would hide the one that means the aim was wrong.
/// </para>
/// <para>
/// <see cref="NotAttempted"/> is not a wire outcome at all: nothing was emitted,
/// so there is nothing to verify.
/// </para>
/// </remarks>
public enum TargetSelectionOutcome : byte
{
    /// <summary>A <c>ct</c> naming the requested id arrived after the click.</summary>
    Confirmed = 0,

    /// <summary>A <c>ct</c> naming a different id arrived after the click.</summary>
    DifferentTarget = 1,

    /// <summary>No <c>ct</c> arrived within the verification window.</summary>
    NotConfirmed = 2,

    /// <summary>Nothing was emitted; there is no wire outcome to report.</summary>
    NotAttempted = 3,
}

/// <summary>The wire's answer to "did the click select <i>that</i> entity?".</summary>
/// <param name="Outcome">Which of the three (or none) happened.</param>
/// <param name="TargetEntityId">The id the click was aimed at.</param>
/// <param name="ObservedEntityId">The id the wire actually named, or null when none arrived.</param>
/// <param name="Waited">How long the verification watched the wire. Zero only when nothing was emitted.</param>
public readonly record struct ClickTargetVerification(
    TargetSelectionOutcome Outcome,
    long TargetEntityId,
    long? ObservedEntityId,
    TimeSpan Waited)
{
    /// <summary>True only when the wire named the requested id.</summary>
    public bool Confirmed => Outcome == TargetSelectionOutcome.Confirmed;

    /// <summary>The verification of an act that never left.</summary>
    public static ClickTargetVerification NotAttempted(long targetEntityId) =>
        new(TargetSelectionOutcome.NotAttempted, targetEntityId, null, TimeSpan.Zero);
}

/// <summary>
/// What one target click did, from the establishment verdict to the wire's
/// answer, in the order it did it.
/// </summary>
/// <param name="Verdict">The establishment verdict on the entity, always present.</param>
/// <param name="ScreenX">The pixel the click was aimed at, or zero when projection failed.</param>
/// <param name="ScreenY">The pixel the click was aimed at, or zero when projection failed.</param>
/// <param name="Scale">The geometry the pixel was computed under; carried for the commit point.</param>
/// <param name="Emitted">Whether the irreversible click actually left.</param>
/// <param name="RefusalReason">
/// Why nothing was emitted: not established, projection refused, the pixel fell
/// outside the window, the scope refused, or the commit point refused the click
/// itself. Null exactly when <paramref name="Emitted"/>.
/// </param>
/// <param name="Verification">The wire's answer. <see cref="ClickTargetVerification"/> with <see cref="TargetSelectionOutcome.NotAttempted"/> when nothing was emitted.</param>
/// <param name="EmittedAtUtc">When the click left, or null.</param>
public sealed record ClickTargetReport(
    TargetVerdict Verdict,
    int ScreenX,
    int ScreenY,
    GeometryShape Scale,
    bool Emitted,
    string? RefusalReason,
    ClickTargetVerification Verification,
    DateTime? EmittedAtUtc)
{
    /// <summary>The one sentence a caller can print: what happened, named.</summary>
    public string Summary
    {
        get
        {
            if (!Verdict.IsEstablished)
                return $"refused: {Verdict.Reason}";

            if (!Emitted)
                return $"not emitted: {RefusalReason}";

            string waited = Verification.Waited.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            return Verification.Outcome switch
            {
                TargetSelectionOutcome.Confirmed =>
                    $"confirmed: ct named entity {Verification.TargetEntityId} in {waited}ms",
                TargetSelectionOutcome.DifferentTarget =>
                    $"wrong target: ct named entity {Verification.ObservedEntityId} instead of {Verification.TargetEntityId}",
                TargetSelectionOutcome.NotConfirmed =>
                    $"not confirmed: no ct within {waited}ms",
                _ => "not attempted"
            };
        }
    }
}

/// <summary>
/// What a target click needs to decide and act: the entity, and the evidence
/// <see cref="TargetEstablishment.Assess"/> reads to judge it.
/// </summary>
/// <remarks>
/// The assessment inputs travel with the request rather than being baked into
/// the executor, so the same executor can click any entity against the evidence
/// that was actually observed for it — the executor calls
/// <see cref="TargetEstablishment.Assess"/> and never re-implements its rule.
/// </remarks>
public readonly record struct ClickTargetRequest(
    SelectableEntity Entity,
    ClassifiedValue<Aggressor>? HitBy,
    ClassifiedValue<TargetedEntity>? Selected,
    GameReferenceDatabase? Catalogue);

/// <summary>
/// Assesses, projects, confines and clicks one established target, then verifies
/// on the wire that the click produced a selection of that entity.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape is <see cref="Navigation.SingleStepExecutor"/>'s.</b> The same
/// order of concerns — assess before act, one irreversible step as the last thing,
/// the commit point revalidated inside <see cref="GatedInputBackend"/> — because
/// the properties that make a step safe are the same ones that make a click safe,
/// and they are not re-decided in every caller.
/// </para>
/// <para>
/// <b>The one new thing is the wire verdict.</b> A walk is verified by the grid
/// position; a target click is verified by <c>ct</c>, the server's own statement
/// of which entity the character is now acting on. The cycle
/// <i>Execute → Verify → Re-observe</i> closes here without inventing anything:
/// the decoder already publishes <see cref="PlayerTargetSelection"/> from
/// <c>ct</c>, carrying the target id.
/// </para>
/// <para>
/// <b>The verdict is a <i>new</i> selection, not a remembered one.</b>
/// <see cref="PlayerTargetSelection"/> is sticky — nothing on the wire clears a
/// selection. So a selection naming the requested id is only testimony when it
/// postdates the click; one that was already there before the click testifies to
/// nothing the click did. The executor reads the latest selection immediately
/// before clicking and only counts one whose own instant is later.
/// </para>
/// <para>
/// <b>The verification window.</b> <see cref="DefaultVerificationWindow"/> is one
/// second, with a 10 ms poll. The wire delivers around 90 packets a second in the
/// recorded captures, so a <c>ct</c> that the click will produce is observable
/// within a few polls; one second is a comfortable multiple of the click→server→
/// <c>ct</c> round-trip while still reporting a genuinely dead click within one
/// command invocation, not a cycle budget. It is a wall-clock window read with
/// <see cref="Stopwatch"/>, so a clock that steps sideways cannot end it early.
/// </para>
/// </remarks>
public sealed class ClickTargetExecutor
{
    /// <summary>Reported when the gate would not open a scope for the click.</summary>
    public const string ScopeRefusedPrefix = "click_target_scope_refused";

    /// <summary>Reported when the cursor could not be placed on the target's pixel.</summary>
    public const string CursorMoveRefusedReason = "click_target_cursor_move_refused";

    /// <summary>Reported when the click itself was refused — normally by the commit point.</summary>
    public const string ClickRefusedPrefix = "click_target_click_refused";

    /// <summary>Reported when the session window is not known, so no geometry can be stamped.</summary>
    public const string NoSessionWindowReason = "click_target_session_window_unknown";

    /// <summary>Reported when the session window's geometry could not be read.</summary>
    public const string GeometryUnknownReason = "click_target_session_geometry_unknown";

    /// <summary>
    /// Reported when the projected pixel falls outside the client window. A pixel
    /// outside is a click on the desktop — on whatever happens to be underneath.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CalibratedScreenProjection.OutsideClientAreaReason"/>
    /// on purpose: the projection answers for the map coordinate, and this answers
    /// for the pixel itself, so a projection that (or a caller who) lets an
    /// off-screen point through is caught here rather than assumed away.
    /// </remarks>
    public const string PixelOutsideWindowReason = "click_target_pixel_outside_window";

    /// <summary>How long the wire verdict watches for the <c>ct</c> that the click should produce.</summary>
    public static readonly TimeSpan DefaultVerificationWindow = TimeSpan.FromMilliseconds(1000);

    /// <summary>How often the wire is re-read while the verification window is open.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly GatedInputBackend _input;
    private readonly IScreenProjection _projection;
    private readonly Func<IntPtr> _sessionWindow;
    private readonly Func<IntPtr, GeometryStamp> _readGeometry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _verificationWindow;
    private readonly TimeSpan _pollInterval;

    /// <param name="input">
    /// The gated boundary, concrete for the same reason <see cref="Navigation.SingleStepExecutor"/>
    /// takes it concrete: an executor built over a raw backend would step around the gate.
    /// </param>
    /// <param name="projection">Map coordinate to client pixel. An uncalibrated one refuses by name.</param>
    /// <param name="sessionWindow">The client window the click is aimed at, re-read per act.</param>
    /// <param name="readGeometry">
    /// How the window's geometry is stamped. Defaults to reading the real window; it
    /// is a seam so a test can state a geometry instead of owning a window.
    /// </param>
    /// <param name="clock">Time source; the system clock unless a test supplies one.</param>
    /// <param name="verificationWindow">How long the wire verdict watches. <see cref="DefaultVerificationWindow"/> when omitted.</param>
    /// <param name="pollInterval">How often the wire is re-read while the window is open. <see cref="DefaultPollInterval"/> when omitted.</param>
    public ClickTargetExecutor(
        GatedInputBackend input,
        IScreenProjection projection,
        Func<IntPtr> sessionWindow,
        Func<IntPtr, GeometryStamp>? readGeometry = null,
        TimeProvider? clock = null,
        TimeSpan? verificationWindow = null,
        TimeSpan? pollInterval = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _sessionWindow = sessionWindow ?? throw new ArgumentNullException(nameof(sessionWindow));
        _clock = clock ?? TimeProvider.System;
        _readGeometry = readGeometry ?? (window => GeometryStamp.Take(window, _clock));

        TimeSpan window = verificationWindow ?? DefaultVerificationWindow;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Ticks, nameof(verificationWindow));
        TimeSpan poll = pollInterval ?? DefaultPollInterval;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(poll.Ticks, nameof(pollInterval));
        _verificationWindow = window;
        _pollInterval = poll;
    }

    /// <summary>
    /// Assesses the entity, projects its square, confines the pixel to the window,
    /// clicks through the gate, and verifies on the wire.
    /// </summary>
    /// <param name="request">The entity and the evidence its establishment is judged on.</param>
    /// <param name="authority">
    /// Under whose authority the click is emitted (ADR-0020 § 2): the operator
    /// command that asked for it, or a <see cref="NosAi.Runtime.Safety.SafetyToken"/>.
    /// A missing authority is refused by the gate, not defaulted.
    /// </param>
    /// <param name="readLatestSelection">
    /// The most recent <see cref="PlayerTargetSelection"/> the wire has published,
    /// or null when none has. Called once before the click (the baseline) and then
    /// repeatedly while the verification window is open. It must not block.
    /// </param>
    public ClickTargetReport Click(
        in ClickTargetRequest request,
        in ActuationAuthority authority,
        Func<PlayerTargetSelection?> readLatestSelection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readLatestSelection);

        // 1. Assess first, before any pixel exists. An entity nothing established
        //    stays unknown, and the unknown does not authorise a click (ADR-0016).
        //    This is TargetEstablishment.Assess's rule, called, never re-derived.
        TargetVerdict verdict = TargetEstablishment.Assess(
            request.Entity, request.HitBy, request.Selected, request.Catalogue);
        if (!verdict.IsEstablished)
            return NotEmitted(request.Entity.EntityId, verdict, 0, 0, default, verdict.Reason);

        // 2. The entity's square becomes a pixel. A refused projection names its own
        //    cause; it is never covered with a reason invented here.
        if (!_projection.TryProject(request.Entity.At.X, request.Entity.At.Y,
                out int screenX, out int screenY, out string? projectionFailure))
            return NotEmitted(request.Entity.EntityId, verdict, 0, 0, _projection.Scale, projectionFailure);

        IntPtr window = _sessionWindow();
        if (window == IntPtr.Zero)
            return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale, NoSessionWindowReason);

        // Taken once, never refreshed while the act is in flight: the commit point
        // needs the geometry as it was *then* or it has nothing to disagree with.
        GeometryStamp stamp = _readGeometry(window);
        if (!stamp.IsKnown)
            return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale, GeometryUnknownReason);

        // 3. Confine the pixel to the window before the cursor moves. An off-window
        //    pixel is a click on the desktop.
        PixelRect client = stamp.Epoch.ClientArea;
        if (screenX < client.X || screenX >= client.Right || screenY < client.Y || screenY >= client.Bottom)
            return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale, PixelOutsideWindowReason);

        var commit = new CommitRequest(stamp, screenX, screenY, _projection.Scale);
        if (!_input.TryBeginActuation(in commit, in authority, out ActuationScope? scope, out string? scopeRefusal) || scope is null)
            return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale,
                $"{ScopeRefusedPrefix}:{scopeRefusal ?? "unknown"}");

        try
        {
            // Reversible: the cursor can be put back, and the gate checks the policy
            // and the open scope but does not revalidate the world for it.
            if (!_input.MoveAbsolute(screenX, screenY))
                return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale, CursorMoveRefusedReason);

            // The baseline: the selection already on the wire, read as late as
            // possible so the gap between "this was the state" and "this is what the
            // click changed" is as small as polling allows.
            DateTime baselineAtUtc = readLatestSelection() is { } baseline
                ? baseline.ObservedAtUtc
                : DateTime.MinValue;

            // The one irreversible step, and the last thing that happens. The commit
            // point's conditions are re-read inside the gate between this call and
            // the pixels.
            DateTime emittedAt = _clock.GetUtcNow().UtcDateTime;
            if (!_input.Click(MouseButton.Left))
            {
                string reason = _input.LastRefusal?.Reason ?? "unknown";
                return NotEmitted(request.Entity.EntityId, verdict, screenX, screenY, _projection.Scale,
                    $"{ClickRefusedPrefix}:{reason}");
            }

            ClickTargetVerification verification =
                VerifySelection(request.Entity.EntityId, baselineAtUtc, readLatestSelection, cancellationToken);

            return new ClickTargetReport(verdict, screenX, screenY, _projection.Scale, true, null, verification, emittedAt);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Watches the wire for a <c>ct</c> that postdates the click, until it names
    /// the target, names someone else, or the window closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only a reading that postdates the click counts.</b> The selection is
    /// sticky by nature (nothing clears it), so the value already on the wire before
    /// the click — captured as <paramref name="baselineAtUtc"/> — testifies to
    /// nothing the click did. Comparing each reading's own instant against the
    /// baseline is what separates "the click selected it" from "it was already
    /// selected", and it does not depend on the executor's clock agreeing with the
    /// packet's clock: both sides of the comparison are the wire's own instants.
    /// </para>
    /// <para>
    /// <b>A different id is a distinct outcome, not "no confirmation".</b> It means
    /// the click landed on the wrong entity — the exact defect this task must be able
    /// to report — and it must read differently from a click that produced nothing.
    /// </para>
    /// </remarks>
    private ClickTargetVerification VerifySelection(
        long targetEntityId,
        DateTime baselineAtUtc,
        Func<PlayerTargetSelection?> readLatestSelection,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        while (true)
        {
            PlayerTargetSelection? latest = readLatestSelection();

            if (latest is { } selection && selection.ObservedAtUtc > baselineAtUtc)
            {
                if (selection.Target.EntityId == targetEntityId)
                    return new ClickTargetVerification(
                        TargetSelectionOutcome.Confirmed, targetEntityId, selection.Target.EntityId,
                        Stopwatch.GetElapsedTime(started));

                return new ClickTargetVerification(
                    TargetSelectionOutcome.DifferentTarget, targetEntityId, selection.Target.EntityId,
                    Stopwatch.GetElapsedTime(started));
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= _verificationWindow || cancellationToken.IsCancellationRequested)
                return new ClickTargetVerification(
                    TargetSelectionOutcome.NotConfirmed, targetEntityId, null, elapsed);

            // Never sleep past the deadline: a poll interval longer than what is
            // left would turn the window into a slightly larger window.
            TimeSpan remaining = _verificationWindow - elapsed;
            Thread.Sleep(remaining < _pollInterval ? remaining : _pollInterval);
        }
    }

    private static ClickTargetReport NotEmitted(
        long entityId,
        TargetVerdict verdict,
        int screenX,
        int screenY,
        GeometryShape scale,
        string? refusalReason) =>
        new(verdict, screenX, screenY, scale, false, refusalReason,
            ClickTargetVerification.NotAttempted(entityId), null);
}
