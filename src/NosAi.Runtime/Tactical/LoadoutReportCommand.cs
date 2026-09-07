using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Loadout;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The operator command that reports what <see cref="LoadoutPlanner"/>
/// would propose right now and whether every proposal passes its hard
/// constraints (AP-07, Q-094): <c>--loadout-report</c>. It captures one
/// live <see cref="GameplayObservation"/> through the same packet-based
/// gateway <c>CollectCommand</c> uses, projects it to a
/// <see cref="WorldModelSnapshot"/> whose <c>Player</c> carries the real
/// wire-confirmed inventory (<c>ivn</c>), resolves each inventory stack's
/// <see cref="EquipmentSlot"/> from the real on-disk item catalogue
/// (<see cref="ItemReferenceDecoder"/> over
/// <see cref="GameReferenceDatabase.Lookup"/>), and prints one line per
/// candidate with its hard-constraint verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only by construction.</b> This command equips, unequips and
/// upgrades nothing: no key press, no mouse event, no input arming, no
/// <see cref="NosAi.Runtime.Orchestration.RuntimeComposition"/> at all --
/// nothing it does can actuate.
/// <c>--equip</c>/<c>--unequip</c> stay out of scope (the second half of
/// T-12, confirming which <c>InventoryKind</c> means "worn" on a real
/// client, is still open). Same read-only spirit as <c>--route</c>
/// (<see cref="Navigation.RouteProbe"/>) and <c>--reference-info</c>.
/// </para>
/// <para>
/// <b>What the live half cannot see, honestly.</b> <see cref="Build"/> is
/// pure and judges whatever <see cref="Player"/> it is given, including a
/// populated <c>Equipment</c> list. The live composition's snapshot,
/// however, always carries an empty <c>Player.Equipment</c>:
/// <see cref="GameplayObservationProjector"/> populates the inventory from
/// the wire's <c>ivn</c> slots and keeps the equipment empty by design
/// (its own remarks record why -- no observation channel yet confirms
/// which slot a worn item occupies). So on a live run the Unequip and
/// Upgrade sections report what the World Model currently carries --
/// nothing -- not what the character is wearing. That observation gap is
/// AP-07 work; this command reports the gap faithfully instead of
/// pretending to see equipment.
/// </para>
/// </remarks>
public static class LoadoutReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--loadout-report";

    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "loadout_report_requires_windows";

    /// <summary>Reported when the live gameplay gateway cannot be opened.</summary>
    public const string GameplayUnavailableReason = "loadout_report_gameplay_provider_unavailable";

    /// <summary>
    /// The three candidate lists one report carries, each already judged by
    /// <see cref="LoadoutPlanner.CheckHardConstraints"/> against the same
    /// <see cref="Player"/> the candidates were generated from.
    /// </summary>
    public readonly record struct LoadoutReport(
        IReadOnlyList<LoadoutConstraintCheck> EquipChecks,
        IReadOnlyList<LoadoutConstraintCheck> UnequipChecks,
        IReadOnlyList<LoadoutConstraintCheck> UpgradeChecks);

    /// <summary>
    /// Generates all three candidate kinds via <see cref="LoadoutPlanner"/>'s
    /// three generators and runs <see cref="LoadoutPlanner.CheckHardConstraints"/>
    /// on every one of them against <paramref name="player"/>. Pure: no
    /// <c>Console</c>, no clock read, no field access outside
    /// <paramref name="player"/>/<paramref name="resolveSlot"/>. It takes its
    /// inputs as parameters the way <c>AutoplayCommand.ExecuteOneCycle</c>
    /// does, but goes further than that method, which actuates: this one
    /// returns a report and touches nothing. So the exact hand-built
    /// <see cref="Player"/>/fake-<paramref name="resolveSlot"/> pattern
    /// <c>LoadoutPlannerTests</c> uses drives the tests here too.
    /// </summary>
    /// <param name="player">The player whose inventory/equipment is judged.</param>
    /// <param name="resolveSlot">
    /// Maps an inventory item to the <see cref="EquipmentSlot"/> it would
    /// occupy, or null when nothing real maps it -- the same lookup
    /// <see cref="LoadoutPlanner.GenerateEquipCandidates"/> already takes
    /// (see its own remarks: an item with no real catalog answer produces no
    /// candidate, never a guessed one). <see cref="BuildResolveSlot"/> is the
    /// real catalog-backed implementation; tests pass a fake.
    /// </param>
    public static LoadoutReport Build(Player player, Func<ItemId, EquipmentSlot?> resolveSlot)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(resolveSlot);

        IReadOnlyList<LoadoutActionCandidate> equips = LoadoutPlanner.GenerateEquipCandidates(player, resolveSlot);
        IReadOnlyList<LoadoutActionCandidate> unequips = LoadoutPlanner.GenerateUnequipCandidates(player);
        IReadOnlyList<LoadoutActionCandidate> upgrades = LoadoutPlanner.GenerateUpgradeCandidates(player);

        return new LoadoutReport(
            Judge(equips, player),
            Judge(unequips, player),
            Judge(upgrades, player));
    }

    /// <summary>
    /// The real catalog-backed slot lookup: for a given <see cref="ItemId"/>,
    /// parse its value as the catalogue's numeric vnum, look the item table
    /// up, and decode the record's declared slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item table's <c>kind</c> string is exactly <c>"item"</c> -- the
    /// second row of <see cref="ReferenceImporter.Tables"/> registers
    /// <c>("item", "NSgtdData.NOS", "Item.dat", ...)</c>, and
    /// <see cref="GameReferenceDatabase.Lookup"/> keys on that kind.
    /// </para>
    /// <para>
    /// The vnum parse mirrors how <see cref="GameplayObservationProjector"/>
    /// builds an <see cref="ItemId"/> from a wire vnum in the first place
    /// (<c>slot.Vnum.ToString(CultureInfo.InvariantCulture)</c>), so the
    /// round trip is exact for every id that channel can produce.
    /// </para>
    /// <para>
    /// Failure propagates as null all the way through, never a guess: a vnum
    /// that does not parse, a catalogue that does not know the vnum
    /// (<see cref="GameReferenceDatabase.Lookup"/> returns null), and a
    /// record whose slot code the enum does not define all answer
    /// "no real slot known", which is what
    /// <see cref="LoadoutPlanner.GenerateEquipCandidates"/> expects to mean
    /// "produce no candidate".
    /// </para>
    /// </remarks>
    public static Func<ItemId, EquipmentSlot?> BuildResolveSlot(GameReferenceDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return item => ResolveSlot(database, item);
    }

    private static EquipmentSlot? ResolveSlot(GameReferenceDatabase database, ItemId item)
    {
        if (!int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vnum))
            return null;

        IReadOnlyList<NosField>? fields = database.Lookup("item", vnum);
        if (fields is null)
            return null;

        return ItemReferenceDecoder.Decode(new NosRecord(vnum, fields))?.Slot;
    }

    /// <summary>
    /// Console entry for <c>--loadout-report</c>. No arguments.
    /// </summary>
    public static int Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows();
    }

    /// <summary>
    /// The live composition, mirroring <see cref="Navigation.CollectCommand"/>'s
    /// own <c>RunWindows</c>/<c>LiveObservationScope</c> shape (same window lookup,
    /// <see cref="ClientMemorySession.TryAttach"/>, packet-based
    /// <see cref="LiveObservationScope.TryOpen"/>): a thin, untested-by-design shell --
    /// exactly like <c>CollectCommand.RunWindows</c>, which has no unit test
    /// either; only the pure parts below are tested. A read-only report needs
    /// none of the gated input machinery the walking commands compose, so
    /// none is built here.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows()
    {
        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return WalkCommand.ExitAbandoned;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return WalkCommand.ExitAbandoned;
        }

        using (session)
        {
            LiveObservationScope? live = LiveObservationScope.TryOpen(processId, out string? gatewayFailure);
            if (live is null)
            {
                Console.WriteLine($"[REFUSED] {GameplayUnavailableReason}:{gatewayFailure}");
                return WalkCommand.ExitAbandoned;
            }

            using (live)
            {
                return RunWindowsCore(processId, live.Gateway);
            }
        }
    }

    /// <summary>The one-capture report, once every live resource is open.</summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindowsCore(int processId, LiveObservationGateway gateway)
    {
        GameplayObservation observation = gateway.Capture().Gameplay;
        if (!observation.Inventory.HasValue)
        {
            // Empty and not-observed must not read the same. The projector
            // turns an unobserved inventory into an empty Player.Inventory,
            // and zero Equip candidates from that would look like "nothing
            // equippable" when the truth is "nothing was read yet".
            Console.WriteLine($"[WARN] loadout-report: inventory not observed ({observation.Inventory.FailureReason ?? "inventory_not_observed"}) -- Equip candidates unknown, not confirmed empty");
        }

        // The World Model player id is the session's character id; on a live
        // client the wire's own id is ground truth, but this command does not
        // hold one (the capture feed is polled for one gameplay observation
        // only; no cond packet is read for the character id). A stable
        // per-process id is used so the projection has a valid id to key on
        // without claiming to know the character id -- the same honest choice
        // CollectCommand.RunWindowsCore documents for --collect.
        var playerId = new EntityId(string.Create(CultureInfo.InvariantCulture, $"player-{processId}"));

        // version: 0 -- the projection's version parameter serves the World
        // Model's own replay/versioning concern, which this one-shot command
        // does not participate in; a fixed 0 is honest and documented as such
        // (the same reasoning CollectCommand.ExecuteOneRound documents for its
        // own version: 0 projections).
        DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, playerId, version: 0, nowUtc);

        // The catalogue on the NOSAI-SSD volume, opened without creating one.
        // Absence is a reported condition, not a crash (the same treatment
        // ReferenceInfoCommand gives a missing file): Equip candidates need
        // the catalogue to resolve slots, Unequip/Upgrade need no catalogue at
        // all, so a missing catalogue degrades only the Equip section and the
        // report still prints in full.
        GameReferenceDatabase? catalog = null;
        if (!GameReferenceLocator.TryOpen(out catalog, out string? catalogFailure))
        {
            Console.WriteLine($"[WARN] loadout-report: catalog unavailable ({catalogFailure}) -- Equip candidates cannot be resolved, Unequip/Upgrade still reported");
        }

        using (catalog)
        {
            Func<ItemId, EquipmentSlot?> resolveSlot = catalog is not null
                ? BuildResolveSlot(catalog)
                : static _ => null;

            LoadoutReport report = Build(snapshot.Player, resolveSlot);
            Print(report);
        }

        // A report with zero candidates in every list is a valid, honest
        // result, not a failure.
        return 0;
    }

    private static IReadOnlyList<LoadoutConstraintCheck> Judge(
        IReadOnlyList<LoadoutActionCandidate> candidates, Player player)
    {
        var checks = new List<LoadoutConstraintCheck>(candidates.Count);
        foreach (LoadoutActionCandidate candidate in candidates)
            checks.Add(LoadoutPlanner.CheckHardConstraints(candidate, player));
        return checks;
    }

    /// <summary>
    /// One line per check, plus a one-line count summary at the end. The
    /// summary counts candidates the way
    /// <see cref="LiveIntegration.Capture.LiveWireMonitor"/> counts frames,
    /// but writes them value-first (<c>3 equip, 0 unequip, ...</c>) rather
    /// than in that monitor's own label-first form.
    /// </summary>
    /// <param name="report">
    /// A report produced by <see cref="Build"/>. A default-constructed
    /// <see cref="LoadoutReport"/> has null lists and is not a valid argument;
    /// <see cref="Build"/> never returns one.
    /// </param>
    public static void Print(LoadoutReport report)
    {
        ArgumentNullException.ThrowIfNull(report.EquipChecks);
        ArgumentNullException.ThrowIfNull(report.UnequipChecks);
        ArgumentNullException.ThrowIfNull(report.UpgradeChecks);

        foreach (LoadoutConstraintCheck check in report.EquipChecks)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"equip candidate: item={check.Candidate.Item!.Value.Value} slot={check.Candidate.Slot!.Value} allowed={check.IsAllowed} violations={Violations(check)}"));
        }

        foreach (LoadoutConstraintCheck check in report.UnequipChecks)
        {
            // An Unequip candidate carries only its slot (LoadoutActionCandidate's
            // constructor refuses an Item for this kind); there is no item to print.
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"unequip candidate: slot={check.Candidate.Slot!.Value} allowed={check.IsAllowed} violations={Violations(check)}"));
        }

        foreach (LoadoutConstraintCheck check in report.UpgradeChecks)
        {
            // An Upgrade candidate carries only its item; there is no slot to print.
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"upgrade candidate: item={check.Candidate.Item!.Value.Value} allowed={check.IsAllowed} violations={Violations(check)}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{report.EquipChecks.Count} equip, {report.UnequipChecks.Count} unequip, {report.UpgradeChecks.Count} upgrade candidates"));
    }

    private static string Violations(LoadoutConstraintCheck check) =>
        check.ViolatedConstraints.Count == 0
            ? "none"
            : string.Join(", ", check.ViolatedConstraints);

    /// <summary>
    /// Finds the running client's window and process id, or names why it could
    /// not.
    /// </summary>
    /// <remarks>
    /// The live capture this command then opens is
    /// <see cref="LiveObservationScope"/>, a shared type in
    /// <c>NosAi.LiveIntegration</c> -- not, as an earlier version of this
    /// comment said, a private copy mirroring
    /// <see cref="Navigation.CollectCommand"/>'s own. Neither command has one
    /// any more; both call the shared type, which is what the extraction was
    /// for.
    /// </remarks>
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
