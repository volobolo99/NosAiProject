// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Tactical — One click on an equipment slot, from calibration to wire verdict
// ============================================================================
//
// The runtime knows where the eighteen equipment-panel slots sit on screen
// (InventoryPanelRoiCalibration) and the wire says what is worn (WornEquipment
// from `equip`). Nothing put the two together. This closes that gap the way
// every actuating command must: verify the calibration, project the slot to a
// pixel, confine it to the window, open one actuation scope, click, and then
// ask the wire — not a belief — whether the slot came off.

using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NosAi.Core.WorldModel;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Tactical;

/// <summary>Which mouse gesture the operator asked to try on the slot.</summary>
/// <remarks>
/// No gesture is a default: nobody has recorded which one unequips, so the
/// operator declares what they are trying and the report says which gesture
/// produced which outcome. A default picked at a desk would turn a
/// <see cref="UnequipOutcome.NotConfirmed"/> into a claim about the game
/// instead of about our bet. Dragging is not a member because the gate's input
/// API cannot express it; that limitation is reported, not worked around by
/// stepping below the gate.
/// </remarks>
public enum UnequipGesture : byte
{
    /// <summary>One left click.</summary>
    Single = 0,

    /// <summary>Two left clicks in one scope.</summary>
    Double = 1,

    /// <summary>One right click.</summary>
    Right = 2,
}

/// <summary>What the wire said became of an unequip click.</summary>
/// <remarks>
/// Four outcomes, kept apart because they call for four different things from
/// the operator. <see cref="Confirmed"/> says a new <c>equip</c> arrived and the
/// requested slot is no longer in it. <see cref="StillWorn"/> says a new
/// <c>equip</c> arrived and the slot is <i>still</i> occupied — the click did
/// something else, which is the defect this task exists to make visible.
/// <see cref="NotConfirmed"/> says no <c>equip</c> arrived within the window,
/// which may be latency or a click that did nothing; it is <b>not</b> the same
/// fact as "still worn", and folding the two together would hide the one that
/// means the aim was wrong. <see cref="NotAttempted"/> is not a wire outcome at
/// all: nothing was emitted, so there is nothing to verify.
/// </remarks>
public enum UnequipOutcome : byte
{
    /// <summary>A new <c>equip</c> arrived and the requested slot is gone.</summary>
    Confirmed = 0,

    /// <summary>A new <c>equip</c> arrived and the requested slot is still occupied.</summary>
    StillWorn = 1,

    /// <summary>No <c>equip</c> arrived within the verification window.</summary>
    NotConfirmed = 2,

    /// <summary>Nothing was emitted; there is no wire outcome to report.</summary>
    NotAttempted = 3,
}

/// <summary>The wire's answer to "did the click take that slot off?".</summary>
/// <param name="Outcome">Which of the four happened.</param>
/// <param name="Slot">The equipment slot the click was aimed at.</param>
/// <param name="Waited">How long the verification watched the wire. Zero only when nothing was emitted.</param>
public readonly record struct UnequipVerification(
    UnequipOutcome Outcome,
    EquipmentSlot Slot,
    TimeSpan Waited)
{
    /// <summary>True only when the wire showed the requested slot gone.</summary>
    public bool Confirmed => Outcome == UnequipOutcome.Confirmed;

    /// <summary>The verification of an act that never left.</summary>
    public static UnequipVerification NotAttempted(EquipmentSlot slot) =>
        new(UnequipOutcome.NotAttempted, slot, TimeSpan.Zero);
}

/// <summary>
/// What one unequip click did, from the calibration verdict to the wire's
/// answer, in the order it did it.
/// </summary>
/// <param name="Slot">The equipment slot that was aimed at.</param>
/// <param name="Gesture">The gesture the operator declared.</param>
/// <param name="ScreenX">The pixel the click was aimed at, or zero when it was never computed.</param>
/// <param name="ScreenY">The pixel the click was aimed at, or zero when it was never computed.</param>
/// <param name="Emitted">Whether the irreversible click actually left.</param>
/// <param name="RefusalReason">
/// Why nothing was emitted: not calibrated, the wrong client resolution, the
/// pixel fell outside the window, the slot was not worn, the scope refused, or
/// the click itself was refused. Null exactly when <paramref name="Emitted"/>.
/// </param>
/// <param name="Verification">The wire's answer. <see cref="UnequipVerification"/> with <see cref="UnequipOutcome.NotAttempted"/> when nothing was emitted.</param>
/// <param name="EmittedAtUtc">When the click left, or null.</param>
public sealed record UnequipReport(
    EquipmentSlot Slot,
    UnequipGesture Gesture,
    int ScreenX,
    int ScreenY,
    bool Emitted,
    string? RefusalReason,
    UnequipVerification Verification,
    DateTime? EmittedAtUtc)
{
    /// <summary>The one sentence a caller can print: what happened, named.</summary>
    public string Summary
    {
        get
        {
            if (!Emitted)
                return $"not emitted: {RefusalReason}";

            string waited = Verification.Waited.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            return Verification.Outcome switch
            {
                UnequipOutcome.Confirmed =>
                    $"confirmed: {Slot} left the equip set in {waited}ms",
                UnequipOutcome.StillWorn =>
                    $"still worn: {Slot} is still in the equip set after {waited}ms",
                UnequipOutcome.NotConfirmed =>
                    $"not confirmed: no equip within {waited}ms",
                _ => "not attempted"
            };
        }
    }
}

/// <summary>What an unequip needs to decide and act: the slot and the gesture.</summary>
public readonly record struct UnequipRequest(EquipmentSlot Slot, UnequipGesture Gesture);

/// <summary>
/// Verifies the equipment-panel calibration, projects one slot to a pixel,
/// confines it to the window, clicks through the gate with the operator's
/// gesture, and verifies on the wire that the slot left the <c>equip</c> set.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape is <see cref="ClickTargetExecutor"/>'s.</b> The same order of
/// concerns — verify before act, one irreversible step as the last thing, the
/// commit point revalidated inside <see cref="GatedInputBackend"/> — because the
/// properties that make a click safe are the same ones that make an unequip
/// click safe, and they are not re-decided here.
/// </para>
/// <para>
/// <b>The wire verdict is a <i>new</i> <c>equip</c>, not a remembered one.</b>
/// <see cref="WornEquipment"/> carries no instant, so "new" is measured by
/// change: the set of slots the wire now reports is compared against the set it
/// reported before the click, and only a set that changed is testimony to what
/// the click did. The recording <c>data/equip_test.noscap</c> shows the server
/// re-sends <c>equip</c> only when the set changes (six packets, six different
/// sets), which is exactly the signal this verdict reads.
/// </para>
/// <para>
/// <b>The slot is not worn.</b> Unequipping a slot that the wire does not
/// currently report as occupied cannot be confirmed — the slot is already
/// "gone", so a changed set would not testify to the click. The executor
/// refuses before emitting, with a named reason, instead of clicking an empty
/// slot and reading its emptiness as success.
/// </para>
/// <para>
/// <b>The slot mapping.</b> The requested <see cref="EquipmentSlot"/> is
/// compared against the <c>equip</c> packet's own slot number
/// (<see cref="WornEquipmentSlot.Slot"/>) as the same integer. That identity is
/// the reading the task's verification presupposes; it has not been wire
/// cross-checked against <c>Item.dat</c> (see the report), and the decoder
/// deliberately does not assert it.
/// </para>
/// </remarks>
public sealed class UnequipExecutor
{
    /// <summary>Reported when the gate would not open a scope for the click.</summary>
    public const string ScopeRefusedPrefix = "unequip_scope_refused";

    /// <summary>Reported when the cursor could not be placed on the slot's pixel.</summary>
    public const string CursorMoveRefusedReason = "unequip_cursor_move_refused";

    /// <summary>Reported when the click itself was refused — normally by the commit point.</summary>
    public const string ClickRefusedPrefix = "unequip_click_refused";

    /// <summary>Reported when the session window is not known, so no geometry can be stamped.</summary>
    public const string NoSessionWindowReason = "unequip_session_window_unknown";

    /// <summary>Reported when the session window's geometry could not be read.</summary>
    public const string GeometryUnknownReason = "unequip_session_geometry_unknown";

    /// <summary>Reported when the client area is not the resolution the calibration was measured on.</summary>
    public const string ResolutionMismatchReason = "unequip_client_area_not_calibrated_resolution";

    /// <summary>Reported when the calibrated slot could not be resolved to a rectangle.</summary>
    public const string SlotNotResolvedReason = "unequip_slot_not_resolved";

    /// <summary>Reported when the pixel falls outside the client window.</summary>
    public const string PixelOutsideWindowReason = "unequip_pixel_outside_window";

    /// <summary>Reported when no <c>equip</c> reading has been observed, so nothing is known to be worn.</summary>
    public const string NoEquipmentObservedReason = "unequip_no_equip_reading";

    /// <summary>Reported when the requested slot is not currently occupied by the wire.</summary>
    public const string SlotNotWornReason = "unequip_slot_not_worn";

    /// <summary>How long the wire verdict watches for the <c>equip</c> that the click should produce.</summary>
    public static readonly TimeSpan DefaultVerificationWindow = TimeSpan.FromMilliseconds(1000);

    /// <summary>How often the wire is re-read while the verification window is open.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly GatedInputBackend _input;
    private readonly Func<IntPtr> _sessionWindow;
    private readonly Func<IntPtr, GeometryStamp> _readGeometry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _verificationWindow;
    private readonly TimeSpan _pollInterval;

    /// <param name="input">
    /// The gated boundary, concrete for the same reason <see cref="ClickTargetExecutor"/>
    /// takes it concrete: an executor built over a raw backend would step around the gate.
    /// </param>
    /// <param name="sessionWindow">The client window the click is aimed at, re-read per act.</param>
    /// <param name="readGeometry">
    /// How the window's geometry is stamped. Defaults to reading the real window; it
    /// is a seam so a test can state a geometry instead of owning a window.
    /// </param>
    /// <param name="clock">Time source; the system clock unless a test supplies one.</param>
    /// <param name="verificationWindow">How long the wire verdict watches. <see cref="DefaultVerificationWindow"/> when omitted.</param>
    /// <param name="pollInterval">How often the wire is re-read while the window is open. <see cref="DefaultPollInterval"/> when omitted.</param>
    public UnequipExecutor(
        GatedInputBackend input,
        Func<IntPtr> sessionWindow,
        Func<IntPtr, GeometryStamp>? readGeometry = null,
        TimeProvider? clock = null,
        TimeSpan? verificationWindow = null,
        TimeSpan? pollInterval = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
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
    /// Verifies the calibration, projects the slot to a pixel, confines it to
    /// the window, clicks through the gate with the declared gesture, and
    /// verifies on the wire that the slot left the <c>equip</c> set.
    /// </summary>
    /// <param name="request">The slot and the gesture.</param>
    /// <param name="calibration">The operator-confirmed equipment-panel ROI, loaded from disk.</param>
    /// <param name="authority">
    /// Under whose authority the click is emitted (ADR-0020 § 2). A missing
    /// authority is refused by the gate, not defaulted.
    /// </param>
    /// <param name="readLatestEquip">
    /// The most recent <see cref="WornEquipment"/> the wire has published, or
    /// null when none has. Called once before the click (the baseline) and then
    /// repeatedly while the verification window is open. It must not block, and
    /// it should surface <c>equip</c> readings (<see cref="EquipmentWireOpcode.Equip"/>)
    /// in particular: the verdict is defined on <c>equip</c>, not <c>eq</c>.
    /// </param>
    public UnequipReport Unequip(
        UnequipRequest request,
        InventoryPanelRoiCalibration calibration,
        in ActuationAuthority authority,
        Func<WornEquipment?> readLatestEquip,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(readLatestEquip);

        // Copied out: an `in` parameter cannot be captured by a lambda, and the
        // slot is compared inside one below.
        EquipmentSlot slot = request.Slot;
        UnequipGesture gesture = request.Gesture;

        // 1. No calibration, no pixel. An uncalibrated panel has no honest
        //    coordinate, and the unknown does not authorise a click.
        if (!calibration.IsCalibrated)
            return NotEmitted(slot, gesture, InventoryPanelRoiCalibration.NotCalibratedReason);

        IntPtr window = _sessionWindow();
        if (window == IntPtr.Zero)
            return NotEmitted(slot, gesture, NoSessionWindowReason);

        // Taken once, never refreshed while the act is in flight: the commit point
        // needs the geometry as it was *then* or it has nothing to disagree with.
        GeometryStamp stamp = _readGeometry(window);
        if (!stamp.IsKnown)
            return NotEmitted(slot, gesture, GeometryUnknownReason);

        PixelRect client = stamp.Epoch.ClientArea;

        // 2. The panel is a fixed-size element: a normalised fraction is only
        //    valid at the resolution it was measured on, and anywhere else it is
        //    a plausible pixel that is wrong. The calibrated resolution is read
        //    from the file, never a literal in the code.
        if (client.Width != calibration.ClientWidth || client.Height != calibration.ClientHeight)
        {
            return NotEmitted(slot, gesture, string.Create(CultureInfo.InvariantCulture,
                $"{ResolutionMismatchReason}:actual_{client.Width}x{client.Height}"
                + $"_calibrated_{calibration.ClientWidth}x{calibration.ClientHeight}"));
        }

        // 3. The slot's rectangle becomes a pixel. A complete calibration names
        //    every declared slot, so this cannot miss; it is kept defensive.
        if (calibration.Resolve(client) is not { } rois || !rois.TryGetValue(slot, out PixelRect roi))
            return NotEmitted(slot, gesture, SlotNotResolvedReason);

        int screenX = roi.X + roi.Width / 2;
        int screenY = roi.Y + roi.Height / 2;

        // 4. Confine the pixel to the window before the cursor moves. An off-window
        //    pixel is a click on the desktop.
        if (screenX < client.X || screenX >= client.Right || screenY < client.Y || screenY >= client.Bottom)
            return NotEmitted(slot, gesture, PixelOutsideWindowReason);

        var commit = new CommitRequest(stamp, screenX, screenY, stamp.Epoch.Shape);
        if (!_input.TryBeginActuation(in commit, in authority, out ActuationScope? scope, out string? scopeRefusal) || scope is null)
            return NotEmitted(slot, gesture, $"{ScopeRefusedPrefix}:{scopeRefusal ?? "unknown"}");

        try
        {
            // Reversible: the cursor can be put back, and the gate checks the policy
            // and the open scope but does not revalidate the world for it.
            if (!_input.MoveAbsolute(screenX, screenY))
                return NotEmitted(slot, gesture, CursorMoveRefusedReason);

            // The baseline: the equip already on the wire, read as late as possible
            // so the gap between "this was the state" and "this is what the click
            // changed" is as small as polling allows.
            WornEquipment? baseline = readLatestEquip();

            // Unequipping a slot the wire does not report as occupied cannot be
            // confirmed: the slot is already "gone". Refuse before emitting.
            if (baseline is not { Opcode: EquipmentWireOpcode.Equip } baselineEquip)
                return NotEmitted(slot, gesture, NoEquipmentObservedReason);
            if (!baselineEquip.Slots.Any(s => s.Slot == (int)slot))
                return NotEmitted(slot, gesture, SlotNotWornReason);

            // The one irreversible step, and the last thing that happens. The commit
            // point's conditions are re-read inside the gate between this call and
            // the pixels.
            DateTime emittedAt = _clock.GetUtcNow().UtcDateTime;
            if (!EmitGesture(gesture, out string? clickRefusal))
                return NotEmitted(slot, gesture, $"{ClickRefusedPrefix}:{clickRefusal ?? "unknown"}");

            UnequipVerification verification =
                Verify(slot, baselineEquip, readLatestEquip, cancellationToken);

            return new UnequipReport(slot, gesture, screenX, screenY, true, null, verification, emittedAt);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>Emits the declared gesture within the already-open scope.</summary>
    private bool EmitGesture(UnequipGesture gesture, out string? refusal)
    {
        refusal = null;
        switch (gesture)
        {
            case UnequipGesture.Single:
                if (!_input.Click(MouseButton.Left)) refusal = _input.LastRefusal?.Reason ?? "unknown";
                return refusal is null;

            case UnequipGesture.Double:
                // Two presses in the same scope, each revalidated by the commit point.
                if (!_input.Click(MouseButton.Left)) { refusal = _input.LastRefusal?.Reason ?? "unknown"; return false; }
                if (!_input.Click(MouseButton.Left)) { refusal = _input.LastRefusal?.Reason ?? "unknown"; return false; }
                return true;

            case UnequipGesture.Right:
                if (!_input.Click(MouseButton.Right)) refusal = _input.LastRefusal?.Reason ?? "unknown";
                return refusal is null;

            default:
                refusal = "unknown_gesture";
                return false;
        }
    }

    /// <summary>
    /// Watches the wire for an <c>equip</c> whose slot set differs from the
    /// baseline, until the requested slot is gone, is still there, or the window
    /// closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only a changed set counts.</b> <see cref="WornEquipment"/> carries no
    /// instant, so "new" is measured by comparing the current set against the
    /// baseline. An unchanged set testifies to nothing the click did.
    /// </para>
    /// <para>
    /// <b>"Still worn" is a distinct outcome, not "no confirmation".</b> It means
    /// a change arrived and the requested slot survived it — the click did
    /// something else — and it must read differently from a click that produced
    /// nothing at all.
    /// </para>
    /// </remarks>
    private UnequipVerification Verify(
        EquipmentSlot slot,
        WornEquipment baseline,
        Func<WornEquipment?> readLatestEquip,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        int slotId = (int)slot;

        while (true)
        {
            WornEquipment? latest = readLatestEquip();

            if (latest is { Opcode: EquipmentWireOpcode.Equip } equip && !SameSlots(baseline, equip))
            {
                bool stillWorn = equip.Slots.Any(s => s.Slot == slotId);
                return new UnequipVerification(
                    stillWorn ? UnequipOutcome.StillWorn : UnequipOutcome.Confirmed,
                    slot,
                    Stopwatch.GetElapsedTime(started));
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= _verificationWindow || cancellationToken.IsCancellationRequested)
                return new UnequipVerification(UnequipOutcome.NotConfirmed, slot, elapsed);

            // Never sleep past the deadline: a poll interval longer than what is
            // left would turn the window into a slightly larger window.
            TimeSpan remaining = _verificationWindow - elapsed;
            Thread.Sleep(remaining < _pollInterval ? remaining : _pollInterval);
        }
    }

    /// <summary>Whether two readings report the same occupied (slot, vnum) set.</summary>
    private static bool SameSlots(WornEquipment a, WornEquipment b)
    {
        if (a.Slots.Length != b.Slots.Length)
            return false;

        var aSlots = new HashSet<(int Slot, int Vnum)>(a.Slots.Select(s => (s.Slot, s.Vnum)));
        foreach (WornEquipmentSlot slot in b.Slots)
        {
            if (!aSlots.Contains((slot.Slot, slot.Vnum)))
                return false;
        }

        return true;
    }

    private static UnequipReport NotEmitted(EquipmentSlot slot, UnequipGesture gesture, string? refusalReason) =>
        new(slot, gesture, 0, 0, false, refusalReason,
            UnequipVerification.NotAttempted(slot), null);
}
