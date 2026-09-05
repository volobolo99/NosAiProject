using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

/// <summary>
/// AP-01/A5 independent audit note (AP-01/A5 command S:5, "Resource con
/// ResourceKind.Custom"): documents, without fixing, the exact shape of a
/// known, accepted limit -- <see cref="Resource"/> has no identity for a
/// <see cref="ResourceKind.Custom"/> pool beyond its free-text
/// <see cref="Resource.CustomName"/>. Two conceptually unrelated game
/// mechanics that both choose the same display name are indistinguishable
/// at the type level; nothing here enforces uniqueness or attaches a
/// stable opaque id the way <see cref="ResourceKind"/>'s built-in members
/// get for free by virtue of being separate enum values.
///
/// Decision recorded by this audit: not a defect requiring a fix in this
/// phase (A5 owns no production file either way). Recorded so a future
/// consumer cannot be surprised by it, and so a future attempt to add real
/// disambiguation (e.g. a stable custom-resource id, separate from the
/// display name) has a pinned "before" behaviour to compare against.
/// </summary>
public sealed class ResourceCustomKindAmbiguityTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TwoUnrelatedCustomResources_SharingOnlyAName_AreIndistinguishableWhenTheirNumbersCoincide()
    {
        // "Charge" here could mean a combo-gauge for one class and an
        // unrelated channeled-cast charge-up for another; Resource has no
        // field that would tell them apart beyond the string itself.
        var comboGaugeCharge = new Resource(ResourceKind.Custom, WorldFact<double>.Live(3, 1.0, Now), WorldFact<double>.Live(5, 1.0, Now), "Charge");
        var channelChargeUp = new Resource(ResourceKind.Custom, WorldFact<double>.Live(3, 1.0, Now), WorldFact<double>.Live(5, 1.0, Now), "Charge");

        Assert.Equal(comboGaugeCharge, channelChargeUp);
    }

    [Fact]
    public void CustomResourceEquality_IsPurelyStructural_ThereIsNoSeparateIdentityFieldToDisambiguate()
    {
        var chargeAtThree = new Resource(ResourceKind.Custom, WorldFact<double>.Live(3, 1.0, Now), WorldFact<double>.Live(5, 1.0, Now), "Charge");
        var chargeAtFour = new Resource(ResourceKind.Custom, WorldFact<double>.Live(4, 1.0, Now), WorldFact<double>.Live(5, 1.0, Now), "Charge");

        // Equality tracks only the current/maximum readings; there is no
        // way to express "this is the same Charge pool whose reading
        // changed" versus "these are two different Charge pools that
        // happen to read differently right now" -- both look identical to
        // this type beyond the coincidence (or not) of their numbers.
        Assert.NotEqual(chargeAtThree, chargeAtFour);
    }
}
