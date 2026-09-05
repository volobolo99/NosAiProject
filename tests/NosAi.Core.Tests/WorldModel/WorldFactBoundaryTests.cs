using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

/// <summary>
/// AP-01/A5 independent audit: boundary/NaN/Infinity coverage for
/// <see cref="WorldFact{T}"/> beyond A1's own <c>WorldFactTests.cs</c>.
/// Follows the AP-00/A5 audit style (docs/agents/phases/AP-00/AP-00_STATUS.md
/// S:7-9): a real defect found here is documented with an intentionally
/// failing regression test, never silently fixed (A5 owns no production
/// file in this phase) and never deleted/weakened to make the suite green.
/// </summary>
public sealed class WorldFactBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);

    // ---- Known defect: NaN confidence bypasses the documented [0,1] clamp ----

    /// <summary>
    /// KNOWN A1 DEFECT (src/NosAi.Core/WorldModel/WorldModelClassification.cs,
    /// <c>WorldFact&lt;T&gt;.ClampConfidence</c>): <c>Math.Clamp(double.NaN, 0, 1)</c>
    /// returns <c>NaN</c> unchanged -- confirmed against the real .NET
    /// runtime (a standalone probe run during this audit), not assumed.
    /// <c>Math.Clamp</c>'s implementation only ever substitutes
    /// <c>min</c>/<c>max</c> when <c>value &lt; min</c> / <c>value &gt;
    /// max</c>, and both comparisons are always false for NaN, so the
    /// original NaN falls through untouched.
    ///
    /// Every existing case in
    /// <c>WorldFactTests.Confidence_IsAlwaysClampedToZeroOneRange</c> only
    /// tries -1 / 0 / 0.5 / 1 / 2.5, never NaN, so this gap was never
    /// exercised before this audit. The XML doc on <see cref="WorldFact{T}"/>
    /// itself promises "a continuous confidence score ... in [0, 1]"
    /// (docs/agents/phases/AP-01/AP-01_STATUS.md S:2 also states this is
    /// "clampata nelle factory"); a NaN confidence breaks that promise and
    /// is not itself caught by <see cref="WorldFact{T}.HasValue"/> (the
    /// fact still reports <c>HasValue == true</c>), so a NaN confidence
    /// silently reaches every downstream consumer as if it were an
    /// ordinary classified fact. See
    /// <c>FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedFirst_WronglyOutlastsAStrictlyBetterLaterCandidate_KnownA2RobustnessGap</c>
    /// in NosAi.Runtime.Tests for a concrete downstream consequence: this
    /// same gap lets a NaN confidence defeat <c>FactFusion</c>'s own
    /// documented deterministic tie-break order.
    ///
    /// Left for A6/A1 to fix -- e.g. by treating a NaN input the same as
    /// constructing <see cref="WorldFact{T}.Unknown"/>, or by clamping via
    /// <c>double.IsNaN(confidence) ? 0d : Math.Clamp(confidence, 0d, 1d)</c>.
    /// INTENTIONALLY FAILING until fixed -- do not delete/weaken/skip.
    /// </summary>
    [Fact]
    public void Confidence_NaNInput_IsNotSanitizedIntoTheDocumentedZeroOneRange_KnownA1RobustnessGap()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, double.NaN, Now);

        Assert.False(double.IsNaN(fact.Confidence),
            "WorldFact<T>'s own XML doc promises Confidence is always clamped into [0,1]; " +
            "Math.Clamp(double.NaN, 0, 1) returns NaN unchanged, so a NaN confidence input " +
            "silently survives into the fact instead of being rejected/clamped/zeroed.");
    }

    /// <summary>
    /// Contrast case for the defect above: unlike NaN,
    /// <see cref="double.PositiveInfinity"/> and
    /// <see cref="double.NegativeInfinity"/> ARE correctly clamped by
    /// <c>Math.Clamp</c> (ordinary relational comparisons with infinity
    /// behave normally -- only NaN comparisons are always false). Confirms
    /// the gap above is specifically about NaN, not "any non-finite double".
    /// </summary>
    [Theory]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void Confidence_InfiniteInput_IsStillCorrectlyClamped_UnlikeNaN(double input, double expected)
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, input, Now);
        Assert.Equal(expected, fact.Confidence);
    }

    /// <summary>
    /// Rules out a plausible-looking but false hypothesis this audit
    /// checked before writing anything down as a finding: does a NaN
    /// inside a <see cref="WorldFact{T}"/> also silently break the "two
    /// snapshots built from the same inputs compare equal" replay
    /// determinism guarantee, since <c>NaN != NaN</c> under the <c>==</c>
    /// operator? Confirmed against the real .NET runtime: no. The
    /// compiler-generated record equality for <see cref="WorldFact{T}"/>
    /// (and every record composing it) compares each field through
    /// <see cref="EqualityComparer{T}.Default"/>, which for
    /// <see cref="double"/> calls the <see cref="IEquatable{T}"/>
    /// <c>double.Equals(double)</c> overload -- and that method, unlike the
    /// <c>==</c> operator, documents that two NaN values compare equal to
    /// each other (also confirmed by hash code: two NaNs hash identically).
    /// So the NaN-confidence gap above is a real [0,1]-clamp defect, but it
    /// does NOT additionally corrupt record equality/replay determinism the
    /// way a naive reading of "NaN != NaN" would otherwise suggest.
    /// </summary>
    [Fact]
    public void TwoFactsWithANaNConfidence_BuiltFromTheSameInputs_AreStillEqual_NaNDoesNotBreakRecordEquality()
    {
        WorldFact<int> a = WorldFact<int>.Live(1, double.NaN, Now);
        WorldFact<int> b = WorldFact<int>.Live(1, double.NaN, Now);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ---- IsFresh boundary: zero and negative maxAge ------------------------

    [Fact]
    public void IsFresh_WithZeroMaxAge_IsTrueOnlyAtTheExactSameInstant()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now);

        Assert.True(fact.IsFresh(TimeSpan.Zero, Now));
        Assert.False(fact.IsFresh(TimeSpan.Zero, Now + TimeSpan.FromTicks(1)));
    }

    /// <summary>
    /// A negative <c>maxAge</c> is never rejected by <see cref="WorldFact{T}.IsFresh"/>
    /// itself (unlike <c>TemporalBelief.DecayConfidence</c>, which
    /// explicitly throws on a non-positive window); it simply makes every
    /// fact -- even one observed at exactly <c>nowUtc</c> -- report as
    /// stale, since <c>age &lt;= negativeMaxAge</c> can only hold for an
    /// impossible non-positive age. Documented current behaviour, not a
    /// contract this type has ever promised to validate; safe (fails
    /// closed toward "not fresh"), so not treated as a defect.
    /// </summary>
    [Fact]
    public void IsFresh_WithNegativeMaxAge_TreatsEveryFactAsStale_FailsClosed()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now);

        Assert.False(fact.IsFresh(TimeSpan.FromSeconds(-1), Now));
    }

    // ---- "Unknown is not zero/false/empty" ---------------------------------

    /// <summary>
    /// AP-01/A5 command S:2, explicit example: is <c>WorldVelocity(0,0)</c> a
    /// legitimate observed "not moving" reading, or could it be confused
    /// with "no velocity estimated"? Both are represented by the exact same
    /// <see cref="WorldVelocity"/> value -- <see cref="WorldFact{T}.HasValue"/>
    /// is the only correct discriminator, never the raw <c>.Value</c>.
    /// </summary>
    [Fact]
    public void WorldVelocity_ZeroZero_IsAsLegitimateAnObservedValueAsAnyOther_OnlyHasValueTellsItApartFromUnknown()
    {
        WorldFact<WorldVelocity> stationary = WorldFact<WorldVelocity>.Derived(new WorldVelocity(0, 0), 1.0, Now);
        WorldFact<WorldVelocity> notYetDerived = WorldFact<WorldVelocity>.Unknown("not_yet_derived");

        // The raw .Value is identical in both cases -- (0,0) is
        // simultaneously "confirmed stationary" and the CLR default used
        // for an unobserved fact.
        Assert.Equal(stationary.Value, notYetDerived.Value);
        Assert.True(stationary.HasValue);
        Assert.False(notYetDerived.HasValue);
    }

    /// <summary>Same discriminator check for the canonical "false" case named by the invariant itself.</summary>
    [Fact]
    public void UnknownBoolFact_DefaultsToFalse_ButIsNeverConfusableWithAnObservedFalse_ViaHasValue()
    {
        WorldFact<bool> unknown = WorldFact<bool>.Unknown("hostility_not_observed");
        WorldFact<bool> observedFalse = WorldFact<bool>.Live(false, 1.0, Now);

        Assert.Equal(observedFalse.Value, unknown.Value); // both false
        Assert.True(observedFalse.HasValue);
        Assert.False(unknown.HasValue);
    }
}
