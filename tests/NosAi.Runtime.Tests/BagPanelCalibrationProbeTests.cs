using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parsing half of <c>--calibrate-bag-panel</c> (C-313): turning the
/// operator's <c>&lt;slotIndex&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
/// tokens into the dictionary <see cref="BagPanelRoiCalibration.Confirmed"/>
/// requires.
/// </summary>
/// <remarks>
/// Unlike <see cref="InventoryPanelCalibrationProbeTests"/>'s fixed 18
/// <c>EquipmentSlot</c> values, the bag scrolls: <see cref="BagPanelRoiCalibration"/>
/// is never all-or-nothing, so there is no "expected exactly N tokens" case
/// to test here -- any number at least one is a valid calibration.
/// </remarks>
public sealed class BagPanelCalibrationProbeTests
{
    private static string Token(int slot, double x = 0.1, double y = 0.2, double w = 0.05, double h = 0.05) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{slot}:{x},{y},{w},{h}");

    [Fact]
    public void ASingleValidToken_ParsesIntoOneEntry()
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(
            new[] { Token(0) }, out IReadOnlyDictionary<int, InventorySlotRoi> rois, out string? reason);

        Assert.True(ok);
        Assert.Null(reason);
        Assert.Single(rois);
        Assert.True(rois.ContainsKey(0));
    }

    [Fact]
    public void SeveralTokens_InAnyOrder_AllParse()
    {
        string[] tokens = { Token(4), Token(0), Token(2) };

        bool ok = BagPanelCalibrationProbe.TryParseSlots(tokens, out IReadOnlyDictionary<int, InventorySlotRoi> rois, out _);

        Assert.True(ok);
        Assert.Equal(3, rois.Count);
        Assert.True(rois.ContainsKey(0));
        Assert.True(rois.ContainsKey(2));
        Assert.True(rois.ContainsKey(4));
    }

    [Fact]
    public void ParsedFractions_AreTheOnesTheOperatorWrote()
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(
            new[] { Token(3, x: 0.11, y: 0.22, w: 0.33, h: 0.44) }, out var rois, out _);

        Assert.True(ok);
        Assert.Equal(new InventorySlotRoi(0.11, 0.22, 0.33, 0.44), rois[3]);
    }

    [Fact]
    public void ParsedTokens_AreAcceptedByConfirmed()
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(
            new[] { Token(0), Token(1), Token(2) }, out var rois, out _);
        Assert.True(ok);

        BagPanelRoiCalibration calibration = BagPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow);
        Assert.True(calibration.IsCalibrated);
    }

    [Fact]
    public void ANegativeSlotIndex_IsRefusedNamingIt()
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(
            new[] { "-1:0.1,0.2,0.05,0.05" }, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("-1", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateSlotIndex_IsRefusedNamingTheToken()
    {
        string[] tokens = { Token(0), Token(1), Token(0) };

        bool ok = BagPanelCalibrationProbe.TryParseSlots(tokens, out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("twice", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0:0.1,0.2,0.3")] // three parts
    [InlineData("0:0.1,0.2,0.3,0.4,0.5")] // five parts
    [InlineData("0:0.1;0.2;0.3;0.4")] // wrong separators
    [InlineData(":0.1,0.2,0.3,0.4")] // missing slot index
    [InlineData("0:")] // empty right-hand side
    [InlineData("banana:0.1,0.2,0.3,0.4")] // not an integer index
    public void AMalformedToken_IsRefused(string token)
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(new[] { token }, out _, out string? reason);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void ZeroTokens_AreRefused()
    {
        bool ok = BagPanelCalibrationProbe.TryParseSlots(Array.Empty<string>(), out _, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }
}
