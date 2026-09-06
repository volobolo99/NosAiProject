using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class StatusEffectContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cooldown_UnknownRemainingDuration_IsNotActive()
    {
        var cooldown = new Cooldown(new SkillId("fireball"), WorldFact<TimeSpan>.Unknown("cooldown_not_observed"));

        Assert.False(cooldown.IsActive);
    }

    [Fact]
    public void Cooldown_KnownPositiveRemainingDuration_IsActive()
    {
        var cooldown = new Cooldown(new SkillId("fireball"), WorldFact<TimeSpan>.Live(TimeSpan.FromSeconds(3), 1.0, Now));

        Assert.True(cooldown.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Cooldown_KnownZeroOrNegativeRemainingDuration_IsNotActive(int seconds)
    {
        var cooldown = new Cooldown(new SkillId("fireball"), WorldFact<TimeSpan>.Live(TimeSpan.FromSeconds(seconds), 1.0, Now));

        Assert.False(cooldown.IsActive);
    }

    [Fact]
    public void BuffAndDebuff_ShareTheSameShape_DistinguishedOnlyByPolarity()
    {
        var buff = new StatusEffect(new StatusEffectId("s1"), StatusEffectPolarity.Buff, WorldFact<string>.Live("Haste", 1.0, Now), WorldFact<TimeSpan>.Live(TimeSpan.FromSeconds(10), 1.0, Now), WorldFact<double>.Live(1.2, 1.0, Now));
        var debuff = buff with { Id = new StatusEffectId("s2"), Polarity = StatusEffectPolarity.Debuff };

        Assert.Equal(StatusEffectPolarity.Buff, buff.Polarity);
        Assert.Equal(StatusEffectPolarity.Debuff, debuff.Polarity);
    }
}
