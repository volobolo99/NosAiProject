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

/// <summary>
/// The operator command that clicks one equipment slot and asks the wire
/// whether the slot came off (AP-07 "togliere un pezzo di equipaggiamento",
/// execute → verify): <c>--unequip &lt;slot&gt; --gesture single|double|right</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>--unequip</c> names an <see cref="EquipmentSlot"/> and a gesture, and the
/// command goes through <see cref="UnequipExecutor"/>, which verifies the
/// panel calibration, projects the slot to a pixel, confines it to the window,
/// opens one actuation scope, clicks, and then verifies on the wire: the
/// decoder publishes <see cref="WornEquipment"/> from <c>equip</c>, so the
/// command can report — from the server, in milliseconds — whether the slot
/// left the set, is still there, or no <c>equip</c> arrived at all.
/// </para>
/// <para>
/// <b>No default gesture.</b> Nobody has recorded which gesture unequips, so
/// the operator declares what they are trying; the report says which gesture
/// produced which outcome. A default picked at a desk would turn a
/// <see cref="UnequipOutcome.NotConfirmed"/> into a claim about the game.
/// </para>
/// <para>
/// <b>Fail closed.</b> Without <c>--arm-input</c>, without a calibration, or
/// with the window out of focus, the click does not leave and the refusal names
/// the real cause — the same direction every actuating command here takes.
/// </para>
/// </remarks>
public static class UnequipCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--unequip";

    /// <summary>Selects the gesture; its value is <c>single</c>, <c>double</c> or <c>right</c>.</summary>
    public const string GestureOption = "--gesture";

    /// <summary>Overrides the verification window; its value is a positive number of milliseconds.</summary>
    public const string VerifyMsOption = "--verify-ms";

    /// <summary>Arms live input. Without it the command refuses before attaching to anything.</summary>
    public const string ArmInputOption = "--arm-input";

    /// <summary>An argument starting with <c>--</c> that is not a known option.</summary>
    public const string UnknownOptionReason = "unknown_option";

    /// <summary>No slot token followed the flag.</summary>
    public const string MissingSlotReason = "unequip_requires_slot";

    /// <summary>More than one positional slot was given.</summary>
    public const string AmbiguousSlotReason = "unequip_ambiguous_slot";

    /// <summary>The positional argument names no <see cref="EquipmentSlot"/>.</summary>
    public const string InvalidSlotReason = "unequip_invalid_slot";

    /// <summary>No <c>--gesture</c> was given. There is no default.</summary>
    public const string MissingGestureReason = "unequip_requires_gesture";

    /// <summary><c>--gesture</c> carried no value, or a value that is not a known gesture.</summary>
    public const string InvalidGestureReason = "unequip_invalid_gesture";

    /// <summary><c>--verify-ms</c> carried no value, or a value that is not a positive integer.</summary>
    public const string InvalidVerifyMsReason = "unequip_invalid_verify_ms";

    /// <summary>Live input was not armed with <c>--arm-input</c>.</summary>
    public const string InputNotArmedReason = "unequip_input_not_armed";

    /// <summary>Reported off Windows, where there is no client to attach to.</summary>
    public const string NotWindowsReason = "unequip_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "unequip_input_backend_not_gated";

    /// <summary>Reported when the equip feed could not be opened. The verdict depends on it.</summary>
    public const string EquipFeedUnavailableReason = "unequip_equip_feed_unavailable";

    /// <summary>Exit code: the wire showed the requested slot leaving the <c>equip</c> set.</summary>
    public const int ExitConfirmed = 0;

    /// <summary>Exit code: a new <c>equip</c> arrived and the requested slot is still occupied.</summary>
    public const int ExitStillWorn = 1;

    /// <summary>Exit code: no <c>equip</c> arrived within the verification window.</summary>
    public const int ExitNotConfirmed = 2;

    /// <summary>Exit code: refused before or at the click, with a named reason.</summary>
    public const int ExitRefused = 3;

    /// <summary>Exit code: the arguments did not name a slot and a gesture.</summary>
    public const int ExitUsage = 4;

    /// <summary>
    /// Validates the argument vector. Returns the named refusal reason, or null
    /// when well-formed (with the parsed slot, gesture, window and arming state).
    /// </summary>
    public static string? TryParse(
        string[] args,
        out EquipmentSlot? slot,
        out UnequipGesture? gesture,
        out int? verifyMs,
        out bool armInput)
    {
        slot = null;
        gesture = null;
        verifyMs = null;
        armInput = false;

        int flagIndex = Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
            return MissingSlotReason;

        for (int i = flagIndex + 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, GestureOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !Enum.TryParse(args[i + 1], ignoreCase: true, out UnequipGesture parsedGesture)
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

            if (slot is not null)
                return AmbiguousSlotReason;

            if (!Enum.TryParse(arg, ignoreCase: true, out EquipmentSlot parsedSlot) || !Enum.IsDefined(parsedSlot))
                return InvalidSlotReason;
            slot = parsedSlot;
        }

        if (slot is null)
            return MissingSlotReason;
        if (gesture is null)
            return MissingGestureReason;

        return null;
    }

    /// <summary>Console entry for <c>--unequip</c>.</summary>
    public static int Run(string[] args)
    {
        string? refusal = TryParse(args, out EquipmentSlot? slot, out UnequipGesture? gesture, out int? verifyMs, out bool armInput);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} <slot> --gesture single|double|right [--verify-ms <n>] --arm-input");
            Console.WriteLine($"  slot = one of: {string.Join(" ", Enum.GetNames<EquipmentSlot>())}");
            return ExitUsage;
        }

        if (!armInput)
        {
            Console.WriteLine($"[REFUSED] {InputNotArmedReason}");
            Console.WriteLine($"  {Flag} clicks the equipment panel; pass --arm-input to arm live input.");
            return ExitRefused;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return ExitRefused;
        }

        return RunWindows(slot!.Value, gesture!.Value, verifyMs);
    }

    /// <summary>
    /// The live composition. Mirror of <c>ClickTargetCommand.RunWindows</c>
    /// (same <see cref="RuntimeComposition.CreateSafe"/>, window lookup,
    /// <see cref="ClientMemorySession.TryAttach"/>, arming) plus the one thing
    /// <c>--click-target</c> reads through <c>GameplayObservation</c> and this
    /// command must read straight off the wire: the <c>equip</c> reading, which
    /// no gameplay provider publishes. Untested by design, exactly like
    /// <c>ClickTargetCommand.RunWindows</c>: only <see cref="TryParse"/> and
    /// <see cref="UnequipExecutor"/> are unit tested.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(EquipmentSlot slot, UnequipGesture gesture, int? verifyMs)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return ExitRefused;
        }

        // The switch is this process's, named, and dies with it. The operator
        // declared the arming by passing --arm-input; the gate still re-checks
        // the policy and the commit point on every call.
        AuthorizationDecision armed = components.Safety.Set(
            SecurityPrincipal.Operator, SafetySwitch.LiveInput, true, "unequip_command");
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

            // The wire is what verifies the click. The equip reading is not
            // published by any gameplay provider, so the capture chain is built
            // here instead of through LiveObservationScope.
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

            // The latest `equip` reading, remembered across polls: the report's
            // Equipment field is "most recent wins" across eq and equip, so an
            // interleaved eq must not erase the equip the verdict is built on.
            WornEquipment? lastEquip = null;
            Func<WornEquipment?> readLatestEquip = () =>
            {
                NetworkObservationReport report = observer.ObservePending();
                if (report.Equipment is { Opcode: EquipmentWireOpcode.Equip } equip)
                    lastEquip = equip;
                return lastEquip;
            };

            string repo = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            InventoryPanelRoiCalibration calibration = InventoryPanelRoiCalibration.Load(
                Path.Combine(repo, InventoryPanelRoiCalibration.RelativePath), out _);

            var executor = new UnequipExecutor(
                gated,
                () => window.Handle,
                verificationWindow: verifyMs is int ms ? TimeSpan.FromMilliseconds(ms) : null);
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
            var request = new UnequipRequest(slot, gesture);

            Console.WriteLine($"slot: {slot}");
            Console.WriteLine($"gesture: {gesture}");

            UnequipReport report = executor.Unequip(request, calibration, in authority, readLatestEquip);

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
                UnequipOutcome.Confirmed => ExitConfirmed,
                UnequipOutcome.StillWorn => ExitStillWorn,
                _ => ExitNotConfirmed
            };
        }
    }

    private static string DescribeVerification(UnequipVerification verification) => verification.Outcome switch
    {
        UnequipOutcome.Confirmed => string.Create(CultureInfo.InvariantCulture,
            $"confirmed: {verification.Slot} left the equip set in {verification.Waited.TotalMilliseconds:F0}ms"),
        UnequipOutcome.StillWorn => string.Create(CultureInfo.InvariantCulture,
            $"still-worn: {verification.Slot} is still in the equip set after {verification.Waited.TotalMilliseconds:F0}ms"),
        UnequipOutcome.NotConfirmed => string.Create(CultureInfo.InvariantCulture,
            $"not-confirmed: no equip within {verification.Waited.TotalMilliseconds:F0}ms"),
        _ => "not-attempted"
    };

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
}
