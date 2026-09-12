using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Security;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Tactical;

public class EquipCommand
{
    public const string Flag = "--equip";
    public const string SlotOption = "--slot";
    public const string GestureOption = "--gesture";
    public const string VerifyMsOption = "--verify-ms";
    public const string ArmInputOption = "--arm-input";
    public const string UnknownOptionReason = "unknown_option";
    public const string MissingItemReason = "equip_requires_item";
    public const string AmbiguousItemReason = "equip_ambiguous_item";
    public const string InvalidItemReason = "equip_invalid_item";
    public const string MissingSlotReason = "equip_requires_slot";
    public const string InvalidSlotReason = "equip_invalid_slot";
    public const string MissingGestureReason = "equip_requires_gesture";
    public const string InvalidGestureReason = "equip_invalid_gesture";
    public const string InvalidVerifyMsReason = "equip_invalid_verify_ms";
    public const string InputNotArmedReason = "equip_input_not_armed";
    public const string NotWindowsReason = "equip_requires_windows";
    public const string ItemNotInInventoryReason = "equip_item_not_in_inventory";
    public const int ExitConfirmed = 0;
    public const int ExitNotWorn = 1;
    public const int ExitNotConfirmed = 2;
    public const int ExitRefused = 3;
    public const int ExitUsage = 4;

    public static string? TryParse(string[] args, out ItemId? item, out EquipmentSlot? slot, out EquipGesture? gesture, out int? verifyMs, out bool armInput)
    {
        throw new NotImplementedException();
    }

    public static int Run(string[] args)
    {
        throw new NotImplementedException();
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(ItemId item, EquipmentSlot slot, EquipGesture gesture, int? verifyMs)
    {
        throw new NotImplementedException();
    }
}
