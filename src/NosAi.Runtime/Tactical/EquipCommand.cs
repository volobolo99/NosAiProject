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
    public const string UngatedBackendReason = "equip_input_backend_not_gated";
    public const string EquipFeedUnavailableReason = "equip_equip_feed_unavailable";
    public const int ExitConfirmed = 0;
    public const int ExitNotWorn = 1;
    public const int ExitNotConfirmed = 2;
    public const int ExitRefused = 3;
    public const int ExitUsage = 4;

    public static string? TryParse(string[] args, out ItemId? item, out EquipmentSlot? slot, out EquipGesture? gesture, out int? verifyMs, out bool armInput)
    {
        item = null;
        slot = null;
        gesture = null;
        verifyMs = null;
        armInput = false;

        int flagIndex = Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
            return MissingItemReason;

        for (int i = flagIndex + 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, SlotOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !Enum.TryParse(args[i + 1], ignoreCase: true, out EquipmentSlot parsedSlot)
                    || !Enum.IsDefined(parsedSlot))
                    return InvalidSlotReason;
                slot = parsedSlot;
                i++;
                continue;
            }

            if (string.Equals(arg, GestureOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !Enum.TryParse(args[i + 1], ignoreCase: true, out EquipGesture parsedGesture)
                    || !Enum.IsDefined(parsedGesture))
                    return InvalidGestureReason;
                gesture = parsedGesture;
                i++;
                continue;
            }

            if (string.Equals(arg, VerifyMsOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)
                    || ms <= 0)
                    return InvalidVerifyMsReason;
                verifyMs = ms;
                i++;
                continue;
            }

            if (string.Equals(arg, ArmInputOption, StringComparison.OrdinalIgnoreCase))
            {
                armInput = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                return $"{UnknownOptionReason}:{arg}";

            if (item is not null)
                return AmbiguousItemReason;

            if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedItemId)
                || parsedItemId <= 0)
                return InvalidItemReason;
            item = new ItemId(parsedItemId.ToString(CultureInfo.InvariantCulture));
        }

        if (item is null)
            return MissingItemReason;
        if (slot is null)
            return MissingSlotReason;
        if (gesture is null)
            return MissingGestureReason;

        return null;
    }

    public static int Run(string[] args)
    {
        string? refusal = TryParse(args, out ItemId? item, out EquipmentSlot? slot, out EquipGesture? gesture, out int? verifyMs, out bool armInput);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} <vnum> --slot <slot> --gesture single|double [--verify-ms <ms>] [--arm-input]");
            Console.WriteLine($"  slot = one of: {string.Join(" ", Enum.GetNames<EquipmentSlot>())}");
            Console.WriteLine($"  gesture = single or double");
            return ExitUsage;
        }

        if (!armInput)
        {
            Console.WriteLine($"[REFUSED] {InputNotArmedReason}");
            Console.WriteLine($"  {Flag} requires --arm-input to arm live input.");
            return ExitRefused;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return ExitRefused;
        }

        return RunWindows(item!.Value, slot!.Value, gesture!.Value, verifyMs);
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(ItemId item, EquipmentSlot slot, EquipGesture gesture, int? verifyMs)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return ExitRefused;
        }

        AuthorizationDecision armed = components.Safety.Set(
            SecurityPrincipal.Operator, SafetySwitch.LiveInput, true, "equip_command");
        if (!armed.Allowed)
        {
            Console.WriteLine($"[REFUSED] {InputNotArmedReason}:{armed.Reason}");
            return ExitRefused;
        }

        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return ExitRefused;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return ExitRefused;
        }

        using (session)
        {
            components.SessionAuthority?.BeginSession(window.Handle, processId);

            if (components.HumanInput is HumanInputMonitor monitor
                && !monitor.TryStart(out string? watchFailure))
            {
                Console.WriteLine($"[WARN] human monitor: {watchFailure}");
            }

            ClientNetworkObservation network = ClientNetworkObserver.Observe(processId);
            if (!network.Observed || network.Primary is not ClientTcpConnection primary)
            {
                Console.WriteLine($"[REFUSED] {EquipFeedUnavailableReason}:{network.FailureReason ?? "no_single_game_connection"}");
                return ExitRefused;
            }

            WinDivertPacketSource? packets = WinDivertPacketSource.TryOpen(
                primary.Remote.Address, primary.Remote.Port, out string? openFailure);
            if (packets is null)
            {
                Console.WriteLine($"[REFUSED] {EquipFeedUnavailableReason}:{openFailure}");
                return ExitRefused;
            }

            var endpoint = new GameEndpoint(primary.Remote.Address.ToString(), primary.Remote.Port);
            using ReassembledObservationSource observationSource =
                ReassembledObservationSource.ForNosTaleWorld(packets, NosAi.Runtime.Contracts.DataSourceKind.Live);
            var observer = new GameTrafficObserver(
                observationSource,
                new ScopedGameTrafficFilter(endpoint),
                new NosTaleWorldProtocolDecoder());

            NetworkObservationReport bagReport = observer.ObservePending();
            int? bagSlotIndex = null;
            foreach (InventorySlotReading inventorySlot in bagReport.InventorySlots)
            {
                if (inventorySlot.Vnum.ToString(CultureInfo.InvariantCulture) == item.Value)
                {
                    bagSlotIndex = inventorySlot.Slot;
                    break;
                }
            }

            if (bagSlotIndex is null)
            {
                Console.WriteLine($"[REFUSED] {ItemNotInInventoryReason}");
                return ExitRefused;
            }

            WornEquipment? lastEquip = null;
            Func<WornEquipmentReading?> readLatestEquip = () =>
            {
                NetworkObservationReport r = observer.ObservePending();
                if (r.Equipment is { Opcode: EquipmentWireOpcode.Equip } equip)
                    lastEquip = equip;
                return lastEquip is null ? null : new WornEquipmentReading(lastEquip.Slots, lastEquip.Opcode);
            };

            string repo = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            BagPanelRoiCalibration calibration = BagPanelRoiCalibration.Load(
                Path.Combine(repo, BagPanelRoiCalibration.RelativePath), out _);

            var executor = new EquipExecutor(
                gated,
                () => window.Handle,
                verificationWindow: verifyMs is int ms ? TimeSpan.FromMilliseconds(ms) : null);
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
            var request = new EquipRequest(item, slot, bagSlotIndex.Value, gesture);

            Console.WriteLine($"item: {item}");
            Console.WriteLine($"slot: {slot}");
            Console.WriteLine($"gesture: {gesture}");

            EquipReport report = executor.Equip(request, calibration, in authority, readLatestEquip);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"pixel: {report.ScreenX},{report.ScreenY}"));

            if (!report.Emitted)
            {
                Console.WriteLine($"not-emitted: {report.RefusalReason}");
                return ExitRefused;
            }

            Console.WriteLine("scope: emitted");
            Console.WriteLine($"verification: {DescribeVerification(report.Verification)}");

            return report.Verification.Outcome switch
            {
                EquipOutcome.Confirmed => ExitConfirmed,
                EquipOutcome.NotWorn => ExitNotWorn,
                _ => ExitNotConfirmed
            };
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryFindWindow(out ClientWindow window, out int processId, out string? failureReason)
    {
        processId = 0;
        foreach (string name in RealClientConnector.DefaultProcessNames)
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    ClientWindow? found = ClientWindowLocator.TryFind(process.Id, out string? why);
                    if (found is not null)
                    {
                        window = found;
                        processId = process.Id;
                        failureReason = null;
                        return true;
                    }

                    failureReason = why;
                }
            }
        }

        window = null!;
        failureReason = $"{InputGuardsProbe.WindowNotLocatedReason}:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }

    private static string DescribeVerification(EquipVerification verification) => verification.Outcome switch
    {
        EquipOutcome.Confirmed => string.Create(CultureInfo.InvariantCulture,
            $"confirmed: {verification.Slot} entered the equip set in {verification.Waited.TotalMilliseconds:F0}ms"),
        EquipOutcome.NotWorn => string.Create(CultureInfo.InvariantCulture,
            $"not-worn: {verification.Slot} was not worn in the equip set after {verification.Waited.TotalMilliseconds:F0}ms"),
        EquipOutcome.NotConfirmed => string.Create(CultureInfo.InvariantCulture,
            $"not-confirmed: no equip within {verification.Waited.TotalMilliseconds:F0}ms"),
        _ => "not-attempted"
    };
}
