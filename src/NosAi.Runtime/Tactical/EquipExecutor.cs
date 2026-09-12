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
    public string Summary => throw new NotImplementedException();
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
    private readonly Func<IntPtr, GeometryStamp>? _readGeometry;
    private readonly TimeProvider? _clock;
    private readonly TimeSpan? _verificationWindow;
    private readonly TimeSpan? _pollInterval;

    public EquipExecutor(GatedInputBackend input, Func<IntPtr> sessionWindow, Func<IntPtr, GeometryStamp>? readGeometry = null, TimeProvider? clock = null, TimeSpan? verificationWindow = null, TimeSpan? pollInterval = null)
    {
        _input = input;
        _sessionWindow = sessionWindow;
        _readGeometry = readGeometry;
        _clock = clock;
        _verificationWindow = verificationWindow;
        _pollInterval = pollInterval;
    }

    public EquipReport Equip(EquipRequest request, BagPanelRoiCalibration calibration, in ActuationAuthority authority, Func<WornEquipment?> readLatestEquip, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private bool EmitGesture(EquipGesture gesture, out string? refusal)
    {
        refusal = null;
        throw new NotImplementedException();
    }

    private EquipVerification Verify(ItemId item, EquipmentSlot targetSlot, WornEquipment baseline, Func<WornEquipment?> readLatestEquip, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
