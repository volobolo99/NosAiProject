using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The chain that reads HP and MP, and the measurements that authorise it.
/// </summary>
/// <remarks>
/// <para>
/// These pin numbers that were derived on a live client on 3 September 2026 and
/// verified across a client restart: the calibrator took the wire's four absolute
/// integers, found the pair in memory, confirmed it across two rounds, and asked
/// what pointed at it. The answer came back identical in two sessions while the
/// heap address it reaches moved from 0x1F52EC78 to 0x1EBCEC78.
/// </para>
/// <para>
/// Pinning them is not ceremony. The offsets are only meaningful together — the
/// distance between them was corroborated independently — so an edit that moves
/// one and not the other produces a reading that still parses and is wrong, which
/// is the failure this repository exists to refuse.
/// </para>
/// </remarks>
public sealed class PlayerVitalsChainTests
{
    /// <summary>The stride a dump showed between consecutive records.</summary>
    private const int MeasuredRecordStride = 0x78;

    // ---------- what was measured

    [Fact]
    public void HpAndMpSitOneRecordApart()
    {
        // Two different module pointers reached the same pair at the same
        // distance, and a dump had already shown a structure repeating every
        // 0x78 bytes. That agreement is why the stride is called measured.
        Assert.Equal(
            MeasuredRecordStride,
            NosTaleClientLayout.MaxMpChainOffset - NosTaleClientLayout.MaxHpChainOffset);
    }

    [Fact]
    public void TheAnchorIsTheOneDerivedHereAndNotTheThirdPartySource()
    {
        // The bot's chain starts at 0x004F4BA8 and takes four dereferences. This
        // one was found by asking what points at an address the wire had already
        // identified, and takes one. They are not the same number, and the
        // difference is the point: this one is ours to defend.
        Assert.Equal(0x51FEA4, NosTaleClientLayout.PlayerVitalsModuleOffset);
        Assert.NotEqual(0x004F4BA8, NosTaleClientLayout.PlayerVitalsModuleOffset);
    }

    [Fact]
    public void TheMaximumIsReadBeforeTheCurrentBecauseThatIsHowItSits()
    {
        // MaxHP occupies the four bytes immediately before HP: a dump showed 7305
        // at both 0x1F7AEC78 and 0x1F7AEC7C while the wire reported a maximum of
        // 7305 and the character was at full health. The chain offsets name the
        // maximum, and the current is one word further on.
        Assert.Equal(0x138, NosTaleClientLayout.MaxHpChainOffset);
        Assert.Equal(0x1B0, NosTaleClientLayout.MaxMpChainOffset);
    }

    [Fact]
    public void EveryRefusalOfTheChainHasAName()
    {
        // An unnamed refusal is indistinguishable from a value, which is the whole
        // reason the reasons are constants rather than literals at the throw site.
        Assert.Equal("player_vitals_pointer_unreadable", NosTaleClientLayout.VitalsPointerUnreadableReason);
        Assert.Equal("player_vitals_pointer_null", NosTaleClientLayout.VitalsPointerNullReason);
        Assert.Equal("player_vitals_block_unreadable", NosTaleClientLayout.VitalsBlockUnreadablePrefix);
    }

    // ---------- the reading

    [Fact]
    public void AReadingReportsBothPercentagesAndTheNumbersBehindThem()
    {
        // The numbers a real session produced, so the arithmetic is checked
        // against something that happened rather than a round example.
        var reading = new PlayerVitalsReading(Hp: 7212, MaxHp: 7305, Mp: 1295, MaxMp: 1420);

        // Rounded, not truncated: 7212/7305 is 98.7%, and the wire's own percent
        // fields round the same way, so a reading that truncated would disagree
        // with the second source by one on most values and look like drift.
        Assert.Equal(99, reading.HpPercent);
        Assert.Equal(91, reading.MpPercent);
        Assert.Equal("hp 7212/7305 (99%), mp 1295/1420 (91%)", reading.Describe());
    }

    [Fact]
    public void AFullCharacterReadsAsAHundredPercent()
    {
        var reading = new PlayerVitalsReading(Hp: 7305, MaxHp: 7305, Mp: 1420, MaxMp: 1420);

        Assert.Equal(100, reading.HpPercent);
        Assert.Equal(100, reading.MpPercent);
    }

    // ---------- the predicate that can withdraw it

    [Theory]
    [InlineData(7212u, 0u, 1295u, 1420u)]      // a maximum of zero
    [InlineData(7212u, 7305u, 1295u, 0u)]      // the other maximum
    [InlineData(9000u, 7305u, 1295u, 1420u)]   // current above its maximum
    [InlineData(7212u, 7305u, 9999u, 1420u)]   // the other current
    [InlineData(7212u, 99_000_000u, 1295u, 1420u)] // a pointer, not a vital
    public void ShapesTheChainMustNotReturnAreRefusedWithAReason(uint hp, uint maxHp, uint mp, uint maxMp)
    {
        // These are what a pointer that stopped being populated, or started naming
        // something else after a patch, looks like from here. The permanent
        // predicate runs on every read precisely so they never become a reading.
        Assert.False(PlayerVitalsBlock.TryRange(hp, maxHp, mp, maxMp, out string? why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Fact]
    public void TheShapeARealSessionProducedIsAccepted()
    {
        Assert.True(PlayerVitalsBlock.TryRange(7212, 7305, 1295, 1420, out string? why));
        Assert.Null(why);
    }
}
