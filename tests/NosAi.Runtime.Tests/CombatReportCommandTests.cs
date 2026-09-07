using System.Linq;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--combat-report</c>: the first production caller
/// <see cref="CombatPlanner"/> has ever had.
/// </summary>
/// <remarks>
/// Everything here drives <see cref="CombatReportCommand.Build"/> from a
/// hand-built <see cref="Player"/> and mob list -- no client, no capture, no
/// clock -- the same shape <c>LoadoutReportCommandTests</c> uses.
/// <see cref="CombatReportCommand.Run"/> is exercised only on its off-Windows
/// branch; the Windows path needs a real attached client.
/// </remarks>
public sealed class CombatReportCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Player PlayerAt(double x, double y) => new(
        new EntityId("player-1"),
        WorldFact<WorldPosition>.Live(new WorldPosition((float)x, (float)y), 1.0, Now),
        WorldFact<float>.Unknown("orientation_not_read", Now),
        WorldFact<bool>.Unknown("alive_not_read", Now),
        WorldFact<MapId>.Unknown("map_not_read", Now),
        CombatantStatus.Empty,
        EquatableArray<Skill>.Empty,
        EquatableArray<Cooldown>.Empty,
        EquatableArray<InventoryItem>.Empty,
        EquatableArray<EquipmentItem>.Empty);

    private static Player PlayerWithUnknownPosition() => PlayerAt(0, 0) with
    {
        Position = WorldFact<WorldPosition>.Unknown("player_unreadable", Now)
    };

    private static Mob MobAt(string id, double x, double y, bool? hostile = true, bool? alive = true) => new(
        new EntityId(id),
        WorldFact<WorldPosition>.Live(new WorldPosition((float)x, (float)y), 1.0, Now),
        WorldFact<string>.Unknown("species_name_catalog_not_available", Now),
        hostile is { } h ? WorldFact<bool>.Live(h, 1.0, Now) : WorldFact<bool>.Unknown("hostility_never_established", Now),
        alive is { } a ? WorldFact<bool>.Derived(a, 1.0, Now) : WorldFact<bool>.Unknown("hp_never_stated", Now),
        CombatantStatus.Empty);

    private static EquatableArray<Mob> Mobs(params Mob[] mobs) => EquatableArray<Mob>.From(mobs);

    [Fact]
    public void AHostileAliveMobInRange_ProducesAnAllowedBasicAttackCandidate()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 1, 0)));

        CombatConstraintCheck check = Assert.Single(report.Checks);
        Assert.Equal(CombatActionKind.BasicAttack, check.Candidate.Kind);
        Assert.Equal("mob-1", check.Candidate.Target!.Value.Value);
        Assert.True(check.IsAllowed);
        Assert.Empty(check.ViolatedConstraints);
    }

    /// <summary>
    /// Nothing here reads the character's skill list, so a
    /// <see cref="CombatActionKind.UseSkill"/> candidate cannot be generated --
    /// stated on the command and pinned here so a later change to
    /// <see cref="Player.Skills"/> has to face this test.
    /// </summary>
    [Fact]
    public void WithNoSkillsKnown_OnlyBasicAttackCandidatesAreEverGenerated()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 1, 0), MobAt("mob-2", 1.5, 0)));

        Assert.Equal(2, report.Checks.Count);
        Assert.All(report.Checks, c => Assert.Equal(CombatActionKind.BasicAttack, c.Candidate.Kind));
    }

    [Fact]
    public void AMobOutOfBasicAttackRange_ProducesNoCandidate()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 50, 0)));

        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, null)]
    [InlineData(null, null)]
    public void AMobWithAnUnknownFact_ProducesNoCandidate(bool? hostile, bool? alive)
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 1, 0, hostile, alive)));

        Assert.Empty(report.Checks);
    }

    [Fact]
    public void AnUnknownPlayerPosition_ProducesNoCandidate()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerWithUnknownPosition(), Mobs(MobAt("mob-1", 1, 0)));

        Assert.Empty(report.Checks);
    }

    // ------------------------------------------------------------- the census

/// <summary>
    /// Each of the four counters gets a different value, so a swap of two
    /// arguments at the <c>new CombatMobCensus(...)</c> call site fails here
    /// instead of passing.
    /// </summary>
    [Fact]
    public void TheCensusCountsWhatWasActuallyEstablished()
    {
        CombatReportCommand.CombatReport report = CombatReportCommand.Build(
            PlayerAt(0, 0),
            Mobs(
                MobAt("mob-1", 1, 0),
                MobAt("mob-2", 1, 0, hostile: null),
                MobAt("mob-3", 1, 0, alive: null),
                MobAt("mob-4", 1, 0, alive: null),
                MobAt("mob-5", 1, 0) with { Position = WorldFact<WorldPosition>.Unknown("never_placed", Now) }));

        Assert.Equal(5, report.Census.Observed);
        Assert.Equal(4, report.Census.KnownHostile);
        Assert.Equal(3, report.Census.KnownAlive);
        Assert.Equal(4, report.Census.Positioned);
    }

    /// <summary>
    /// The whole point of the census: an empty report must say which of the
    /// several possible worlds it is reporting, because they mean different
    /// things to the operator.
    /// </summary>
    [Fact]
    public void NoMonsterObserved_AndMonstersWithNoEstablishedHostility_ReadDifferently()
    {
        CombatReportCommand.CombatReport nothingSeen =
            CombatReportCommand.Build(PlayerAt(0, 0), EquatableArray<Mob>.Empty);
        CombatReportCommand.CombatReport seenButNotHostile =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 1, 0, hostile: null)));

        Assert.Empty(nothingSeen.Checks);
        Assert.Empty(seenButNotHostile.Checks);

        string first = CombatReportCommand.ExplainEmpty(nothingSeen.Census);
        string second = CombatReportCommand.ExplainEmpty(seenButNotHostile.Census);

        Assert.StartsWith("no_monster_observed", first, StringComparison.Ordinal);
        Assert.StartsWith("no_mob_known_hostile", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AValidButDistantMob_IsExplainedAsOutOfRange_NotAsMissingFacts()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 50, 0)));

        Assert.StartsWith("no_candidate_in_range", CombatReportCommand.ExplainEmpty(report.Census), StringComparison.Ordinal);
    }


    // ------------------------------------------- what --engage would answer

    /// <summary>
    /// The divergence this half of the report exists to close: a mob outside
    /// basic-attack range but inside skill range is invisible to the planner's
    /// candidates and attacked by <c>--engage</c>.
    /// </summary>
    /// <remarks>
    /// Before the <c>engage:</c> verdicts existed, <c>--combat-report</c>
    /// printed <c>0 candidates</c> and <c>[WARN] no_candidate_in_range</c> for
    /// this exact world, while <c>--engage mob-1 &lt;skill&gt;</c> pressed the
    /// key: <see cref="CombatPlanner.GenerateCandidates"/> could only ever
    /// produce <see cref="CombatActionKind.BasicAttack"/> candidates here
    /// (<see cref="Player.Skills"/> is empty on every live observation), and
    /// those are judged at <see cref="CombatPlanner.DefaultBasicAttackRange"/>
    /// while <c>--engage</c> judges at
    /// <see cref="CombatPlanner.DefaultSkillRange"/>. The report was wrong in
    /// the dangerous direction: it showed nothing where the runtime would act.
    /// </remarks>
    [Fact]
    public void AMobBetweenTheTwoRanges_ProducesNoCandidate_ButAnEngageVerdictThatWouldAct()
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", 4, 0)));

        Assert.True(4.0 > CombatPlanner.DefaultBasicAttackRange, "the mob must be outside basic-attack range");
        Assert.True(4.0 <= CombatPlanner.DefaultSkillRange, "the mob must be inside skill range");

        Assert.Empty(report.Checks);

        CombatConstraintCheck verdict = Assert.Single(report.EngageVerdicts);
        Assert.Equal("mob-1", verdict.Candidate.Target!.Value.Value);
        Assert.True(verdict.IsAllowed, "--engage would act on this mob, so the report must say so");
    }

    /// <summary>Every observed mob gets a verdict, including the ones no candidate is generated against.</summary>
    [Fact]
    public void EveryObservedMobGetsAVerdict_EvenThoseTheCandidateListSkips()
    {
        CombatReportCommand.CombatReport report = CombatReportCommand.Build(
            PlayerAt(0, 0),
            Mobs(
                MobAt("in-range", 1, 0),
                MobAt("not-hostile", 1, 0, hostile: null),
                MobAt("far-away", 50, 0)));

        Assert.Equal(3, report.EngageVerdicts.Count);
        Assert.Equal(
            new[] { "in-range", "not-hostile", "far-away" },
            report.EngageVerdicts.Select(v => v.Candidate.Target!.Value.Value).ToArray());
    }

    /// <summary>
    /// The verdict names the same violations <c>--engage</c> would refuse
    /// with, one per reason the target fails.
    /// </summary>
    [Theory]
    [InlineData(1.0, null, true, "target_not_hostile")]
    [InlineData(1.0, true, null, "target_not_alive")]
    [InlineData(50.0, true, true, "target_out_of_range")]
    public void AnUnactionableMob_IsRefusedByName(double x, bool? hostile, bool? alive, string expected)
    {
        CombatReportCommand.CombatReport report =
            CombatReportCommand.Build(PlayerAt(0, 0), Mobs(MobAt("mob-1", x, 0, hostile, alive)));

        CombatConstraintCheck verdict = Assert.Single(report.EngageVerdicts);
        Assert.False(verdict.IsAllowed);
        Assert.Contains(expected, verdict.ViolatedConstraints);
    }

    /// <summary>
    /// Pins the independence <see cref="CombatReportCommand.TargetVerdictSkill"/>
    /// rests on: <see cref="CombatPlanner.CheckTargetConstraints"/> reads the
    /// candidate's kind and target, never its skill.
    /// </summary>
    /// <remarks>
    /// If the target-side check ever starts consulting the skill -- a per-skill
    /// range being the obvious reason -- this test goes red, rather than the
    /// report quietly printing a verdict computed with a skill the operator
    /// never named.
    /// </remarks>
    [Fact]
    public void TheEngageVerdictDoesNotDependOnWhichSkillIsNamed()
    {
        EquatableArray<Mob> mobs = Mobs(MobAt("near", 1, 0), MobAt("mid", 4, 0), MobAt("far", 50, 0));

        CombatReportCommand.CombatReport unnamed = CombatReportCommand.Build(PlayerAt(0, 0), mobs);
        CombatReportCommand.CombatReport named = CombatReportCommand.Build(PlayerAt(0, 0), mobs, new SkillId("some-real-skill"));
        CombatReportCommand.CombatReport other = CombatReportCommand.Build(PlayerAt(0, 0), mobs, new SkillId("a-different-one"));

        for (int i = 0; i < unnamed.EngageVerdicts.Count; i++)
        {
            Assert.Equal(unnamed.EngageVerdicts[i].IsAllowed, named.EngageVerdicts[i].IsAllowed);
            Assert.Equal(unnamed.EngageVerdicts[i].IsAllowed, other.EngageVerdicts[i].IsAllowed);
            Assert.Equal(unnamed.EngageVerdicts[i].ViolatedConstraints, named.EngageVerdicts[i].ViolatedConstraints);
            Assert.Equal(unnamed.EngageVerdicts[i].ViolatedConstraints, other.EngageVerdicts[i].ViolatedConstraints);
        }
    }

    /// <summary>
    /// An unknown player position stops the check before the target is looked
    /// at, so every mob is refused for the player's own reason -- the one
    /// violation name <c>--engage</c> lists that no mob can cause.
    /// </summary>
    [Fact]
    public void AnUnknownPlayerPosition_RefusesEveryMobForThePlayersOwnReason()
    {
        CombatReportCommand.CombatReport report = CombatReportCommand.Build(
            PlayerWithUnknownPosition(), Mobs(MobAt("mob-1", 1, 0), MobAt("mob-2", 4, 0)));

        Assert.Equal(2, report.EngageVerdicts.Count);
        Assert.All(report.EngageVerdicts, v =>
        {
            Assert.False(v.IsAllowed);
            Assert.Equal(new[] { "player_position_unknown" }, v.ViolatedConstraints.ToArray());
        });
    }

    // ------------------------------------------------- console entry and guards

    [Fact]
    public void Print_OnADefaultReport_IsRefusedRatherThanThrowingNullReference()
    {
        Assert.Throws<ArgumentNullException>(() => CombatReportCommand.Print(default));
    }

/// <summary>
    /// The off-Windows refusal. Skipped on Windows rather than returning
    /// early: an early return makes the test report as passed on the one
    /// platform this project actually builds for, which is a green light for
    /// an assertion that never ran.
    /// </summary>
    [NonWindowsFact]
    public void Run_OffWindows_RefusesWithoutTouchingTheClient()
    {
        Assert.Equal(NosAi.Runtime.Navigation.WalkCommand.ExitAbandoned, CombatReportCommand.Run());
    }

    [Fact]
    public void RefusalReasons_AreTheNamedIdentifiersTheOperatorSees()
    {
        Assert.Equal("--combat-report", CombatReportCommand.Flag);
        Assert.Equal("combat_report_requires_windows", CombatReportCommand.NotWindowsReason);
        Assert.Equal("combat_report_entity_feed_unavailable", CombatReportCommand.EntityFeedUnavailableReason);
        Assert.Equal("target-verdict-any-skill", CombatReportCommand.TargetVerdictSkill);
    }
}
