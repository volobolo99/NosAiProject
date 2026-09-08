using System.Globalization;
using System.Collections;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Gate4;
using NosAi.Runtime.Gate5;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Every certification suite the runtime carries lives in CertificationSuites,
        // because the operator's test page needs the same list. Two copies would have
        // diverged the first time a suite was added to one and not the other.
        IReadOnlyDictionary<string, Func<Task<bool>>> suites = CertificationSuites.ByFlag;

        foreach (string argument in args)
        {
            if (suites.TryGetValue(argument, out Func<Task<bool>>? suite))
                return await suite().ConfigureAwait(false) ? 0 : 1;
        }

        // Runs every suite and reports AP-10's per-stage scorecard instead of one
        // bool. The suites already answered pass/fail per gate; what nothing did
        // was say which of the fourteen stages that evidences, what backs the
        // claim, and what is missing.
        if (args.Any(a => string.Equals(a, CertificationReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return await CertificationReportCommand.RunAsync().ConfigureAwait(false);

        // A mistyped suite flag used to fall through to the normal bootstrap and
        // start the whole runtime: "--1-test" instead of "--gate1-test" left a
        // host running for as long as nobody noticed, holding the build's output
        // files. Anything shaped like a suite or probe flag that is not one is a
        // typo, and a typo must not boot the runtime.
        string? mistyped = args.FirstOrDefault(a =>
            a.StartsWith("--", StringComparison.Ordinal) &&
            (a.EndsWith("-test", StringComparison.OrdinalIgnoreCase) ||
             a.EndsWith("-probe", StringComparison.OrdinalIgnoreCase)) &&
            !suites.ContainsKey(a) &&
            !KnownProbeFlags.Contains(a));
        if (mistyped is not null)
        {
            Console.Error.WriteLine($"Unknown suite or probe flag: {mistyped}");
            Console.Error.WriteLine("Run --list-suites to see the available suites, or use --dxgi-probe / --input-probe.");
            return 2;
        }

        // One screen instead of a list of flags to remember. It adds no
        // capability: every entry calls the command its flag calls, and the ones
        // that actuate are not in it.
        if (args.Any(a => string.Equals(a, "--menu", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Operator.OperatorMenu.Run();

        if (args.Any(a => string.Equals(a, "--list-suites", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string flag in suites.Keys.OrderBy(f => f, StringComparer.Ordinal))
                Console.WriteLine(flag);
            return 0;
        }

        // Which modules the runtime actually reaches. --list-suites answers "what
        // can be run"; this answers the question the audit of 2026-08-30 asked and
        // a document could not keep answering: what is wired, what is only
        // reachable from its own suite, and what nothing reaches at all.
        if (args.Any(a => string.Equals(a, "--module-report", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Write(NosAi.Runtime.Observability.ModuleReachability.Report());
            return 0;
        }

        // Real-environment probe for the DXGI capture backend. The perception suite
        // certifies the contract without a desktop; only a real interactive session
        // can say whether Desktop Duplication actually yields live pixels here.
        if (args.Any(a => string.Equals(a, "--dxgi-probe", StringComparison.OrdinalIgnoreCase)))
            return RunDxgiProbe();

        // Real-environment probe for the input layer. --input-test certifies the
        // contract against a recording backend; only a real desktop can say
        // whether SendInput actually reaches the OS input queue.
        if (args.Any(a => string.Equals(a, "--input-probe", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.LowLevel.InputEnvironmentProbe.RunConsoleProbe();

        // Real-environment probe for the HUD reader (T-03). The Control Panel has
        // had this behind a button; running it here makes the test repeatable and
        // quotable instead of clicked and described.
        if (args.Any(a => string.Equals(a, "--hud-probe", StringComparison.OrdinalIgnoreCase)))
        {
            // --calibrate-target <x> <y> <w> <h> records the target-frame region
            // (ADR-0018). The four fractions are the operator's confirmation that
            // the crop they just looked at is the target frame; nothing infers
            // them, because a reading of the wrong pixels is what the calibration
            // exists to rule out.
            int calibrateFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--calibrate-target", StringComparison.OrdinalIgnoreCase));
            (double, double, double, double)? region = null;
            if (calibrateFlag >= 0)
            {
                if (calibrateFlag + 4 >= args.Length
                    || !double.TryParse(args[calibrateFlag + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double rx)
                    || !double.TryParse(args[calibrateFlag + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ry)
                    || !double.TryParse(args[calibrateFlag + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out double rw)
                    || !double.TryParse(args[calibrateFlag + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out double rh))
                {
                    Console.Error.WriteLine(
                        "--calibrate-target <x> <y> <width> <height> requires four fractions of the client area.");
                    return 2;
                }

                region = (rx, ry, rw, rh);
            }

            return NosAi.Runtime.Perception.HudProbe.RunConsoleProbe(calibrateTarget: region);
        }

        // AP-07/A2+A4: records where the equipment-panel slots sit on this
        // operator's client, the screen-space layout --calibrate-target
        // (ADR-0018) is to the target frame what this is to the equipment
        // panel: one crop per declared EquipmentSlot value or none, confirmed
        // by the operator against inventory_panel_latest.bmp. The fractions
        // are that confirmation; nothing infers them. Refuses, without
        // attaching to any client, unless exactly one token per declared slot
        // is present.
        if (args.Any(a => string.Equals(a, "--calibrate-inventory-panel", StringComparison.OrdinalIgnoreCase)))
        {
            int panelFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--calibrate-inventory-panel", StringComparison.OrdinalIgnoreCase));
            int panelSlotCount = Enum.GetValues(typeof(NosAi.Core.WorldModel.EquipmentSlot)).Length;
            int panelTokenCount = args.Length - (panelFlag + 1);
            if (panelTokenCount != 0 && panelTokenCount != panelSlotCount)
            {
                Console.Error.WriteLine(
                    $"[REFUSED] --calibrate-inventory-panel requires all {panelSlotCount} slot tokens "
                    + "(one per declared EquipmentSlot value, in any order), or none at all to report "
                    + "the current state. Example: --calibrate-inventory-panel "
                    + "Weapon:<x>,<y>,<w>,<h> Armor:<x>,<y>,<w>,<h> Hat:<x>,<y>,<w>,<h> ...");
                return 1;
            }

            var panelTokens = new string[panelTokenCount];
            for (int i = 0; i < panelTokenCount; i++)
                panelTokens[i] = args[panelFlag + 1 + i];

            if (panelTokenCount == 0)
            {
                // Zero tokens: only report the current calibration state.
                return NosAi.Runtime.Perception.InventoryPanelCalibrationProbe.Run(slots: null);
            }

            if (!NosAi.Runtime.Perception.InventoryPanelCalibrationProbe.TryParseSlots(
                    panelTokens, out IReadOnlyDictionary<NosAi.Core.WorldModel.EquipmentSlot,
                        NosAi.Runtime.Perception.InventorySlotRoi>? panelRois, out string? panelReason))
            {
                Console.Error.WriteLine($"[REFUSED] {panelReason}");
                Console.Error.WriteLine("  --calibrate-inventory-panel requires one slot token per declared "
                    + "EquipmentSlot value, each 'Slot:<x>,<y>,<w>,<h>' as fractions of the client area.");
                return 1;
            }

            return NosAi.Runtime.Perception.InventoryPanelCalibrationProbe.Run(slots: panelRois);
        }

        // Physical client rect, window DPI, monitor handle, epoch, the process's
        // actual awareness mode, and whether the stored calibration can be applied
        // under that regime. Non-zero when it cannot.
        if (args.Any(a => string.Equals(a, "--window-probe", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ClientWindowDpiProbe.Run();

        // The five commit-point conditions against the live window. Observation
        // only: a refused verdict is printed, nothing is emitted. --watch <s>
        // keeps the stamp taken at the start so the three real-client proofs
        // (window moved, point covered, hand on the mouse) are named refusals.
        if (args.Any(a => string.Equals(a, "--input-guards", StringComparison.OrdinalIgnoreCase)))
        {
            int guardsWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int seconds = guardsWatchFlag >= 0 && guardsWatchFlag + 1 < args.Length
                          && int.TryParse(args[guardsWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                          && parsed > 0
                ? parsed
                : 0;

            return NosAi.Runtime.LowLevel.InputGuardsProbe.Run(seconds);
        }

        // Session actuation verdict: integrity comparison and the harmless probe.
        // A verification command — non-zero when the session is not actuating.
        // --watch <n> repeats n times at 1 s, calling EnsureVerified so the
        // operator can bring the client forward and see the verdict change.
        if (args.Any(a => string.Equals(a, "--input-authority", StringComparison.OrdinalIgnoreCase)))
        {
            int authorityWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int repeats = authorityWatchFlag >= 0 && authorityWatchFlag + 1 < args.Length
                          && int.TryParse(args[authorityWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRepeats)
                          && parsedRepeats > 0
                ? parsedRepeats
                : 0;

            return NosAi.Runtime.LowLevel.InputAuthorityProbe.Run(repeats);
        }

        // One adjacent-cell step (S4 / C2-4). The chain and the executor already
        // exist; this only prints them, audits them, and names the operator
        // command as the authority of the act. It does not arm input.
        int stepFlag = Array.FindIndex(args, a =>
            string.Equals(a, NosAi.Runtime.Navigation.SingleStepCommand.Flag, StringComparison.OrdinalIgnoreCase));
        if (stepFlag >= 0)
        {
            if (stepFlag + 2 >= args.Length
                || !int.TryParse(args[stepFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stepDx)
                || !int.TryParse(args[stepFlag + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stepDy))
            {
                Console.Error.WriteLine("--step <dx> <dy> requires two integer cell offsets.");
                return NosAi.Runtime.Navigation.SingleStepCommand.ExitUsage;
            }

            return NosAi.Runtime.Navigation.SingleStepCommand.Run(stepDx, stepDy);
        }

        // Walk a path to an absolute cell (C2-7). The controller admits, decides
        // and replans; this command is the loop around those four methods, the
        // printout, and the audit. It does not arm input. --dry-run admits and
        // prints the guard ladder without reaching the input backend.
        int walkFlag = Array.FindIndex(args, a =>
            string.Equals(a, NosAi.Runtime.Navigation.WalkCommand.Flag, StringComparison.OrdinalIgnoreCase));
        if (walkFlag >= 0)
        {
            if (walkFlag + 2 >= args.Length
                || !int.TryParse(args[walkFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int walkGx)
                || !int.TryParse(args[walkFlag + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int walkGy))
            {
                Console.Error.WriteLine("--walk <gx> <gy> requires two integer cell coordinates.");
                return NosAi.Runtime.Navigation.WalkCommand.ExitUsage;
            }

            bool dryRun = args.Any(a =>
                string.Equals(a, NosAi.Runtime.Navigation.WalkCommand.DryRunFlag, StringComparison.OrdinalIgnoreCase));
            return NosAi.Runtime.Navigation.WalkCommand.Run(walkGx, walkGy, dryRun);
        }

        // One scout round (or --watch <n> rounds): pick the best unvisited frontier on
        // the current map from the real World Model (ExplorationPlanner, AP-04) and
        // walk to it by calling the same WalkCommand.Execute --walk uses, under the
        // same commanded-authority family. It does not arm input.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.ScoutCommand.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int scoutWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int scoutRounds = scoutWatchFlag >= 0 && scoutWatchFlag + 1 < args.Length
                              && int.TryParse(args[scoutWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedScoutRounds)
                              && parsedScoutRounds > 0
                ? parsedScoutRounds
                : 1;

            return NosAi.Runtime.Navigation.ScoutCommand.Run(scoutRounds);
        }

        // One engage round (or --watch <n> rounds): execute and verify one
        // UseSkill act named directly by the operator (target entity id + skill
        // id), resolving the key from the operator's own keybinds and reading the
        // player's vitals before/after. Same commanded-authority family as
        // --walk/--scout. It does not arm input.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.EngageCommand.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int engageIndex = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Tactical.EngageCommand.Flag, StringComparison.OrdinalIgnoreCase));
            if (engageIndex + 2 >= args.Length)
            {
                Console.WriteLine("[REFUSED] --engage requires <targetEntityId> <skillId>");
                return 1;
            }

            string targetEntityId = args[engageIndex + 1];
            string skillId = args[engageIndex + 2];

            int engageWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int engageRounds = engageWatchFlag >= 0 && engageWatchFlag + 1 < args.Length
                               && int.TryParse(args[engageWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedEngageRounds)
                               && parsedEngageRounds > 0
                ? parsedEngageRounds
                : 1;

            return NosAi.Runtime.Tactical.EngageCommand.Run(targetEntityId, skillId, engageRounds);
        }

        // One collect round (or --watch <n> rounds): walk to an operator-named
        // position and verify one Collect objective by reading the player's own
        // inventory count of the named vnum before and after the walk (wire ivn
        // evidence, not OCR). Same commanded-authority family as --walk/--scout/
        // --engage. It does not arm input.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.CollectCommand.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int collectIndex = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Navigation.CollectCommand.Flag, StringComparison.OrdinalIgnoreCase));
            if (collectIndex + 3 >= args.Length
                || !int.TryParse(args[collectIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int collectX)
                || !int.TryParse(args[collectIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int collectY))
            {
                Console.WriteLine("[REFUSED] --collect requires <x> <y> <vnum> [<requiredCount>]");
                return 1;
            }

            string collectVnum = args[collectIndex + 3];
            int? requiredCount = collectIndex + 4 < args.Length
                && int.TryParse(args[collectIndex + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRequired)
                ? parsedRequired
                : null;

            int collectWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int collectRounds = collectWatchFlag >= 0 && collectWatchFlag + 1 < args.Length
                                && int.TryParse(args[collectWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCollectRounds)
                                && parsedCollectRounds > 0
                ? parsedCollectRounds
                : 1;

            return NosAi.Runtime.Navigation.CollectCommand.Run(collectX, collectY, collectVnum, requiredCount, collectRounds);
        }

        // One recover round (or --watch <n> rounds): execute and verify one
        // UseConsumable press at one operator-named quickbar slot, resolving the
        // key from the operator's own keybinds (the consumable.{slot} intent) and
        // reading the player's vitals before/after. Same commanded-authority
        // family as --walk/--scout/--engage/--collect. It does not arm input.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.RecoverCommand.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int recoverIndex = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Tactical.RecoverCommand.Flag, StringComparison.OrdinalIgnoreCase));
            if (recoverIndex + 1 >= args.Length
                || !int.TryParse(args[recoverIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int recoverSlot))
            {
                Console.WriteLine("[REFUSED] --recover requires <slot>");
                return 1;
            }

            int recoverWatchFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            int recoverRounds = recoverWatchFlag >= 0 && recoverWatchFlag + 1 < args.Length
                                && int.TryParse(args[recoverWatchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRecoverRounds)
                                && parsedRecoverRounds > 0
                ? parsedRecoverRounds
                : 1;

            return NosAi.Runtime.Tactical.RecoverCommand.Run(recoverSlot, recoverRounds);
        }

        // The first operator command that chooses, cycle by cycle, which
        // already-built act to attempt: reads the live vitals/position/map,
        // asks StrategyPlanner which StrategicGoalKind is most urgent
        // (Survival/Exploration only -- the other kinds have no honest signal
        // today), and dispatches to RecoverCommand/ScoutCommand under its own
        // commanded authority. It does not arm input and never emits an input
        // event itself. --cycles is its own name (not --watch): --autoplay
        // choosing what to do each cycle is different from --watch repeating
        // the same operator-named act.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.AutoplayCommand.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int cyclesFlag = Array.FindIndex(args, a => string.Equals(a, "--cycles", StringComparison.OrdinalIgnoreCase));
            int cycles = cyclesFlag >= 0 && cyclesFlag + 1 < args.Length
                         && int.TryParse(args[cyclesFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCycles)
                ? parsedCycles
                : 1;

            int slotFlag = Array.FindIndex(args, a => string.Equals(a, "--recover-slot", StringComparison.OrdinalIgnoreCase));
            int? recoverSlot = slotFlag >= 0 && slotFlag + 1 < args.Length
                               && int.TryParse(args[slotFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSlot)
                ? parsedSlot
                : null;

            return NosAi.Runtime.Tactical.AutoplayCommand.Run(cycles, recoverSlot);
        }

        // Which intents the operator bound, and which the runtime can ask for
        // that are not bound. Non-zero when the file is missing or a required
        // prefix is uncovered. Does not write data/keybinds.json.
        if (args.Any(a => string.Equals(a, "--keybinds-check", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.LowLevel.KeybindsCheck.Run();

        if (args.Any(a => string.Equals(a, "--extract-maps", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapGridExtractor.RunExtract();

        int mapInfoFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--map-info", StringComparison.OrdinalIgnoreCase));
        if (mapInfoFlag >= 0)
        {
            if (mapInfoFlag + 1 >= args.Length
                || !int.TryParse(args[mapInfoFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
            {
                Console.Error.WriteLine("--map-info <mapId> requires the map identifier.");
                return 2;
            }

            return NosAi.Runtime.Navigation.MapGridExtractor.RunInfo(mapId);
        }

        // Standing-cell proof: map id and position from the live client, the
        // bytes of that cell and its eight neighbours from the extracted grid.
        // Read-only — a blocked cell is reported, not rewritten.
        if (args.Any(a => string.Equals(a, "--grid-check", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapGridCheck.Run();

        // The 777 grids as an oracle: a word in memory is a map id only while it
        // names a .grid that contains the character, and only while that word
        // changes across a portal. +0x30 was a pointer; this does not follow it.
        if (args.Any(a => string.Equals(a, "--find-mapid", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.MapIdFinder.Run();

        // The same oracle, aimed at the selected entity instead of the map id
        // (ADR-0021). --no-target declares the pass that tells the selection apart
        // from the client's own entity list, which every entry of that list would
        // otherwise survive.
        // The chain the hunt established, and the proof its last link still needs:
        // manager -> target pointer -> candidate id, against what ct named on the wire.
        if (args.Any(a => string.Equals(a, "--target-chain", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.TargetChainProbe.Run();

        // Reports whether a chain of already-observed portal crossings
        // (Q-070/Q-071) can get the operator from the current map to the
        // named one, and what it is. Read-only: plans, never walks.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.RouteProbe.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int routeFlagIndex = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Navigation.RouteProbe.Flag, StringComparison.OrdinalIgnoreCase));
            if (routeFlagIndex + 3 >= args.Length
                || !int.TryParse(args[routeFlagIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int routeDestinationMapId)
                || !float.TryParse(args[routeFlagIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float routeDestinationX)
                || !float.TryParse(args[routeFlagIndex + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out float routeDestinationY))
            {
                Console.WriteLine("[REFUSED] --route requires <destinationMapId> <x> <y>");
                return 1;
            }

            return NosAi.Runtime.Navigation.RouteProbe.Run(routeDestinationMapId, routeDestinationX, routeDestinationY);
        }

        if (args.Any(a => string.Equals(a, "--find-target", StringComparison.OrdinalIgnoreCase)))
        {
            // No flag says whether a target is selected any more: the hunt asks, round
            // by round, while it runs. --no-target is gone rather than ignored, because
            // a flag that is accepted and does nothing is worse than one that is refused.
            return NosAi.Runtime.Navigation.TargetIdFinder.Run();
        }

        // Map coordinate to window pixel (F2-3). Two commands because a
        // calibration is gathered across several moments in the game, so the
        // samples have to outlive one invocation, exactly as --memory-scan's
        // candidates do.
        int screenSampleFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--screen-sample", StringComparison.OrdinalIgnoreCase));
        if (screenSampleFlag >= 0)
        {
            if (screenSampleFlag + 2 >= args.Length
                || !int.TryParse(args[screenSampleFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapX)
                || !int.TryParse(args[screenSampleFlag + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapY))
            {
                Console.Error.WriteLine(
                    "--screen-sample <mapX> <mapY> requires the coordinates the game is showing.");
                return 2;
            }

            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunSample(null, mapX, mapY);
        }

        // The calibration with nobody in it: the runtime picks the pixels, clicks
        // them and reads back which square the client resolved each one to. It
        // walks the character, so the gate stays shut without --arm-input.
        if (args.Any(a => string.Equals(a, "--screen-autocalibrate", StringComparison.OrdinalIgnoreCase)))
        {
            bool armInput = args.Any(a => string.Equals(a, "--arm-input", StringComparison.OrdinalIgnoreCase));
            return NosAi.Runtime.Perception.ScreenProjectionAutoCalibrator.Run(armInput);
        }

        // Collects samples by watching the operator click to walk. The character's
        // own position cannot calibrate this: the camera follows it, so it stays
        // drawn in the same place and the samples describe nothing.
        int watchFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--screen-watch", StringComparison.OrdinalIgnoreCase));
        if (watchFlag >= 0)
        {
            int seconds = watchFlag + 1 < args.Length
                          && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSeconds)
                          && parsedSeconds > 0
                ? parsedSeconds
                : 300;

            // Dodici campioni con una pausa vera fra un clic e l'altro non stanno
            // in un minuto, e cinque campioni non determinano il fit.
            return NosAi.Runtime.Perception.ScreenProjectionWatcher.Run(
                seconds, wanted: NosAi.Runtime.Perception.ScreenSampleCoach.DefaultWantedSamples);
        }

        if (args.Any(a => string.Equals(a, "--screen-calibrate", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunSolve(null);

        if (args.Any(a => string.Equals(a, "--screen-samples-clear", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Perception.ScreenProjectionProbe.RunClear(null);

        // T-11 in one command: resolve the character object in the running client
        // and check the id it holds against the id the server sent. Read-only.
        int playerProbeFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--player-probe", StringComparison.OrdinalIgnoreCase));
        if (playerProbeFlag >= 0)
        {
            int pidFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--pid", StringComparison.OrdinalIgnoreCase));
            int targetPid = pidFlag >= 0 && pidFlag + 1 < args.Length
                            && int.TryParse(args[pidFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPid)
                ? parsedPid
                : 0;

            int expectFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--expect-id", StringComparison.OrdinalIgnoreCase));
            long? expectedId = expectFlag >= 0 && expectFlag + 1 < args.Length
                               && long.TryParse(args[expectFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedId)
                ? parsedId
                : null;

            return NosAi.LiveIntegration.PlayerObjectProbe.Run(targetPid, expectedId);
        }

        // Phase 1 of the memory-layout extension: entity names as candidates,
        // beside the name an `in` (or `drop`) gave for the same id when a
        // recording has one. Never LIVE.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.EntityNameProbe.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.EntityNameProbe.Flag, StringComparison.OrdinalIgnoreCase));
            string? capture = flag + 1 < args.Length && !args[flag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 1]
                : null;
            return NosAi.LiveIntegration.EntityNameProbe.Run(capture);
        }

        // The second source the phases above are checked against, taken now
        // rather than read out of an archive. Entity ids are per-session and
        // vitals are per-instant, so a recording corroborates only the session it
        // was taken in; the probes had no way to obtain one until this flag.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.Capture.WireRecorder.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.Capture.WireRecorder.Flag, StringComparison.OrdinalIgnoreCase));
            string? endpoint = flag + 1 < args.Length && !args[flag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 1]
                : null;
            string? file = flag + 2 < args.Length && !args[flag + 2].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 2]
                : null;

            var watchSeconds = 0;
            int watchAt = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            if (watchAt >= 0
                && watchAt + 1 < args.Length
                && int.TryParse(args[watchAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                && seconds > 0)
            {
                watchSeconds = seconds;
            }

            return NosAi.LiveIntegration.Capture.WireRecorder.Run(endpoint, file, watchSeconds);
        }

        // Decodifica in tempo reale: stessa catena di decodifica di --world-replay
        // (NosTaleWorldProtocolDecoder) applicata al filo live invece che a un file,
        // cosi' ogni pacchetto in arrivo viene stampato subito, non dopo la fine
        // della cattura. RETE mostra la riga grezza per qualunque opcode; CLIENT la
        // lettura semantica solo per gli opcode che il decoder reale conosce.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.Capture.LiveWireMonitor.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.Capture.LiveWireMonitor.Flag, StringComparison.OrdinalIgnoreCase));
            string? endpoint = flag + 1 < args.Length && !args[flag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 1]
                : null;

            var watchSeconds = 0;
            int watchAt = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            if (watchAt >= 0
                && watchAt + 1 < args.Length
                && int.TryParse(args[watchAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                && seconds > 0)
            {
                watchSeconds = seconds;
            }

            return NosAi.LiveIntegration.Capture.LiveWireMonitor.Run(endpoint, watchSeconds);
        }

        // Phase 2 the other way round. Instead of asking which memory looks like
        // health, this asks the wire what health is and looks for those two
        // numbers side by side, then requires them to move together. No operator
        // judgement, and no offset from anyone else's build.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.PlayerVitalsCalibrator.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.PlayerVitalsCalibrator.Flag, StringComparison.OrdinalIgnoreCase));
            string? endpoint = flag + 1 < args.Length && !args[flag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 1]
                : null;

            var roundSeconds = 20;
            int watchAt = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            if (watchAt >= 0
                && watchAt + 1 < args.Length
                && int.TryParse(args[watchAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int perRound)
                && perRound > 0)
            {
                roundSeconds = perRound;
            }

            return NosAi.LiveIntegration.PlayerVitalsCalibrator.Run(endpoint, roundSeconds);
        }

        // What points at an address, so a calibrated heap address can become a
        // distance from something the runtime resolves again on every read. A
        // reboot killed one confirmed address during this work; that is the whole
        // reason this exists.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.PointerAnchorHunter.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.PointerAnchorHunter.Flag, StringComparison.OrdinalIgnoreCase));
            string? target = flag + 1 < args.Length && !args[flag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[flag + 1]
                : null;

            int span = NosAi.LiveIntegration.PointerAnchorHunter.DefaultSpan;
            int spanAt = Array.FindIndex(args, a => string.Equals(a, "--window", StringComparison.OrdinalIgnoreCase));
            if (spanAt >= 0 && spanAt + 1 < args.Length && TryParseWindow(args[spanAt + 1], out int requestedSpan))
                span = requestedSpan;

            return NosAi.LiveIntegration.PointerAnchorHunter.Run(target, span);
        }

        // Phase 3 over the whole process instead of two 8 KB windows. The windowed
        // finder returned zero survivors on a real client across two valid rounds,
        // which is the shape phase 2 had before the search was widened rather than
        // the window.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.SkillCooldownSweep.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Navigation.SkillCooldownSweep.Flag, StringComparison.OrdinalIgnoreCase));
            if (flag + 1 >= args.Length
                || !int.TryParse(args[flag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sweepSlot)
                || sweepSlot < 0)
            {
                Console.Error.WriteLine(
                    "--sweep-cooldown <slot> requires the slot the wire numbers this skill with.");
                return 2;
            }

            return NosAi.Runtime.Navigation.SkillCooldownSweep.Run(sweepSlot);
        }

        // Phase 2 of the memory-layout extension: player HP/MP as candidates
        // found by scanning the resolved bases, beside the percentage a
        // recording derived from stat/st/in. Never LIVE. Never an RVA.
        if (args.Any(a => string.Equals(a, NosAi.LiveIntegration.PlayerVitalsProbe.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.LiveIntegration.PlayerVitalsProbe.Flag, StringComparison.OrdinalIgnoreCase));
            string? capture = null;
            int watchSeconds = 0;
            int windowBytes = NosAi.LiveIntegration.PlayerVitalsScan.DefaultWindowBytes;
            for (int i = flag + 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--watch", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    && seconds > 0)
                {
                    watchSeconds = seconds;
                    i++;
                    continue;
                }

                // How far past each base to look, so the span can be widened
                // against a real client without a rebuild. Hex is accepted
                // because every offset in this area is written that way.
                if (string.Equals(args[i], "--window", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                    && TryParseWindow(args[i + 1], out int requested))
                {
                    windowBytes = requested;
                    i++;
                    continue;
                }

                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    capture = args[i];
            }

            return NosAi.LiveIntegration.PlayerVitalsProbe.Run(capture, watchSeconds, windowBytes);
        }

        // Phase 3 of the memory-layout extension: which word is this skill's
        // cooldown, decided by how it behaves against the wire's `sr` rather than
        // by either of the two chains the sources disagree about.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.SkillCooldownProbe.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int flag = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Navigation.SkillCooldownProbe.Flag, StringComparison.OrdinalIgnoreCase));
            if (flag + 1 >= args.Length
                || !int.TryParse(args[flag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int skillSlot)
                || skillSlot < 0)
            {
                Console.Error.WriteLine(
                    "--skill-cooldowns <slot> requires the slot the wire numbers this skill with.");
                return 2;
            }

            return NosAi.Runtime.Navigation.SkillCooldownProbe.Run(skillSlot);
        }

        // What a recording says, read by the runtime's own decoder and needing no
        // driver. WinDivertProbe --world does the same, but it has to sit beside a
        // staged WinDivert.dll and it holds the runtime assembly open while it
        // runs; this reads a capture with nothing staged and nothing locked.
        int worldReplayFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--world-replay", StringComparison.OrdinalIgnoreCase));
        if (worldReplayFlag >= 0)
        {
            string? capture = worldReplayFlag + 1 < args.Length ? args[worldReplayFlag + 1] : null;
            if (string.IsNullOrWhiteSpace(capture))
            {
                Console.Error.WriteLine("--world-replay <file.noscap> requires a recording path.");
                return 2;
            }

            if (!File.Exists(capture))
            {
                Console.Error.WriteLine($"Recording not found: {capture}");
                return 2;
            }

            GameReferenceDatabase? catalog = null;
            if (!GameReferenceLocator.TryOpen(out catalog, out string? catalogReason))
                Console.Error.WriteLine($"reference catalog: {catalogReason}");
            else
                Console.Error.WriteLine($"reference catalog: {catalog!.DatabasePath}");

            try
            {
                return NosAi.Runtime.Observability.WorldReplayCommand.Run(capture, catalog);
            }
            finally
            {
                catalog?.Dispose();
            }
        }

        // Which catalogue this process would open, and what is in it. A missing
        // file is reported rather than invented: replay without names is still
        // a replay, and this is how the operator tells the two cases apart.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.ReferenceInfoCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.ReferenceInfoCommand.Run();

        // AP-07/Q-094: what LoadoutPlanner would propose right now against
        // the live inventory and the real on-disk item catalogue, and whether
        // each proposal passes its hard constraints. Read-only: no key, no
        // mouse, no input arming -- nothing here can equip, unequip or
        // upgrade. A report with zero candidates is a valid result, not a
        // failure; a missing catalogue degrades only the Equip section.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.LoadoutReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Tactical.LoadoutReportCommand.Run();

        // What CombatPlanner would propose right now against the mobs actually
        // observed, and whether each proposal passes its hard constraints.
        // Read-only in the same sense as --loadout-report: no key, no mouse, no
        // input arming -- executing a combat act is --engage's job. A report
        // with zero candidates is a valid result and says which of the several
        // possible reasons it had.
        int combatReportIndex = Array.FindIndex(args, a =>
            string.Equals(a, NosAi.Runtime.Tactical.CombatReportCommand.Flag, StringComparison.OrdinalIgnoreCase));
        if (combatReportIndex >= 0)
        {
            // The skill id is optional and, unlike --engage's, purely
            // cosmetic: the engage: verdicts do not depend on it
            // (CombatReportCommand.TargetVerdictSkill). It is accepted so an
            // operator can type the same two words they are about to give
            // --engage. A following token starting with '-' is another flag,
            // not this argument.
            string? reportSkill = combatReportIndex + 1 < args.Length
                                  && !args[combatReportIndex + 1].StartsWith('-')
                ? args[combatReportIndex + 1]
                : null;
            return NosAi.Runtime.Tactical.CombatReportCommand.Run(reportSkill);
        }

        // Re-imports the reference catalogue and the broader native file
        // inventory from the installed client, reporting what a client update
        // changed since the last run.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.ClientUpdateCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.ClientUpdateCommand.Run(args);

        // The decision path over real game bytes, offline. WinDivertProbe --world
        // reports what a recording says; this reports what the runtime decides
        // about it, which is the half nothing exercised before.
        int replayFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--decide-replay", StringComparison.OrdinalIgnoreCase));
        if (replayFlag >= 0)
        {
            string? recording = replayFlag + 1 < args.Length ? args[replayFlag + 1] : null;
            if (string.IsNullOrWhiteSpace(recording))
            {
                Console.Error.WriteLine("--decide-replay <file.noscap> requires a recording path.");
                return 2;
            }

            int cycleFlag = Array.FindIndex(args, a =>
                string.Equals(a, "--decide-cycles", StringComparison.OrdinalIgnoreCase));
            int cycles = cycleFlag >= 0 && cycleFlag + 1 < args.Length
                         && int.TryParse(args[cycleFlag + 1], out int parsed) && parsed > 0
                ? parsed
                : 200;

            return await NosAi.Runtime.Observability.DecideReplayCommand.RunAsync(recording, cycles).ConfigureAwait(false);
        }

        // Measure what the wire actually carries instead of guessing it (AP-05).
        // Read-only, offline, no driver: censuses every opcode's field shape, or
        // prints one opcode's raw lines up to --max. It never assigns a meaning
        // to a field and never actuates.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.WireInspectCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.WireInspectCommand.Run(args);

        // The durable action-outcome ledger's read path (AP-09). Read-only:
        // summarises what --scout/--autoplay/--engage/--recover recorded, or
        // prints one context's rows with --context. It never appends, migrates
        // or feeds the planner/ranker/Guard/Safety.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.OutcomeReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.OutcomeReportCommand.Run(args);

        // The prediction-calibration store's read path (AP-09). Read-only:
        // reports what PredictionLedger's resolved calibration looks like, or one
        // context's row with --context. It never saves, migrates or feeds the
        // planner/ranker/Guard/Safety.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.LearningReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.LearningReportCommand.Run(args);

        // The reference skill catalogue next to what the wire observed (AP-05).
        // Read-only: opens the catalogue and, with --recording, reads a capture;
        // it never writes, imports or feeds the planner. Each field is marked
        // confirmed or provisional.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.SkillReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.SkillReportCommand.Run(args);

        // The reference monster catalogue next to what the wire observed. Read-only
        // in the same sense as --skill-report: opens the catalogue and, with
        // --recording, reads a capture; it never writes, imports or feeds the
        // planner. The level is confirmed against st field 3; the HP/MP bonus stays
        // provisional because the wire's st field 9/10 is the total, not the bonus.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Observability.MonsterReportCommand.Flag, StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Observability.MonsterReportCommand.Run(args);

        // Offset discovery for the memory provider (ADR-0014). Read-only, and it
        // answers nothing on its own: an address is identified by narrowing across
        // several changes of the value, which is why the candidate set persists
        // between invocations rather than living inside one run.
        int scanFlag = Array.FindIndex(args, a =>
            string.Equals(a, "--memory-scan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--memory-narrow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--memory-dump", StringComparison.OrdinalIgnoreCase));
        if (scanFlag >= 0)
            return NosAi.LiveIntegration.MemoryScanProbe.Run(args, scanFlag);

        // Diagnostic read of the durable event log (M075-M076). Sola lettura: it
        // reports how complete the audit trail is, gaps included, so a missing
        // event is visible rather than silently absent. An optional path follows.
        int eventLogFlag = Array.FindIndex(args, a => string.Equals(a, "--event-log-report", StringComparison.OrdinalIgnoreCase));
        if (eventLogFlag >= 0)
        {
            string? path = eventLogFlag + 1 < args.Length && !args[eventLogFlag + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[eventLogFlag + 1]
                : null;
            var health = NosAi.Runtime.Gate2.EventLogDiagnostics.Inspect(path);
            Console.WriteLine(NosAi.Runtime.Gate2.EventLogDiagnostics.Describe(health));
            // Exit non-zero when the log is present but incomplete, so a script can
            // notice a lossy audit trail without parsing the text.
            return health.Readable && health.IsComplete ? 0 : 1;
        }

        // Operator immediate halt against a runtime already listening. Disarms
        // then aborts; a new process cannot halt the one that is actually armed.
        if (args.Any(a => string.Equals(a, "--halt", StringComparison.OrdinalIgnoreCase)))
            return await NosAi.Runtime.Operator.HaltCli.RunAsync(
                NosAi.Runtime.Operator.HaltCli.PortFromArgs(args)).ConfigureAwait(false);

        var logger = new ConsoleRuntimeLogger();
        Gate1HostOptions options;
        try
        {
            options = Gate1HostOptionsLoader.Load(ReadEnvironment(), args);
        }
        catch (Exception ex)
        {
            logger.Error("Gate 1 configuration is invalid; refusing to start.", ex);
            return 2;
        }

        await using var host = new Gate1BootstrapHost(options, logger);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await host.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (GuardChannelBindException ex)
        {
            // The Guard channel is the authenticated PC-phone link, so a failed bind
            // must fail closed. It is a configuration problem, not a defect: report
            // the port and the remedy, without a stack trace the operator cannot use.
            logger.Error($"Gate 1 bootstrap failed; the runtime is not serving. reason={ex.Reason}");
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            // A raw unhandled stack trace told the operator nothing actionable and
            // left the runtime dead. Report the failure and exit deliberately.
            logger.Error("Gate 1 bootstrap failed; the runtime is not serving.", ex);
            return 3;
        }

        // AP-01/A4: optional runtime wiring of the existing Gate 1 observation
        // snapshot into the Unified World Model. Independent of the decision
        // loop above -- it only reads the same snapshot and fuses it, it never
        // acts -- so it is its own opt-in flag rather than folded into --decide.
        // Owned here rather than by Gate1BootstrapHost (out of scope for this
        // task); `await using` on a possibly-null value disposes it only when
        // one was actually created, the same as every other optional component
        // in this method.
        //
        // AP-02/A4: when the same flag is on, the fusion loop also gets a real
        // screen-vitals source (host.Capture().Client.ProcessId feeds process
        // discovery -> client window -> DXGI frame -> ScreenVitalReader, all
        // inside ScreenVitalsCapture). No separate flag: the vitals fusion is
        // an enrichment of the same cycle --fuse-world-model already runs, not
        // an independent feature. `visualCapture` is declared before `fusion`
        // so it disposes AFTER fusion on the way out (using declarations
        // unwind in reverse order): the pump must stop calling into it before
        // its DXGI resource is released.
        int? AttachedProcessId()
        {
            NosAi.Runtime.Contracts.ClassifiedValue<int?> processId = host.Capture().Client.ProcessId;
            return processId.HasValue ? processId.Value : null;
        }

        // The target-frame calibration (ADR-0018) is loaded once, when the same
        // --fuse-world-model flag that builds the visual capture is on (so no
        // file I/O runs when it is off). A missing/absent file loads as
        // Uncalibrated -- the state before the operator has aimed the reader --
        // which TargetStateComposer reports honestly as
        // target_roi_not_calibrated until HudProbe's workflow writes one.
        NosAi.Runtime.Perception.TargetRoiCalibration targetCalibration = NosAi.Runtime.Perception.TargetRoiCalibration.Uncalibrated;
        if (options.FuseWorldModel)
        {
            string repo = NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            targetCalibration = NosAi.Runtime.Perception.TargetRoiCalibration.Load(
                Path.Combine(repo, NosAi.Runtime.Perception.TargetRoiCalibration.RelativePath), out _);
        }

        // Same reasoning as targetCalibration immediately above: loaded once,
        // only when --fuse-world-model is on, and a missing/absent file loads
        // as Uncalibrated -- the state before an operator has confirmed a
        // dialog-window crop -- which DialogWindowStateComposer reports
        // honestly as dialog_roi_not_calibrated until an operator calibrates one.
        NosAi.Runtime.Perception.DialogRoiCalibration dialogCalibration = NosAi.Runtime.Perception.DialogRoiCalibration.Uncalibrated;
        if (options.FuseWorldModel)
        {
            string repo = NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            dialogCalibration = NosAi.Runtime.Perception.DialogRoiCalibration.Load(
                Path.Combine(repo, NosAi.Runtime.Perception.DialogRoiCalibration.RelativePath), out _);
        }

        using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = options.FuseWorldModel
            ? new NosAi.Runtime.Perception.ScreenVitalsCapture(AttachedProcessId, targetCalibration: targetCalibration, dialogCalibration: dialogCalibration)
            : null;

        // AP-03/A4: when the same flag is on, the fusion loop also gets a real
        // map reconstruction source (client grid lookup + projection + merge +
        // SQLite persistence, all inside MapReconstructionSource). No separate
        // flag, same reasoning as AP-02/A4's vitals: map reconstruction is an
        // enrichment of the same cycle, not an independent feature.
        // `mapReconstruction` is declared before `fusion` for the same
        // reverse-unwind-order reason as `visualCapture` above: the pump must
        // stop calling into it before its SQLite connection is disposed.
        using NosAi.Runtime.WorldModel.Fusion.MapReconstructionSource? mapReconstruction = options.FuseWorldModel
            ? new NosAi.Runtime.WorldModel.Fusion.MapReconstructionSource(logger: logger)
            : null;

        // mapSource is Func<WorldModelSnapshot, MapModel> (no separate instant
        // parameter -- see WorldModelFusionLoop's own class remarks), while
        // MapReconstructionSource.Resolve takes an explicit nowUtc so it never
        // reads the wall clock itself. The snapshot's own ObservedAtUtc is
        // already exactly this cycle's nowUtc (GameplayObservationProjector
        // stamps it, and neither WorldModelTemporalEnricher.Enrich nor
        // VisualObservationFusion.FuseVitals change it), so reading it off the
        // snapshot supplies Resolve's instant without a second, independent
        // clock read.
        NosAi.Core.WorldModel.MapModel ResolveMap(NosAi.Core.WorldModel.WorldModelSnapshot snapshot) =>
            mapReconstruction!.Resolve(snapshot, snapshot.ObservedAtUtc);

        // Runs once on every real startup, not only when an operator remembers
        // to run --client-updates by hand: notices whether the installed
        // client changed since this machine last recorded it. Silent when
        // nothing changed, so an ordinary startup is not buried under an
        // always-identical report; a clear notice only when it actually
        // finds something. Absence of the NOSAI-SSD volume or of the client
        // itself is not fatal here -- ReferenceInfoCommand/--client-updates
        // already treat both as a named, non-fatal state, and startup must
        // not refuse to serve Gate 1 over a reference-catalogue concern.
        if (GameReferenceLocator.TryFindDedicatedDataDirectory(out string referenceDataDirectory, out _))
        {
            try
            {
                GameReferenceLocation referenceLocation = GameReferenceLocator.LocateIn(referenceDataDirectory);
                using GameReferenceDatabase referenceDatabase = GameReferenceDatabase.Open(referenceLocation.Path!);
                ClientUpdateReport clientUpdate = ClientUpdateCommand.RunDetailed(
                    referenceDatabase, ReferenceImporter.DefaultDataDirectory);
                if (clientUpdate.AnyChange)
                {
                    Console.WriteLine("=== Aggiornamento client rilevato ===");
                    Console.Write(clientUpdate.Text);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or Microsoft.Data.Sqlite.SqliteException)
            {
                logger.Error("Controllo aggiornamenti client fallito all'avvio; il resto dell'avvio prosegue.", ex);
            }
        }

        // AP-05: with the same flag on, the fusion loop also gets the reference
        // catalogue, so the entities the wire reports become real Mob/Npc
        // records instead of an empty list.
        //
        // Opened *after* the client-update check above, deliberately: that
        // check writes to this very file (ReferenceImporter.ImportAll), and
        // holding a second connection open across it is how a reader and a
        // writer on one SQLite file start refusing each other. Nothing between
        // here and there needs the loop running.
        //
        // A missing or unreadable catalogue is not fatal and not a special
        // case: TryOpen names the reason, the classifier answers
        // CatalogueNotLoaded for every vnum, and both entity lists stay empty
        // exactly as they were before this wiring existed -- the same
        // non-fatal treatment the client-update check gives a missing
        // NOSAI-SSD volume.
        GameReferenceDatabase? fusionCatalogue = null;
        if (options.FuseWorldModel && !GameReferenceLocator.TryOpen(out fusionCatalogue, out string? fusionCatalogueReason))
            logger.Warning($"World Model: catalogo di riferimento non disponibile ({fusionCatalogueReason}); mob e NPC resteranno vuoti.");

        // Held only to dispose the handle at the end of this scope: TryOpen's
        // `out` parameter cannot itself be a `using` declaration, and the
        // classifier deliberately does not own the connection. Declared before
        // `fusion` so the pump stops asking before the connection is disposed
        // -- `using` declarations unwind in reverse.
        using GameReferenceDatabase? fusionCatalogueLifetime = fusionCatalogue;
        var entityClassifier = new NosAi.Runtime.Autonomy.CatalogueClassifier(fusionCatalogue);

        await using NosAi.Runtime.WorldModel.Fusion.WorldModelFusionLoop? fusion = options.FuseWorldModel
            ? new NosAi.Runtime.WorldModel.Fusion.WorldModelFusionLoop(
                host.Capture,
                logger,
                TimeSpan.FromMilliseconds(options.FuseWorldModelIntervalMs),
                visualSource: visualCapture!.Capture,
                mapSource: ResolveMap,
                classifyVnum: entityClassifier.Classify)
            : null;
        fusion?.Start(cts.Token);

        var snapshot = host.Capture();
        Console.WriteLine("NosAi Runtime 1.0 Beta — Gate 1");
        Console.WriteLine($"Health: {host.Health}");
        Console.WriteLine($"Guard port: {host.GuardPort}");
        if (host.DashboardPort is int dashboard)
        {
            Console.WriteLine($"Runtime operator API: http://127.0.0.1:{dashboard}/");
            // The UI defaults to the runtime's default port, so only a non-default
            // port needs the operator to export the override.
            Console.WriteLine(dashboard == Gate1HostOptions.DefaultDashboardPort
                ? "Operator UI: python -m nosai.dashboard.server   (then open http://127.0.0.1:8765/)"
                : $"Operator UI: set NOSAI_RUNTIME_URL=http://127.0.0.1:{dashboard} then run: python -m nosai.dashboard.server");
        }
        else
        {
            Console.WriteLine($"Runtime operator API: UNAVAILABLE ({host.DashboardFailureReason})");
        }
        Console.WriteLine($"Client: {snapshot.Client.Status} ({snapshot.Client.Availability.Source.ToWire()})");
        Console.WriteLine($"Client process: {FormatClassified(snapshot.Client.ProcessName)} pid={FormatClassified(snapshot.Client.ProcessId)}");
        Console.WriteLine($"Client window: {FormatClassified(snapshot.Client.WindowTitle)} {FormatClassified(snapshot.Client.WindowHandle)}");
        Console.WriteLine($"Gameplay baseline: {FormatClassified(snapshot.Client.GameplayBaseline)}");
        Console.WriteLine($"Hardware CPU: {FormatClassified(snapshot.Hardware.Cpu)}");
        Console.WriteLine("Live input and packet injection remain disabled. Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    /// <summary>A window size as decimal or as 0x-prefixed hex, positive only.</summary>
    private static bool TryParseWindow(string text, out int bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes);

        return parsed && bytes > 0;
    }

    private static string FormatClassified<T>(ClassifiedValue<T> field)
        => !field.HasValue
            ? $"UNKNOWN ({field.FailureReason})"
            : $"{field.Value} [{field.Source.ToWire()}]";

    /// <summary>Probe flags, which live outside the suite table.</summary>
    private static readonly HashSet<string> KnownProbeFlags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--dxgi-probe", "--input-probe", "--memory-scan", "--memory-narrow", "--memory-dump",
            "--hud-probe", "--window-probe", "--target-chain", "--input-guards", "--input-authority", "--step", "--walk", "--dry-run", "--keybinds-check", "--halt", "--event-log-report", "--decide-replay", "--player-probe", "--entity-names", "--player-vitals", "--skill-cooldowns", "--sweep-cooldown", "--record-wire", "--live-decode", "--calibrate-vitals", "--anchor-hunt", "--world-replay", "--reference-info", "--client-updates",
            "--screen-sample", "--screen-calibrate", "--screen-samples-clear", "--screen-watch",
            "--screen-autocalibrate", "--arm-input", "--scout", "--engage", "--collect", "--recover", "--autoplay", "--cycles", "--recover-slot", "--route", "--calibrate-inventory-panel", "--loadout-report", "--combat-report", "--certification-report", "--wire-inspect", "--outcome-report", "--learning-report", "--skill-report", "--monster-report"
        };

    private static int RunDxgiProbe()
    {
        Console.WriteLine("=== DXGI Desktop Duplication probe ===");
        if (!NosAi.Runtime.Perception.DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
        {
            Console.WriteLine($"[UNAVAILABLE] {unavailable!.Reason} (hr=0x{unavailable.HResult:X8})");
            Console.WriteLine("No live capture in this session. Perception stays UNKNOWN; no pixels are invented.");
            return 1;
        }

        using (capture)
        {
            Console.WriteLine($"[OK] duplication open: {capture!.Width}x{capture.Height}");
            for (int attempt = 1; attempt <= 40; attempt++)
            {
                if (!capture.TryAcquire(out var frame))
                {
                    // A still desktop legitimately produces no new frame.
                    Thread.Sleep(50);
                    continue;
                }
                ReadOnlySpan<byte> pixels = frame.Bgra.Span;
                var sampled = new HashSet<int>();
                byte min = 255, max = 0;
                for (int i = 0; i + 3 < pixels.Length; i += 64)
                {
                    if (sampled.Count < 4096)
                        sampled.Add(pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16));
                    if (pixels[i] < min) min = pixels[i];
                    if (pixels[i] > max) max = pixels[i];
                }

                Console.WriteLine($"[frame {attempt}] {frame.Width}x{frame.Height} source={frame.Source.ToWire()} " +
                                  $"bytes={frame.Bgra.Length} distinctColours={sampled.Count} blueMin={min} blueMax={max}");
                if (sampled.Count > 1)
                {
                    Console.WriteLine("=== DXGI probe passed: real desktop pixels captured. ===");
                    return 0;
                }
                // A uniform frame is normal right after DuplicateOutput; keep asking
                // (and keep writing to the console, which itself changes the screen).
                Thread.Sleep(60);
            }
            Console.WriteLine("[TIMEOUT] no frame within the attempt budget (a fully static desktop can do this).");
            return 1;
        }
    }

    private static Dictionary<string, string?> ReadEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                result[key] = entry.Value as string;
        }
        return result;
    }
}
