using NosAi.Core.WorldModel;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// C-312: <see cref="EquipCommand.TryParse"/> and the operator-facing refusals
/// of <see cref="EquipCommand.Run"/> that do not require a real client (the
/// same split <see cref="UnequipCommandTests"/> draws: parsing is pure,
/// <c>RunWindows</c> needs a real Windows session and is not unit-tested here).
/// </summary>
public sealed class EquipCommandTests
{
    [Fact]
    public void AnItemSlotGestureAndArmInput_ParseCleanly()
    {
        string? refusal = EquipCommand.TryParse(
            new[] { EquipCommand.Flag, "909", "--slot", "Necklace", "--gesture", "single", "--arm-input" },
            out ItemId? item, out EquipmentSlot? slot, out EquipGesture? gesture, out int? verifyMs, out bool armInput);

        Assert.Null(refusal);
        Assert.Equal("909", item!.Value.Value);
        Assert.Equal(EquipmentSlot.Necklace, slot);
        Assert.Equal(EquipGesture.Single, gesture);
        Assert.Null(verifyMs);
        Assert.True(armInput);
    }

    [Fact]
    public void ACaseInsensitiveSlotAndGesture_ParseCleanly()
    {
        string? refusal = EquipCommand.TryParse(
            new[] { EquipCommand.Flag, "909", "--slot", "necklace", "--gesture", "DOUBLE", "--arm-input" },
            out ItemId? item, out EquipmentSlot? slot, out EquipGesture? gesture, out _, out _);

        Assert.Null(refusal);
        Assert.Equal(EquipmentSlot.Necklace, slot);
        Assert.Equal(EquipGesture.Double, gesture);
    }

    [Fact]
    public void AVerifyWindow_ParsesCleanly()
    {
        string? refusal = EquipCommand.TryParse(
            new[] { EquipCommand.Flag, "909", "--slot", "Weapon", "--gesture", "double", "--verify-ms", "250", "--arm-input" },
            out _, out EquipmentSlot? slot, out EquipGesture? gesture, out int? verifyMs, out _);

        Assert.Null(refusal);
        Assert.Equal(EquipmentSlot.Weapon, slot);
        Assert.Equal(EquipGesture.Double, gesture);
        Assert.Equal(250, verifyMs);
    }

    [Theory]
    [InlineData("--equip", "equip_requires_item")]
    [InlineData("--equip|909", "equip_requires_slot")]
    [InlineData("--equip|909|--slot|Necklace", "equip_requires_gesture")]
    [InlineData("--equip|909|--slot|Banana|--gesture|single", "equip_invalid_slot")]
    [InlineData("--equip|909|--slot|99|--gesture|single", "equip_invalid_slot")]
    [InlineData("--equip|909|--slot|Necklace|--gesture", "equip_invalid_gesture")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|bogus", "equip_invalid_gesture")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|right", "equip_invalid_gesture")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|single|--verify-ms", "equip_invalid_verify_ms")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|single|--verify-ms|abc", "equip_invalid_verify_ms")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|single|--verify-ms|0", "equip_invalid_verify_ms")]
    [InlineData("--equip|909|1234|--slot|Necklace|--gesture|single", "equip_ambiguous_item")]
    [InlineData("--equip|abc|--slot|Necklace|--gesture|single", "equip_invalid_item")]
    [InlineData("--equip|0|--slot|Necklace|--gesture|single", "equip_invalid_item")]
    [InlineData("--equip|909|--slot|Necklace|--gesture|single|--bogus", "unknown_option:--bogus")]
    public void MalformedArguments_RefuseWithTheNamedReason(string joined, string expectedReason)
    {
        string[] args = joined.Split('|');
        string? refusal = EquipCommand.TryParse(args, out _, out _, out _, out _, out _);

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
            int exit = EquipCommand.Run(args);
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
        (int exit, string output) = RunCaptured(new[] { EquipCommand.Flag });

        Assert.Equal(EquipCommand.ExitUsage, exit);
        Assert.Contains(EquipCommand.MissingItemReason, output, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedButNotArmed_RefusesBeforeTouchingWindows()
    {
        (int exit, string output) = RunCaptured(
            new[] { EquipCommand.Flag, "909", "--slot", "Necklace", "--gesture", "single" });

        Assert.Equal(EquipCommand.ExitRefused, exit);
        Assert.Contains(EquipCommand.InputNotArmedReason, output, StringComparison.Ordinal);
    }
}
