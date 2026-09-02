using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The default-key table is a transcription of <c>docs/TASTI_E_BERSAGLIO.md</c>
/// § 1.2, not a decision about which key does what.
/// </summary>
public sealed class NosTaleDefaultKeyCatalogTests
{
    [Fact]
    public void ProvenanceMatchesTheSourceDocument()
    {
        Assert.Equal("2026-09-02", NosTaleDefaultKeyCatalog.CollectedOn);
        Assert.Equal("docs/TASTI_E_BERSAGLIO.md § 1.2", NosTaleDefaultKeyCatalog.SourceSection);
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "LowLevel", "NosTaleDefaultKeyCatalog.cs"));
        Assert.Contains("NosTaleClientLayout", source, StringComparison.Ordinal);
        Assert.Contains("2 September 2026", source, StringComparison.Ordinal);
        Assert.Contains("NosSmooth.Local", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDocumentRowIsPresentAndNothingElse()
    {
        IReadOnlyList<DefaultKeyDeclaration> entries = NosTaleDefaultKeyCatalog.Entries;
        Assert.Equal(15, entries.Count);
        Assert.Equal(
            [
                "I", "K", "P", "O", "L", "N", "F12", "F6", "F7", "F8",
                "Spazio", "Z", "A", "Tab", "1…0, Q, W, E, R, T"
            ],
            entries.Select(e => e.Key).ToArray());
        Assert.Equal("inventario", entries[0].DeclaredEffect);
        Assert.Equal(DefaultKeyClass.Interface, entries[0].Class);
        Assert.Equal("seleziona il mostro successivo", entries[9].DeclaredEffect);
        Assert.Equal(DefaultKeyClass.Selection, entries[9].Class);
        Assert.Equal(
            "seleziona il mostro successivo e attacca con l'attacco primario",
            entries[10].DeclaredEffect);
        Assert.Equal(DefaultKeyClass.SelectionAndAct, entries[10].Class);
        Assert.Equal("slot rapidi", entries[14].DeclaredEffect);
        Assert.Equal(DefaultKeyClass.EmptyByDesign, entries[14].Class);
    }

    [Fact]
    public void QuickSlotsStayOneRow()
    {
        Assert.DoesNotContain(NosTaleDefaultKeyCatalog.Entries, e => e.Key is "1" or "Q" or "W");
        Assert.Contains(NosTaleDefaultKeyCatalog.Entries, e => e.Key == "1…0, Q, W, E, R, T");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
