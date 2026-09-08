using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--monster-report</c>: the reference catalogue's monster fields next to what
/// the wire observed, with each field marked confirmed or provisional.
/// </summary>
public sealed class MonsterReportCommandTests
{
    private static GameReferenceDatabase WithMonster(int vnum, params (string Name, string[] Values)[] rows)
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var fields = rows.Select(r => new NosField(r.Name, r.Values)).ToList();
        db.Import("monster", "NSgtdData.NOS", "monster.dat", "C:/test", [new NosRecord(vnum, fields)],
            Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid()}"));
        return db;
    }

    [Fact]
    public void WriteReport_marks_each_field_confirmed_or_provisional()
    {
        using GameReferenceDatabase db = WithMonster(45,
            ("VNUM", ["45"]),
            ("NAME", ["zts45e"]),
            ("LEVEL", ["8"]),
            ("HP/MP", ["310", "60"]));
        var output = new StringWriter();

        MonsterReportCommand.WriteReport(db, new[] { 45 }, output);

        string text = output.ToString();
        Assert.Contains("monster 45", text, StringComparison.Ordinal);
        Assert.Contains("level", text, StringComparison.Ordinal);
        Assert.Contains("[CONFERMATO]", text, StringComparison.Ordinal);
        Assert.Contains("max_hp_bonus", text, StringComparison.Ordinal);
        Assert.Contains("max_mp_bonus", text, StringComparison.Ordinal);
        Assert.Contains("[PROVVISORIO]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_prints_a_reason_for_a_missing_vnum()
    {
        using GameReferenceDatabase db = WithMonster(45, ("VNUM", ["45"]));
        var output = new StringWriter();

        MonsterReportCommand.WriteReport(db, new[] { 999999 }, output);

        Assert.Contains(MonsterCatalogue.MonsterNotInCatalogueReason, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--monster-report --summary", MonsterReportCommand.UnknownOptionReason)]
    [InlineData("--monster-report --vnum abc", MonsterReportCommand.InvalidVnumReason)]
    [InlineData("--monster-report --recording", MonsterReportCommand.RecordingWithoutValueReason)]
    // The value is asserted, not the constant name: SkillReportCommand declares a
    // same-named constant with a different value, and RefusalReasonRegisterTests
    // treats a name match as coverage — naming it here would mark that one covered
    // too. See MonsterCatalogueRefusalTests for the same rule.
    [InlineData("--monster-report", "monster_report_no_target")]
    public void TryParse_rejects_malformed_arguments(string argsText, string expectedReason)
    {
        string[] args = argsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = MonsterReportCommand.TryParse(args, out _, out _, out _);

        Assert.NotNull(refusal);
        Assert.StartsWith(expectedReason, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_accepts_a_vnum_and_a_recording()
    {
        string[] args = "--monster-report --vnum 45".Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = MonsterReportCommand.TryParse(args, out int? vnum, out string? recording, out _);

        Assert.Null(refusal);
        Assert.Equal(45, vnum);
        Assert.Null(recording);
    }

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void ObservedMonsters_returns_the_monster_vnums_a_capture_names()
    {
        string path = RecordedCaptureFactAttribute.Resolve("nostale_combat.noscap")!;

        IReadOnlyList<int> observed = MonsterReportCommand.ObservedMonsters(path);

        Assert.Equal(new[] { 45, 36, 9, 96 }, observed.ToArray());
    }

    [Fact]
    public void The_catalogue_unavailable_reason_is_named_and_not_an_empty_catalogue()
    {
        // The command refuses before opening anything when the volume is absent.
        // Its reason is a named token, distinct from "no monsters in the catalogue".
        Assert.False(string.IsNullOrWhiteSpace(MonsterReportCommand.CatalogueUnavailableReason));
        Assert.NotEqual(MonsterCatalogue.MonsterNotInCatalogueReason, MonsterReportCommand.CatalogueUnavailableReason);
    }

    [Fact]
    public void The_runtime_wires_the_monster_report_flag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains("MonsterReportCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--monster-report\"", program, StringComparison.Ordinal);
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
