using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The oracle that replaces the target-frame calibration (ADR-0021). What is
/// tested here is the part that decides — the proof rule, the sentence the
/// operator acts on, and the round trip of the candidate file. The scan itself
/// needs a live client and is exercised by the bench, not here.
/// </summary>
public sealed class TargetIdFinderTests
{
    private static TargetIdHit Anchored(long offset = 0x40, long entityId = 313906)
        => new(MapIdAnchorKind.PlayerManager, offset, entityId);

    private static TargetIdHit Bare(long address = 0x1234_5678, long entityId = 313906)
        => new(MapIdAnchorKind.Heap, address, entityId);

    [Fact]
    public void AnAddressIsNotAnOffsetAndCannotBeTheAnswer()
    {
        // The distinction the map-id hunt was built on: a bare address worked
        // once, in one process. It cannot be written down.
        Assert.False(Bare().IsDurable);
        Assert.True(Anchored().IsDurable);
        Assert.False(TargetIdFinder.Proven(new[] { Bare() }, passes: 9, restarts: 9, sawCleared: true));
    }

    [Fact]
    public void WithoutAPassThatHadNoTargetNothingIsProven()
    {
        // The whole point of the cleared pass: every entry of the client's own
        // entity list holds a scene id, so a set narrowed only on
        // target-selected passes still contains the list.
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored() }, passes: 5, restarts: 2, sawCleared: false));

        Assert.True(TargetIdFinder.Proven(
            new[] { Anchored() }, passes: 5, restarts: 2, sawCleared: true));
    }

    [Fact]
    public void OneSurvivorIsRequired()
    {
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored(0x40), Anchored(0x44) }, passes: 5, restarts: 2, sawCleared: true));
    }

    [Fact]
    public void ARestartIsRequiredBecauseThatIsWhatSeparatesAnOffsetFromAnAddress()
    {
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored() }, passes: 5, restarts: 0, sawCleared: true));
    }

    [Fact]
    public void TheAdviceAsksForTheClearedPassBeforeMoreSelections()
    {
        // Order matters: more selected passes do not shrink a set that still
        // holds the entity list, so asking for them first wastes the operator's
        // time on an experiment that cannot discriminate.
        string advice = TargetIdFinder.Advice(count: 400, durable: 400, passes: 1, restarts: 0, sawCleared: false);

        Assert.Contains("TOGLI il bersaglio", advice);
    }

    [Fact]
    public void OnceClearedTheAdviceAsksForDifferentSelections()
    {
        string advice = TargetIdFinder.Advice(count: 40, durable: 40, passes: 1, restarts: 0, sawCleared: true);

        Assert.Contains("DIVERSO", advice);
    }

    [Fact]
    public void AnEmptySetSendsThemBackToAScanAndSaysWhatItWouldMean()
    {
        string advice = TargetIdFinder.Advice(count: 0, durable: 0, passes: 3, restarts: 1, sawCleared: true);

        Assert.Contains("Nessun candidato", advice);
        Assert.Contains("32 bit", advice);
    }

    [Fact]
    public void TheCandidateFileSurvivesARoundTrip()
    {
        var candidates = new TargetIdCandidates(
            Passes: 3,
            Restarts: 1,
            SawCleared: true,
            ProcessId: 7932,
            Hits: new[] { Anchored(0x40, 313906), Bare(0xDEAD, 3205) });

        string text = TargetIdFinder.Format(candidates);

        Assert.Contains("passes=3", text);
        Assert.Contains("cleared=1", text);
        Assert.Contains("process=7932", text);
        Assert.Contains("manager 40 313906", text);
        Assert.Contains("heap DEAD 3205", text);
    }

    [Fact]
    public void AHitLineParsesBackToTheHitThatWroteIt()
    {
        Assert.True(TargetIdFinder.TryParseHit("manager 40 313906", out TargetIdHit hit));
        Assert.Equal(MapIdAnchorKind.PlayerManager, hit.Anchor);
        Assert.Equal(0x40, hit.Offset);
        Assert.Equal(313906, hit.EntityId);
    }

    [Fact]
    public void ALineThatNamesNoAnchorIsNotAHit()
    {
        Assert.False(TargetIdFinder.TryParseHit("nonsense 40 1", out _));
        Assert.False(TargetIdFinder.TryParseHit("manager 40", out _));
    }
}
