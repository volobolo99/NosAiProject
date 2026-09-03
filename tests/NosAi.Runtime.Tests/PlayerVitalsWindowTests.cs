using System.Buffers.Binary;
using NosAi.LiveIntegration;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// How far past each resolved base the stats block is looked for.
/// </summary>
/// <remarks>
/// The scan used to borrow <see cref="MapIdAnchors.StructWindow"/>, which answers
/// a different question — how far an offset may sit from an anchor and still be
/// called anchor-relative — and so capped the search at 0x1000 by coincidence.
/// A real client then reported a maximum of 7305 on the wire that appeared in no
/// candidate from either window, which is a scan looking in the wrong place.
/// These fix the bound to a number chosen for this scan.
/// </remarks>
public sealed class PlayerVitalsWindowTests
{
    // The values a real session reported, so the synthetic block is the shape
    // that was actually missed rather than an invented one.
    private const uint MaxHp = 7305;
    private const uint Hp = 3733;
    private const uint MaxMp = 1420;
    private const uint Mp = 1420;

    private static byte[] WindowWithBlockAt(int windowBytes, int blockOffset)
    {
        var window = new byte[windowBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(
            window.AsSpan(blockOffset + PlayerVitalsBlock.MaxMpOffset), MaxMp);
        BinaryPrimitives.WriteUInt32LittleEndian(
            window.AsSpan(blockOffset + PlayerVitalsBlock.MpOffset), Mp);
        BinaryPrimitives.WriteUInt32LittleEndian(
            window.AsSpan(blockOffset + PlayerVitalsBlock.MaxHpOffset), MaxHp);
        BinaryPrimitives.WriteUInt32LittleEndian(
            window.AsSpan(blockOffset + PlayerVitalsBlock.HpOffset), Hp);
        return window;
    }

    // ---------- the bound

    [Fact]
    public void AWindowSmallerThanOneBlockIsRaisedToOneBlock()
    {
        // Asking for less than a block cannot mean "read nothing": it means the
        // caller does not know the block size, and one block is the least that
        // could answer.
        Assert.Equal(PlayerVitalsBlock.Size, PlayerVitalsScan.ClampWindow(0));
        Assert.Equal(PlayerVitalsBlock.Size, PlayerVitalsScan.ClampWindow(16));
        Assert.Equal(PlayerVitalsBlock.Size, PlayerVitalsScan.ClampWindow(-4096));
    }

    [Fact]
    public void AWindowLargerThanTheCeilingIsCappedRatherThanTrusted()
    {
        // A window sizes both a read and a loop, so it is bounded before either.
        Assert.Equal(PlayerVitalsScan.MaxWindowBytes, PlayerVitalsScan.ClampWindow(int.MaxValue));
        Assert.Equal(
            PlayerVitalsScan.MaxWindowBytes,
            PlayerVitalsScan.ClampWindow(PlayerVitalsScan.MaxWindowBytes + 1));
    }

    [Fact]
    public void AWorkableWindowPassesThroughUnchanged()
    {
        Assert.Equal(0x8000, PlayerVitalsScan.ClampWindow(0x8000));
        Assert.Equal(
            PlayerVitalsScan.DefaultWindowBytes,
            PlayerVitalsScan.ClampWindow(PlayerVitalsScan.DefaultWindowBytes));
    }

    [Fact]
    public void TheScanNoLongerBorrowsTheMapIdAnchorRule()
    {
        // The decoupling is the point: these two numbers answer different
        // questions and must be free to move apart.
        Assert.True(PlayerVitalsScan.DefaultWindowBytes > MapIdAnchors.StructWindow);
        Assert.True(PlayerVitalsScan.MaxWindowBytes >= PlayerVitalsScan.DefaultWindowBytes);
    }

    // ---------- what the widened scan can now reach

    [Fact]
    public void ABlockPastTheOldFourKilobyteCapIsFound()
    {
        const int blockOffset = 0x1400;
        Assert.True(blockOffset > MapIdAnchors.StructWindow, "the point is that it is past the old cap");

        byte[] window = WindowWithBlockAt(0x2000, blockOffset);
        var hits = new List<PlayerVitalsHit>();

        PlayerVitalsScan.Collect(window, MapIdAnchorKind.PlayerManager, hits);

        PlayerVitalsHit planted = Assert.Single(hits, h => h.Offset == blockOffset);
        Assert.Equal(MaxHp, planted.Block.MaxHp);
        Assert.Equal(Hp, planted.Block.Hp);
        Assert.Equal(MaxMp, planted.Block.MaxMp);
        Assert.Equal(Mp, planted.Block.Mp);
        Assert.Equal(MapIdAnchorKind.PlayerManager, planted.Anchor);
    }

    [Fact]
    public void TheSameBlockInsideTheOldCapIsStillFound()
    {
        // Widening must not move the floor: what the narrow scan found, the wide
        // scan still finds at the same offset.
        const int blockOffset = 0x200;
        byte[] window = WindowWithBlockAt(0x2000, blockOffset);
        var hits = new List<PlayerVitalsHit>();

        PlayerVitalsScan.Collect(window, MapIdAnchorKind.PlayerObject, hits);

        PlayerVitalsHit planted = Assert.Single(hits, h => h.Offset == blockOffset);
        Assert.Equal(MaxHp, planted.Block.MaxHp);
    }

    [Fact]
    public void AZeroFilledWindowYieldsNothingHoweverWideItIs()
    {
        // A zero maximum is refused by the permanent predicate, so empty memory
        // cannot become a candidate no matter how much of it is read.
        var window = new byte[0x4000];
        var hits = new List<PlayerVitalsHit>();

        PlayerVitalsScan.Collect(window, MapIdAnchorKind.PlayerManager, hits);

        Assert.Empty(hits);
    }
}
