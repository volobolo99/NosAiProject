using NosAi.Core.CharacterControl;
using Xunit;

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

    /// <summary>
    /// The age bound is the caller's to state, and its default is unchanged.
    /// </summary>
    /// <remarks>
    /// It became a parameter when the guard was wired into <c>--engage</c>, whose
    /// sightings come from a packet capture that announces a change and then says
    /// nothing: at 500 ms every stationary monster would be refused. A caller
    /// that says nothing still gets 500 ms, which is what the three tests above
    /// assert; a caller that widens it is stating a fact about its own channel.
    /// </remarks>
    [Fact]
    public void TheAgeBoundIsTheCallersToState_AndDefaultsToFiveHundredMilliseconds()
    {
        var guard = new FailClosedCharacterActionGuard();
        var action = new CharacterAction("a", CharacterActionKind.Move, null, "movement.move", 1, 0.95);

        Assert.False(guard.IsAllowed(action, new CharacterControlContext(true, true, 501)));
        Assert.True(guard.IsAllowed(action, new CharacterControlContext(true, true, 501, MaxObservationAgeMs: 30_000)));
        Assert.False(guard.IsAllowed(action, new CharacterControlContext(true, true, 30_001, MaxObservationAgeMs: 30_000)));
    }

    /// <summary>
    /// A negative age is a clock disagreeing with itself, not an unusually fresh
    /// reading, and no widening of the bound makes it acceptable.
    /// </summary>
    [Fact]
    public void ANegativeAgeIsRefusedHoweverWideTheBound()
    {
        var guard = new FailClosedCharacterActionGuard();
        var action = new CharacterAction("a", CharacterActionKind.Move, null, "movement.move", 1, 0.95);

        Assert.False(guard.IsAllowed(action, new CharacterControlContext(true, true, -1, MaxObservationAgeMs: 30_000)));
    }
}
