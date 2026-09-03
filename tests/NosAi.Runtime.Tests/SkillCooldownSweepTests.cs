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
}
