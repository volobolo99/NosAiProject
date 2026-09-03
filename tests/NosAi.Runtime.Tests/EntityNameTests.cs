using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Phase 1 of the memory-layout extension: a name is a candidate with a
/// predicate, never LIVE, and the wire column is a second source the operator
/// can see disagree.
/// </summary>
public sealed class EntityNameTests
{
    [Fact]
    public void APrintableTerminatedStringIsACandidateAndIsUnknown()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("Fox\0extra");

        Assert.True(EntityNameText.TryParseAnsi(bytes, out string? name, out string? why), why);
        Assert.Equal("Fox", name);

        EntityNameCandidate candidate = EntityNameCandidate.Candidate(name!);
        Assert.True(candidate.HasValue);
        Assert.Equal(DataSourceKind.Unknown, candidate.Source);
        Assert.Equal(EntityNameCandidate.NotEstablishedReason, candidate.Reason);
    }

    [Fact]
    public void AnEmptyStringIsRefused()
    {
        Assert.False(EntityNameText.TryParseAnsi(new byte[] { 0 }, out _, out string? why));
        Assert.Equal(EntityNameText.EmptyReason, why);
    }

    [Fact]
    public void AMissingTerminatorIsRefused()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("NoTerminatorAtAll");

        Assert.False(EntityNameText.TryParseAnsi(bytes, out _, out string? why));
        Assert.Equal(EntityNameText.UnterminatedReason, why);
    }

    [Fact]
    public void ANonPrintableByteIsRefusedWithTheByteNamed()
    {
        byte[] bytes = [0x46, 0x01, 0x00];

        Assert.False(EntityNameText.TryParseAnsi(bytes, out _, out string? why));
        Assert.Equal("entity_name_not_printable:0x01", why);
    }

    [Fact]
    public void AByteAboveSevenEIsRefused()
    {
        Assert.False(EntityNameText.TryParseAnsi([0xC0, 0x00], out _, out string? why));
        Assert.Equal("entity_name_not_printable:0xC0", why);
    }

    [Fact]
    public void TheParseStopsAtTheFirstTerminator()
    {
        Assert.True(EntityNameText.TryParseAnsi(Encoding.ASCII.GetBytes("A\0B\0"), out string? name, out _));
        Assert.Equal("A", name);
    }

    [Fact]
    public void OnlyMonsterAndGroundItemHaveANameChain()
    {
        Assert.True(NosTaleClientLayout.TryNameChain(MapEntityKind.Monster, out int monsterFrom, out int monsterPtr, out _));
        Assert.Equal(NosTaleClientLayout.MonsterNameObjectOffset, monsterFrom);
        Assert.Equal(NosTaleClientLayout.MonsterNamePointerOffset, monsterPtr);

        Assert.True(NosTaleClientLayout.TryNameChain(MapEntityKind.GroundItem, out int itemFrom, out int itemPtr, out _));
        Assert.Equal(NosTaleClientLayout.GroundItemNameObjectOffset, itemFrom);
        Assert.Equal(NosTaleClientLayout.GroundItemNamePointerOffset, itemPtr);

        Assert.False(NosTaleClientLayout.TryNameChain(MapEntityKind.Player, out _, out _, out string? player));
        Assert.Equal("entity_name_chain_not_established:player", player);

        Assert.False(NosTaleClientLayout.TryNameChain(MapEntityKind.Npc, out _, out _, out string? npc));
        Assert.Equal("entity_name_chain_not_established:npc", npc);
    }

    [Fact]
    public void TheMonsterAndItemChainsAreNotTheSame()
    {
        NosTaleClientLayout.TryNameChain(MapEntityKind.Monster, out int monsterFrom, out int monsterPtr, out _);
        NosTaleClientLayout.TryNameChain(MapEntityKind.GroundItem, out int itemFrom, out int itemPtr, out _);

        Assert.NotEqual(monsterFrom, itemFrom);
        Assert.NotEqual(monsterPtr, itemPtr);
    }

    [Fact]
    public void AnEnterPacketOfType3YieldsTheIdAndVnum()
    {
        Assert.True(WireEntityNameTable.TryParsePacket(
            "in 3 36 313826 109 63 2 100 100", catalog: null, out WireEntityName entry));

        Assert.Equal(313826, entry.EntityId);
        Assert.Equal(36, entry.Vnum);
        Assert.Null(entry.Name);
        Assert.Equal("in", entry.Opcode);
    }

    [Fact]
    public void AnEnterPacketOfType1YieldsTheNameField()
    {
        Assert.True(WireEntityNameTable.TryParsePacket(
            "in 1 Alice 4242 10 20 2 100 100", catalog: null, out WireEntityName entry));

        Assert.Equal(4242, entry.EntityId);
        Assert.Equal("Alice", entry.Name);
        Assert.Null(entry.Vnum);
    }

    [Fact]
    public void ADropPacketYieldsTheDropIdAndVnum()
    {
        Assert.True(WireEntityNameTable.TryParsePacket(
            "drop 2006 1092257 110 63 1 0 3443217", catalog: null, out WireEntityName entry));

        Assert.Equal(1092257, entry.EntityId);
        Assert.Equal(2006, entry.Vnum);
        Assert.Equal("drop", entry.Opcode);
    }

    [Fact]
    public void ConcordanceNamesAMatchAndAMismatchWithoutPromoting()
    {
        var memory = EntityNameCandidate.Candidate("Fox");
        var same = new WireEntityName(1, "Fox", 36, "in");
        var other = new WireEntityName(1, "Wolf", 36, "in");

        Assert.Equal("match", WireEntityNameTable.Compare(memory, same));
        Assert.Equal("MISMATCH", WireEntityNameTable.Compare(memory, other));
        Assert.Equal(DataSourceKind.Unknown, memory.Source);
    }

    [Fact]
    public void ConcordanceNamesAMissingSide()
    {
        Assert.Equal("mem-only", WireEntityNameTable.Compare(EntityNameCandidate.Candidate("Fox"), null));
        Assert.Equal("wire-only", WireEntityNameTable.Compare(
            EntityNameCandidate.Missing(EntityNameCandidate.NotEstablishedReason),
            new WireEntityName(1, "Fox", 36, "in")));
        Assert.Equal("—", WireEntityNameTable.Compare(default, null));
    }

    [Fact]
    public void TheOperatorRowPutsBothNamesOnOneLine()
    {
        var entity = new MapEntityReading(
            313826, 109, 63, EntityNameCandidate.Candidate("Fox"));
        var wire = new WireEntityName(313826, "Wolf", 36, "in");

        string row = EntityNameProbe.FormatRow(MapEntityKind.Monster, entity, wire, "MISMATCH");

        Assert.Contains("313826", row, StringComparison.Ordinal);
        Assert.Contains("Fox", row, StringComparison.Ordinal);
        Assert.Contains("Wolf", row, StringComparison.Ordinal);
        Assert.Contains("MISMATCH", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeWiresTheEntityNamesFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        string menu = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Operator", "OperatorMenu.cs"));

        Assert.Contains("EntityNameProbe.Flag", program, StringComparison.Ordinal);
        Assert.Contains("--entity-names", program, StringComparison.Ordinal);
        Assert.Contains("RunEntityNames", menu, StringComparison.Ordinal);
        Assert.Contains("Nomi delle entita", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultReadingCarriesNoNameAndIsNotLive()
    {
        var reading = new MapEntityReading(1, 2, 3);

        Assert.False(reading.Name.HasValue);
        Assert.Equal(DataSourceKind.Unknown, reading.Name.Source);
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
