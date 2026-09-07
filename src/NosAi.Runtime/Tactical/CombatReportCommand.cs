using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using CatalogueClassifier = NosAi.Runtime.Autonomy.CatalogueClassifier;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The operator command that reports what <see cref="CombatPlanner"/> would
/// propose right now, and whether each proposal passes its hard constraints:
/// <c>--combat-report</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="CombatPlanner.GenerateCandidates"/> had
/// no production caller at all -- it was reachable only from its own unit
/// tests, so nothing it decided had ever been run against a real client. This
/// command is that caller, in the same read-only shape
/// <see cref="LoadoutReportCommand"/> gave <c>LoadoutPlanner</c>: generate,
/// judge, print, actuate nothing.
/// </para>
/// <para>
/// <b>Read-only by construction.</b> No key press, no mouse event, no input
/// arming, no <see cref="NosAi.Runtime.Orchestration.RuntimeComposition"/> at
/// all -- nothing it does can actuate. Executing a combat act and verifying it
/// is <see cref="EngageCommand"/>'s job and stays there.
/// </para>
/// <para>
/// <b>It exercises the whole AP-05 chain end to end.</b> A live packet capture
/// supplies the entities; the reference catalogue decides which vnums are
/// monsters; <see cref="GameplayObservationProjector"/> turns those into real
/// <see cref="Mob"/> records with a fraction-only health and an
/// <c>IsHostile</c> established only by the wire's record of a hit; client
/// memory supplies the player's own position and vitals; and
/// <see cref="CombatPlanner"/> judges the result. Every link in that chain is
/// currently <c>Present</c> rather than <c>Verified</c>, and this command is
/// the one place an operator can watch all of them at once on a real client.
/// </para>
/// <para>
/// <b>Two honest limits, reported rather than hidden.</b> No observation
/// channel reads the character's skill list, so <see cref="Player.Skills"/> is
/// empty here and only <see cref="CombatActionKind.BasicAttack"/> candidates
/// can ever be generated -- a <see cref="CombatActionKind.UseSkill"/> candidate
/// would need a skill catalogue this command does not have. And a candidate is
/// only generated against a mob already known hostile, which today means one
/// the wire recorded hitting this character: standing next to a monster that
/// has not attacked yields no candidate, which is a refusal to guess, not a
/// bug. <see cref="CombatMobCensus"/> exists so those two cases read
/// differently from "no monster in sight".
/// </para>
/// </remarks>
public static class CombatReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--combat-report";

    /// <summary>Reported off Windows, where there is no client process to read.</summary>
    public const string NotWindowsReason = "combat_report_requires_windows";

    /// <summary>Reported when the live entity feed cannot be opened.</summary>
    public const string EntityFeedUnavailableReason = "combat_report_entity_feed_unavailable";

    /// <summary>
    /// What the mob list looked like, so a report with no candidates says
    /// which of the several possible reasons it had.
    /// </summary>
    /// <param name="Observed">Mobs the projection produced at all: entities the catalogue established as monsters.</param>
    /// <param name="KnownHostile">Of those, how many carry a hostility that was actually established.</param>
    /// <param name="KnownAlive">Of those, how many carry an aliveness that was actually established.</param>
    /// <param name="Positioned">Of those, how many carry a known position.</param>
    public readonly record struct CombatMobCensus(int Observed, int KnownHostile, int KnownAlive, int Positioned);

    /// <summary>
    /// One report: every candidate <see cref="CombatPlanner"/> generated,
    /// already judged by <see cref="CombatPlanner.CheckHardConstraints"/>
    /// against the same <see cref="Player"/> and mobs it was generated from,
    /// plus the census that explains an empty list.
    /// </summary>
    public readonly record struct CombatReport(
        IReadOnlyList<CombatConstraintCheck> Checks,
        CombatMobCensus Census);

    /// <summary>
    /// Generates candidates via <see cref="CombatPlanner.GenerateCandidates"/>
    /// and runs <see cref="CombatPlanner.CheckHardConstraints"/> on every one
    /// of them against the same inputs.
    /// </summary>
    /// <remarks>
    /// Pure: no <c>Console</c>, no clock read, no field access outside its two
    /// parameters, so the whole decision is testable from a hand-built
    /// <see cref="Player"/> and mob list with no client attached -- the same
    /// discipline <see cref="LoadoutReportCommand.Build"/> follows.
    /// </remarks>
    /// <param name="player">The player the candidates are generated for and judged against.</param>
    /// <param name="mobs">This moment's mobs, as the canonical World Model holds them.</param>
    public static CombatReport Build(Player player, EquatableArray<Mob> mobs)
    {
        ArgumentNullException.ThrowIfNull(player);

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        var checks = new List<CombatConstraintCheck>(candidates.Count);
        foreach (CombatActionCandidate candidate in candidates)
            checks.Add(CombatPlanner.CheckHardConstraints(candidate, player, mobs));

        return new CombatReport(checks, Census(mobs));
    }

    private static CombatMobCensus Census(EquatableArray<Mob> mobs)
    {
        int hostile = 0;
        int alive = 0;
        int positioned = 0;

        foreach (Mob mob in mobs)
        {
            if (mob.IsHostile is { HasValue: true, Value: true }) hostile++;
            if (mob.IsAlive is { HasValue: true, Value: true }) alive++;
            if (mob.Position.HasValue) positioned++;
        }

        return new CombatMobCensus(mobs.Count, hostile, alive, positioned);
    }

    /// <summary>One line per candidate, preceded by the census that explains an empty list.</summary>
    /// <param name="report">A report produced by <see cref="Build"/>.</param>
    public static void Print(CombatReport report)
    {
        ArgumentNullException.ThrowIfNull(report.Checks);

        CombatMobCensus census = report.Census;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"mobs: {census.Observed} observed, {census.KnownHostile} known hostile, {census.KnownAlive} known alive, {census.Positioned} positioned"));

        foreach (CombatConstraintCheck check in report.Checks)
        {
            string skill = check.Candidate.Skill is { } id ? $" skill={id.Value}" : string.Empty;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"candidate: {check.Candidate.Kind} target={check.Candidate.Target!.Value.Value}{skill} allowed={check.IsAllowed} violations={Violations(check)}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{report.Checks.Count} candidates, {AllowedCount(report.Checks)} allowed"));

        if (report.Checks.Count == 0)
            Console.WriteLine($"[WARN] {ExplainEmpty(census)}");
    }

    /// <summary>
    /// Why a report carries no candidate at all. Each case is a different
    /// fact about the world and must not read as the others.
    /// </summary>
    internal static string ExplainEmpty(CombatMobCensus census) => census switch
    {
        { Observed: 0 } =>
            "no_monster_observed -- nessuna entita' e' stata stabilita come mostro dal catalogo (feed assente, o solo vnum sconosciuti)",
        { KnownHostile: 0 } =>
            "no_mob_known_hostile -- mostri visti, ma nessuno ha ancora colpito il personaggio: l'ostilita' non e' stabilita, non e' smentita",
        { KnownAlive: 0 } =>
            "no_mob_known_alive -- nessun pacchetto ha ancora dichiarato la vita di questi mostri",
        { Positioned: 0 } =>
            "no_mob_positioned -- nessun mostro ha una posizione nota",
        _ =>
            "no_candidate_in_range -- mostri validi, ma nessuno entro la portata dell'attacco base",
    };

    private static int AllowedCount(IReadOnlyList<CombatConstraintCheck> checks)
    {
        int allowed = 0;
        foreach (CombatConstraintCheck check in checks)
        {
            if (check.IsAllowed)
                allowed++;
        }

        return allowed;
    }

    private static string Violations(CombatConstraintCheck check) =>
        check.ViolatedConstraints.Count == 0 ? "none" : string.Join('|', check.ViolatedConstraints);

    /// <summary>Console entry for <c>--combat-report</c>. No arguments.</summary>
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
    /// The live composition: client window, memory session for the player's own
    /// position and vitals, packet capture for the entities around them, and
    /// the reference catalogue that decides which of those entities are
    /// monsters. A thin, untested-by-design shell, exactly like
    /// <c>LoadoutReportCommand.RunWindows</c>; only <see cref="Build"/> and
    /// <see cref="ExplainEmpty"/> below it are tested.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows()
    {
        if (!TryFindWindow(out int processId, out string? windowFailure))
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
            ClientMemorySession attached = session!;
            if (!attached.TryReadPlayer(out PlayerObjectReading reading, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return WalkCommand.ExitAbandoned;
            }

            // The entity feed is the one resource this command cannot report
            // without: with no entities there are no mobs, and with no mobs the
            // report would be an empty page that says nothing about the client.
            // Unlike --scout/--autoplay, which carry on risk-blind, refusing
            // here is the honest answer to "what would the combat planner
            // propose right now": nothing, because nothing was observed.
            using LiveObservationScope? feed = LiveObservationScope.TryOpen(processId, out string? feedFailure);
            if (feed is null)
            {
                Console.WriteLine($"[REFUSED] {EntityFeedUnavailableReason}:{feedFailure}");
                return WalkCommand.ExitAbandoned;
            }

            GameReferenceDatabase? catalogue = null;
            if (!GameReferenceLocator.TryOpen(out catalogue, out string? catalogueFailure))
                Console.WriteLine($"[WARN] entity_catalogue_unavailable:{catalogueFailure} -- nessun vnum verra' stabilito come mostro");

            using (catalogue)
            {
                DateTime now = DateTime.UtcNow;
                var playerId = new EntityId(string.Create(CultureInfo.InvariantCulture, $"player-{processId}"));
                GameplayObservation observation = feed.Gateway.Capture().Gameplay;

                EquatableArray<Mob> mobs = GameplayObservationProjector
                    .Project(observation, playerId, version: 0, now, new CatalogueClassifier(catalogue).Classify)
                    .Mobs;

                Print(Build(LivePlayer(playerId, attached, reading, now), mobs));
            }
        }

        return 0;
    }

    /// <summary>
    /// The player as this command can actually read them: a real position from
    /// the client's own memory, real HP bounds when the vitals read succeeds,
    /// and everything else explicitly Unknown or empty.
    /// </summary>
    /// <remarks>
    /// <see cref="Player.Skills"/> stays empty because nothing here reads the
    /// character's skill list, which is why only
    /// <see cref="CombatActionKind.BasicAttack"/> candidates can be generated
    /// -- see this class's own remarks. Filling it with a guess to make the
    /// report look richer would be exactly the fabrication
    /// <see cref="CombatPlanner"/> refuses at the other end.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static Player LivePlayer(
        EntityId playerId,
        ClientMemorySession attached,
        PlayerObjectReading reading,
        DateTime nowUtc)
    {
        Resource health = attached.TryReadPlayerVitals(out PlayerVitalsReading vitals, out string? vitalsFailure)
            ? new Resource(
                ResourceKind.Health,
                WorldFact<double>.Live(vitals.Hp, confidence: 1d, nowUtc),
                WorldFact<double>.Live(vitals.MaxHp, confidence: 1d, nowUtc))
            : new Resource(
                ResourceKind.Health,
                WorldFact<double>.Unknown(vitalsFailure ?? "vitals_unreadable", nowUtc),
                WorldFact<double>.Unknown(vitalsFailure ?? "vitals_unreadable", nowUtc));

        return new Player(
            playerId,
            WorldFact<WorldPosition>.Live(new WorldPosition(reading.X, reading.Y), confidence: 1d, nowUtc),
            WorldFact<float>.Unknown("orientation_not_read", nowUtc),
            WorldFact<bool>.Unknown("alive_not_read", nowUtc),
            WorldFact<MapId>.Unknown("map_not_read", nowUtc),
            new CombatantStatus(
                EquatableArray<Resource>.From(new[] { health }),
                EquatableArray<StatusEffect>.Empty),
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            EquatableArray<InventoryItem>.Empty,
            EquatableArray<EquipmentItem>.Empty);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryFindWindow(out int processId, out string? failureReason)
    {
        processId = 0;
        foreach (string name in RealClientConnector.DefaultProcessNames)
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (ClientWindowLocator.TryFind(process.Id, out string? why) is not null)
                    {
                        processId = process.Id;
                        failureReason = null;
                        return true;
                    }

                    failureReason = why;
                }
            }
        }

        failureReason = $"{InputGuardsProbe.WindowNotLocatedReason}:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }
}
