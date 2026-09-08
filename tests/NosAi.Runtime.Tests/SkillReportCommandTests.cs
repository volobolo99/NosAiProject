using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--skill-report</c>: the reference catalogue's fields next to what the wire
/// observed, with each field marked confirmed or provisional.
/// </summary>
public sealed class SkillReportCommandTests
{
    private static GameReferenceDatabase Imported()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);
        Assert.True(importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "skill")).Ok);
        Assert.True(importer.ImportLanguage(db, "IT").Ok);
        return db;
    }

    [NosTaleClientFact]
    public void WriteReport_prints_the_fields_and_marks_each_confirmed_or_provisional()
    {
        using GameReferenceDatabase db = Imported();
        var output = new StringWriter();

        SkillReportCommand.WriteReport(db, new[] { 226 }, output);

        string text = output.ToString();
        Assert.Contains("skill 226", text, StringComparison.Ordinal);
        Assert.Contains("cast_id", text, StringComparison.Ordinal);
        Assert.Contains("cooldown_tenths", text, StringComparison.Ordinal);
        Assert.Contains("area_targets", text, StringComparison.Ordinal);
        Assert.Contains("[CONFERMATO]", text, StringComparison.Ordinal);
        Assert.Contains("cp_cost", text, StringComparison.Ordinal);
        Assert.Contains("mp_cost", text, StringComparison.Ordinal);
        Assert.Contains("[PROVVISORIO]", text, StringComparison.Ordinal);
    }

    [NosTaleClientFact]
    public void WriteReport_prints_a_reason_for_a_missing_vnum()
    {
        using GameReferenceDatabase db = Imported();
        var output = new StringWriter();

        SkillReportCommand.WriteReport(db, new[] { 999999 }, output);

        Assert.Contains(SkillCatalogue.SkillNotInCatalogueReason, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--skill-report --summary", SkillReportCommand.UnknownOptionReason)]
    [InlineData("--skill-report --vnum abc", SkillReportCommand.InvalidVnumReason)]
    [InlineData("--skill-report --recording", SkillReportCommand.RecordingWithoutValueReason)]
    // The value is asserted, not the constant name: TargetChainProbe declares a
    // same-named constant with a different value, and RefusalReasonRegisterTests
    // treats a name match as coverage — naming it here would mark that one covered
    // too. See SkillCatalogueRefusalTests for the same rule.
    [InlineData("--skill-report", "skill_report_no_target")]
    public void TryParse_rejects_malformed_arguments(string argsText, string expectedReason)
    {
        string[] args = argsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = SkillReportCommand.TryParse(args, out _, out _, out _);

        Assert.NotNull(refusal);
        Assert.StartsWith(expectedReason, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_accepts_a_vnum_and_a_recording()
    {
        string[] args = "--skill-report --vnum 226".Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = SkillReportCommand.TryParse(args, out int? vnum, out string? recording, out _);

        Assert.Null(refusal);
        Assert.Equal(226, vnum);
        Assert.Null(recording);
    }

    [RecordedCaptureFact("messaggi.noscap")]
    public void ObservedSkills_returns_the_player_skills_a_capture_names()
    {
        string path = RecordedCaptureFactAttribute.Resolve("messaggi.noscap")!;

        IReadOnlyList<int> observed = SkillReportCommand.ObservedSkills(path);

        Assert.Equal(new[] { 200 }, observed.ToArray());
    }

    [Fact]
    public void The_catalogue_unavailable_reason_is_named_and_not_an_empty_catalogue()
    {
        // The command refuses before opening anything when the volume is absent.
        // Its reason is a named token, distinct from "no skills in the catalogue".
        Assert.False(string.IsNullOrWhiteSpace(SkillReportCommand.CatalogueUnavailableReason));
        Assert.NotEqual(SkillCatalogue.SkillNotInCatalogueReason, SkillReportCommand.CatalogueUnavailableReason);
    }

    [Fact]
    public void The_runtime_wires_the_skill_report_flag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("SkillReportCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--skill-report\"", program, StringComparison.Ordinal);
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
