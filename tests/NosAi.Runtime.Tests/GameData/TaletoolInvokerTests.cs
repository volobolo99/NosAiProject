using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class TaletoolInvokerTests
{
    [Fact]
    public void ParseScanJson_ReadsEveryField_FromAFullEntry()
    {
        const string json = """
            [
                {"file": "Item.dat", "archive_type": "text", "details": "records=4979", "error": null}
            ]
            """;

        IReadOnlyList<ClientInventoryEntry>? entries = TaletoolInvoker.ParseScanJson(json);

        Assert.NotNull(entries);
        ClientInventoryEntry entry = Assert.Single(entries!);
        Assert.Equal("Item.dat", entry.File);
        Assert.Equal("text", entry.ArchiveType);
        Assert.Equal("records=4979", entry.Details);
        Assert.Null(entry.Error);
    }

    [Fact]
    public void ParseScanJson_ReadsAnErrorEntry_WithNullArchiveTypeAndDetails()
    {
        const string json = """
            [
                {"file": "broken.NOS", "archive_type": "unsupported", "details": null, "error": "no_line_terminator"}
            ]
            """;

        IReadOnlyList<ClientInventoryEntry>? entries = TaletoolInvoker.ParseScanJson(json);

        Assert.NotNull(entries);
        ClientInventoryEntry entry = Assert.Single(entries!);
        Assert.Equal("unsupported", entry.ArchiveType);
        Assert.Null(entry.Details);
        Assert.Equal("no_line_terminator", entry.Error);
    }

    [Fact]
    public void ParseScanJson_SkipsAnEntryMissingItsFileName_RatherThanFabricatingOne()
    {
        const string json = """
            [
                {"archive_type": "text"},
                {"file": "Skill.dat", "archive_type": "text"}
            ]
            """;

        IReadOnlyList<ClientInventoryEntry>? entries = TaletoolInvoker.ParseScanJson(json);

        Assert.NotNull(entries);
        ClientInventoryEntry entry = Assert.Single(entries!);
        Assert.Equal("Skill.dat", entry.File);
    }

    [Fact]
    public void ParseScanJson_ReturnsNull_WhenTheRootIsNotAnArray()
    {
        const string json = """{"file": "Item.dat"}""";

        Assert.Null(TaletoolInvoker.ParseScanJson(json));
    }

    [Fact]
    public void ParseScanJson_ReturnsNull_OnInvalidJson()
    {
        Assert.Null(TaletoolInvoker.ParseScanJson("not json"));
    }

    [Fact]
    public void ParseScanJson_OnAnEmptyArray_ReturnsAnEmptyList_NotNull()
    {
        IReadOnlyList<ClientInventoryEntry>? entries = TaletoolInvoker.ParseScanJson("[]");

        Assert.NotNull(entries);
        Assert.Empty(entries!);
    }

    [Fact]
    public void Scan_ReportsANamedFailure_WhenTheDataDirectoryDoesNotExist()
    {
        TaletoolScanResult result = TaletoolInvoker.Scan(
            Path.Combine(Path.GetTempPath(), $"nosai-missing-{Guid.NewGuid():N}"),
            executablePath: "/does-not-matter-for-this-check");

        Assert.False(result.Ok);
        Assert.StartsWith("data_dir_not_found:", result.FailureReason);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Scan_ReportsANamedFailure_WhenTheExecutableCannotBeStarted()
    {
        string existingDirectory = Path.GetTempPath();

        TaletoolScanResult result = TaletoolInvoker.Scan(
            existingDirectory,
            executablePath: Path.Combine(existingDirectory, $"nosai-no-such-tool-{Guid.NewGuid():N}.exe"));

        Assert.False(result.Ok);
        Assert.StartsWith("process_start_failed:", result.FailureReason);
    }

    [Fact]
    public void Scan_ReportsANamedFailure_WhenNoExecutableCanBeResolved()
    {
        string? previous = Environment.GetEnvironmentVariable(TaletoolInvoker.ExecutableVariable);
        try
        {
            Environment.SetEnvironmentVariable(TaletoolInvoker.ExecutableVariable, null);

            TaletoolScanResult result = TaletoolInvoker.Scan(Path.GetTempPath());

            Assert.False(result.Ok);
            Assert.StartsWith("taletool_not_found:", result.FailureReason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TaletoolInvoker.ExecutableVariable, previous);
        }
    }
}
