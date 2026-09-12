using System.Globalization;
using System.Text;

namespace NosAi.Runtime.Perception;

public class BagPanelRoiCalibration
{
    public const string RelativePath = "data/perception/bag-panel-roi.calibration";
    public const string NotCalibratedReason = "bag_panel_roi_not_calibrated";
    public const string SlotNotCalibratedReason = "bag_slot_index_not_in_calibrated_range";
    private const string Magic = "nosai-bag-panel-roi";
    private const int Version = 1;

    public bool IsCalibrated { get; }
    public int ClientWidth { get; }
    public int ClientHeight { get; }
    public DateTime? CalibratedAtUtc { get; }

    public static BagPanelRoiCalibration Uncalibrated { get; } = null!;

    public static BagPanelRoiCalibration Confirmed(IReadOnlyDictionary<int, InventorySlotRoi> rois, int clientWidth, int clientHeight, DateTime calibratedAtUtc)
    {
        throw new NotImplementedException();
    }

    public static BagPanelRoiCalibration Load(string path, out string? failureReason)
    {
        throw new NotImplementedException();
    }

    public void Save(string path)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<int, PixelRect>? Resolve(PixelRect clientArea)
    {
        throw new NotImplementedException();
    }
}
