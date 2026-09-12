using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NosAi.Core.WorldModel;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Tactical;

public enum EquipGesture : byte
{
    Single = 0,
    Double = 1
}

public enum EquipOutcome : byte
{
    Confirmed = 0,
    NotWorn = 1,
    NotConfirmed = 2,
    NotAttempted = 3
}

public readonly record struct EquipVerification(EquipOutcome Outcome, EquipmentSlot Slot, TimeSpan Waited)
{
    public bool Confirmed => Outcome == EquipOutcome.Confirmed;
    public static EquipVerification NotAttempted(EquipmentSlot slot) => new(EquipOutcome.NotAttempted, slot, TimeSpan.Zero);
}

public sealed record EquipReport(ItemId Item, EquipmentSlot TargetSlot, int BagSlotIndex, EquipGesture Gesture, int ScreenX, int ScreenY, bool Emitted, string? RefusalReason, EquipVerification Verification, DateTime? EmittedAtUtc)
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
                EquipOutcome.Confirmed =>
                    $"confirmed: {TargetSlot} now carries {Item} in {waited}ms",
                EquipOutcome.NotWorn =>
                    $"not worn: {TargetSlot} changed but does not carry {Item} after {waited}ms",
                EquipOutcome.NotConfirmed =>
                    $"not confirmed: no equip change within {waited}ms",
                _ => "not attempted"
            };
        }
    }
}

public readonly record struct EquipRequest(ItemId Item, EquipmentSlot TargetSlot, int BagSlotIndex, EquipGesture Gesture);

public sealed class EquipExecutor
{
    public const string NotCalibratedReason = "equip_not_calibrated";
    public const string NoSessionWindowReason = "equip_no_session_window";
    public const string GeometryUnknownReason = "equip_geometry_unknown";
    public const string ResolutionMismatchReason = "equip_resolution_mismatch";
    public const string BagSlotNotResolvedReason = "equip_bag_slot_not_resolved";
    public const string PixelOutsideWindowReason = "equip_pixel_outside_window";
    public const string ScopeRefusedPrefix = "equip_scope_refused:";
    public const string ClickRefusedPrefix = "equip_click_refused:";
    public const string NoEquipmentObservedReason = "equip_no_equipment_observed";

    public static readonly TimeSpan DefaultVerificationWindow = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly GatedInputBackend _input;
    private readonly Func<IntPtr> _sessionWindow;
    private readonly Func<IntPtr, GeometryStamp> _readGeometry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _verificationWindow;
    private readonly TimeSpan _pollInterval;

    public EquipExecutor(GatedInputBackend input, Func<IntPtr> sessionWindow, Func<IntPtr, GeometryStamp>? readGeometry = null, TimeProvider? clock = null, TimeSpan? verificationWindow = null, TimeSpan? pollInterval = null)
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

    public EquipReport Equip(EquipRequest request, BagPanelRoiCalibration calibration, in ActuationAuthority authority, Func<WornEquipmentReading?> readLatestEquip, CancellationToken cancellationToken = default)
    {
        EquipmentSlot slot = request.TargetSlot;
        ItemId item = request.Item;
        EquipGesture gesture = request.Gesture;
        int bagSlotIndex = request.BagSlotIndex;

        if (!calibration.IsCalibrated)
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, NotCalibratedReason, EquipVerification.NotAttempted(slot), null);

        IntPtr window = _sessionWindow();
        if (window == IntPtr.Zero)
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, NoSessionWindowReason, EquipVerification.NotAttempted(slot), null);

        GeometryStamp stamp = _readGeometry(window);
        if (!stamp.IsKnown)
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, GeometryUnknownReason, EquipVerification.NotAttempted(slot), null);

        PixelRect client = stamp.Epoch.ClientArea;

        // A normalised fraction is only valid at the resolution it was measured
        // on: the same guard UnequipExecutor applies before its own Resolve.
        if (client.Width != calibration.ClientWidth || client.Height != calibration.ClientHeight)
        {
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false,
                string.Create(CultureInfo.InvariantCulture,
                    $"{ResolutionMismatchReason}:actual_{client.Width}x{client.Height}"
                    + $"_calibrated_{calibration.ClientWidth}x{calibration.ClientHeight}"),
                EquipVerification.NotAttempted(slot), null);
        }

        if (calibration.Resolve(client) is not { } rois || !rois.TryGetValue(bagSlotIndex, out PixelRect roi))
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, BagSlotNotResolvedReason, EquipVerification.NotAttempted(slot), null);

        int screenX = roi.X + roi.Width / 2;
        int screenY = roi.Y + roi.Height / 2;

        if (screenX < client.X || screenX >= client.Right || screenY < client.Y || screenY >= client.Bottom)
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, PixelOutsideWindowReason, EquipVerification.NotAttempted(slot), null);

        var commit = new CommitRequest(stamp, screenX, screenY, stamp.Epoch.Shape);
        if (!_input.TryBeginActuation(in commit, in authority, out ActuationScope? scope, out string? scopeRefusal) || scope is null)
            return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, $"{ScopeRefusedPrefix}:{scopeRefusal ?? "unknown"}", EquipVerification.NotAttempted(slot), null);

        try
        {
            if (!_input.MoveAbsolute(screenX, screenY))
                return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, PixelOutsideWindowReason, EquipVerification.NotAttempted(slot), null);

            WornEquipmentReading? baseline = readLatestEquip() ?? new WornEquipmentReading(System.Collections.Immutable.ImmutableArray<WornEquipmentSlot>.Empty, EquipmentWireOpcode.Equip);

            DateTime emittedAt = _clock.GetUtcNow().UtcDateTime;
            if (!EmitGesture(gesture, out string? clickRefusal))
                return new EquipReport(item, slot, bagSlotIndex, gesture, 0, 0, false, $"{ClickRefusedPrefix}:{clickRefusal ?? "unknown"}", EquipVerification.NotAttempted(slot), null);

            EquipVerification verification = Verify(item, slot, baseline, readLatestEquip, cancellationToken);

            return new EquipReport(item, slot, bagSlotIndex, gesture, screenX, screenY, true, null, verification, emittedAt);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private bool EmitGesture(EquipGesture gesture, out string? refusal)
    {
        refusal = null;
        switch (gesture)
        {
            case EquipGesture.Single:
                if (!_input.Click(MouseButton.Left)) refusal = _input.LastRefusal?.Reason ?? "unknown";
                return refusal is null;

            case EquipGesture.Double:
                if (!_input.Click(MouseButton.Left)) { refusal = _input.LastRefusal?.Reason ?? "unknown"; return false; }
                if (!_input.Click(MouseButton.Left)) { refusal = _input.LastRefusal?.Reason ?? "unknown"; return false; }
                return true;

            default:
                refusal = "unknown_gesture";
                return false;
        }
    }

    private EquipVerification Verify(ItemId item, EquipmentSlot targetSlot, WornEquipmentReading baseline, Func<WornEquipmentReading?> readLatestEquip, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        int slotId = (int)targetSlot;
        var baselineSlots = new HashSet<(int Slot, int Vnum)>(baseline.Slots.Select(s => (s.Slot, s.Vnum)));

        while (true)
        {
            WornEquipmentReading? latest = readLatestEquip();

            if (latest is { } equip)
            {
                bool changed = equip.Slots.Count != baseline.Slots.Count
                    || equip.Slots.Any(s => !baselineSlots.Contains((s.Slot, s.Vnum)));

                if (changed)
                {
                    bool wornHere = equip.Slots.Any(s =>
                        s.Slot == slotId && s.Vnum.ToString(CultureInfo.InvariantCulture) == item.Value);
                    return new EquipVerification(
                        wornHere ? EquipOutcome.Confirmed : EquipOutcome.NotWorn,
                        targetSlot,
                        Stopwatch.GetElapsedTime(started));
                }
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= _verificationWindow || cancellationToken.IsCancellationRequested)
                return new EquipVerification(EquipOutcome.NotConfirmed, targetSlot, elapsed);

            TimeSpan remaining = _verificationWindow - elapsed;
            Thread.Sleep(remaining < _pollInterval ? remaining : _pollInterval);
        }
    }
}
