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

    [Fact]
    public void TheCensusCountsWhatWasActuallyEstablished()
    {
        CombatReportCommand.CombatReport report = CombatReportCommand.Build(
            PlayerAt(0, 0),
            Mobs(
                MobAt("mob-1", 1, 0),
                MobAt("mob-2", 1, 0, hostile: null),
                MobAt("mob-3", 1, 0, alive: null)));

        Assert.Equal(3, report.Census.Observed);
        Assert.Equal(2, report.Census.KnownHostile);
        Assert.Equal(2, report.Census.KnownAlive);
        Assert.Equal(3, report.Census.Positioned);
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

    // ------------------------------------------------- console entry and guards

    [Fact]
    public void Print_OnADefaultReport_IsRefusedRatherThanThrowingNullReference()
    {
        Assert.Throws<ArgumentNullException>(() => CombatReportCommand.Print(default));
    }

    [Fact]
    public void Run_OffWindows_RefusesWithoutTouchingTheClient()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(NosAi.Runtime.Navigation.WalkCommand.ExitAbandoned, CombatReportCommand.Run());
    }

    [Fact]
    public void RefusalReasons_AreTheNamedIdentifiersTheOperatorSees()
    {
        Assert.Equal("--combat-report", CombatReportCommand.Flag);
        Assert.Equal("combat_report_requires_windows", CombatReportCommand.NotWindowsReason);
        Assert.Equal("combat_report_entity_feed_unavailable", CombatReportCommand.EntityFeedUnavailableReason);
    }
}
