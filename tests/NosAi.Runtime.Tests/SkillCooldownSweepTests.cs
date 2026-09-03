using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The cooldown hunt over the whole process instead of two 8 KB windows.
/// </summary>
/// <remarks>
/// <para>
/// The windowed finder returned zero survivors across two valid rounds on a real
/// client: the wire announced both restorations, so the oracle had its evidence
/// and found nothing where it was looking. Phase 2 failed the same way for the
/// same reason, and was fixed by widening the search rather than the window.
/// </para>
/// <para>
/// The predicate here assumes nothing about how a cooldown is encoded. A word
/// qualifies by <i>moving when the skill is used and returning to exactly what it
/// held when ready</i> — true of a countdown, of a ready-at timestamp, and of a
/// tick counter, and false of a word that merely drifts.
/// </para>
/// </remarks>
public sealed class SkillCooldownSweepTests
{
    private static readonly IntPtr Region = new(0x1F520000);

    private static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * sizeof(uint)];
        for (var i = 0; i < values.Length; i++)
            BitConverter.GetBytes(values[i]).CopyTo(bytes, i * sizeof(uint));
        return bytes;
    }

    private static Func<IntPtr, uint?> Memory(params (IntPtr At, uint Value)[] words)
    {
        var map = new Dictionary<long, uint>();
        foreach ((IntPtr at, uint value) in words)
            map[at.ToInt64()] = value;
        return a => map.TryGetValue(a.ToInt64(), out uint v) ? v : null;
    }

    // ---------- what moved

    [Fact]
    public void OnlyTheWordsThatMovedAreCarriedForward()
    {
        var changed = new List<SweepWord>();

        SkillCooldownSweep.CollectChanged(
            Region,
            Words(0, 7, 0, 99),
            Words(0, 7, 12, 99),
            changed);

        SweepWord word = Assert.Single(changed);
        Assert.Equal(new IntPtr(Region.ToInt64() + 8), word.Address);
    }

    [Fact]
    public void TheValueCarriedIsTheRestingOneNotTheBusyOne()
    {
        // It is what the word has to come back to, and comparing against it later
        // is the entire test. Carrying the busy value would test nothing.
        var changed = new List<SweepWord>();

        SkillCooldownSweep.CollectChanged(Region, Words(0), Words(4500), changed);

        Assert.Equal(0u, Assert.Single(changed).ReadyValue);
    }

    [Fact]
    public void NothingMovingIsNoCandidates()
    {
        var changed = new List<SweepWord>();
        SkillCooldownSweep.CollectChanged(Region, Words(1, 2, 3), Words(1, 2, 3), changed);
        Assert.Empty(changed);
    }

    // ---------- what came back

    [Fact]
    public void AWordBackAtItsRestingValueSurvives()
    {
        var candidates = new[] { new SweepWord(Region, 0u) };

        List<SweepWord> kept = SkillCooldownSweep.KeepRestored(candidates, Memory((Region, 0u)));

        Assert.Single(kept);
    }

    [Fact]
    public void AWordStillMovingWhenTheWireSaysReadyIsNotACooldown()
    {
        // The announcement is the instant the skill is available. A word that has
        // not returned by then is measuring something else.
        var candidates = new[] { new SweepWord(Region, 0u) };

        Assert.Empty(SkillCooldownSweep.KeepRestored(candidates, Memory((Region, 250u))));
    }

    [Fact]
    public void AnUnreadableWordIsDroppedRatherThanReadAsZero()
    {
        var candidates = new[] { new SweepWord(Region, 0u) };

        Assert.Empty(SkillCooldownSweep.KeepRestored(candidates, Memory()));
    }

    // ---------- two rounds are the argument

    [Fact]
    public void OnlyAWordThatBehavedInBothRoundsIsKept()
    {
        // One round of "moved and came back" is not evidence: a great many words
        // in 195 MB do that. The intersection is the argument.
        var real = new IntPtr(0x1F52A100);
        var coincidence = new IntPtr(0x1F52B200);

        var first = new[] { new SweepWord(real, 0u), new SweepWord(coincidence, 0u) };
        var second = new[] { new SweepWord(real, 0u) };

        SweepWord kept = Assert.Single(SkillCooldownSweep.Intersect(first, second));
        Assert.Equal(real, kept.Address);
    }

    [Fact]
    public void AnAddressThatRestedOnADifferentNumberIsNotTheSameWord()
    {
        // Returning to a different value is passing through, not resting.
        var at = new IntPtr(0x1F52A100);
        var first = new[] { new SweepWord(at, 0u) };
        var second = new[] { new SweepWord(at, 900u) };

        Assert.Empty(SkillCooldownSweep.Intersect(first, second));
    }

    // ---------- verdicts

    [Fact]
    public void NoSurvivorIsNamedAndNotReportedAsAResult()
    {
        Assert.Equal(
            SkillCooldownSweep.NoCandidateReason,
            SkillCooldownSweep.Verdict(Array.Empty<SweepWord>()));
    }

    [Fact]
    public void SeveralSurvivorsAreAmbiguousWithTheirCount()
    {
        var many = new[]
        {
            new SweepWord(new IntPtr(0x10), 0u),
            new SweepWord(new IntPtr(0x20), 0u),
        };

        string? why = SkillCooldownSweep.Verdict(many);

        Assert.NotNull(why);
        Assert.StartsWith(SkillCooldownSweep.AmbiguousPrefix, why, StringComparison.Ordinal);
        Assert.Contains("2", why, StringComparison.Ordinal);
    }

    [Fact]
    public void OneSurvivorIsTheOnlyThingThatCounts()
    {
        Assert.Null(SkillCooldownSweep.Verdict(new[] { new SweepWord(Region, 0u) }));
    }

    // ---------- and not otherwise

    [Fact]
    public void AWordThatSitsStillWhileNothingHappensIsKept()
    {
        var candidates = new[] { new SweepWord(Region, 0u) };

        Assert.Single(SkillCooldownSweep.KeepStill(candidates, Memory((Region, 0u))));
    }

    [Fact]
    public void AWordThatMovesWhileNothingHappensIsChurnAndIsDropped()
    {
        // The half the first version was missing. "Moved when the skill was used
        // and came back" admits everything that churns: two rounds of it left 8265
        // survivors on a live client, and their values -- 490441108, 842281263 --
        // were ASCII read as integers, so the sweep was walking string buffers.
        var candidates = new[] { new SweepWord(Region, 0u) };

        Assert.Empty(SkillCooldownSweep.KeepStill(candidates, Memory((Region, 7u))));
    }

    [Fact]
    public void RepeatedSamplingIsWhatRemovesAWordThatChurnsAndReturns()
    {
        // Checked once at the end, a word that churns has had time to come back.
        // The quiet control samples repeatedly for exactly this population, so the
        // filter has to be composable across samples rather than a single verdict.
        var candidates = new List<SweepWord> { new(Region, 0u) };

        List<SweepWord> afterQuietSample = SkillCooldownSweep.KeepStill(candidates, Memory((Region, 3u)));
        List<SweepWord> afterItReturned = SkillCooldownSweep.KeepStill(afterQuietSample, Memory((Region, 0u)));

        Assert.Empty(afterItReturned);
    }

    [Fact]
    public void AnUnreadableWordDoesNotSurviveTheQuietControlEither()
    {
        var candidates = new[] { new SweepWord(Region, 0u) };

        Assert.Empty(SkillCooldownSweep.KeepStill(candidates, Memory()));
    }

    // ---------- a cooldown belongs to a table

    private static SweepWord At(long offset, uint ready = 0u) =>
        new(new IntPtr(Region.ToInt64() + offset), ready);

    [Fact]
    public void WordsSpacedAtTheSkillStrideAreKeptAsATable()
    {
        // 0x48 is the bot's own stride, where "skill n is ready" is
        // *(DWORD*)(table + (n - 1) * 0x48) == 0. The stride is borrowed; its
        // starting addresses are not, because the same source puts the vitals at
        // an RVA this client does not use.
        var table = new[]
        {
            At(0x0),
            At(SkillCooldownSweep.SkillStride),
            At(SkillCooldownSweep.SkillStride * 2),
            At(SkillCooldownSweep.SkillStride * 3),
        };

        Assert.Equal(4, SkillCooldownSweep.KeepInSkillTable(table).Count);
    }

    [Fact]
    public void AWordWithNoNeighboursAtTheStrideIsScatteredChurn()
    {
        // The population this filter exists for: string buffers move and come
        // back, but they do not line up 0x48 apart.
        var scattered = new[] { At(0x0), At(0x13), At(0x2C1), At(0x9F4) };

        Assert.Empty(SkillCooldownSweep.KeepInSkillTable(scattered));
    }

    [Fact]
    public void ATableSurvivesEvenWhenScatteredWordsSurroundIt()
    {
        var mixed = new List<SweepWord>
        {
            At(0x11), At(0x2F),
            At(0x1000), At(0x1000 + SkillCooldownSweep.SkillStride), At(0x1000 + SkillCooldownSweep.SkillStride * 2),
            At(0x9001),
        };

        List<SweepWord> kept = SkillCooldownSweep.KeepInSkillTable(mixed);

        Assert.Equal(3, kept.Count);
        Assert.All(kept, w => Assert.True(w.Address.ToInt64() >= Region.ToInt64() + 0x1000));
    }

    [Fact]
    public void AWordAtTheEndOfItsTableStillCounts()
    {
        // The bot describes separate tables for slots 1-4 and 5+, so a real entry
        // can sit at an edge with neighbours on one side only.
        var table = new[] { At(0x0), At(SkillCooldownSweep.SkillStride), At(SkillCooldownSweep.SkillStride * 2) };

        Assert.Contains(SkillCooldownSweep.KeepInSkillTable(table), w => w.Address == Region);
    }

    // ---------- the stride is measured, not borrowed

    [Fact]
    public void TheSpacingTheSurvivorsActuallyHaveIsTheOneReported()
    {
        // 0x48 is what the third-party source says and it matched nothing on this
        // client: the negative control left 114 words behaving like cooldowns and
        // not one was 0x48 from another. So the spacing is measured.
        const int Real = 0x60;
        var table = new[] { At(0x0), At(Real), At(Real * 2), At(Real * 3), At(Real * 4) };

        SkillCooldownSweep.StrideFinding found = Assert.NotNull(SkillCooldownSweep.DeriveStride(table));

        Assert.Equal(Real, found.Stride);
        Assert.Equal(5, found.Run.Count);
        Assert.NotEqual(SkillCooldownSweep.SkillStride, found.Stride);
    }

    [Fact]
    public void AddressesWithNoRepeatedDistanceAreNotATable()
    {
        // Scattered churn produces no run, and that is an answer rather than an
        // absence: it says these words are not laid out like skills.
        var scattered = new[] { At(0x0), At(0x14), At(0x2C8), At(0x9F4), At(0x1B30) };

        Assert.Null(SkillCooldownSweep.DeriveStride(scattered));
    }

    [Fact]
    public void ATableIsFoundEvenWithScatteredWordsAroundIt()
    {
        const int Real = 0x50;
        var mixed = new List<SweepWord> { At(0x7), At(0x1F3) };
        for (var i = 0; i < 6; i++)
            mixed.Add(At(0x4000 + Real * i));

        SkillCooldownSweep.StrideFinding found = Assert.NotNull(SkillCooldownSweep.DeriveStride(mixed));

        Assert.Equal(Real, found.Stride);
        Assert.Equal(6, found.Run.Count);
    }

    // ---------- the one thing both sources agree on

    [Fact]
    public void OnlyTheCandidatesRestingAtZeroAreRanked()
    {
        // The spec says a word is a candidate only if it falls to zero when the
        // skill becomes available, and the bot's own test is == 0. They contradict
        // each other about the chain and the stride; on this they do not.
        var mixed = new[]
        {
            At(0x0, ready: 0u),
            At(0x10, ready: 161024409u),   // a repeated word in a module block
            At(0x20, ready: 1065353216u),  // the float 1.0, beside MP
            At(0x30, ready: 25700u),       // 0x6464: two bytes of 100, not a number
        };

        SweepWord only = Assert.Single(SkillCooldownSweep.RestingAtZero(mixed));
        Assert.Equal(Region, only.Address);
    }

    [Fact]
    public void NothingRestingAtZeroIsAnEmptyRankingAndNotAnError()
    {
        // Applied last and only among anchored candidates, so an empty result
        // means the ranking said nothing -- the survivors stand as they were.
        var none = new[] { At(0x0, ready: 7u), At(0x10, ready: 9u) };

        Assert.Empty(SkillCooldownSweep.RestingAtZero(none));
    }

    [Fact]
    public void TwoWordsAreNotARunHoweverEvenlySpaced()
    {
        // Any two addresses are a constant distance apart. A table needs enough
        // entries that the distance means something.
        var pair = new[] { At(0x0), At(0x40) };

        Assert.Null(SkillCooldownSweep.DeriveStride(pair));
    }
}
