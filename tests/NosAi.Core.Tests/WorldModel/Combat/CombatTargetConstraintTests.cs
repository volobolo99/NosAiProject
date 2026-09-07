using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Combat;

/// <summary>
/// <see cref="CombatPlanner.CheckTargetConstraints"/>: the target half of the
/// hard-constraint stage, checkable by a caller that cannot observe the
/// character's skill list.
/// </summary>
/// <remarks>
/// The property that matters for safety is the relationship to the full check,
/// not the method in isolation: this one may narrow what is verified, never
/// what is allowed. Several tests below assert exactly that pairing.
/// </remarks>
public sealed class CombatTargetConstraintTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Player PlayerAt(double x, double y, params Skill[] skills) => new(
        new EntityId("player-1"),
        WorldFact<WorldPosition>.Live(new WorldPosition((float)x, (float)y), 1.0, Now),
        WorldFact<float>.Unknown("r", Now),
        WorldFact<bool>.Unknown("r", Now),
        WorldFact<MapId>.Unknown("r", Now),
        CombatantStatus.Empty,
        EquatableArray<Skill>.From(skills),
        EquatableArray<Cooldown>.Empty,
        EquatableArray<InventoryItem>.Empty,
        EquatableArray<EquipmentItem>.Empty);

    private static Mob MobAt(string id, double x, double y, bool? hostile = true, bool? alive = true) => new(
        new EntityId(id),
        WorldFact<WorldPosition>.Live(new WorldPosition((float)x, (float)y), 1.0, Now),
        WorldFact<string>.Unknown("r", Now),
        hostile is { } h ? WorldFact<bool>.Live(h, 1.0, Now) : WorldFact<bool>.Unknown("r", Now),
        alive is { } a ? WorldFact<bool>.Derived(a, 1.0, Now) : WorldFact<bool>.Unknown("r", Now),
        CombatantStatus.Empty);

    private static EquatableArray<Mob> Mobs(params Mob[] mobs) => EquatableArray<Mob>.From(mobs);

    private static CombatActionCandidate SkillAt(string targetId) =>
        new(CombatActionKind.UseSkill, target: new EntityId(targetId), skill: new SkillId("7"));

    [Fact]
    public void AHostileAliveTargetInSkillRange_Passes()
    {
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt("mob-1"), PlayerAt(0, 0), Mobs(MobAt("mob-1", 3, 0)));

        Assert.True(check.IsAllowed);
        Assert.Empty(check.ViolatedConstraints);
    }

    /// <summary>
    /// The reason this method exists: the same candidate through the full
    /// check is refused, and the refusal is about the unread skill list rather
    /// than about the target -- which is exactly what a caller judging a
    /// target cannot act on.
    /// </summary>
    [Fact]
    public void TheSameCandidate_IsRefusedByTheFullCheck_ForTheSkillHalfAlone()
    {
        CombatActionCandidate candidate = SkillAt("mob-1");
        Player player = PlayerAt(0, 0);
        EquatableArray<Mob> mobs = Mobs(MobAt("mob-1", 3, 0));

        Assert.True(CombatPlanner.CheckTargetConstraints(candidate, player, mobs).IsAllowed);

        CombatConstraintCheck full = CombatPlanner.CheckHardConstraints(candidate, player, mobs);
        Assert.False(full.IsAllowed);
        Assert.Equal(new[] { "skill_list_not_observed" }, full.ViolatedConstraints);
    }

    [Theory]
    [InlineData("mob-2", "target_not_found")]
    public void AnUnknownTarget_IsNamed(string targetId, string expected)
    {
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt(targetId), PlayerAt(0, 0), Mobs(MobAt("mob-1", 3, 0)));

        Assert.False(check.IsAllowed);
        Assert.Contains(expected, check.ViolatedConstraints);
    }

    [Fact]
    public void AnUnestablishedHostility_IsNamed_AndIsNotReadAsFriendly()
    {
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt("mob-1"), PlayerAt(0, 0), Mobs(MobAt("mob-1", 3, 0, hostile: null)));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_not_hostile", check.ViolatedConstraints);
    }

    [Fact]
    public void AnUnestablishedAliveness_IsNamed()
    {
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt("mob-1"), PlayerAt(0, 0), Mobs(MobAt("mob-1", 3, 0, alive: null)));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_not_alive", check.ViolatedConstraints);
    }

    [Fact]
    public void ATargetBeyondSkillRange_IsNamed()
    {
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt("mob-1"), PlayerAt(0, 0), Mobs(MobAt("mob-1", 100, 0)));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_out_of_range", check.ViolatedConstraints);
    }

    /// <summary>
    /// An unknown player position makes every further measurement meaningless,
    /// so it is reported alone rather than followed by consequential
    /// violations the caller would have to know to ignore.
    /// </summary>
    [Fact]
    public void AnUnknownPlayerPosition_IsTheOnlyViolationReported()
    {
        Player blind = PlayerAt(0, 0) with { Position = WorldFact<WorldPosition>.Unknown("unreadable", Now) };

        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            SkillAt("mob-1"), blind, Mobs(MobAt("mob-1", 100, 0, hostile: null)));

        Assert.False(check.IsAllowed);
        Assert.Equal(new[] { "player_position_unknown" }, check.ViolatedConstraints);
    }

    /// <summary>
    /// The safety property, stated as a test: every target-side violation this
    /// method reports, the full check reports too. It can refuse where the full
    /// check would allow, never the other way round.
    /// </summary>
    [Theory]
    [InlineData("mob-2", 3, true, true)]
    [InlineData("mob-1", 100, true, true)]
    [InlineData("mob-1", 3, false, true)]
    [InlineData("mob-1", 3, true, false)]
    public void EveryViolationItReports_IsAlsoReportedByTheFullCheck(
        string targetId, double mobX, bool hostileKnown, bool aliveKnown)
    {
        CombatActionCandidate candidate = SkillAt(targetId);
        Player player = PlayerAt(0, 0);
        EquatableArray<Mob> mobs = Mobs(MobAt(
            "mob-1", mobX, 0,
            hostile: hostileKnown ? true : null,
            alive: aliveKnown ? true : null));

        CombatConstraintCheck targetOnly = CombatPlanner.CheckTargetConstraints(candidate, player, mobs);
        CombatConstraintCheck full = CombatPlanner.CheckHardConstraints(candidate, player, mobs);

        Assert.False(targetOnly.IsAllowed);
        Assert.All(targetOnly.ViolatedConstraints, v => Assert.Contains(v, full.ViolatedConstraints));
    }

    [Fact]
    public void ABasicAttackCandidate_IsMeasuredAgainstTheBasicAttackRange()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: new EntityId("mob-1"));

        // 3.0 is inside the default skill range (6.0) and outside the default
        // basic-attack range (2.0), so the kind alone decides the verdict.
        CombatConstraintCheck check = CombatPlanner.CheckTargetConstraints(
            candidate, PlayerAt(0, 0), Mobs(MobAt("mob-1", 3, 0)));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_out_of_range", check.ViolatedConstraints);
    }

    [Fact]
    public void NullArguments_AreRefusedAtTheBoundary()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CombatPlanner.CheckTargetConstraints(null!, PlayerAt(0, 0), EquatableArray<Mob>.Empty));
        Assert.Throws<ArgumentNullException>(() =>
            CombatPlanner.CheckTargetConstraints(SkillAt("mob-1"), null!, EquatableArray<Mob>.Empty));
    }
}
