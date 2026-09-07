namespace NosAi.Core.WorldModel.Combat;

/// <summary>
/// Turns the current <see cref="Player"/>/<see cref="Mob"/> state into
/// candidate combat acts and filters them against hard constraints
/// (docs/ROADMAP_ESECUTIVA.md S:AP-05's "Candidate generation" and "hard
/// constraints" stages). Pure and stateless, mirroring
/// <c>NosAi.Core.WorldModel.Exploration.ExplorationPlanner</c>: no I/O, no
/// clock reads, safe to call once per decision cycle.
/// </summary>
/// <remarks>
/// <b>Scope, honestly restricted.</b> This type does not attempt the
/// "short-horizon simulation" or "combo prefix" stages
/// (<see cref="CombatSimulationResult"/>/<see cref="ComboPlan"/> stay
/// unproduced here). Both would need per-skill damage and resource-cost
/// figures to be anything but fabricated, and <see cref="Skill"/> (AP-01)
/// carries neither -- only <c>Id</c>/<c>Name</c>/<c>Level</c>/<c>IsUsable</c>.
/// Inventing placeholder damage numbers to fill that gap would be exactly
/// the kind of simulated data this project refuses to pass off as real
/// (docs/NOSAI_ARCHITECTURE_BASELINE.md's classification discipline). The
/// real fix is a data source for those figures (a client-derived skill
/// stat table, or an empirically observed-damage history -- AP-09's
/// "learn" stage), not a formula invented here.
/// </remarks>
public static class CombatPlanner
{
    /// <summary>Default melee/basic-attack range, in the same units as <see cref="WorldPosition"/>.</summary>
    public const double DefaultBasicAttackRange = 2.0;

    /// <summary>Default skill range. Real per-skill range is not modelled by AP-01's <see cref="Skill"/> contract; this is a single conservative default until it is.</summary>
    /// <remarks>
    /// <b>This constant now gates an actuation.</b> While
    /// <see cref="GenerateCandidates"/> had no production caller it only shaped
    /// a proposal nothing consumed. Since <c>--engage</c> judges its
    /// operator-named target with <see cref="CheckTargetConstraints"/>, this
    /// number decides whether the runtime presses a key: a target beyond it is
    /// refused as <c>target_out_of_range</c>. The direction is fail-closed --
    /// a default that is too small refuses acts that would have been legal,
    /// never the reverse -- so promoting it cost nothing in safety, but it is
    /// no longer a placeholder whose value only affects a report. Replacing it
    /// with a real per-skill range is <c>docs/agents/EXECUTION_QUEUE.md</c>
    /// Q-103.
    /// </remarks>
    public const double DefaultSkillRange = 6.0;

    /// <summary>
    /// One <see cref="CombatActionKind.BasicAttack"/> candidate per viable
    /// mob within <paramref name="basicAttackRange"/>, and one
    /// <see cref="CombatActionKind.UseSkill"/> candidate per (ready skill,
    /// viable mob within <paramref name="skillRange"/>) pair.
    /// </summary>
    /// <remarks>
    /// Deliberately does not generate untargeted/self-cast
    /// <see cref="CombatActionKind.UseSkill"/> candidates: <see cref="Skill"/>
    /// carries no fact saying whether a given skill needs a target or can be
    /// self/AoE-cast, so generating one for every ready skill would guess
    /// at a distinction the data does not make. A future AP-01 extension
    /// (or an AP-05 data-gap task) naming that fact is a prerequisite for
    /// generating self-cast candidates honestly, not something to
    /// approximate here.
    /// </remarks>
    /// <param name="player">Must have a known <see cref="Player.Position"/> for any candidate to be generated -- an unknown player position makes every range check meaningless, so this method returns an empty list rather than guessing.</param>
    /// <param name="mobs">This cycle's known mobs.</param>
    public static IReadOnlyList<CombatActionCandidate> GenerateCandidates(
        Player player,
        EquatableArray<Mob> mobs,
        double basicAttackRange = DefaultBasicAttackRange,
        double skillRange = DefaultSkillRange)
    {
        ArgumentNullException.ThrowIfNull(player);

        var candidates = new List<CombatActionCandidate>();
        if (!player.Position.HasValue)
            return candidates;

        WorldPosition playerPosition = player.Position.Value;

        foreach (Mob mob in mobs)
        {
            if (!IsViableTarget(mob) || !mob.Position.HasValue)
                continue;

            double distance = Distance(playerPosition, mob.Position.Value);

            if (distance <= basicAttackRange)
                candidates.Add(new CombatActionCandidate(CombatActionKind.BasicAttack, target: mob.Id));

            // An unobserved skill list generates nothing, which is what it
            // generated before this was a fact -- but now for a stated reason
            // rather than because an empty array happened to loop zero times.
            if (distance <= skillRange && player.Skills.HasValue)
            {
                foreach (Skill skill in player.Skills.Value)
                {
                    if (IsSkillReady(skill, player.Cooldowns))
                        candidates.Add(new CombatActionCandidate(CombatActionKind.UseSkill, target: mob.Id, skill: skill.Id));
                }
            }
        }

        return candidates;
    }

    /// <summary>A mob worth generating a candidate against: known hostile and known alive. Unknown either fact -- never assumed viable by omission.</summary>
    public static bool IsViableTarget(Mob mob) =>
        mob.IsHostile is { HasValue: true, Value: true } && mob.IsAlive is { HasValue: true, Value: true };

    /// <summary>Usable and not on cooldown. Unknown usability -- never assumed ready by omission.</summary>
    /// <remarks>
    /// An <b>unobserved</b> cooldown list is not an empty one: nobody having
    /// read which abilities are on cooldown is not evidence that none of them
    /// is. It answers false, so a skill is proposed only where both halves were
    /// actually observed -- the fail-closed direction, and the one this method's
    /// own "never assumed ready by omission" already applied to usability.
    /// </remarks>
    public static bool IsSkillReady(Skill skill, WorldFact<EquatableArray<Cooldown>> cooldowns)
    {
        ArgumentNullException.ThrowIfNull(cooldowns);

        if (skill.IsUsable is not { HasValue: true, Value: true })
            return false;

        if (!cooldowns.HasValue)
            return false;

        foreach (Cooldown cooldown in cooldowns.Value)
        {
            if (cooldown.SkillId.Equals(skill.Id) && cooldown.IsActive)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The hard-constraint stage: range, target validity (hostile/alive/
    /// positioned) and skill readiness, checked from already-known facts
    /// only. Does <b>not</b> check a resource cost -- <see cref="Skill"/>
    /// carries none, so a real check cannot be written; do not add a
    /// fabricated one here, add the missing fact to AP-01 first.
    /// </summary>
    public static CombatConstraintCheck CheckHardConstraints(
        CombatActionCandidate candidate,
        Player player,
        EquatableArray<Mob> mobs,
        double basicAttackRange = DefaultBasicAttackRange,
        double skillRange = DefaultSkillRange)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);

        var violations = new List<string>();
        if (CollectTargetViolations(candidate, player, mobs, basicAttackRange, skillRange, violations))
        {
            if (candidate.Kind == CombatActionKind.UseSkill && candidate.Skill is { } skillId)
                CheckSkillReady(skillId, player, violations);
        }

        return Verdict(candidate, violations);
    }

    /// <summary>
    /// The <b>target half</b> of the hard-constraint stage on its own: the
    /// player's own position, and the target's presence, hostility, aliveness,
    /// position and range. Skill readiness is deliberately not checked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists for one honest reason. <see cref="Player.Skills"/> is an
    /// <see cref="EquatableArray{T}"/> with no way to say "not observed", so an
    /// empty list reads identically as "this character has no skills" and as
    /// "nothing has read the skill list". No observation channel in this
    /// project reads it today, which means
    /// <see cref="CheckHardConstraints"/> run against a live-observed player
    /// always refuses the act. The refusal now names the real cause --
    /// <c>skill_list_not_observed</c> rather than <c>skill_not_found</c>, so
    /// it no longer blames the character for a gap in the observation
    /// channels -- but it is still a refusal, and still one this method's
    /// caller cannot do anything about.
    /// </para>
    /// <para>
    /// A caller that cannot observe skills has three choices: check nothing,
    /// check everything and be refused by a violation it invented, or check
    /// exactly what it can observe and say so. This method is the third. It
    /// narrows what is verified, never what is allowed: every violation it can
    /// report, <see cref="CheckHardConstraints"/> reports too, so a candidate
    /// this method refuses is refused by the full check as well. Do not read a
    /// verdict from here as "all hard constraints passed".
    /// </para>
    /// </remarks>
    public static CombatConstraintCheck CheckTargetConstraints(
        CombatActionCandidate candidate,
        Player player,
        EquatableArray<Mob> mobs,
        double basicAttackRange = DefaultBasicAttackRange,
        double skillRange = DefaultSkillRange)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);

        var violations = new List<string>();
        CollectTargetViolations(candidate, player, mobs, basicAttackRange, skillRange, violations);
        return Verdict(candidate, violations);
    }

    /// <summary>
    /// Adds the target-side violations, and reports whether the player's own
    /// position was known -- an unknown position makes every further check
    /// meaningless, so the caller stops there rather than piling on
    /// consequential violations.
    /// </summary>
    private static bool CollectTargetViolations(
        CombatActionCandidate candidate,
        Player player,
        EquatableArray<Mob> mobs,
        double basicAttackRange,
        double skillRange,
        List<string> violations)
    {
        if (!player.Position.HasValue)
        {
            violations.Add("player_position_unknown");
            return false;
        }

        if (candidate.Target is { } target)
        {
            double range = candidate.Kind == CombatActionKind.BasicAttack ? basicAttackRange : skillRange;
            CheckTarget(target, mobs, player.Position.Value, range, violations);
        }

        return true;
    }

    private static CombatConstraintCheck Verdict(CombatActionCandidate candidate, List<string> violations) =>
        violations.Count == 0
            ? CombatConstraintCheck.Allowed(candidate)
            : CombatConstraintCheck.Violated(candidate, EquatableArray<string>.From(violations));

    private static void CheckTarget(
        EntityId target,
        EquatableArray<Mob> mobs,
        WorldPosition playerPosition,
        double range,
        List<string> violations)
    {
        Mob? found = null;
        foreach (Mob mob in mobs)
        {
            if (mob.Id.Equals(target))
            {
                found = mob;
                break;
            }
        }

        if (found is not { } mobFound)
        {
            violations.Add("target_not_found");
            return;
        }

        if (mobFound.IsHostile is not { HasValue: true, Value: true })
            violations.Add("target_not_hostile");

        if (mobFound.IsAlive is not { HasValue: true, Value: true })
            violations.Add("target_not_alive");

        if (!mobFound.Position.HasValue)
        {
            violations.Add("target_position_unknown");
            return;
        }

        if (Distance(playerPosition, mobFound.Position.Value) > range)
            violations.Add("target_out_of_range");
    }

    private static void CheckSkillReady(SkillId skillId, Player player, List<string> violations)
    {
        // A list nobody has read and a list that simply lacks this skill are
        // different facts, and only the second says anything about the
        // character. This used to be inferred from the list being empty, which
        // was a heuristic that happened to be right only because no channel
        // ever observed a genuinely empty one; it is now read off the fact.
        if (!player.Skills.HasValue)
        {
            violations.Add("skill_list_not_observed");
            return;
        }

        Skill? found = null;
        foreach (Skill skill in player.Skills.Value)
        {
            if (skill.Id.Equals(skillId))
            {
                found = skill;
                break;
            }
        }

        if (found is not { } skillFound)
        {
            violations.Add("skill_not_found");
            return;
        }

        if (skillFound.IsUsable is not { HasValue: true, Value: true })
            violations.Add("skill_not_usable");

        // Fail closed, for the reason IsSkillReady's remarks give: not knowing
        // which abilities are on cooldown is not knowing that this one is off it.
        if (!player.Cooldowns.HasValue)
        {
            violations.Add("cooldown_list_not_observed");
            return;
        }

        foreach (Cooldown cooldown in player.Cooldowns.Value)
        {
            if (cooldown.SkillId.Equals(skillId) && cooldown.IsActive)
            {
                violations.Add("skill_on_cooldown");
                break;
            }
        }
    }

    private static double Distance(WorldPosition a, WorldPosition b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
