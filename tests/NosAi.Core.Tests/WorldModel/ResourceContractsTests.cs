using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class ResourceContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StandardKind_DoesNotRequireACustomName()
    {
        var hp = new Resource(ResourceKind.Health, WorldFact<double>.Live(80, 1.0, Now), WorldFact<double>.Live(100, 1.0, Now));

        Assert.Null(hp.CustomName);
        Assert.Equal(80, hp.Current.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CustomKind_WithoutAName_Throws(string? customName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Resource(
            ResourceKind.Custom,
            WorldFact<double>.Unknown("reason"),
            WorldFact<double>.Unknown("reason"),
            customName));
    }

    [Fact]
    public void CustomKind_WithAName_Succeeds()
    {
        var resource = new Resource(ResourceKind.Custom, WorldFact<double>.Live(1, 1.0, Now), WorldFact<double>.Live(1, 1.0, Now), "Combo Gauge");

        Assert.Equal("Combo Gauge", resource.CustomName);
    }

    [Fact]
    public void MissingObservation_LeavesBothBoundsExplicitlyUnknown()
    {
        var resource = new Resource(ResourceKind.Mana, WorldFact<double>.Unknown("hud_not_visible"), WorldFact<double>.Unknown("hud_not_visible"));

        Assert.False(resource.Current.HasValue);
        Assert.False(resource.Maximum.HasValue);
    }
}
