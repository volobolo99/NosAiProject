using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The operator command that reports what <see cref="CombatPlanner"/> would
/// propose right now, and -- separately -- what <c>--engage</c> would answer
/// for each mob in sight: <c>--combat-report [skillId]</c>.
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
/// <b>Why the report has two halves.</b> No observation channel reads the
/// character's skill list, so <see cref="Player.Skills"/> is empty here and
/// <see cref="CombatPlanner.GenerateCandidates"/> can only ever produce
/// <see cref="CombatActionKind.BasicAttack"/> candidates, judged at
/// <see cref="CombatPlanner.DefaultBasicAttackRange"/>. <c>--engage</c>
/// judges a <see cref="CombatActionKind.UseSkill"/> candidate, at
/// <see cref="CombatPlanner.DefaultSkillRange"/>, which is three times
/// wider. Printing only the planner's candidates would therefore say
/// nothing at all about a mob between those two radii -- and say it in the
/// dangerous direction, showing an empty report for a mob <c>--engage</c>
/// would attack. So the report also carries one verdict per observed mob
/// from the same call <see cref="EngageCommand"/> makes.
/// </para>
/// <para>
/// <b>A refusal to guess is not a bug.</b> A candidate is only generated
/// against a mob already known hostile, which today means one the wire
/// recorded hitting this character: standing next to a monster that has
/// not attacked yields no candidate. <see cref="CombatMobCensus"/> exists
/// so that case reads differently from "no monster in sight".
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
    /// The skill id the <c>engage:</c> verdicts are computed with when the
    /// operator names none.
    /// </summary>
    /// <remarks>
    /// <see cref="CombatPlanner.CheckTargetConstraints"/> reads
    /// <see cref="CombatActionCandidate.Kind"/> (to pick the range) and
    /// <see cref="CombatActionCandidate.Target"/>, and never reads
    /// <see cref="CombatActionCandidate.Skill"/> -- the skill half of the hard
    /// constraints lives in <see cref="CombatPlanner.CheckHardConstraints"/>,
    /// which this command does not call for these lines. The verdict is
    /// therefore identical for every skill id, which is why one can be
    /// supplied at all rather than demanded from the operator; a test pins
    /// that independence so the day it stops holding is a red test and not a
    /// silently wrong report. Naming a real skill on the command line changes
    /// nothing today and is accepted only so the invocation can mirror
    /// <c>--engage</c>'s exactly.
    /// </remarks>
    public const string TargetVerdictSkill = "target-verdict-any-skill";

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
    /// One report, in two halves that answer two different questions.
    /// </summary>
    /// <param name="Checks">
    /// What the planner proposes: every candidate
    /// <see cref="CombatPlanner.GenerateCandidates"/> produced, judged by
    /// <see cref="CombatPlanner.CheckHardConstraints"/> against the same
    /// inputs it was generated from.
    /// </param>
    /// <param name="EngageVerdicts">
    /// What <c>--engage</c> would answer: one verdict per <b>observed</b> mob,
    /// from the same <see cref="CombatPlanner.CheckTargetConstraints"/> call
    /// <see cref="EngageCommand"/> makes before it presses anything.
    /// </param>
    /// <param name="Census">The mob population both halves were computed from; explains an empty <paramref name="Checks"/>.</param>
    public readonly record struct CombatReport(
        IReadOnlyList<CombatConstraintCheck> Checks,
        IReadOnlyList<CombatConstraintCheck> EngageVerdicts,
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
    /// <param name="skill">The skill <c>--engage</c> would be given. Only the <see cref="CombatActionKind"/> it implies is used -- see <see cref="TargetVerdictSkill"/>.</param>
    public static CombatReport Build(Player player, EquatableArray<Mob> mobs, SkillId? skill = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        var checks = new List<CombatConstraintCheck>(candidates.Count);
        foreach (CombatActionCandidate candidate in candidates)
            checks.Add(CombatPlanner.CheckHardConstraints(candidate, player, mobs));

        SkillId judged = skill ?? new SkillId(TargetVerdictSkill);
        var verdicts = new List<CombatConstraintCheck>(mobs.Count);
        foreach (Mob mob in mobs)
        {
            var asEngageWould = new CombatActionCandidate(
                CombatActionKind.UseSkill, target: mob.Id, skill: judged);
            verdicts.Add(CombatPlanner.CheckTargetConstraints(asEngageWould, player, mobs));
        }

        return new CombatReport(checks, verdicts, Census(mobs));
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

    /// <summary>
    /// The census, then one <c>engage:</c> line per observed mob, then one
    /// <c>candidate:</c> line per planner proposal.
    /// </summary>
    /// <remarks>
    /// The <c>engage:</c> lines come first because they are the ones an
    /// operator acts on: each names a mob and says whether <c>--engage</c>
    /// would refuse it and with which violations. The <c>candidate:</c> lines
    /// below them are the planner's own proposals, which today are
    /// <see cref="CombatActionKind.BasicAttack"/> only and so cover a strictly
    /// narrower radius -- reading them as a prediction of <c>--engage</c> is
    /// exactly the mistake the <c>engage:</c> lines exist to prevent.
    /// </remarks>
    /// <param name="report">A report produced by <see cref="Build"/>.</param>
    public static void Print(CombatReport report)
    {
        ArgumentNullException.ThrowIfNull(report.Checks);
        ArgumentNullException.ThrowIfNull(report.EngageVerdicts);

        CombatMobCensus census = report.Census;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"mobs: {census.Observed} observed, {census.KnownHostile} known hostile, {census.KnownAlive} known alive, {census.Positioned} positioned"));

        foreach (CombatConstraintCheck verdict in report.EngageVerdicts)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"engage: target={verdict.Candidate.Target!.Value.Value} would_act={verdict.IsAllowed} violations={Violations(verdict)}"));
        }

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
    /// <param name="skill">
    /// The skill an <c>--engage</c> invocation would name, if the operator
    /// gave one. Accepted so the two invocations can read the same; it does
    /// not change a verdict (<see cref="TargetVerdictSkill"/>).
    /// </param>
    public static int Run(string? skill = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(string.IsNullOrWhiteSpace(skill) ? null : new SkillId(skill));
    }

    /// <summary>
    /// The live composition: client window, memory session for the player's own
    /// position and vitals, packet capture for the entities around them, and
    /// the reference catalogue that decides which of those entities are
    /// monsters. <see cref="LiveCombatObserver"/> owns the last two and is
    /// shared with <see cref="EngageCommand"/>, so the picture reported here
    /// is the same picture a refusal there is computed from; the window is
    /// found by <see cref="TryFindWindow"/> and the memory session is opened
    /// and owned by this method, then passed in. A thin,
    /// untested-by-design shell, exactly like
    /// <c>LoadoutReportCommand.RunWindows</c>; only <see cref="Build"/> and
    /// <see cref="ExplainEmpty"/> below it are tested.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(SkillId? skill)
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

        ClientMemorySession attached = session!;
        using (attached)
        {
            // The entity feed is the one resource this command cannot report
            // without: with no entities there are no mobs, and with no mobs the
            // report would be an empty page that says nothing about the client.
            // Unlike --scout/--autoplay, which carry on risk-blind, refusing
            // here is the honest answer to "what would the combat planner
            // propose right now": nothing was observed, so nothing is reported.
            using LiveCombatObserver? observer =
                LiveCombatObserver.TryOpen(processId, out string? feedFailure, out string? catalogueWarning);
            if (observer is null)
            {
                Console.WriteLine($"[REFUSED] {EntityFeedUnavailableReason}:{feedFailure}");
                return WalkCommand.ExitAbandoned;
            }

            if (catalogueWarning is not null)
                Console.WriteLine($"[WARN] entity_catalogue_unavailable:{catalogueWarning} -- nessun vnum verra' stabilito come mostro");

            DateTime now = DateTime.UtcNow;
            if (!observer.TryObserve(attached, now, out Player player, out EquatableArray<Mob> mobs, out string? observeFailure))
            {
                Console.WriteLine($"[REFUSED] {observeFailure}");
                return WalkCommand.ExitAbandoned;
            }

            Print(Build(player, mobs, skill));
        }

        return 0;
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
