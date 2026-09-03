using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Finding HP and MP by what the wire says they are, rather than by what a block
/// of memory looks like.
/// </summary>
/// <remarks>
/// The numbers here are the ones a real session produced: maxHp 7305, hp falling
/// 7060 → 6891, maxMp 1420. The scan for a shape found fourteen candidates on
/// that client and not one of them carried 7305, which is what sent this the
/// other way round.
/// </remarks>
public sealed class PlayerVitalsCalibratorTests
{
    private const uint MaxHp = 7305;
    private const uint HpFirst = 7060;
    private const uint HpSecond = 6891;

    private static readonly IntPtr Real = new(0x1F7AEC78);
    private static readonly IntPtr Decoy = new(0x0E22E170);
    private static readonly IntPtr Unreadable = new(0x00DEAD00);

    /// <summary>A word-addressable memory this test writes, with holes.</summary>
    private static Func<IntPtr, uint?> Memory(params (IntPtr At, uint Value)[] words)
    {
        var map = new Dictionary<long, uint>();
        foreach ((IntPtr at, uint value) in words)
            map[at.ToInt64()] = value;

        return address => map.TryGetValue(address.ToInt64(), out uint value) ? value : null;
    }

    // ---------- one round narrows

    [Fact]
    public void AnAddressHoldingTheMaximumWithTheCurrentBesideItIsKept()
    {
        Func<IntPtr, uint?> memory = Memory(
            (Real, MaxHp),
            (Real + 4, HpFirst));

        List<VitalsPairHit> kept = PlayerVitalsCalibrator.KeepAdjacent(
            new[] { Real }, memory, MaxHp, HpFirst);

        VitalsPairHit hit = Assert.Single(kept);
        Assert.Equal(Real, hit.Address);
        Assert.Equal(MaxHp, hit.Max);
        Assert.Equal(HpFirst, hit.Current);
    }

    [Fact]
    public void AnAddressHoldingTheMaximumWithSomethingElseBesideItIsDropped()
    {
        // The maximum alone is a common number. The pair is what carries meaning.
        Func<IntPtr, uint?> memory = Memory(
            (Real, MaxHp),
            (Real + 4, 42));

        Assert.Empty(PlayerVitalsCalibrator.KeepAdjacent(new[] { Real }, memory, MaxHp, HpFirst));
    }

    [Fact]
    public void AnUnreadableWordIsDroppedAndNotReadAsZero()
    {
        // Unreadable and zero are different answers, and only one is a number.
        // Treating the hole as 0 would admit an address nobody can read.
        Func<IntPtr, uint?> memory = Memory((Unreadable, MaxHp));

        Assert.Empty(PlayerVitalsCalibrator.KeepAdjacent(
            new[] { Unreadable }, memory, MaxHp, 0));
    }

    // ---------- the second round is the proof

    [Fact]
    public void ACoincidenceThatDoesNotFollowTheNewValueIsDropped()
    {
        // Both addresses hold 7305/7060 in the first round; only one is health.
        Func<IntPtr, uint?> first = Memory(
            (Real, MaxHp), (Real + 4, HpFirst),
            (Decoy, MaxHp), (Decoy + 4, HpFirst));

        List<VitalsPairHit> round1 = PlayerVitalsCalibrator.KeepAdjacent(
            new[] { Real, Decoy }, first, MaxHp, HpFirst);
        Assert.Equal(2, round1.Count);

        // The character takes a hit. Health follows the wire; the decoy is frozen.
        Func<IntPtr, uint?> second = Memory(
            (Real, MaxHp), (Real + 4, HpSecond),
            (Decoy, MaxHp), (Decoy + 4, HpFirst));

        List<VitalsPairHit> round2 = PlayerVitalsCalibrator.Confirm(
            round1, second, MaxHp, HpSecond);

        VitalsPairHit survivor = Assert.Single(round2);
        Assert.Equal(Real, survivor.Address);
        Assert.Equal(HpSecond, survivor.Current);
        Assert.Null(PlayerVitalsCalibrator.Verdict(round2, "hp"));
    }

    [Fact]
    public void AMaximumThatMovedDropsTheAddressToo()
    {
        // A maximum that changes while the current does is a pointer that moved,
        // not a level-up: both words are re-checked, not just the current.
        Func<IntPtr, uint?> before = Memory((Real, MaxHp), (Real + 4, HpFirst));
        List<VitalsPairHit> round1 = PlayerVitalsCalibrator.KeepAdjacent(
            new[] { Real }, before, MaxHp, HpFirst);

        Func<IntPtr, uint?> after = Memory((Real, 9999), (Real + 4, HpSecond));

        Assert.Empty(PlayerVitalsCalibrator.Confirm(round1, after, MaxHp, HpSecond));
    }

    // ---------- when a round proves nothing, it says so

    [Fact]
    public void AValueThatDidNotMoveCannotConfirmAnything()
    {
        Assert.False(PlayerVitalsCalibrator.CanConfirm(HpFirst, HpFirst));
        Assert.True(PlayerVitalsCalibrator.CanConfirm(HpFirst, HpSecond));
    }

    [Fact]
    public void AnUnchangedRoundIsNamedWithTheFieldAndTheValue()
    {
        string? why = PlayerVitalsCalibrator.UnchangedReason(HpFirst, HpFirst, "hp");

        Assert.NotNull(why);
        Assert.StartsWith(PlayerVitalsCalibrator.UnchangedPrefix, why, StringComparison.Ordinal);
        Assert.Contains("hp", why, StringComparison.Ordinal);
        Assert.Contains("7060", why, StringComparison.Ordinal);
        Assert.Null(PlayerVitalsCalibrator.UnchangedReason(HpFirst, HpSecond, "hp"));
    }

    // ---------- verdicts

    [Fact]
    public void NoSurvivorIsNamedRatherThanReportedAsSuccess()
    {
        string? why = PlayerVitalsCalibrator.Verdict(Array.Empty<VitalsPairHit>(), "mp");

        Assert.NotNull(why);
        Assert.StartsWith(PlayerVitalsCalibrator.NoCandidatePrefix, why, StringComparison.Ordinal);
        Assert.Contains("mp", why, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralSurvivorsAreAmbiguousAndTheCountIsShown()
    {
        var survivors = new[]
        {
            new VitalsPairHit(Real, MaxHp, HpSecond),
            new VitalsPairHit(Decoy, MaxHp, HpSecond),
        };

        string? why = PlayerVitalsCalibrator.Verdict(survivors, "hp");

        Assert.NotNull(why);
        Assert.StartsWith(PlayerVitalsCalibrator.AmbiguousPrefix, why, StringComparison.Ordinal);
        Assert.Contains("2", why, StringComparison.Ordinal);
    }

    [Fact]
    public void AHitDescribesItselfAsCurrentOverMaximum()
    {
        Assert.Equal("0x1F7AEC78  6891/7305", new VitalsPairHit(Real, MaxHp, HpSecond).Describe());
    }
}
