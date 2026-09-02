using System.Globalization;
using NosAi.Runtime.Navigation;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The oracle that replaces the target-frame calibration (ADR-0021), after the scene
/// list stopped being usable on the live build.
/// </summary>
/// <remarks>
/// <para>
/// What is tested here is the part that decides: the behavioural rules, the plausibility
/// bound, the proof rule, the sentence the operator acts on, and the round trip of the
/// candidate file. The snapshot itself needs a live client and is exercised by the bench.
/// </para>
/// <para>
/// The rules take a reader delegate precisely so that the discrimination can be shown
/// against a memory that this test writes — a selection field, a counter, and a frozen
/// id, all behaving as they would in the client.
/// </para>
/// </remarks>
public sealed class TargetIdFinderTests
{
    private readonly ITestOutputHelper _output;

    public TargetIdFinderTests(ITestOutputHelper output) => _output = output;

    /// <summary>The character's own id, as the player object reports it on the measured build.</summary>
    private const long PlayerId = 3_443_217;

    private static TargetIdHit Anchored(long offset = 0x40, long entityId = 313906, long nobody = 0)
        => new(MapIdAnchorKind.PlayerManager, offset, entityId, nobody);

    private static TargetIdHit Bare(long address = 0x1234_5678, long entityId = 313906, long nobody = 0)
        => new(MapIdAnchorKind.Heap, address, entityId, nobody);

    // -------------------------------------------------------- the plausibility bound

    /// <summary>
    /// Anchored on a measurement, and deliberately generous: its only job is to keep the
    /// survivor list workable, and being too tight would lose the answer.
    /// </summary>
    [Fact]
    public void PlausibilityIsMeasuredFromTheCharactersOwnId()
    {
        Assert.True(TargetIdFinder.IsPlausibleEntityId(313_906, PlayerId));
        Assert.True(TargetIdFinder.IsPlausibleEntityId(PlayerId, PlayerId));
        Assert.True(TargetIdFinder.IsPlausibleEntityId(
            PlayerId * TargetIdFinder.PlausibleIdCeilingFactor, PlayerId));

        // Above the ceiling: a pointer, a tick count, a bit pattern — not an id from the
        // scheme that produced the one we measured.
        Assert.False(TargetIdFinder.IsPlausibleEntityId(
            (PlayerId * TargetIdFinder.PlausibleIdCeilingFactor) + 1, PlayerId));
        Assert.False(TargetIdFinder.IsPlausibleEntityId(0xFFFFFFFF, PlayerId));
        Assert.False(TargetIdFinder.IsPlausibleEntityId(0, PlayerId));
        Assert.False(TargetIdFinder.IsPlausibleEntityId(-1, PlayerId));
    }

    /// <summary>Without a measured id there is no defensible bound, so nothing is plausible.</summary>
    [Fact]
    public void WithNoMeasuredIdNothingPasses()
    {
        Assert.False(TargetIdFinder.IsPlausibleEntityId(313_906, playerEntityId: 0));
    }

    // ------------------------------------------------------------ the selection round

    private static Func<TargetIdHit, long?> Reads(params (long Offset, long? Value)[] values)
    {
        var map = new Dictionary<long, long?>();
        foreach ((long offset, long? value) in values)
            map[offset] = value;

        return hit => map.TryGetValue(hit.Offset, out long? value) ? value : null;
    }

    [Fact]
    public void ASelectionKeepsOnlyWordsThatMovedToANewPlausibleId()
    {
        var previous = new[]
        {
            Anchored(0x10, entityId: 100, nobody: 0),   // moves to a new id: kept
            Anchored(0x20, entityId: 100, nobody: 0),   // frozen on the old id
            Anchored(0x30, entityId: 100, nobody: 0),   // goes to the sentinel
            Anchored(0x40, entityId: 100, nobody: 0),   // moves to something implausible
            Anchored(0x50, entityId: 100, nobody: 0),   // unreadable
        };

        List<TargetIdHit> kept = TargetIdFinder.NarrowOnSelection(
            previous,
            Reads((0x10, 200), (0x20, 100), (0x30, 0), (0x40, 0xFFFFFFFF), (0x50, null)),
            PlayerId,
            sentinelKnown: true);

        Assert.Single(kept);
        Assert.Equal(0x10, kept[0].Offset);
        Assert.Equal(200, kept[0].EntityId);
    }

    /// <summary>
    /// Before any clearing there is no sentinel to compare against, so the rule that
    /// rejects "went back to nobody" cannot be applied — and is not.
    /// </summary>
    [Fact]
    public void BeforeTheFirstClearingTheSentinelIsNotUsed()
    {
        List<TargetIdHit> kept = TargetIdFinder.NarrowOnSelection(
            new[] { Anchored(0x10, entityId: 100, nobody: 0) },
            Reads((0x10, 200)),
            PlayerId,
            sentinelKnown: false);

        Assert.Single(kept);
    }

    // -------------------------------------------------------------- the cleared round

    /// <summary>
    /// The first clearing records and proves nothing on its own: any word that changed
    /// passes it. That is exactly why a second one is required.
    /// </summary>
    [Fact]
    public void TheFirstClearingRecordsTheSentinelItFinds()
    {
        List<TargetIdHit> kept = TargetIdFinder.NarrowOnCleared(
            new[] { Anchored(0x10, entityId: 200), Anchored(0x20, entityId: 200) },
            Reads((0x10, -1), (0x20, 200)),
            sentinelKnown: false);

        Assert.Single(kept);
        Assert.Equal(0x10, kept[0].Offset);
        Assert.Equal(-1, kept[0].NobodyValue);
    }

    /// <summary>The sentinel is whatever the client uses, so it is not filtered for plausibility.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0xFFFFFFFF)]
    public void AnySentinelValueIsAccepted(long sentinel)
    {
        List<TargetIdHit> kept = TargetIdFinder.NarrowOnCleared(
            new[] { Anchored(0x10, entityId: 200) },
            Reads((0x10, sentinel)),
            sentinelKnown: false);

        Assert.Single(kept);
        Assert.Equal(sentinel, kept[0].NobodyValue);
    }

    /// <summary>
    /// The rule that carries the whole oracle: a second clearing requires the SAME value
    /// back. A counter changes every time and can never satisfy it.
    /// </summary>
    [Fact]
    public void TheSecondClearingRequiresTheSameValueBack()
    {
        var previous = new[]
        {
            Anchored(0x10, entityId: 200, nobody: -1),  // returns to -1: kept
            Anchored(0x20, entityId: 200, nobody: -1),  // a counter: some other value
            Anchored(0x30, entityId: 200, nobody: -1),  // did not leave the selected id
        };

        List<TargetIdHit> kept = TargetIdFinder.NarrowOnCleared(
            previous,
            Reads((0x10, -1), (0x20, 4_211_909), (0x30, 200)),
            sentinelKnown: true);

        Assert.Single(kept);
        Assert.Equal(0x10, kept[0].Offset);
    }

    // ------------------------------------------------- the oracle, against a fake client

    /// <summary>
    /// Four words that behave like the four things actually in a client's memory, driven
    /// through the rounds the operator performs. Exactly one survives, and it is the one
    /// that tracks the selection.
    /// </summary>
    [Fact]
    public void OnlyTheSelectionSurvivesAWholeRound()
    {
        const long Selection = 0x10;
        const long Counter = 0x20;
        const long FrozenId = 0x30;
        const long Position = 0x40;

        long counter = 1_000;
        long position = 500;
        long selection = 0;

        Func<TargetIdHit, long?> memory = hit => hit.Offset switch
        {
            Selection => selection,
            Counter => ++counter,
            // A remembered id: it held the first target and never moved again.
            FrozenId => 111_111,
            Position => position += 3,
            _ => null,
        };

        // Round 1 selected 111_111; round 2 cleared. That is the state the first
        // narrowing would have produced against a snapshot.
        selection = -1;
        var candidates = new List<TargetIdHit>
        {
            Anchored(Selection, entityId: 111_111),
            Anchored(Counter, entityId: 111_111),
            Anchored(FrozenId, entityId: 111_111),
            Anchored(Position, entityId: 111_111),
        };

        candidates = TargetIdFinder.NarrowOnCleared(candidates, memory, sentinelKnown: false);
        _output.WriteLine($"dopo la prima deselezione: {candidates.Count}");

        // The frozen id never leaves 111_111, so it is already gone.
        Assert.DoesNotContain(candidates, c => c.Offset == FrozenId);

        selection = 222_222;
        candidates = TargetIdFinder.NarrowOnSelection(candidates, memory, PlayerId, sentinelKnown: true);
        _output.WriteLine($"dopo la seconda selezione: {candidates.Count}");

        selection = -1;
        candidates = TargetIdFinder.NarrowOnCleared(candidates, memory, sentinelKnown: true);
        _output.WriteLine($"dopo la seconda deselezione: {candidates.Count}");

        // The counter and the position keep changing but never come back to -1.
        Assert.Single(candidates);
        Assert.Equal(Selection, candidates[0].Offset);
        Assert.Equal(-1, candidates[0].NobodyValue);
    }

    // ------------------------------------------------------------------- the proof rule

    [Fact]
    public void AnAddressIsNotAnOffsetAndCannotBeTheAnswer()
    {
        Assert.False(Bare().IsDurable);
        Assert.True(Anchored().IsDurable);
        Assert.False(TargetIdFinder.Proven(new[] { Bare() }, selections: 9, restarts: 9, sawCleared: true));
    }

    [Fact]
    public void WithoutAClearingNothingIsProven()
    {
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored() }, selections: 5, restarts: 2, sawCleared: false));

        Assert.True(TargetIdFinder.Proven(
            new[] { Anchored() }, selections: 5, restarts: 2, sawCleared: true));
    }

    [Fact]
    public void OneSurvivorIsRequired()
    {
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored(0x40), Anchored(0x44) }, selections: 5, restarts: 2, sawCleared: true));
    }

    [Fact]
    public void ARestartIsRequiredBecauseThatIsWhatSeparatesAnOffsetFromAnAddress()
    {
        Assert.False(TargetIdFinder.Proven(
            new[] { Anchored() }, selections: 5, restarts: 0, sawCleared: true));
    }

    // ---------------------------------------------------------------------- the advice

    [Fact]
    public void WithNoClearingTheAdviceAsksForTheRoundToReachOne()
    {
        string advice = TargetIdFinder.Advice(count: 400, durable: 400, selections: 1, restarts: 0, sawCleared: false);

        Assert.Contains("TORNARE", advice);
    }

    [Fact]
    public void OnceClearedTheAdviceAsksForDifferentSelections()
    {
        string advice = TargetIdFinder.Advice(count: 40, durable: 40, selections: 1, restarts: 0, sawCleared: true);

        Assert.Contains("selezioni diverse", advice);
    }

    /// <summary>
    /// An empty set is not a fault, and the sentence says so: it means no word behaved
    /// like the selection, which is a result.
    /// </summary>
    [Fact]
    public void AnEmptySetIsExplainedRatherThanReportedAsAFailure()
    {
        string advice = TargetIdFinder.Advice(count: 0, durable: 0, selections: 3, restarts: 1, sawCleared: true);

        Assert.Contains("Nessun candidato", advice);
        Assert.Contains("Non e' un guasto", advice);
    }

    [Fact]
    public void TheRestartAdviceSaysTheRoundResumesFromTheSurvivors()
    {
        string advice = TargetIdFinder.Advice(count: 1, durable: 1, selections: 3, restarts: 0, sawCleared: true);

        Assert.Contains("Chiudi NosTale", advice);
        Assert.Contains("superstiti", advice);
    }

    // ------------------------------------------------------------------- the file

    [Fact]
    public void TheCandidateFileSurvivesARoundTrip()
    {
        var candidates = new TargetIdCandidates(
            Selections: 3,
            Restarts: 1,
            SawCleared: true,
            ProcessId: 7932,
            Hits: new[] { Anchored(0x40, 313906, nobody: -1), Bare(0xDEAD, 3205, nobody: 0) });

        string text = TargetIdFinder.Format(candidates);

        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"version={TargetIdFinder.FormatVersion}"), text);
        Assert.Contains("selections=3", text);
        Assert.Contains("cleared=1", text);
        Assert.Contains("process=7932", text);
        Assert.Contains("manager 40 313906 -1", text);
        Assert.Contains("heap DEAD 3205 0", text);
    }

    [Fact]
    public void AHitLineParsesBackToTheHitThatWroteIt()
    {
        Assert.True(TargetIdFinder.TryParseHit("manager 40 313906 -1", out TargetIdHit hit));
        Assert.Equal(MapIdAnchorKind.PlayerManager, hit.Anchor);
        Assert.Equal(0x40, hit.Offset);
        Assert.Equal(313906, hit.EntityId);
        Assert.Equal(-1, hit.NobodyValue);
    }

    /// <summary>
    /// A row from the scene-list oracle has no sentinel and was chosen by a rule this code
    /// no longer applies. It is refused rather than migrated: two proofs mixed into one
    /// set produce survivors nobody can describe.
    /// </summary>
    [Fact]
    public void ARowFromTheOldOracleIsNotAHit()
    {
        Assert.False(TargetIdFinder.TryParseHit("manager 40 313906", out _));
        Assert.False(TargetIdFinder.TryParseHit("nonsense 40 1 0", out _));
    }

    [Fact]
    public void AFileFromTheOldOracleIsRefusedWithItsReason()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-target-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "# old\npasses=3\nrestarts=1\nprocess=1\ncleared=1\nmanager 40 313906\n");

        try
        {
            Assert.Null(TargetIdFinder.TryLoad(path, out string? note));
            Assert.NotNull(note);
            Assert.Contains("versione 1", note);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileThisOracleWroteReadsBack()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-target-" + Guid.NewGuid().ToString("N") + ".txt");
        var candidates = new TargetIdCandidates(2, 1, true, 4321, new[] { Anchored(0x40, 313906, -1) });
        File.WriteAllText(path, TargetIdFinder.Format(candidates));

        try
        {
            TargetIdCandidates? loaded = TargetIdFinder.TryLoad(path, out string? note);

            Assert.Null(note);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Selections);
            Assert.Equal(1, loaded.Restarts);
            Assert.True(loaded.SawCleared);
            Assert.Equal(4321, loaded.ProcessId);
            Assert.Equal(-1, Assert.Single(loaded.Hits).NobodyValue);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
