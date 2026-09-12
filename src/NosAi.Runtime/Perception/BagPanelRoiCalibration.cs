using System;
using System.Globalization;
using System.Text;
using NosAi.Core.WorldModel;

namespace NosAi.Runtime.Perception;

public class BagPanelRoiCalibration
{
    public const string RelativePath = "data/perception/bag-panel-roi.calibration";
    public const string NotCalibratedReason = "bag_panel_roi_not_calibrated";
    public const string SlotNotCalibratedReason = "bag_slot_index_not_in_calibrated_range";
    private const string Magic = "nosai-bag-panel-roi";
    private const int Version = 1;

    private readonly IReadOnlyDictionary<int, InventorySlotRoi> _rois;

    public bool IsCalibrated { get; }
    public int ClientWidth { get; }
    public int ClientHeight { get; }
    public DateTime? CalibratedAtUtc { get; }

    private BagPanelRoiCalibration(
        bool isCalibrated,
        IReadOnlyDictionary<int, InventorySlotRoi> rois,
        int clientWidth,
        int clientHeight,
        DateTime? calibratedAtUtc)
    {
        IsCalibrated = isCalibrated;
        _rois = rois;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        CalibratedAtUtc = calibratedAtUtc;
    }

    public static BagPanelRoiCalibration Uncalibrated { get; } =
        new(false, new Dictionary<int, InventorySlotRoi>(), 0, 0, null);

    public static BagPanelRoiCalibration Confirmed(IReadOnlyDictionary<int, InventorySlotRoi> rois, int clientWidth, int clientHeight, DateTime calibratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rois);

        // Not all-or-nothing like InventoryPanelRoiCalibration's 18 EquipmentSlot
        // values: the bag scrolls, so the operator calibrates however many slots
        // are visible on one page, never a fixed count. Only a negative index is
        // rejected -- there is no upper bound to enforce here.
        foreach (int slot in rois.Keys)
        {
            if (slot < 0)
                throw new ArgumentException($"Bag slot index {slot} cannot be negative.", nameof(rois));
        }

        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth), "The client area has no extent.");

        foreach ((int slot, InventorySlotRoi roi) in rois)
        {
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(rois), $"The crop for slot {slot} has no extent.");
            if (roi.X < 0 || roi.Y < 0 || roi.X + roi.Width > 1.0 || roi.Y + roi.Height > 1.0)
                throw new ArgumentOutOfRangeException(nameof(rois), $"The crop for slot {slot} falls outside the client area.");
        }

        return new BagPanelRoiCalibration(
            true, new Dictionary<int, InventorySlotRoi>(rois), clientWidth, clientHeight, calibratedAtUtc);
    }

    public static BagPanelRoiCalibration Load(string path, out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        failureReason = null;

        if (!File.Exists(path))
        {
            failureReason = NotCalibratedReason;
            return Uncalibrated;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException ex)
        {
            failureReason = $"bag_panel_roi_unreadable:{ex.GetType().Name}";
            return Uncalibrated;
        }

        if (lines.Length < 2 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "bag_panel_roi_header_unrecognised";
            return Uncalibrated;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "bag_panel_roi_header_unrecognised";
            return Uncalibrated;
        }

        if (version != Version)
        {
            failureReason = $"bag_panel_roi_version_unsupported:{version}";
            return Uncalibrated;
        }

        string[] meta = lines[1].Split(' ');
        if (meta.Length != 3
            || !int.TryParse(meta[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(meta[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !DateTime.TryParse(meta[2], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
        {
            failureReason = "bag_panel_roi_entry_malformed";
            return Uncalibrated;
        }

        var rois = new Dictionary<int, InventorySlotRoi>(1);
        for (int i = 2; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split(' ');
            if (fields.Length != 5
                || !int.TryParse(fields[0], out int slot)
                || !TryFraction(fields[1], out double x)
                || !TryFraction(fields[2], out double y)
                || !TryFraction(fields[3], out double width)
                || !TryFraction(fields[4], out double height))
            {
                failureReason = "bag_panel_roi_entry_malformed";
                return Uncalibrated;
            }

            rois[slot] = new InventorySlotRoi(x, y, width, height);
        }

        try
        {
            return Confirmed(rois, clientWidth, clientHeight, at);
        }
        catch (ArgumentException)
        {
            failureReason = "bag_panel_roi_entry_malformed";
            return Uncalibrated;
        }
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsCalibrated)
            throw new InvalidOperationException("There is no calibration to write.");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(Magic).Append(' ').Append(Version).Append('\n');
        text
            .Append(ClientWidth.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientHeight.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        foreach ((int slot, InventorySlotRoi roi) in _rois)
        {
            text
                .Append(slot).Append(' ')
                .Append(roi.X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(roi.Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(roi.Width.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(roi.Height.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(path, text.ToString());
    }

    private static bool TryFraction(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public IReadOnlyDictionary<int, PixelRect>? Resolve(PixelRect clientArea)
    {
        if (!IsCalibrated || clientArea.Width <= 0 || clientArea.Height <= 0)
            return null;

        var resolved = new Dictionary<int, PixelRect>(_rois.Count);
        foreach ((int slot, InventorySlotRoi roi) in _rois)
        {
            int rx = clientArea.X + (int)Math.Round(roi.X * clientArea.Width);
            int ry = clientArea.Y + (int)Math.Round(roi.Y * clientArea.Height);
            int rw = Math.Max(1, (int)Math.Round(roi.Width * clientArea.Width));
            int rh = Math.Max(1, (int)Math.Round(roi.Height * clientArea.Height));
            resolved[slot] = new PixelRect(rx, ry, rw, rh);
        }
        return resolved;
    }
}
