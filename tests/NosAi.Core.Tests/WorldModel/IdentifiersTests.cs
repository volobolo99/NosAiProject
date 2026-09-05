using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class IdentifiersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EntityId_RejectsNullOrWhitespace(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new EntityId(value!));
    }

    [Fact]
    public void EntityId_ToString_ReturnsTheValue()
    {
        var id = new EntityId("player-1");
        Assert.Equal("player-1", id.ToString());
    }

    [Fact]
    public void EntityId_SameValue_AreEqual()
    {
        Assert.Equal(new EntityId("x"), new EntityId("x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MapId_RejectsNullOrWhitespace(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MapId(value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EveryOtherIdType_RejectsNullOrEmpty(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PortalId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new ItemId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new SkillId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new StatusEffectId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new QuestId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new ActionId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new GoalId(value!));
    }
}
