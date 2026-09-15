using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The file that decides whether an Equip click (C-312) can be aimed at a
/// real bag slot on this operator's client. Unlike
/// <see cref="InventoryPanelRoiCalibrationTests"/>'s equipment panel, the bag
/// scrolls: a calibration is never all-or-nothing, it covers whichever slots
/// the operator confirmed on one visible page.
/// </summary>
public sealed class BagPanelRoiCalibrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-bag-roi-" + Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static Dictionary<int, InventorySlotRoi> OnePage(int count = 5, double sizeStep = 0.05)
    {
        var rois = new Dictionary<int, InventorySlotRoi>();
        double y = 0.0;
        for (int slot = 0; slot < count; slot++)
        {
            rois[slot] = new InventorySlotRoi(0.1, y, 0.05, 0.05);
            y += sizeStep;
        }
        return rois;
    }

    [Fact]
    public void A_missing_file_is_uncalibrated_and_is_not_broken()
    {
        BagPanelRoiCalibration loaded = BagPanelRoiCalibration.Load(Path_("absent"), out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal(BagPanelRoiCalibration.NotCalibratedReason, reason);
    }

    [Fact]
    public void A_confirmed_calibration_survives_a_round_trip()
    {
        var at = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        Dictionary<int, InventorySlotRoi> rois = OnePage();
        BagPanelRoiCalibration written = BagPanelRoiCalibration.Confirmed(rois, 1920, 1080, at);
        string path = Path_("bag-panel-roi.calibration");
        written.Save(path);

        BagPanelRoiCalibration loaded = BagPanelRoiCalibration.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsCalibrated);
        Assert.Equal(1920, loaded.ClientWidth);
        Assert.Equal(1080, loaded.ClientHeight);
        Assert.Equal(at, loaded.CalibratedAtUtc);

        IReadOnlyDictionary<int, PixelRect> resolved = loaded.Resolve(new PixelRect(0, 0, 1920, 1080))!;
        Assert.Equal(rois.Count, resolved.Count);
        foreach (int slot in rois.Keys)
            Assert.True(resolved.ContainsKey(slot));
    }

    [Fact]
    public void An_uncalibrated_calibration_resolves_to_no_regions_at_all()
        => Assert.Null(BagPanelRoiCalibration.Uncalibrated.Resolve(new PixelRect(0, 0, 1920, 1080)));

    [Fact]
    public void A_confirmed_calibration_resolves_every_calibrated_slot_against_the_client_area()
    {
        var rois = new Dictionary<int, InventorySlotRoi>(OnePage())
        {
            [3] = new InventorySlotRoi(0.30, 0.40, 0.10, 0.05)
        };
        BagPanelRoiCalibration calibration = BagPanelRoiCalibration.Confirmed(
            rois, 1920, 1080, DateTime.UtcNow);

        PixelRect slot3 = calibration.Resolve(new PixelRect(100, 50, 1000, 500))![3];

        Assert.Equal(100 + 300, slot3.X);
        Assert.Equal(50 + 200, slot3.Y);
        Assert.Equal(100, slot3.Width);
        Assert.Equal(25, slot3.Height);
    }

    [Fact]
    public void A_partial_page_is_confirmed_whole_not_all_or_nothing()
    {
        // Unlike InventoryPanelRoiCalibration's 18 EquipmentSlot values, the bag
        // scrolls: however many slots the operator confirmed on one visible
        // page is a valid, complete calibration -- there is no fixed count to
        // reject a smaller (or larger) one against.
        Dictionary<int, InventorySlotRoi> threeSlots = OnePage(count: 3);

        BagPanelRoiCalibration calibration = BagPanelRoiCalibration.Confirmed(
            threeSlots, 1920, 1080, DateTime.UtcNow);

        Assert.True(calibration.IsCalibrated);
        IReadOnlyDictionary<int, PixelRect> resolved = calibration.Resolve(new PixelRect(0, 0, 1920, 1080))!;
        Assert.Equal(3, resolved.Count);
    }

    [Fact]
    public void ASlotOutsideTheCalibratedRange_ResolvesToNothing_NotAGuess()
    {
        Dictionary<int, InventorySlotRoi> rois = OnePage(count: 5);
        BagPanelRoiCalibration calibration = BagPanelRoiCalibration.Confirmed(
            rois, 1920, 1080, DateTime.UtcNow);

        IReadOnlyDictionary<int, PixelRect> resolved = calibration.Resolve(new PixelRect(0, 0, 1920, 1080))!;

        Assert.False(resolved.ContainsKey(40));
    }

    [Fact]
    public void ANegativeSlotIndex_IsRefused()
    {
        Dictionary<int, InventorySlotRoi> rois = OnePage();
        rois[-1] = new InventorySlotRoi(0.1, 0.1, 0.05, 0.05);

        Assert.Throws<ArgumentException>(
            () => BagPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow));
    }

    [Fact]
    public void ARegionOutsideTheClientArea_IsRefused()
    {
        Dictionary<int, InventorySlotRoi> rois = OnePage();
        rois[0] = new InventorySlotRoi(0.95, 0.4, 0.4, 0.3);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => BagPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow));
    }

    [Fact]
    public void The_uncalibrated_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => BagPanelRoiCalibration.Uncalibrated.Save(Path_("never")));

    [Theory]
    [InlineData("garbage")]
    [InlineData("nosai-bag-panel-roi 1")]
    [InlineData("nosai-bag-panel-roi 1\n1920 1080 2026-09-12T12:00:00Z\n0 0.1 0.1 0.05")]
    public void A_malformed_file_is_uncalibrated_with_a_reason(string contents)
    {
        string path = Path_("broken");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, contents);

        BagPanelRoiCalibration loaded = BagPanelRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void A_future_version_is_refused_rather_than_guessed_at()
    {
        string path = Path_("future");
        Directory.CreateDirectory(_directory);
        string body = "nosai-bag-panel-roi 2\n1920 1080 2026-09-12T12:00:00Z\n0 0.1 0.1 0.05 0.05\n";
        File.WriteAllText(path, body);

        BagPanelRoiCalibration loaded = BagPanelRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("bag_panel_roi_version_unsupported:2", reason);
    }
}
