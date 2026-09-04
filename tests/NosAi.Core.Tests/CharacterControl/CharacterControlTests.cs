using NosAi.Core.CharacterControl;

namespace NosAi.Core.Tests.CharacterControl;

public sealed class CharacterControlTests
{
    [Fact]
    public void GuardRejectsStaleObservation()
    {
        var guard = new FailClosedCharacterActionGuard();
        var action = new CharacterAction("attack", CharacterActionKind.BasicAttack,
            new CharacterTarget("mob-1", 10, 10), "combat.basic_attack", 1, 0.99);

        Assert.False(guard.IsAllowed(action, new CharacterControlContext(true, true, 501)));
    }

    [Fact]
    public void GuardRejectsUnknownOrLowConfidenceAction()
    {
        var guard = new FailClosedCharacterActionGuard();
        var action = new CharacterAction("attack", CharacterActionKind.BasicAttack,
            new CharacterTarget("mob-1", 10, 10), "combat.basic_attack", 1, 0.50);

        Assert.False(guard.IsAllowed(action, new CharacterControlContext(true, true, 10)));
    }

    [Fact]
    public void GuardAllowsFreshSafeClientAction()
    {
        var guard = new FailClosedCharacterActionGuard();
        var action = new CharacterAction("attack", CharacterActionKind.BasicAttack,
            new CharacterTarget("mob-1", 10, 10), "combat.basic_attack", 1, 0.95);

        Assert.True(guard.IsAllowed(action, new CharacterControlContext(true, true, 20)));
    }
}
