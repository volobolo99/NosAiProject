using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Phase 2 of the memory-layout extension: HP/MP are a candidate found by
/// scanning a resolved base, never an RVA, never LIVE.
/// </summary>
public sealed class PlayerVitalsTests
{
    private static byte[] BlockBytes(uint hp, uint maxHp, uint mp, uint maxMp)
    {
        var bytes = new byte[PlayerVitalsBlock.Size];
        BitConverter.GetBytes(maxMp).CopyTo(bytes, PlayerVitalsBlock.MaxMpOffset);
        BitConverter.GetBytes(mp).CopyTo(bytes, PlayerVitalsBlock.MpOffset);
        BitConverter.GetBytes(maxHp).CopyTo(bytes, PlayerVitalsBlock.MaxHpOffset);
        BitConverter.GetBytes(hp).CopyTo(bytes, PlayerVitalsBlock.HpOffset);
        return bytes;
    }

    [Fact]
    public void TheIntraBlockLayoutIsTheThirdSourceShapeNotAnRva()
    {
        Assert.Equal(0x00, PlayerVitalsBlock.MaxMpOffset);
        Assert.Equal(0x04, PlayerVitalsBlock.MpOffset);
        Assert.Equal(0xF0, PlayerVitalsBlock.MaxHpOffset);
        Assert.Equal(0xF4, PlayerVitalsBlock.HpOffset);
        Assert.Equal(0xF0, PlayerVitalsBlock.PairDistance);
        Assert.Equal(PlayerVitalsBlock.PairDistance, PlayerVitalsBlock.HpOffset - PlayerVitalsBlock.MpOffset);
        Assert.Equal(0xF8, PlayerVitalsBlock.Size);
    }

    [Fact]
    public void TheStartingRvaIsNotInTheRuntime()
    {
        string root = RepositoryRoot();
        foreach (string relative in new[]
                 {
                     Path.Combine("src", "NosAi.Runtime", "LiveIntegration", "PlayerVitals.cs"),
                     Path.Combine("src", "NosAi.Runtime", "LiveIntegration", "PlayerVitalsProbe.cs"),
                     Path.Combine("src", "NosAi.Runtime", "LiveIntegration", "WirePlayerVitals.cs"),
                     Path.Combine("src", "NosAi.Runtime", "LiveIntegration", "NosTaleClientLayout.cs"),
                 })
        {
            string text = File.ReadAllText(Path.Combine(root, relative));
            Assert.DoesNotContain("004F4BA8", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0x4F4BA8", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void APlausibleBlockParsesAndStaysUnknown()
    {
        Assert.True(PlayerVitalsBlock.TryParse(
            BlockBytes(7218, 7305, 1362, 1420), out PlayerVitalsBlock block, out string? why), why);
        Assert.Equal(7218u, block.Hp);
        Assert.Equal(7305u, block.MaxHp);
        Assert.Equal(1362u, block.Mp);
        Assert.Equal(1420u, block.MaxMp);
        Assert.Equal(99, block.HpPercent);

        var candidate = PlayerVitalsCandidate.From(
            new PlayerVitalsHit(MapIdAnchorKind.PlayerObject, 0x1A0, block));
        Assert.True(candidate.HasValue);
        Assert.Equal(DataSourceKind.Unknown, candidate.Source);
        Assert.Equal(PlayerVitalsCandidate.NotEstablishedReason, candidate.Reason);
        Assert.Equal("object+0x1A0", candidate.DescribeOffset());
    }

    [Fact]
    public void AZeroMaximumIsRefused()
    {
        Assert.False(PlayerVitalsBlock.TryParse(BlockBytes(10, 0, 5, 20), out _, out string? hpWhy));
        Assert.Equal(PlayerVitalsBlock.MaxHpZeroReason, hpWhy);

        Assert.False(PlayerVitalsBlock.TryParse(BlockBytes(10, 20, 5, 0), out _, out string? mpWhy));
        Assert.Equal(PlayerVitalsBlock.MaxMpZeroReason, mpWhy);
    }

    [Fact]
    public void ACurrentAboveItsMaximumIsRefusedWithBothValues()
    {
        Assert.False(PlayerVitalsBlock.TryParse(BlockBytes(50, 40, 1, 10), out _, out string? why));
        Assert.Equal("player_vitals_hp_above_max:50>40", why);
    }

    [Fact]
    public void APointerSizedWordIsNotAVital()
    {
        Assert.False(PlayerVitalsBlock.TryParse(
            BlockBytes(10, 0x0A00_0000, 1, 10), out _, out string? why));
        Assert.Equal("player_vitals_max_hp_implausible:167772160", why);
    }

    [Fact]
    public void AShortSpanIsRefusedBeforeAnyWordIsRead()
    {
        Assert.False(PlayerVitalsBlock.TryParse(new byte[PlayerVitalsBlock.Size - 1], out _, out string? why));
        Assert.Equal(PlayerVitalsBlock.TruncatedReason, why);
    }

    [Fact]
    public void TheScanFindsABlockAsADistanceFromTheBase()
    {
        int offset = 0x40;
        var window = new byte[(int)MapIdAnchors.StructWindow];
        BlockBytes(7288, 7305, 1420, 1420).CopyTo(window, offset);

        var hits = new List<PlayerVitalsHit>();
        PlayerVitalsScan.Collect(window, MapIdAnchorKind.PlayerManager, hits);

        Assert.Contains(hits, h => h.Offset == offset && h.Block.Hp == 7288);
        // A start four bytes later still parses: it reads MP as MaxMP and HP as
        // MaxHP, with zeros for the currents. Uniqueness is not concordance —
        // that ghost's maximum moves when HP does, and the oracle drops it.
        Assert.True(hits.Count > 1, "a single structural parse is not evidence the offset is the block");
    }

    [Fact]
    public void TwoPlantedBlocksLeaveTheScanAmbiguous()
    {
        var window = new byte[(int)MapIdAnchors.StructWindow];
        BlockBytes(100, 100, 50, 50).CopyTo(window, 0x20);
        BlockBytes(200, 200, 50, 50).CopyTo(window, 0x200);

        var hits = new List<PlayerVitalsHit>();
        PlayerVitalsScan.Collect(window, MapIdAnchorKind.PlayerObject, hits);

        Assert.Contains(hits, h => h.Offset == 0x20 && h.Block.Hp == 100);
        Assert.Contains(hits, h => h.Offset == 0x200 && h.Block.Hp == 200);
        Assert.True(hits.Count > 2);
    }

    [Fact]
    public void TheDamageOracleDropsTheShiftedGhost()
    {
        int offset = 0x40;
        var beforeWindow = new byte[(int)MapIdAnchors.StructWindow];
        var afterWindow = new byte[(int)MapIdAnchors.StructWindow];
        BlockBytes(7305, 7305, 1420, 1420).CopyTo(beforeWindow, offset);
        BlockBytes(7218, 7305, 1420, 1420).CopyTo(afterWindow, offset);

        var before = new List<PlayerVitalsHit>();
        var after = new List<PlayerVitalsHit>();
        PlayerVitalsScan.Collect(beforeWindow, MapIdAnchorKind.PlayerObject, before);
        PlayerVitalsScan.Collect(afterWindow, MapIdAnchorKind.PlayerObject, after);

        List<PlayerVitalsHit> survivors = PlayerVitalsOracle.Survivors(before, after);
        PlayerVitalsHit survivor = Assert.Single(survivors);
        Assert.Equal(offset, survivor.Offset);
        Assert.Equal(7218u, survivor.Block.Hp);
    }

    [Fact]
    public void ContinuityRefusesAJumpLargerThanTheMaximum()
    {
        var before = new PlayerVitalsBlock(7305, 7305, 1420, 1420);
        var after = new PlayerVitalsBlock(1, 7305, 1420, 1420);
        Assert.True(PlayerVitalsPredicate.TryContinuity(before, after, out _));

        var moved = new PlayerVitalsBlock(50, 100, 50, 50);
        var fromElsewhere = new PlayerVitalsBlock(5000, 100, 50, 50);
        Assert.False(PlayerVitalsPredicate.TryContinuity(moved, fromElsewhere, out string? why));
        Assert.Equal("player_vitals_hp_jumped:4950_over_100", why);
    }

    [Fact]
    public void TheDamageOracleKeepsOnlyHpThatFellWhileMaximaHeld()
    {
        var full = new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x40, new PlayerVitalsBlock(7305, 7305, 1420, 1420));
        var hit = new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x40, new PlayerVitalsBlock(7218, 7305, 1420, 1420));
        var other = new PlayerVitalsHit(
            MapIdAnchorKind.PlayerManager, 0x80, new PlayerVitalsBlock(100, 100, 50, 50));
        var otherSame = other with { Block = new PlayerVitalsBlock(100, 100, 50, 50) };
        var maxMoved = new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x40, new PlayerVitalsBlock(7218, 8000, 1420, 1420));

        Assert.True(PlayerVitalsOracle.TookDamage(full, hit));
        Assert.False(PlayerVitalsOracle.TookDamage(full, full));
        Assert.False(PlayerVitalsOracle.TookDamage(full, maxMoved));

        List<PlayerVitalsHit> survivors = PlayerVitalsOracle.Survivors(
            [full, other], [hit, otherSame]);
        PlayerVitalsHit survivor = Assert.Single(survivors);
        Assert.Equal(0x40, survivor.Offset);
        Assert.Equal(7218u, survivor.Block.Hp);
    }

    [Fact]
    public void AStatPacketYieldsAbsoluteVitalsAndADerivedPercent()
    {
        Assert.True(WirePlayerVitalsParser.TryParsePacket(
            "stat 7288 7305 1420 1420 0 1184", playerId: null, out WirePlayerVitals entry));

        Assert.Equal(7288, entry.Hp);
        Assert.Equal(7305, entry.MaxHp);
        Assert.Equal(1420, entry.Mp);
        Assert.Equal(1420, entry.MaxMp);
        Assert.Equal(100, entry.HpPercent);
        Assert.Equal("stat", entry.Opcode);
    }

    [Fact]
    public void AnStPacketForAnotherEntityIsIgnoredWithoutThePlayerId()
    {
        Assert.False(WirePlayerVitalsParser.TryParsePacket(
            "st 3 313816 8 0 66 100 198 52 310 52 0", playerId: null, out _));
    }

    [Fact]
    public void AnStPacketForThisCharacterUsesTheAbsoluteFieldsNotFieldFive()
    {
        Assert.True(WirePlayerVitalsParser.TryParsePacket(
            "st 1 3443217 56 0 99 100 7288 1420 7305 1420 0",
            playerId: 3443217, out WirePlayerVitals entry));

        Assert.Equal(7288, entry.Hp);
        Assert.Equal(7305, entry.MaxHp);
        Assert.Equal(100, entry.HpPercent);
        Assert.Equal("st", entry.Opcode);
    }

    [Fact]
    public void AnEnterPacketOfType1YieldsPercentsForThisCharacter()
    {
        Assert.True(WirePlayerVitalsParser.TryParsePacket(
            "in 1 Alice 3443217 10 20 2 99 100", playerId: 3443217, out WirePlayerVitals entry));

        Assert.Null(entry.Hp);
        Assert.Equal(99, entry.HpPercent);
        Assert.Equal(100, entry.MpPercent);
        Assert.Equal("in", entry.Opcode);
    }

    [Fact]
    public void ConcordanceNamesAMatchAndAMismatchWithoutPromoting()
    {
        var memory = PlayerVitalsCandidate.From(new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x40, new PlayerVitalsBlock(7288, 7305, 1420, 1420)));
        var same = new WirePlayerVitals(7288, 7305, 1420, 1420, 100, 100, "stat");
        var other = new WirePlayerVitals(100, 100, 50, 50, 100, 100, "stat");

        Assert.Equal("match", WirePlayerVitalsParser.Compare(memory, same));
        Assert.Equal("MISMATCH", WirePlayerVitalsParser.Compare(memory, other));
        Assert.Equal(DataSourceKind.Unknown, memory.Source);
    }

    [Fact]
    public void PercentsWithinOnePointMatchAndTwoDoNot()
    {
        var memory = PlayerVitalsCandidate.From(new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x40, new PlayerVitalsBlock(7218, 7305, 1420, 1420)));
        // 7218/7305 = 98.81 → 99
        Assert.Equal(99, memory.HpPercent);
        Assert.True(PlayerVitalsPredicate.TryMatchPercent(99, 100, out _));
        Assert.False(PlayerVitalsPredicate.TryMatchPercent(99, 97, out string? why));
        Assert.Equal("player_vitals_ratio_mismatch:99_not_97", why);

        var wirePercentOnly = new WirePlayerVitals(null, null, null, null, 99, 100, "in");
        Assert.Equal("match", WirePlayerVitalsParser.Compare(memory, wirePercentOnly));
    }

    [Fact]
    public void TheOperatorRowPutsBothSourcesOnOneLine()
    {
        var hit = new PlayerVitalsHit(
            MapIdAnchorKind.PlayerObject, 0x1A0, new PlayerVitalsBlock(7218, 7305, 1362, 1420));
        var wire = new WirePlayerVitals(100, 100, 50, 50, 100, 100, "stat");

        string row = PlayerVitalsProbe.FormatRow(hit, wire, "MISMATCH");

        Assert.Contains("object+0x1A0", row, StringComparison.Ordinal);
        Assert.Contains("7218", row, StringComparison.Ordinal);
        Assert.Contains("7305", row, StringComparison.Ordinal);
        Assert.Contains("100/100", row, StringComparison.Ordinal);
        Assert.Contains("MISMATCH", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeWiresThePlayerVitalsFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        string menu = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Operator", "OperatorMenu.cs"));

        Assert.Contains("PlayerVitalsProbe.Flag", program, StringComparison.Ordinal);
        Assert.Contains("--player-vitals", program, StringComparison.Ordinal);
        Assert.Contains("RunPlayerVitals", menu, StringComparison.Ordinal);
        Assert.Contains("HP e MP del personaggio", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingCandidateIsNotLiveAndNotZero()
    {
        var missing = PlayerVitalsCandidate.Missing(PlayerVitalsCandidate.NotFoundReason);
        Assert.False(missing.HasValue);
        Assert.Equal(DataSourceKind.Unknown, missing.Source);
        Assert.Equal(PlayerVitalsCandidate.NotFoundReason, missing.Reason);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
