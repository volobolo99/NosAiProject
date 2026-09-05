using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Combat;

public sealed class CombatPlannerTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static Player BuildPlayer(
        WorldPosition? position = null,
        EquatableArray<Skill>? skills = null,
        EquatableArray<Cooldown>? cooldowns = null) =>
        new(
            new EntityId("player-1"),
            position is { } p ? WorldFact<WorldPosition>.Live(p, 1d, Now) : WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            CombatantStatus.Empty,
            skills ?? EquatableArray<Skill>.Empty,
            cooldowns ?? EquatableArray<Cooldown>.Empty,
            EquatableArray<InventoryItem>.Empty,
            EquatableArray<EquipmentItem>.Empty);

    private static Mob BuildMob(
        string id = "mob-1",
        WorldPosition? position = null,
        bool? hostile = true,
        bool? alive = true) =>
        new(
            new EntityId(id),
            position is { } p ? WorldFact<WorldPosition>.Live(p, 1d, Now) : WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<string>.Live("wolf", 1d, Now),
            hostile is { } h ? WorldFact<bool>.Live(h, 1d, Now) : WorldFact<bool>.Unknown("r", Now),
            alive is { } a ? WorldFact<bool>.Live(a, 1d, Now) : WorldFact<bool>.Unknown("r", Now),
            CombatantStatus.Empty);

    private static Skill BuildSkill(string id = "skill-1", bool usable = true) =>
        new(new SkillId(id), WorldFact<string>.Live("fireball", 1d, Now), WorldFact<int>.Live(1, 1d, Now),
            WorldFact<bool>.Live(usable, 1d, Now));

    // ---- GenerateCandidates ----

    [Fact]
    public void GenerateCandidates_UnknownPlayerPosition_ReturnsEmpty()
    {
        Player player = BuildPlayer(position: null);
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(0f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_MobInBasicAttackRange_ProducesBasicAttackCandidate()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Contains(candidates, c => c.Kind == CombatActionKind.BasicAttack);
    }

    [Fact]
    public void GenerateCandidates_MobOutOfAllRange_ProducesNoCandidate()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(100f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_NonHostileMob_ProducesNoCandidate()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f), hostile: false) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_DeadMob_ProducesNoCandidate()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f), alive: false) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_UnknownHostility_ProducesNoCandidate()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f), hostile: null) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_ReadySkillWithMobInSkillRange_ProducesUseSkillCandidate()
    {
        var skill = BuildSkill();
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f), skills: EquatableArray<Skill>.From(new[] { skill }));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(5f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.Contains(candidates, c => c.Kind == CombatActionKind.UseSkill && c.Skill == skill.Id);
    }

    [Fact]
    public void GenerateCandidates_SkillOnCooldown_ProducesNoUseSkillCandidate()
    {
        var skill = BuildSkill();
        var cooldown = new Cooldown(skill.Id, WorldFact<TimeSpan>.Live(TimeSpan.FromSeconds(5), 1d, Now));
        Player player = BuildPlayer(
            position: new WorldPosition(0f, 0f),
            skills: EquatableArray<Skill>.From(new[] { skill }),
            cooldowns: EquatableArray<Cooldown>.From(new[] { cooldown }));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(5f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.DoesNotContain(candidates, c => c.Kind == CombatActionKind.UseSkill);
    }

    [Fact]
    public void GenerateCandidates_UnusableSkill_ProducesNoUseSkillCandidate()
    {
        var skill = BuildSkill(usable: false);
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f), skills: EquatableArray<Skill>.From(new[] { skill }));
        var mobs = EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(5f, 0f)) });

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, mobs);

        Assert.DoesNotContain(candidates, c => c.Kind == CombatActionKind.UseSkill);
    }

    [Fact]
    public void GenerateCandidates_NoUntargetedSelfCastCandidatesAreEverProduced()
    {
        var skill = BuildSkill();
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f), skills: EquatableArray<Skill>.From(new[] { skill }));

        IReadOnlyList<CombatActionCandidate> candidates = CombatPlanner.GenerateCandidates(player, EquatableArray<Mob>.Empty);

        Assert.Empty(candidates);
    }

    // ---- CheckHardConstraints ----

    [Fact]
    public void CheckHardConstraints_ValidBasicAttack_IsAllowed()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { mob });
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, mobs);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void CheckHardConstraints_TargetOutOfRange_Violates()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(100f, 0f));
        var mobs = EquatableArray<Mob>.From(new[] { mob });
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, mobs);

        Assert.False(check.IsAllowed);
        Assert.Contains("target_out_of_range", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_TargetNotFound_Violates()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: new EntityId("ghost"));

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.Empty);

        Assert.False(check.IsAllowed);
        Assert.Contains("target_not_found", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_TargetNotHostile_Violates()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(1f, 0f), hostile: false);
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_not_hostile", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_TargetDead_Violates()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(1f, 0f), alive: false);
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("target_not_alive", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_UnknownPlayerPosition_Violates()
    {
        Player player = BuildPlayer(position: null);
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("player_position_unknown", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_SkillOnCooldown_Violates()
    {
        var skill = BuildSkill();
        var cooldown = new Cooldown(skill.Id, WorldFact<TimeSpan>.Live(TimeSpan.FromSeconds(1), 1d, Now));
        Player player = BuildPlayer(
            position: new WorldPosition(0f, 0f),
            skills: EquatableArray<Skill>.From(new[] { skill }),
            cooldowns: EquatableArray<Cooldown>.From(new[] { cooldown }));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: skill.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("skill_on_cooldown", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_SkillNotFound_Violates()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: new SkillId("missing"));

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("skill_not_found", check.ViolatedConstraints);
    }

    [Fact]
    public void CheckHardConstraints_ValidUseSkill_IsAllowed()
    {
        var skill = BuildSkill();
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f), skills: EquatableArray<Skill>.From(new[] { skill }));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: skill.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.True(check.IsAllowed);
    }
}
