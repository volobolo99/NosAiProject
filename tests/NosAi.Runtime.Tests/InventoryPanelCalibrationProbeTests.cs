using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parsing half of <c>--calibrate-inventory-panel</c> (AP-07): turning
/// the operator's <c>&lt;EquipmentSlot&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
/// tokens into the one-entry-per-declared-slot dictionary
/// <see cref="InventoryPanelRoiCalibration.Confirmed"/> requires.
/// </summary>
/// <remarks>
/// Every test is written against <see cref="Enum.GetValues{T}"/> of
/// <see cref="EquipmentSlot"/> rather than against a named slot list, so the
/// suite keeps meaning exactly what the declared enum means -- the enum was
/// corrected to NosTale's real eighteen values after a third-party item
/// database cross-check (see <see cref="EquipmentSlot"/>'s own remarks), and
/// no test here may reintroduce an older, smaller assumption about how many
/// slots a panel has.
/// </remarks>
public sealed class InventoryPanelCalibrationProbeTests
{
    private static EquipmentSlot[] Declared => Enum.GetValues<EquipmentSlot>();

    private static string Token(EquipmentSlot slot) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{slot}:0.1,{0.2 + 0.01 * (int)slot},0.3,0.4");

    private static string[] AllTokens()
        => Declared.Select(Token).ToArray();

    [Fact]
    public void Every_declared_slot_in_enum_order_parses_into_one_entry_per_slot()
    {
        string[] tokens = AllTokens();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            tokens, out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois, out string? reason);

        Assert.True(ok);
        Assert.Null(reason);
        Assert.Equal(Declared.Length, rois.Count);
        foreach (EquipmentSlot slot in Declared)
            Assert.True(rois.ContainsKey(slot));
    }

    [Fact]
    public void Slot_order_does_not_matter()
    {
        string[] tokens = Declared.Reverse().Select(Token).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            tokens, out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois, out _);

        Assert.True(ok);
        Assert.Equal(Declared.Length, rois.Count);
        foreach (EquipmentSlot slot in Declared)
            Assert.True(rois.ContainsKey(slot));
    }

    [Fact]
    public void Parsed_fractions_are_the_ones_the_operator_wrote()
    {
        var written = new Dictionary<EquipmentSlot, InventorySlotRoi>();
        for (int i = 0; i < Declared.Length; i++)
        {
            double x = 0.02 + 0.03 * i;
            written[Declared[i]] = new InventorySlotRoi(x, 0.3, 0.1, 0.05);
        }

        string[] tokens = Declared.Select(s =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{s}:{written[s].X},{written[s].Y},{written[s].Width},{written[s].Height}")).ToArray();

        Assert.True(InventoryPanelCalibrationProbe.TryParseSlots(tokens, out var rois, out _));

        Assert.Equal(Declared.Length, rois.Count);
        foreach (EquipmentSlot slot in Declared)
            Assert.Equal(written[slot], rois[slot]);
    }

    [Fact]
    public void Parsed_tokens_are_accepted_by_Confirmed_in_any_order()
    {
        string[] tokens = Declared.Reverse().Select(Token).ToArray();

        Assert.True(InventoryPanelCalibrationProbe.TryParseSlots(tokens, out var rois, out _));

        // Must not throw: the caller's own contract is that the parsing output
        // feeds Confirmed unchanged.
        InventoryPanelRoiCalibration calibration =
            InventoryPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow);
        Assert.True(calibration.IsCalibrated);
    }

    [Fact]
    public void A_missing_slot_is_refused_with_the_expected_count()
    {
        // One declared slot never appears: no dictionary can cover all values.
        string[] tokens = Declared.Skip(1).Select(Token).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains(Declared.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_slot_is_refused_naming_the_token()
    {
        string[] tokens = AllTokens();
        tokens[^1] = Token(Declared[0]); // last now duplicates the first

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
        Assert.Contains(Declared[0].ToString(), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_fraction_is_refused_naming_the_token()
    {
        string[] tokens = AllTokens();
        tokens[3] = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Declared[3]}:0.1,0.2,banana,0.4");

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains(Declared[3].ToString(), reason, StringComparison.Ordinal);
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
    public void One_more_token_than_declared_is_refused()
    {
        string[] tokens = AllTokens().Append(Token(Declared[0])).ToArray();

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("18", reason, StringComparison.Ordinal);
        Assert.Contains("19", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void One_fewer_token_than_declared_is_refused()
    {
        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(
            AllTokens().Skip(1).ToArray(), out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("17", reason, StringComparison.Ordinal);
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
    public void A_duplicate_slot_is_not_silently_overwritten()
    {
        // Exactly Declared.Length tokens, the first slot twice and the last
        // never: the second occurrence must be refused as a duplicate, never
        // overwrite the first.
        string[] tokens = AllTokens();
        tokens[^1] = Token(Declared[0]);

        bool ok = InventoryPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
    }
}
