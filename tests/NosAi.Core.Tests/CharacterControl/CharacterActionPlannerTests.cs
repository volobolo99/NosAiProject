using NosAi.Core.CharacterControl;

namespace NosAi.Core.Tests.CharacterControl;

public sealed class CharacterActionPlannerTests
{
    [Fact]
    public void Select_RejectsStaleObservation()
    {
        var snapshot = Snapshot(observedAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Null(new CharacterActionPlanner(250).Select(snapshot));
    }

    [Fact]
    public void Select_PrioritizesRecoveryWhenHpIsCritical()
    {
        var snapshot = Snapshot(hp: 10, maxHp: 100, stats: new Dictionary<string, double>
        {
            ["inventory.use_item.confidence"] = 0.99
        });

        var action = new CharacterActionPlanner().Select(snapshot);

        Assert.NotNull(action);
        Assert.Equal(CharacterActionKind.UseItem, action.Value.Kind);
        Assert.Equal("inventory.use_item", action.Value.FunctionId);
    }

    [Fact]
    public void Select_UsesSkillOnlyWhenReadyAndConfidenceIsHigh()
    {
        var snapshot = Snapshot(inCombat: true, stats: new Dictionary<string, double>
        {
            ["skill_confidence"] = 0.95
        }, cooldowns: new Dictionary<string, int> { ["skill"] = 0 });

        var action = new CharacterActionPlanner().Select(snapshot);

        Assert.NotNull(action);
        Assert.Equal(CharacterActionKind.UseSkill, action.Value.Kind);
    }

    [Fact]
    public void Select_FallsBackToBasicAttackWhenSkillIsUnavailable()
    {
        var snapshot = Snapshot(inCombat: true, stats: new Dictionary<string, double>
        {
            ["skill_confidence"] = 0.50
        }, cooldowns: new Dictionary<string, int> { ["skill"] = 1000 });

        var action = new CharacterActionPlanner().Select(snapshot);

        Assert.NotNull(action);
        Assert.Equal(CharacterActionKind.BasicAttack, action.Value.Kind);
    }

    private static CharacterWorldSnapshot Snapshot(
        int hp = 100,
        int maxHp = 100,
        bool inCombat = false,
        DateTimeOffset? observedAt = null,
        IReadOnlyDictionary<string, double>? stats = null,
        IReadOnlyDictionary<string, int>? cooldowns = null)
        => new(
            "character-1", 10, 20, hp, maxHp, 100, 100, inCombat, "target-1", 3,
            observedAt ?? DateTimeOffset.UtcNow,
            stats ?? new Dictionary<string, double>(),
            cooldowns ?? new Dictionary<string, int>());
}
