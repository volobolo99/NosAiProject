using NosAi.Core.WorldModel;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-01/A5 independent audit: boundary/NaN/duplicate-channel coverage for
/// <see cref="FactFusion"/> beyond A2's own <c>FactFusionTests.cs</c>.
/// Follows the AP-00/A5 audit style (docs/agents/phases/AP-00/AP-00_STATUS.md
/// S:7-9): a real defect found here is documented with an intentionally
/// failing regression test, never silently fixed (A5 owns no production
/// file in this phase) and never deleted/weakened.
/// </summary>
public sealed class FactFusionBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    /// <summary>
    /// KNOWN A2 DEFECT (src/NosAi.Runtime/WorldModel/Fusion/FactFusion.cs,
    /// <c>IsBetter</c>): a NaN confidence -- constructible today via the A1
    /// gap documented by
    /// <c>WorldFactBoundaryTests.Confidence_NaNInput_IsNotSanitizedIntoTheDocumentedZeroOneRange_KnownA1RobustnessGap</c>
    /// in NosAi.Core.Tests -- defeats every one of <c>FactFusion</c>'s own
    /// documented deterministic tie-break rules whenever it happens to be
    /// the FIRST same-rank candidate in the caller's list.
    ///
    /// Root cause: <c>IsBetter</c>'s confidence branch reads
    /// <c>if (candidate.Confidence != current.Confidence) return candidate.Confidence &gt; current.Confidence;</c>.
    /// Confirmed against the real .NET runtime during this audit: a
    /// NaN-vs-x comparison is asymmetric under IEEE-754 -- <c>NaN != x</c>
    /// is always true, but BOTH <c>x &gt; NaN</c> and <c>NaN &gt; x</c> are
    /// always false. So once a same-rank NaN-confidence candidate sits in
    /// the "current winner" slot, <c>IsBetter(anyLaterCandidate, winner)</c>
    /// takes that early-return branch and returns false regardless of the
    /// later candidate's own confidence, timestamp, or channel name -- the
    /// documented tie-break chain (confidence -&gt; timestamp -&gt; channel
    /// name) never even runs. The practical effect: "whichever same-rank
    /// candidate the caller happened to list first wins" the moment a NaN
    /// confidence is involved, silently replacing the documented
    /// determinism rule with list-order luck.
    ///
    /// See the companion (passing) test below for the order-reversed case,
    /// which proves this is specifically an ordering artifact of where the
    /// NaN candidate lands in the input list, not a consistent (if wrong)
    /// rule applied evenly.
    ///
    /// INTENTIONALLY FAILING until fixed -- do not delete/weaken/skip. A
    /// plausible fix direction (left for A6/A2, out of A5's file
    /// ownership): treat a NaN confidence as unconditionally worse than any
    /// non-NaN confidence of the same source rank (e.g. compare
    /// <c>double.IsNaN(x) ? -1d : x</c> instead of the raw confidence), or
    /// reject a NaN-confidence candidate from <c>usable</c> entirely, the
    /// same way stale/Unknown candidates already are.
    /// </summary>
    [Fact]
    public void NaNConfidenceCandidate_WhenListedFirst_WronglyOutlastsAStrictlyBetterLaterCandidate_KnownA2RobustnessGap()
    {
        var corrupted = new FusionCandidate<int>(WorldFact<int>.Live(1, double.NaN, Now), "z-channel");
        var strictlyBetter = new FusionCandidate<int>(WorldFact<int>.Live(2, 0.9, Now + TimeSpan.FromSeconds(1)), "a-channel");

        FusionOutcome<int> outcome = FactFusion.Resolve(
            new[] { corrupted, strictlyBetter }, Now + TimeSpan.FromSeconds(1), MaxAge);

        Assert.Equal(2, outcome.Result.Value);
    }

    /// <summary>
    /// Companion to the defect above: the SAME two candidates, only with
    /// the NaN-confidence one listed SECOND instead of first, correctly
    /// resolve to the strictly-better candidate. This is what proves the
    /// defect above is an ordering artifact -- <c>FactFusion</c>'s own XML
    /// doc promises "two runs over the same candidates always agree"
    /// regardless of list order, and this pair of tests shows that promise
    /// does not hold once a NaN confidence is present.
    /// </summary>
    [Fact]
    public void NaNConfidenceCandidate_WhenListedSecond_CorrectlyLoses_ProvingTheDefectIsAnOrderingArtifact()
    {
        var corrupted = new FusionCandidate<int>(WorldFact<int>.Live(1, double.NaN, Now), "z-channel");
        var strictlyBetter = new FusionCandidate<int>(WorldFact<int>.Live(2, 0.9, Now + TimeSpan.FromSeconds(1)), "a-channel");

        FusionOutcome<int> outcome = FactFusion.Resolve(
            new[] { strictlyBetter, corrupted }, Now + TimeSpan.FromSeconds(1), MaxAge);

        Assert.Equal(2, outcome.Result.Value);
    }

    /// <summary>
    /// Duplicate-channel coverage requested by the AP-01/A5 command
    /// ("comportamento di FactFusion.Resolve con candidati duplicati dello
    /// stesso canale"). Not a defect: when two candidates are tied on every
    /// documented criterion including channel name, resolution still
    /// deterministically falls through to "whichever the caller listed
    /// first" -- itself a fixed, reproducible rule given a fixed input
    /// list, just one <c>FactFusion</c>'s own XML doc does not spell out by
    /// name. Documented here so it cannot silently change.
    ///
    /// Also pins down a minor, non-blocking diagnostic oddity noticed while
    /// writing this test: the resulting
    /// <see cref="FusionOutcome{T}.DisagreementDetail"/> names the very
    /// same channel as both the winner and the "disagreeing" loser, which
    /// could read as nonsensical to a human/log reader even though the
    /// underlying resolution itself is correct.
    /// </summary>
    [Fact]
    public void DuplicateChannelName_WithConflictingValues_ResolvesToTheFirstListedCandidate_WithASelfReferencingDisagreementDetail()
    {
        var firstReading = new FusionCandidate<int>(WorldFact<int>.Live(1, 0.8, Now), "network");
        var secondReadingSameChannel = new FusionCandidate<int>(WorldFact<int>.Live(2, 0.8, Now), "network");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { firstReading, secondReadingSameChannel }, Now, MaxAge);

        Assert.Equal(1, outcome.Result.Value);
        Assert.True(outcome.Disagreement);
        Assert.Equal("'network' won over disagreeing channel(s): network", outcome.DisagreementDetail);
    }
}
