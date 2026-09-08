using NosAi.Core.WorldModel;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-07/A2A4: <see cref="UnequipCommand"/> — the argument boundary and the
/// operator-facing refusals. Everything past <c>RunWindows</c> attaches to a
/// running client and is untested by design, exactly like
/// <c>ClickTargetCommand.RunWindows</c>: a unit test must not be one elevation
/// and one attached client away from actuating.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class UnequipCommandTests
{
    // ------------------------------------------------------------- pure parsing

    [Fact]
    public void ASlotGestureAndArmInput_ParseCleanly()
    {
        string? refusal = UnequipCommand.TryParse(
            new[] { UnequipCommand.Flag, "Necklace", "--gesture", "single", "--arm-input" },
            out EquipmentSlot? slot, out UnequipGesture? gesture, out int? verifyMs, out bool armInput);

        Assert.Null(refusal);
        Assert.Equal(EquipmentSlot.Necklace, slot);
        Assert.Equal(UnequipGesture.Single, gesture);
        Assert.Null(verifyMs);
        Assert.True(armInput);
    }

    [Fact]
    public void ACaseInsensitiveSlotName_ParsesCleanly()
    {
        string? refusal = UnequipCommand.TryParse(
            new[] { UnequipCommand.Flag, "necklace", "--gesture", "right", "--arm-input" },
            out EquipmentSlot? slot, out UnequipGesture? gesture, out _, out _);

        Assert.Null(refusal);
        Assert.Equal(EquipmentSlot.Necklace, slot);
        Assert.Equal(UnequipGesture.Right, gesture);
    }

    [Fact]
    public void AVerifyWindow_ParsesCleanly()
    {
        string? refusal = UnequipCommand.TryParse(
            new[] { UnequipCommand.Flag, "Weapon", "--gesture", "double", "--verify-ms", "250", "--arm-input" },
            out EquipmentSlot? slot, out UnequipGesture? gesture, out int? verifyMs, out _);

        Assert.Null(refusal);
        Assert.Equal(EquipmentSlot.Weapon, slot);
        Assert.Equal(UnequipGesture.Double, gesture);
        Assert.Equal(250, verifyMs);
    }

    [Theory]
    [InlineData("--unequip", "unequip_requires_slot")]
    [InlineData("--unequip|Necklace", "unequip_requires_gesture")]
    [InlineData("--unequip|Banana|--gesture|single", "unequip_invalid_slot")]
    [InlineData("--unequip|99|--gesture|single", "unequip_invalid_slot")]
    [InlineData("--unequip|Necklace|--gesture", "unequip_invalid_gesture")]
    [InlineData("--unequip|Necklace|--gesture|bogus", "unequip_invalid_gesture")]
    [InlineData("--unequip|Necklace|--gesture|single|--verify-ms", "unequip_invalid_verify_ms")]
    [InlineData("--unequip|Necklace|--gesture|single|--verify-ms|abc", "unequip_invalid_verify_ms")]
    [InlineData("--unequip|Necklace|--gesture|single|--verify-ms|0", "unequip_invalid_verify_ms")]
    [InlineData("--unequip|Necklace|Ring|--gesture|single", "unequip_ambiguous_slot")]
    [InlineData("--unequip|Necklace|--gesture|single|--bogus", "unknown_option:--bogus")]
    public void MalformedArguments_RefuseWithTheNamedReason(string joined, string expectedReason)
    {
        string[] args = joined.Split('|');
        string? refusal = UnequipCommand.TryParse(args, out _, out _, out _, out _);

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
            int exit = UnequipCommand.Run(args);
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
        (int exit, string output) = RunCaptured(new[] { UnequipCommand.Flag });

        Assert.Equal(UnequipCommand.ExitUsage, exit);
        Assert.Contains(UnequipCommand.MissingSlotReason, output, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutArmInput_RefusesWithTheArmReason_BeforeAttachingToAnything()
    {
        (int exit, string output) = RunCaptured(
            new[] { UnequipCommand.Flag, "Necklace", "--gesture", "single" });

        Assert.Equal(UnequipCommand.ExitRefused, exit);
        Assert.Contains(UnequipCommand.InputNotArmedReason, output, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the exit codes are distinct

    [Fact]
    public void TheFourExitCodes_AreDistinct()
    {
        Assert.NotEqual(UnequipCommand.ExitConfirmed, UnequipCommand.ExitStillWorn);
        Assert.NotEqual(UnequipCommand.ExitStillWorn, UnequipCommand.ExitNotConfirmed);
        Assert.NotEqual(UnequipCommand.ExitNotConfirmed, UnequipCommand.ExitRefused);
        Assert.NotEqual(UnequipCommand.ExitRefused, UnequipCommand.ExitUsage);
    }

    // ------------------------------------------------------------- wiring

    [Fact]
    public void TheRuntimeWiresTheUnequipFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(UnequipCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("UnequipCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + UnequipCommand.Flag + "\"", program, StringComparison.Ordinal);
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
