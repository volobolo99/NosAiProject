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
            // Nessuna skill passata significa "nessuno ha letto le abilita'", non
            // "il personaggio non ne ha": e' la distinzione che ADR-0027 ha reso
            // esprimibile, ed e' quella che i test qui sotto verificano.
            skills is { } observedSkills
                ? WorldFact<EquatableArray<Skill>>.Live(observedSkills, 1d, Now)
                : WorldFact<EquatableArray<Skill>>.Unknown("skill_list_never_read", Now),
            WorldFact<EquatableArray<Cooldown>>.Live(cooldowns ?? EquatableArray<Cooldown>.Empty, 1d, Now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, Now));

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
        // A skill list that was read and does not contain this skill: only
        // then is "not found" a statement about the character. The fixture
        // seeds a different skill on purpose -- an empty list would exercise
        // the case below instead.
        Player player = BuildPlayer(
            position: new WorldPosition(0f, 0f),
            skills: EquatableArray<Skill>.From(new[] { BuildSkill("skill-1") }));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: new SkillId("missing"));

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("skill_not_found", check.ViolatedConstraints);
    }

    /// <summary>
    /// An empty skill list is not "this character has no such skill": it is
    /// "nobody read the skills". Both refuse the act, but only one of them
    /// says something about the character, and an operator reading the
    /// refusal must be able to tell which.
    /// </summary>
    [Fact]
    public void CheckHardConstraints_SkillListNeverRead_SaysSo_NotSkillNotFound()
    {
        Player player = BuildPlayer(position: new WorldPosition(0f, 0f));
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: new SkillId("missing"));

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("skill_list_not_observed", check.ViolatedConstraints);
        Assert.DoesNotContain("skill_not_found", check.ViolatedConstraints);
    }

    /// <summary>
    /// Non sapere quali abilita' siano in cooldown non e' sapere che questa non
    /// lo e'. Con la lista non osservata il candidato viene rifiutato per nome.
    /// </summary>
    /// <remarks>
    /// Fail-closed nella stessa direzione che <c>IsSkillReady</c> gia' applicava
    /// all'usabilita' ignota. Finche' <c>Player.Cooldowns</c> era un array nudo
    /// questo caso non era esprimibile: una lista mai letta era un array vuoto,
    /// cioe' "nessun cooldown attivo", cioe' un permesso.
    /// </remarks>
    [Fact]
    public void CheckHardConstraints_CooldownListNeverRead_RefusesByName()
    {
        var skill = BuildSkill();
        Player player = BuildPlayer(
            position: new WorldPosition(0f, 0f),
            skills: EquatableArray<Skill>.From(new[] { skill }));
        player = player with
        {
            Cooldowns = WorldFact<EquatableArray<Cooldown>>.Unknown("cooldowns_never_read", Now)
        };
        var mob = BuildMob(position: new WorldPosition(1f, 0f));
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: skill.Id);

        CombatConstraintCheck check = CombatPlanner.CheckHardConstraints(
            candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.False(check.IsAllowed);
        Assert.Contains("cooldown_list_not_observed", check.ViolatedConstraints);
    }

    /// <summary>
    /// Stessa regola sul percorso della generazione: nessun candidato skill
    /// nasce da una lista di cooldown mai letta.
    /// </summary>
    [Fact]
    public void GenerateCandidates_CooldownListNeverRead_ProposesNoSkill()
    {
        Player player = BuildPlayer(
            position: new WorldPosition(0f, 0f),
            skills: EquatableArray<Skill>.From(new[] { BuildSkill() }));
        player = player with
        {
            Cooldowns = WorldFact<EquatableArray<Cooldown>>.Unknown("cooldowns_never_read", Now)
        };
        var mob = BuildMob(position: new WorldPosition(1f, 0f));

        IReadOnlyList<CombatActionCandidate> candidates =
            CombatPlanner.GenerateCandidates(player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.DoesNotContain(candidates, c => c.Kind == CombatActionKind.UseSkill);
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
