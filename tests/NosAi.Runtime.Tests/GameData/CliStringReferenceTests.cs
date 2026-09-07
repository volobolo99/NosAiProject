using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// The client-message text archive (<c>NScliData_&lt;LANG&gt;.NOS</c>), opened
/// with the same reader that already handles <c>NSlangData</c> and <c>NSgtdData</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the archive <c>ReferenceImporter</c> did not read. Its single entry,
/// <c>conststring.dat</c>, carries the client's system-message strings indexed by
/// integer. The key decodes through <see cref="NosDataTable.ReadNumberedText"/>;
/// what it does <b>not</b> do is index the wire's <c>sayi</c> id — that link stays
/// open (T-16, <c>docs/PROTOCOLLO_NOSTALE.md</c> § <c>sayi</c>), and no test here
/// asserts it.
/// </para>
/// </remarks>
public sealed class CliStringReferenceTests
{
    private readonly ITestOutputHelper _output;

    public CliStringReferenceTests(ITestOutputHelper output) => _output = output;

    private static string ArchivePath()
    {
        string dir = NosTaleClientFactAttribute.ResolveDirectory()!;
        return Path.Combine(dir, "NScliData_IT.NOS");
    }

    private static Dictionary<string, string> ReadAll()
    {
        NosArchiveResult archive = NosArchive.Open(ArchivePath());
        Assert.True(archive.Ok, archive.FailureReason ?? "archive not ok");
        Assert.Single(archive.Entries);
        MemoryReadOutcome payload = NosArchive.ReadEntry(ArchivePath(), archive.Entries[0]);
        Assert.True(payload.Ok, payload.FailureReason ?? "read not ok");
        return NosDataTable.ReadNumberedText(payload.Bytes);
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
    public void Conststring_decodes_to_thousands_of_numbered_lines()
    {
        NosArchiveResult archive = NosArchive.Open(ArchivePath());
        NosArchiveEntry entry = archive.Entries[0];
        MemoryReadOutcome payload = NosArchive.ReadEntry(ArchivePath(), entry);

        Assert.True(payload.Ok, payload.FailureReason ?? "read not ok");

        NosTableResult decoded = NosDataTable.Decode(entry.Name!, payload.Bytes);

        Assert.True(decoded.Ok, decoded.FailureReason ?? "decode not ok");
        // Measured on the installed client: 7 870 lines, 7 566 distinct numeric keys.
        Assert.True(decoded.LineCount >= 7000, $"lines={decoded.LineCount}");
    }

    [NosTaleClientFact]
    public void The_numbered_text_resolves_the_collect_messages()
    {
        Dictionary<string, string> entries = ReadAll();

        // Read from the file, not expected: the key 10666 carries the collect
        // message and 3099 the short "collected" text (see the 2026-09-08 addendum).
        Assert.Equal("Hai raccolto [%s]:<NEW_TYPE><0>", entries["10666"]);
        Assert.Equal("è raccolto.", entries["3099"]);
        Assert.Equal("è ottenuto.", entries["3098"]);

        Evidence.Live(_output, "10666", entries["10666"]);
        Evidence.Live(_output, "3099", entries["3099"]);
    }

    [NosTaleClientFact]
    public void A_key_that_is_not_a_message_does_not_resolve_to_a_message()
    {
        Dictionary<string, string> entries = ReadAll();

        // 975, 654 and 2110 are the wire's message ids and they are not keys here;
        // 697 resolves to an unrelated label ("Lacrima"). The table is indexable by
        // number, and that is all it claims to be -- not a sayi id lookup.
        Assert.False(entries.ContainsKey("975"));
        Assert.False(entries.ContainsKey("654"));
        Assert.False(entries.ContainsKey("2110"));
        Assert.Equal("Lacrima", entries["697"]);

        // Exact key, not a guessed neighbour: a number beyond the table's range
        // resolves to nothing rather than to the closest real row.
        Assert.False(entries.ContainsKey("999999"));
    }

    [NosTaleClientFact]
    public void ImportLanguage_imports_conststring_as_numbered_text()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();

        LanguageImportReport report = new ReferenceImporter(directory).ImportLanguage(db, "IT");

        Assert.True(report.Ok, report.FailureReason);
        Assert.True(report.EntriesByKind.TryGetValue("conststring", out int count), "conststring non importata");
        // Measured on the installed client: thousands of numbered messages.
        Assert.True(count >= 7000, $"conststring entries={count}");
        Evidence.Live(_output, "conststring", count);
    }
}
