using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A2A4: <see cref="ClickTargetCommand"/> -- the argument boundary and the
/// operator-facing refusals. Everything past <c>RunWindows</c> attaches to a
/// running client and is untested by design, exactly like
/// <c>ScoutCommand.RunWindows</c>: a unit test must not be one elevation and one
/// attached client away from actuating.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class ClickTargetCommandTests
{
    // ------------------------------------------------------------- pure parsing

    [Fact]
    public void AnEntityIdAndArmInput_ParseCleanly()
    {
        string? refusal = ClickTargetCommand.TryParse(
            new[] { ClickTargetCommand.Flag, "313816", ClickTargetCommand.ArmInputOption },
            out long? entityId, out int? vnum, out bool armInput);

        Assert.Null(refusal);
        Assert.Equal(313816L, entityId);
        Assert.Null(vnum);
        Assert.True(armInput);
    }

    [Fact]
    public void AVnumAndArmInput_ParseCleanly()
    {
        string? refusal = ClickTargetCommand.TryParse(
            new[] { ClickTargetCommand.Flag, ClickTargetCommand.VnumOption, "45", ClickTargetCommand.ArmInputOption },
            out long? entityId, out int? vnum, out bool armInput);

        Assert.Null(refusal);
        Assert.Null(entityId);
        Assert.Equal(45, vnum);
        Assert.True(armInput);
    }

    [Theory]
    [InlineData("--click-target", "click_target_requires_entity_id_or_vnum")]
    [InlineData("--click-target|--vnum", "invalid_vnum")]
    [InlineData("--click-target|--vnum|abc", "invalid_vnum")]
    [InlineData("--click-target|abc", "invalid_entity_id")]
    [InlineData("--click-target|313816|--vnum|45", "click_target_ambiguous_target")]
    [InlineData("--click-target|313816|999999", "click_target_ambiguous_target")]
    [InlineData("--click-target|--bogus", "unknown_option:--bogus")]
    public void MalformedArguments_RefuseWithTheNamedReason(string joined, string expectedReason)
    {
        string[] args = joined.Split('|');
        string? refusal = ClickTargetCommand.TryParse(args, out _, out _, out _);

        Assert.Equal(expectedReason, refusal);
    }

    // ------------------------------------------------------------- operator refusals

    private static (int ExitCode, string Output) RunCaptured(string[] args)
    {
        TextWriter original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            int exit = ClickTargetCommand.Run(args);
            return (exit, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void WithoutArguments_RefusesWithTheUsageReason()
    {
        (int exit, string output) = RunCaptured(new[] { ClickTargetCommand.Flag });

        Assert.Equal(ClickTargetCommand.ExitUsage, exit);
        Assert.Contains(ClickTargetCommand.MissingTargetReason, output, StringComparison.Ordinal);
    }

    [Fact]
    public void WithAnUnknownOption_RefusesNamingThatOption()
    {
        (int exit, string output) = RunCaptured(new[] { ClickTargetCommand.Flag, "--bogus" });

        Assert.Equal(ClickTargetCommand.ExitUsage, exit);
        Assert.Contains($"{ClickTargetCommand.UnknownOptionReason}:--bogus", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutArmInput_RefusesWithTheArmReason_BeforeAttachingToAnything()
    {
        (int exit, string output) = RunCaptured(
            new[] { ClickTargetCommand.Flag, "313816" });

        Assert.Equal(ClickTargetCommand.ExitRefused, exit);
        Assert.Contains(ClickTargetCommand.InputNotArmedReason, output, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the three wire outcomes are distinct

    [Fact]
    public void TheThreeVerificationOutcomes_AreDistinctNamedStates()
    {
        Assert.NotEqual(TargetSelectionOutcome.Confirmed, TargetSelectionOutcome.DifferentTarget);
        Assert.NotEqual(TargetSelectionOutcome.DifferentTarget, TargetSelectionOutcome.NotConfirmed);
        Assert.NotEqual(TargetSelectionOutcome.Confirmed, TargetSelectionOutcome.NotConfirmed);
        Assert.NotEqual(TargetSelectionOutcome.NotAttempted, TargetSelectionOutcome.NotConfirmed);
    }

    // ------------------------------------------------------------- wiring

    [Fact]
    public void TheRuntimeWiresTheClickTargetFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(ClickTargetCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("ClickTargetCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + ClickTargetCommand.Flag + "\"", program, StringComparison.Ordinal);
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
