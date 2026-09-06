using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Strategy;

public sealed class CombatRecencyTrackerTests
{
    private static readonly DateTime T0 = DateTime.UnixEpoch;

    [Fact]
    public void FirstPoll_HasNoHistory_ReportsUnknown()
    {
        (WorldFact<bool> inCombat, CombatRecencyTracker.State next) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 100, T0);

        Assert.False(inCombat.HasValue);
        Assert.Equal("insufficient_history", inCombat.Reason);
        Assert.Equal(100, next.PreviousHp);
        Assert.Null(next.LastDamageAtUtc);
    }

    [Fact]
    public void SecondPoll_NoHpDrop_ReportsNotInCombat()
    {
        (_, CombatRecencyTracker.State afterFirst) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 100, T0);

        (WorldFact<bool> inCombat, _) =
            CombatRecencyTracker.Update(afterFirst, currentHp: 100, T0.AddSeconds(1));

        Assert.True(inCombat.HasValue);
        Assert.False(inCombat.Value);
        Assert.Equal("no_recent_hp_loss", inCombat.Reason);
    }

    [Fact]
    public void HpDrop_ReportsInCombat()
    {
        (_, CombatRecencyTracker.State afterFirst) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 100, T0);

        (WorldFact<bool> inCombat, CombatRecencyTracker.State afterHit) =
            CombatRecencyTracker.Update(afterFirst, currentHp: 60, T0.AddSeconds(1));

        Assert.True(inCombat.HasValue);
        Assert.True(inCombat.Value);
        Assert.Equal("recent_hp_loss", inCombat.Reason);
        Assert.Equal(T0.AddSeconds(1), afterHit.LastDamageAtUtc);
    }

    [Fact]
    public void HpRise_IsNotTreatedAsDamage()
    {
        (_, CombatRecencyTracker.State afterFirst) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 60, T0);

        (WorldFact<bool> inCombat, _) =
            CombatRecencyTracker.Update(afterFirst, currentHp: 100, T0.AddSeconds(1));

        Assert.True(inCombat.HasValue);
        Assert.False(inCombat.Value);
    }

    [Fact]
    public void WithinDecayWindow_StaysInCombatWithNoFurtherDrop()
    {
        (_, CombatRecencyTracker.State afterFirst) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 100, T0);
        (_, CombatRecencyTracker.State afterHit) =
            CombatRecencyTracker.Update(afterFirst, currentHp: 60, T0.AddSeconds(1));

        (WorldFact<bool> stillInCombat, _) = CombatRecencyTracker.Update(
            afterHit, currentHp: 60, T0.AddSeconds(5), TimeSpan.FromSeconds(15));

        Assert.True(stillInCombat.Value);
    }

    [Fact]
    public void PastDecayWindow_NoFurtherDrop_ReportsNotInCombat()
    {
        (_, CombatRecencyTracker.State afterFirst) =
            CombatRecencyTracker.Update(CombatRecencyTracker.State.Initial, currentHp: 100, T0);
        (_, CombatRecencyTracker.State afterHit) =
            CombatRecencyTracker.Update(afterFirst, currentHp: 60, T0.AddSeconds(1));

        (WorldFact<bool> noLongerInCombat, _) = CombatRecencyTracker.Update(
            afterHit, currentHp: 60, T0.AddSeconds(20), TimeSpan.FromSeconds(15));

        Assert.False(noLongerInCombat.Value);
        Assert.Equal("no_recent_hp_loss", noLongerInCombat.Reason);
    }
}
