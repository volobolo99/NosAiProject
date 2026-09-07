using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// The client-message text archive (<c>NScliData_&lt;LANG&gt;.NOS</c>), opened
/// with the same reader that already handles <c>NSlangData</c> and <c>NSgtdData</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the archive <c>ReferenceImporter</c> does not read. Its single entry,
/// <c>conststring.dat</c>, carries the client's system-message strings — the
/// candidate for the text behind a <c>sayi</c> message id (see
/// <c>docs/PROTOCOLLO_NOSTALE.md</c> § <c>sayi</c> and T-16).
/// </para>
/// <para>
/// What these two tests establish is the container and its payload. They
/// deliberately do <b>not</b> assert a key-to-text resolution: the key column is a
/// binary "signed integer" that <see cref="NosDataTable.ReadKeyedText"/> does not
/// decode (it is neither tab-separated ASCII nor the 0x8N packed-nibble form
/// <see cref="NosDataTable"/> handles), so no lookup is claimed here.
/// </para>
/// </remarks>
public sealed class CliStringReferenceTests
{
    private static string ArchivePath()
    {
        string dir = NosTaleClientFactAttribute.ResolveDirectory()!;
        return Path.Combine(dir, "NScliData_IT.NOS");
    }

    [NosTaleClientFact]
    public void NscliData_IT_opens_with_one_entry()
    {
        NosArchiveResult archive = NosArchive.Open(ArchivePath());

        Assert.True(archive.Ok, archive.FailureReason ?? "archive not ok");
        Assert.Equal(NosArchiveFormat.NamedEntries, archive.Format);

        // Measured on the installed client: exactly one entry, conststring.dat.
        Assert.Single(archive.Entries);
        Assert.Equal("conststring.dat", archive.Entries[0].Name);
    }

    [NosTaleClientFact]
    public void Conststring_decodes_to_thousands_of_lines_and_carries_the_collect_text()
    {
        NosArchiveResult archive = NosArchive.Open(ArchivePath());
        NosArchiveEntry entry = archive.Entries[0];
        MemoryReadOutcome payload = NosArchive.ReadEntry(ArchivePath(), entry);

        Assert.True(payload.Ok, payload.FailureReason ?? "read not ok");

        NosTableResult decoded = NosDataTable.Decode(entry.Name!, payload.Bytes);

        Assert.True(decoded.Ok, decoded.FailureReason ?? "decode not ok");
        // Measured on the installed client: 7 870 lines.
        Assert.True(decoded.LineCount >= 7000, $"lines={decoded.LineCount}");

        // The value column is readable Italian; this is the collect-object text,
        // read from the file rather than assumed.
        Assert.Contains(decoded.Lines!, line => line.Contains("è raccolto.", StringComparison.Ordinal));
    }
}
