using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The file that decides whether an Equip/Unequip click can be aimed at a
/// real equipment-panel slot on this operator's client.
/// </summary>
public sealed class InventoryPanelRoiCalibrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-inventory-roi-" + Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static Dictionary<EquipmentSlot, InventorySlotRoi> AllSlots(double sizeStep = 0.05)
    {
        var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>();
        double y = 0.0;
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            rois[slot] = new InventorySlotRoi(0.1, y, 0.05, 0.05);
            y += sizeStep;
        }
        return rois;
    }

    [Fact]
    public void A_missing_file_is_uncalibrated_and_is_not_broken()
    {
        InventoryPanelRoiCalibration loaded = InventoryPanelRoiCalibration.Load(Path_("absent"), out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal(InventoryPanelRoiCalibration.NotCalibratedReason, reason);
    }

    [Fact]
    public void A_confirmed_calibration_survives_a_round_trip()
    {
        var at = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        Dictionary<EquipmentSlot, InventorySlotRoi> rois = AllSlots();
        InventoryPanelRoiCalibration written = InventoryPanelRoiCalibration.Confirmed(rois, 1920, 1080, at);
        string path = Path_("inventory-panel-roi.calibration");
        written.Save(path);

        InventoryPanelRoiCalibration loaded = InventoryPanelRoiCalibration.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsCalibrated);
        Assert.Equal(1920, loaded.ClientWidth);
        Assert.Equal(1080, loaded.ClientHeight);
        Assert.Equal(at, loaded.CalibratedAtUtc);

        IReadOnlyDictionary<EquipmentSlot, PixelRect> resolved = loaded.Resolve(new PixelRect(0, 0, 1920, 1080))!;
        Assert.Equal(rois.Count, resolved.Count);
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            Assert.True(resolved.ContainsKey(slot));
    }

    [Fact]
    public void An_uncalibrated_calibration_resolves_to_no_regions_at_all()
        => Assert.Null(InventoryPanelRoiCalibration.Uncalibrated.Resolve(new PixelRect(0, 0, 1920, 1080)));

    [Fact]
    public void A_confirmed_calibration_resolves_every_slot_against_the_client_area()
    {
        var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>(AllSlots())
        {
            [EquipmentSlot.Weapon] = new InventorySlotRoi(0.30, 0.40, 0.10, 0.05)
        };
        InventoryPanelRoiCalibration calibration = InventoryPanelRoiCalibration.Confirmed(
            rois, 1920, 1080, DateTime.UtcNow);

        PixelRect weapon = calibration.Resolve(new PixelRect(100, 50, 1000, 500))![EquipmentSlot.Weapon];

        Assert.Equal(100 + 300, weapon.X);
        Assert.Equal(50 + 200, weapon.Y);
        Assert.Equal(100, weapon.Width);
        Assert.Equal(25, weapon.Height);
    }

    [Fact]
    public void MissingASlot_IsRefused_RatherThanCalibratedPartially()
    {
        Dictionary<EquipmentSlot, InventorySlotRoi> incomplete = AllSlots();
        incomplete.Remove(EquipmentSlot.Weapon);

        Assert.Throws<ArgumentException>(
            () => InventoryPanelRoiCalibration.Confirmed(incomplete, 1920, 1080, DateTime.UtcNow));
    }

    [Fact]
    public void ARegionOutsideTheClientArea_IsRefused()
    {
        Dictionary<EquipmentSlot, InventorySlotRoi> rois = AllSlots();
        rois[EquipmentSlot.Boots] = new InventorySlotRoi(0.95, 0.4, 0.4, 0.3);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => InventoryPanelRoiCalibration.Confirmed(rois, 1920, 1080, DateTime.UtcNow));
    }

    [Fact]
    public void The_uncalibrated_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => InventoryPanelRoiCalibration.Uncalibrated.Save(Path_("never")));

    [Theory]
    [InlineData("garbage")]
    [InlineData("nosai-inventory-panel-roi 1")]
    [InlineData("nosai-inventory-panel-roi 1\n1920 1080 2026-09-06T12:00:00Z")]
    public void A_malformed_file_is_uncalibrated_with_a_reason(string contents)
    {
        string path = Path_("broken");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, contents);

        InventoryPanelRoiCalibration loaded = InventoryPanelRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void A_future_version_is_refused_rather_than_guessed_at()
    {
        string path = Path_("future");
        Directory.CreateDirectory(_directory);
        var body = new System.Text.StringBuilder("nosai-inventory-panel-roi 2\n1920 1080 2026-09-06T12:00:00Z\n");
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            body.Append(slot).Append(" 0.1 0.1 0.05 0.05\n");
        File.WriteAllText(path, body.ToString());

        InventoryPanelRoiCalibration loaded = InventoryPanelRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("inventory_panel_roi_version_unsupported:2", reason);
    }

    [Fact]
    public void AFileNamingAnUnknownSlot_IsRefused()
    {
        string path = Path_("unknown-slot");
        Directory.CreateDirectory(_directory);
        var body = new System.Text.StringBuilder("nosai-inventory-panel-roi 1\n1920 1080 2026-09-06T12:00:00Z\n");
        body.Append("NotARealSlot 0.1 0.1 0.05 0.05\n");
        for (int i = 1; i < Enum.GetValues<EquipmentSlot>().Length; i++)
            body.Append(((EquipmentSlot)i)).Append(" 0.1 0.1 0.05 0.05\n");
        File.WriteAllText(path, body.ToString());

        InventoryPanelRoiCalibration loaded = InventoryPanelRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("inventory_panel_roi_entry_malformed", reason);
    }
}
