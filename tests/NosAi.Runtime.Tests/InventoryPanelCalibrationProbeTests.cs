using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parsing half of <c>--calibrate-inventory-panel</c> (AP-07): turning
/// the operator's eight <c>&lt;EquipmentSlot&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
/// tokens into the one-entry-per-slot dictionary
/// <see cref="InventoryPanelRoiCalibration.Confirmed"/> requires.
/// </summary>
public sealed class InventoryPanelCalibrationProbeTests
{
    private static string Token(EquipmentSlot slot) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{slot}:0.1,0.2,0.3,0.4");

    private static string[] AllTokens()
        => Enum.GetValues<EquipmentSlot>().Select(Token).ToArray();

    [Fact]
    public void All_eight_tokens_in_declared_order_parse_into_one_entry_per_slot()
    {
        string[] tokens = AllTokens();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            tokens, out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois, out string? reason);

        Assert.True(ok);
        Assert.Null(reason);
        Assert.Equal(Enum.GetValues<EquipmentSlot>().Length, rois.Count);
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            Assert.True(rois.ContainsKey(slot));
    }

    [Fact]
    public void Slot_order_does_not_matter()
    {
        string[] tokens = Enum.GetValues<EquipmentSlot>().Reverse().Select(Token).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            tokens, out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois, out _);

        Assert.True(ok);
        Assert.Equal(Enum.GetValues<EquipmentSlot>().Length, rois.Count);
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            Assert.True(rois.ContainsKey(slot));
    }

    [Fact]
    public void Parsed_fractions_are_the_ones_the_operator_wrote()
    {
        string[] tokens =
        {
            "Weapon:0.05,0.30,0.10,0.04",
            "Shield:0.10,0.40,0.05,0.03",
            "Helmet:0.20,0.20,0.15,0.02",
            "Armor:0.30,0.60,0.04,0.08",
            "Gloves:0.40,0.25,0.20,0.05",
            "Boots:0.50,0.70,0.06,0.02",
            "Accessory1:0.60,0.35,0.09,0.09",
            "Accessory2:0.80,0.15,0.12,0.03"
        };

        Assert.True(InventoryPanelCalibrationProbe.TryParseSlots(tokens, out var rois, out _));

        Assert.Equal(new InventorySlotRoi(0.05, 0.30, 0.10, 0.04), rois[EquipmentSlot.Weapon]);
        Assert.Equal(new InventorySlotRoi(0.10, 0.40, 0.05, 0.03), rois[EquipmentSlot.Shield]);
        Assert.Equal(new InventorySlotRoi(0.20, 0.20, 0.15, 0.02), rois[EquipmentSlot.Helmet]);
        Assert.Equal(new InventorySlotRoi(0.30, 0.60, 0.04, 0.08), rois[EquipmentSlot.Armor]);
        Assert.Equal(new InventorySlotRoi(0.40, 0.25, 0.20, 0.05), rois[EquipmentSlot.Gloves]);
        Assert.Equal(new InventorySlotRoi(0.50, 0.70, 0.06, 0.02), rois[EquipmentSlot.Boots]);
        Assert.Equal(new InventorySlotRoi(0.60, 0.35, 0.09, 0.09), rois[EquipmentSlot.Accessory1]);
        Assert.Equal(new InventorySlotRoi(0.80, 0.15, 0.12, 0.03), rois[EquipmentSlot.Accessory2]);
    }

    [Fact]
    public void Parsed_tokens_are_accepted_by_Confirmed_in_any_order()
    {
        string[] tokens = Enum.GetValues<EquipmentSlot>().Reverse().Select(Token).ToArray();

        Assert.True(InventoryPanelCalibrationProbe.TryParseSlots(tokens, out var rois, out _));

        // Must not throw: the caller's own contract is that the parsing output
        // feeds Confirmed unchanged.
        InventoryPanelRoiCalibration calibration =
            InventoryPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow);
        Assert.True(calibration.IsCalibrated);
    }

    [Fact]
    public void A_missing_slot_is_refused_naming_the_reason()
    {
        // Seven tokens: Armor never appears, so no dictionary can cover all
        // eight declared slots.
        string[] tokens = Enum.GetValues<EquipmentSlot>()
            .Where(s => s != EquipmentSlot.Armor)
            .Select(Token)
            .ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("8", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_slot_is_refused_naming_the_token()
    {
        string[] tokens =
        {
            "Weapon:0.1,0.2,0.3,0.4",
            "Weapon:0.5,0.5,0.1,0.1", // duplicate: Shield missing
            "Helmet:0.1,0.2,0.3,0.4",
            "Armor:0.1,0.2,0.3,0.4",
            "Gloves:0.1,0.2,0.3,0.4",
            "Boots:0.1,0.2,0.3,0.4",
            "Accessory1:0.1,0.2,0.3,0.4",
            "Accessory2:0.1,0.2,0.3,0.4"
        };

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
        Assert.Contains("Weapon", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_fraction_is_refused_naming_the_token()
    {
        string[] tokens = AllTokens();
        tokens[3] = "Armor:0.1,0.2,banana,0.4";

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("Armor", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Armor:0.1,0.2,0.3")] // three parts
    [InlineData("Armor:0.1,0.2,0.3,0.4,0.5")] // five parts
    [InlineData("Armor:0.1;0.2;0.3;0.4")] // wrong separators
    [InlineData(":0.1,0.2,0.3,0.4")] // missing slot name
    [InlineData("Armor:")] // empty right-hand side
    public void A_token_that_is_not_four_comma_fractions_is_refused(string token)
    {
        string[] tokens = AllTokens();
        tokens[3] = token;

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void An_unknown_slot_name_is_refused_naming_it()
    {
        string[] tokens = AllTokens();
        tokens[3] = "Legs:0.1,0.2,0.3,0.4";

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("Legs", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Nine_tokens_are_refused()
    {
        string[] tokens = AllTokens().Concat(new[] { "Weapon:0.9,0.9,0.01,0.01" }).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("8", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Seven_tokens_are_refused()
    {
        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            AllTokens().Skip(1).ToArray(), out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("8", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_tokens_are_refused()
    {
        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            Array.Empty<string>(), out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Fact]
    public void A_duplicate_slot_after_the_first_is_not_silently_overwritten()
    {
        // Exactly eight tokens, Weapon twice and Shield absent: the second
        // Weapon must be refused as a duplicate, never overwrite the first.
        string[] tokens =
        {
            "Weapon:0.1,0.2,0.3,0.4",
            "Weapon:0.9,0.9,0.05,0.05",
            "Helmet:0.1,0.2,0.3,0.4",
            "Armor:0.1,0.2,0.3,0.4",
            "Gloves:0.1,0.2,0.3,0.4",
            "Boots:0.1,0.2,0.3,0.4",
            "Accessory1:0.1,0.2,0.3,0.4",
            "Accessory2:0.1,0.2,0.3,0.4"
        };

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
    }
}
