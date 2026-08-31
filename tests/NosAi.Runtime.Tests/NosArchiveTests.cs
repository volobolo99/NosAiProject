using System.Buffers.Binary;
using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Reading the NosTale client's own <c>.NOS</c> archives.
/// </summary>
/// <remarks>
/// The synthetic tests pin the parsing rules and every way the reader must refuse.
/// The <see cref="NosTaleClientFactAttribute"/> tests read the real installation,
/// because a container format is only established by the bytes the game ships.
/// </remarks>
public sealed class NosArchiveTests
{
    private readonly ITestOutputHelper _output;

    public NosArchiveTests(ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------- named format

    /// <summary>Builds a named-format archive: count, then entries.</summary>
    private static byte[] BuildNamed(params (string Name, byte[] Payload)[] entries)
    {
        var buffer = new List<byte>();
        buffer.AddRange(BitConverter.GetBytes((uint)entries.Length));
        foreach ((string name, byte[] payload) in entries)
        {
            byte[] nameBytes = Encoding.Latin1.GetBytes(name);
            buffer.AddRange(BitConverter.GetBytes(1u));                      // flag
            buffer.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            buffer.AddRange(nameBytes);
            buffer.AddRange(BitConverter.GetBytes(1u));                      // second flag
            buffer.AddRange(BitConverter.GetBytes((uint)payload.Length));
            buffer.AddRange(payload);
        }
        return buffer.ToArray();
    }

    [Fact]
    public void ANamedArchiveYieldsItsEntriesWithTheirNames()
    {
        byte[] archive = BuildNamed(
            ("conststring.dat", new byte[] { 1, 2, 3 }),
            ("second.dat", new byte[] { 9 }));

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(NosArchiveFormat.NamedEntries, result.Format);
        Assert.Equal(DataSourceKind.Live, result.Source);
        Assert.Collection(result.Entries,
            e => Assert.Equal("conststring.dat", e.Name),
            e => Assert.Equal("second.dat", e.Name));
        Assert.Equal(3, result.Entries[0].Length);
    }

    [Fact]
    public void ANamedEntryPointsAtItsOwnBytes()
    {
        // The offset must locate the payload exactly; off by a header and the
        // caller decodes the neighbouring entry without ever knowing.
        byte[] payload = { 0xAA, 0xBB, 0xCC, 0xDD };
        byte[] archive = BuildNamed(("x.dat", payload));

        NosArchiveEntry entry = NosArchive.Parse("test.NOS", archive).Entries[0];

        Assert.Equal(payload, archive.AsSpan((int)entry.Offset, entry.Length).ToArray());
    }

    [Fact]
    public void AnEntryClaimingMoreBytesThanExistIsRefused()
    {
        byte[] archive = BuildNamed(("x.dat", new byte[] { 1, 2, 3 }));
        // Inflate the declared size past the end of the file.
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(archive.Length - 7), 9999);

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.False(result.Ok);
        Assert.StartsWith("entry_exceeds_file", result.FailureReason);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void ATruncatedArchiveIsRefusedRatherThanPartlyReturned()
    {
        // Half a directory is not a directory. Returning the entries read so far
        // would present an incomplete archive as a complete one.
        byte[] archive = BuildNamed(("a.dat", new byte[] { 1 }), ("b.dat", new byte[] { 2 }));

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive.AsSpan(0, archive.Length - 6));

        Assert.False(result.Ok);
        Assert.Empty(result.Entries);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void AnImplausibleEntryCountIsRefusedBeforeAnythingIsAllocated()
    {
        var archive = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, uint.MaxValue);

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.False(result.Ok);
        Assert.StartsWith("entry_count_implausible", result.FailureReason);
    }

    // ------------------------------------------------------- numbered format

    /// <summary>Builds a numbered-format archive: header, index of (id, offset), payloads.</summary>
    private static byte[] BuildNumbered(params (int Id, byte[] Payload)[] entries)
    {
        const int headerSize = 21;
        int indexEnd = headerSize + entries.Length * 8;

        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("NT Data 02"));
        buffer.AddRange(new byte[] { 0, 0 });
        buffer.AddRange(BitConverter.GetBytes(0x20040715u));
        buffer.AddRange(BitConverter.GetBytes((uint)entries.Length));
        buffer.Add(0);

        long at = indexEnd;
        foreach ((int id, byte[] payload) in entries)
        {
            buffer.AddRange(BitConverter.GetBytes((uint)id));
            buffer.AddRange(BitConverter.GetBytes((uint)at));
            at += payload.Length;
        }
        foreach ((_, byte[] payload) in entries)
            buffer.AddRange(payload);

        return buffer.ToArray();
    }

    [Fact]
    public void ANumberedArchiveYieldsItsEntriesWithTheGamesOwnIds()
    {
        // The id is the game's identifier, not a position: NSmpData01 starts at
        // 1024 because those are map ids. Renumbering them would destroy the very
        // thing the archive is being read for.
        byte[] archive = BuildNumbered((1024, new byte[] { 1, 2 }), (2006, new byte[] { 3 }));

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(NosArchiveFormat.NumberedIndex, result.Format);
        Assert.Equal("NT Data 02", result.Magic);
        Assert.Equal(new int?[] { 1024, 2006 }, result.Entries.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void TheLastNumberedEntryRunsToTheEndOfTheFile()
    {
        // Only the start of each payload is recorded, so the final length is the
        // distance to EOF. Getting this wrong silently truncates the last entry.
        byte[] archive = BuildNumbered((1, new byte[] { 1, 2 }), (2, new byte[] { 3, 4, 5, 6 }));

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.Equal(2, result.Entries[0].Length);
        Assert.Equal(4, result.Entries[1].Length);
        Assert.Equal(archive.Length, result.Entries[1].Offset + result.Entries[1].Length);
    }

    [Fact]
    public void AnIndexThatDoesNotEndWhereThePayloadsBeginIsRefused()
    {
        // This is the invariant that establishes the layout at all. If it does not
        // hold, the bytes are not what this reader believes and nothing is returned.
        byte[] archive = BuildNumbered((1, new byte[] { 1, 2 }));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(25), 999);

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.False(result.Ok);
        Assert.StartsWith("first_payload_not_after_index", result.FailureReason);
    }

    [Fact]
    public void DescendingOffsetsAreRefused()
    {
        byte[] archive = BuildNumbered((1, new byte[] { 1, 2 }), (2, new byte[] { 3, 4 }));
        // Push the second payload before the first.
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(33), 21);

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.False(result.Ok);
        Assert.StartsWith("offsets_not_ascending", result.FailureReason);
    }

    [Fact]
    public void AnIndexLargerThanTheFileIsRefused()
    {
        byte[] archive = BuildNumbered((1, new byte[] { 1 }));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(16), 100_000);

        NosArchiveResult result = NosArchive.Parse("test.NOS", archive);

        Assert.False(result.Ok);
        Assert.StartsWith("index_exceeds_file", result.FailureReason);
    }

    // ------------------------------------------------------------- refusals

    [Fact]
    public void AMissingFileIsReportedNotThrown()
    {
        NosArchiveResult result = NosArchive.Open(Path.Combine(Path.GetTempPath(), "nosai-absent-4f1c.NOS"));

        Assert.False(result.Ok);
        Assert.Equal("file_not_found", result.FailureReason);
        Assert.Equal(DataSourceKind.Unknown, result.Source);
    }

    [Fact]
    public void AnEmptyFileIsNotAnEmptyArchive()
    {
        // "Nothing in it" and "cannot read it" are different answers, and only one
        // of them means the game has no data of that kind.
        NosArchiveResult result = NosArchive.Parse("test.NOS", Array.Empty<byte>());

        Assert.False(result.Ok);
        Assert.StartsWith("file_too_short", result.FailureReason);
        Assert.Equal(DataSourceKind.Unknown, result.Source);
    }

    // -------------------------------------------- the real client's archives

    /// <summary>
    /// The banner of the one container layout this reader does not yet handle.
    /// </summary>
    /// <remarks>
    /// Two archives of a stock installation open with <c>CCINF V1.20</c> and carry a
    /// different structure. They are named here so the gap is a stated one: a reader
    /// that quietly skipped them would report a complete read of an incomplete set.
    /// </remarks>
    private const string NotYetSupportedMagic = "CCINF";

    [NosTaleClientFact]
    public void EveryArchiveOfTheRealInstallationIsReadOrNamedAsUnsupported()
    {
        // Real-environment verification: the layouts were derived from these bytes,
        // so these bytes are what must confirm them.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        string[] archives = Directory.GetFiles(directory, "*.NOS");

        var unexpected = new List<string>();
        var unsupported = new List<string>();
        int named = 0, numbered = 0, entries = 0;

        foreach (string path in archives)
        {
            NosArchiveResult result = NosArchive.Open(path);
            if (!result.Ok)
            {
                string label = $"{Path.GetFileName(path)} [{result.Magic ?? "-"}]: {result.FailureReason}";
                if (result.Magic?.StartsWith(NotYetSupportedMagic, StringComparison.Ordinal) == true)
                    unsupported.Add(label);
                else
                    unexpected.Add(label);
                continue;
            }
            entries += result.Entries.Count;
            if (result.Format == NosArchiveFormat.NamedEntries)
                named++;
            else
                numbered++;
        }

        Evidence.Live(_output, "archivi", archives.Length, directory);
        Evidence.Live(_output, "formatoConNomi", named);
        Evidence.Live(_output, "formatoNumerato", numbered);
        Evidence.Live(_output, "vociTotali", entries);
        Evidence.Unknown(_output, "formatoNonSupportato",
            $"{unsupported.Count} archivi CCINF V1.20: struttura diversa, non ancora letta");
        foreach (string failure in unexpected.Take(5))
            Evidence.Unknown(_output, "archivioNonLetto", failure);

        Assert.NotEmpty(archives);
        Assert.Empty(unexpected);
        Assert.Equal(archives.Length, named + numbered + unsupported.Count);
    }

    [NosTaleClientFact]
    public void AnEntryOfTheRealInstallationCanBeReadBack()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        string path = Path.Combine(directory, "NScliData_IT.NOS");
        Assert.True(File.Exists(path), $"atteso {path}");

        NosArchiveResult archive = NosArchive.Open(path);
        Assert.True(archive.Ok, archive.FailureReason);

        NosArchiveEntry entry = archive.Entries[0];
        MemoryReadOutcome payload = NosArchive.ReadEntry(path, entry);

        Evidence.Live(_output, "voce", entry.Describe());
        Evidence.Live(_output, "byteLetti", payload.Bytes.Length);
        Evidence.Live(_output, "byteDichiarati", entry.Length);

        Assert.True(payload.Ok, payload.FailureReason);
        Assert.Equal(entry.Length, payload.Bytes.Length);
    }

    [NosTaleClientFact]
    public void TheEntriesAndTheTrailerAccountForTheWholeArchive()
    {
        // The strongest check available without decoding: the entries plus the
        // trailer must account for every byte. The 12-byte trailer was not assumed
        // -- this test failed until it was measured, and it is exactly 12 on all 18
        // named archives of this installation.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        string path = Path.Combine(directory, "NScliData_IT.NOS");

        NosArchiveResult archive = NosArchive.Open(path);
        Assert.True(archive.Ok, archive.FailureReason);

        NosArchiveEntry last = archive.Entries[^1];
        long accountedTo = last.Offset + last.Length;

        Evidence.Live(_output, "dimensioneFile", archive.FileLength);
        Evidence.Live(_output, "fineUltimaVoce", accountedTo);
        Evidence.Live(_output, "trailer", archive.TrailerLength);
        Evidence.Live(_output, "voci", archive.Entries.Count);

        Assert.Equal(12, archive.TrailerLength);
        Assert.Equal(archive.FileLength, accountedTo + archive.TrailerLength);
    }

    [NosTaleClientFact]
    public void EveryNamedArchiveEndsWithTheSameTrailerLength()
    {
        // One file could be coincidence. Every one of them is a layout.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        var lengths = new List<long>();

        foreach (string path in Directory.GetFiles(directory, "*.NOS"))
        {
            NosArchiveResult archive = NosArchive.Open(path);
            if (archive.Ok && archive.Format == NosArchiveFormat.NamedEntries)
                lengths.Add(archive.TrailerLength);
        }

        Evidence.Live(_output, "archiviConNomi", lengths.Count);
        Evidence.Live(_output, "lunghezzeTrailerDistinte", string.Join(",", lengths.Distinct()));

        Assert.NotEmpty(lengths);
        Assert.All(lengths, length => Assert.Equal(12, length));
    }
}
