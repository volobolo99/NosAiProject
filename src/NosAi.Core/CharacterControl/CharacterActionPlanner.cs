namespace NosAi.Core.CharacterControl;

using NosAi.Core.Game;

public sealed class CharacterActionPlanner : ICharacterActionPlanner
{
    private readonly double _maxObservationAgeMs;

    public CharacterActionPlanner(double maxObservationAgeMs = 250)
    {
        if (maxObservationAgeMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxObservationAgeMs));
        _maxObservationAgeMs = maxObservationAgeMs;
    }

    public CharacterAction? Select(CharacterWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var ageMs = Math.Max(0, (DateTimeOffset.UtcNow - snapshot.ObservedAt).TotalMilliseconds);
        if (ageMs > _maxObservationAgeMs || snapshot.MaxHp <= 0 || snapshot.Hp < 0)
            return null;

        if (snapshot.Hp * 100.0 / snapshot.MaxHp <= 20.0 &&
            TryGetUsableFunction("inventory.use_item", snapshot.Stats, out var healConfidence))
        {
            return new CharacterAction("use-recovery-item", CharacterActionKind.UseItem, null,
                "inventory.use_item", 100, healConfidence);
        }

        if (!snapshot.InCombat && snapshot.TargetId is not null && snapshot.TargetDistance > 2.0)
        {
            return new CharacterAction("move-to-target", CharacterActionKind.Move,
                new CharacterTarget(snapshot.TargetId, snapshot.X, snapshot.Y),
                "movement.move", 60, 0.95);
        }

        if (snapshot.InCombat && snapshot.TargetId is not null)
        {
            if (snapshot.CooldownsMs.TryGetValue("skill", out var skillCooldown) && skillCooldown <= 0 &&
                snapshot.Stats.TryGetValue("skill_confidence", out var skillConfidence) && skillConfidence >= 0.90)
            {
                return new CharacterAction("use-skill", CharacterActionKind.UseSkill,
                    new CharacterTarget(snapshot.TargetId, snapshot.X, snapshot.Y),
                    "combat.skill", 90, Math.Clamp(skillConfidence, 0, 1));
            }

            return new CharacterAction("basic-attack", CharacterActionKind.BasicAttack,
                new CharacterTarget(snapshot.TargetId, snapshot.X, snapshot.Y),
                "combat.basic_attack", 80, 0.95);
        }

        return null;
    }

    private static bool TryGetUsableFunction(string id, IReadOnlyDictionary<string, double> stats, out double confidence)
    {
        confidence = stats.TryGetValue($"{id}.confidence", out var value) ? Math.Clamp(value, 0, 1) : 0;
        return GameFunctionCatalog.TryGet(id, out var definition) && confidence >= definition!.MinimumConfidence;
    }
}
