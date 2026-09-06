using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parsing half of <c>--calibrate-inventory-panel</c> (AP-07): turning
/// the operator's <c>&lt;EquipmentSlot&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
/// tokens -- one per declared <see cref="EquipmentSlot"/> value -- into the
/// one-entry-per-slot dictionary <see cref="InventoryPanelRoiCalibration.Confirmed"/>
/// requires.
/// </summary>
public sealed class InventoryPanelCalibrationProbeTests
{
    private static string Token(EquipmentSlot slot) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{slot}:0.1,0.2,0.3,0.4");

    private static string[] AllTokens()
        => Enum.GetValues<EquipmentSlot>().Select(Token).ToArray();

    [Fact]
    public void All_declared_tokens_in_declared_order_parse_into_one_entry_per_slot()
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
        // Each declared slot gets its own distinct fraction (derived from its
        // position) so a mix-up between two slots' values cannot go unnoticed.
        EquipmentSlot[] declared = Enum.GetValues<EquipmentSlot>();
        string[] tokens = declared
            .Select((slot, i) => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{slot}:0.0{i + 1:00},0.1{i + 1:00},0.2{i + 1:00},0.3{i + 1:00}"))
            .ToArray();

        Assert.True(InventoryPanelCalibrationProbe.TryParseSlots(tokens, out var rois, out string? reason));
        Assert.Null(reason);

        for (int i = 0; i < declared.Length; i++)
        {
            var expected = new InventorySlotRoi(
                double.Parse($"0.0{i + 1:00}", System.Globalization.CultureInfo.InvariantCulture),
                double.Parse($"0.1{i + 1:00}", System.Globalization.CultureInfo.InvariantCulture),
                double.Parse($"0.2{i + 1:00}", System.Globalization.CultureInfo.InvariantCulture),
                double.Parse($"0.3{i + 1:00}", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(expected, rois[declared[i]]);
        }
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
        // One slot short: Armor never appears, so no dictionary can cover
        // every declared slot.
        string[] tokens = Enum.GetValues<EquipmentSlot>()
            .Where(s => s != EquipmentSlot.Armor)
            .Select(Token)
            .ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains(Enum.GetValues<EquipmentSlot>().Length.ToString(), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_slot_is_refused_naming_the_token()
    {
        // Still exactly one token per declared slot; the last declared
        // slot's token is replaced by a second "Weapon" token.
        string[] tokens = AllTokens();
        tokens[^1] = "Weapon:0.5,0.5,0.1,0.1";

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
    public void One_token_more_than_declared_is_refused()
    {
        string[] tokens = AllTokens().Concat(new[] { "Weapon:0.9,0.9,0.01,0.01" }).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains(Enum.GetValues<EquipmentSlot>().Length.ToString(), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void One_token_fewer_than_declared_is_refused()
    {
        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            AllTokens().Skip(1).ToArray(), out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains(Enum.GetValues<EquipmentSlot>().Length.ToString(), reason, StringComparison.Ordinal);
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
        // Still exactly one token per declared slot, so the count check
        // passes and the duplicate check is the one under test: the last
        // declared slot's token is replaced by a second "Weapon" token, so
        // Weapon appears twice and that last slot is consequently missing.
        // The second Weapon must be refused as a duplicate, never overwrite
        // the first.
        string[] tokens = AllTokens();
        tokens[^1] = "Weapon:0.9,0.9,0.05,0.05";

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
    }
}
