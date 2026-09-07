using System.Globalization;
using System.Text;
using NosAi.Core.WorldModel;

namespace NosAi.Runtime.Perception;

/// <summary>One equipment slot's crop, as a fraction of the client area.</summary>
public readonly record struct InventorySlotRoi(double X, double Y, double Width, double Height);

/// <summary>
/// Where each <see cref="EquipmentSlot"/> icon sits on this operator's
/// inventory/equipment panel, confirmed by an operator against a real
/// screenshot of their own client -- the layout
/// <c>AP-07_A1_STATUS.md</c> "AP-07/A2+A4" names as the missing piece
/// before an Equip/Unequip click can target a real coordinate: equipping
/// is a drag/click on a fixed UI panel, not a hotkey, and no code in this
/// repository can name where that panel's slots are without guessing at
/// NosTale's own UI art.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same recipe as <see cref="DialogRoiCalibration"/> and
/// <see cref="TargetRoiCalibration"/>, extended from one region to
/// eight.</b> Each of the eight <see cref="EquipmentSlot"/> values gets
/// its own crop, because the panel is not one rectangle: it is eight
/// fixed icon slots (weapon, shield, helmet, ...) arranged around the
/// character preview, and a click aimed at the panel's bounding box
/// would land on whichever slot happens to be there, not the one the
/// candidate names.
/// </para>
/// <para>
/// <b>All eight or none.</b> Unlike <see cref="DialogRoiCalibration"/>'s
/// single region, a real calibration pass has the operator confirm the
/// whole panel in one sitting -- the equipment panel is either open (all
/// eight icons visible at once) or not, so there is no honest partial
/// state between "the operator has not calibrated this panel" and "every
/// slot has a confirmed crop".
/// </para>
/// <para>
/// <b>Specifica di una macchina, e versionata lo stesso dal 2026-09-07.</b> Same reasoning
/// as the two calibrations above: the fractions are of one client's UI
/// theme at one resolution. Lives in <c>data/perception/</c>. A fresh
/// clone reads <see cref="NotCalibratedReason"/>, distinct from a broken
/// read.
/// </para>
/// <para>
/// <b>What this type deliberately does not do.</b> It says where a slot
/// is on screen, nothing about what occupies it: reading whether a slot
/// currently holds an item, and which one, is a separate, still-open gap
/// (<c>AP-07_A1_STATUS.md</c> "Perché <c>GenerateEquipCandidates</c> non
/// esiste" -- no code decodes an item's equipment category). A click
/// aimed by this calibration can be verified only by the coarse
/// inventory-kind signal <see cref="GameTrafficObserver.InventorySlotReading"/>'s
/// own remarks document, not by reading the panel itself.
/// </para>
/// </remarks>
public sealed record InventoryPanelRoiCalibration
{
    /// <summary>Where the calibration lives, relative to the repository root.</summary>
    public const string RelativePath = "data/perception/inventory-panel-roi.calibration";

    /// <summary>Reported by every consumer while no calibration exists.</summary>
    public const string NotCalibratedReason = "inventory_panel_roi_not_calibrated";

    private const string Magic = "nosai-inventory-panel-roi";
    private const int Version = 1;

    /// <summary>Exactly one crop per declared <see cref="EquipmentSlot"/> value.</summary>
    private static readonly EquipmentSlot[] Slots =
        (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));

    private readonly IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> _rois;

    private InventoryPanelRoiCalibration(
        bool isCalibrated,
        IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois,
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

    /// <summary>Whether a real calibration was loaded. False is not "use a guess".</summary>
    public bool IsCalibrated { get; }

    /// <summary>The client area the fractions were measured against, in pixels.</summary>
    public int ClientWidth { get; }

    /// <summary>The client area the fractions were measured against, in pixels.</summary>
    public int ClientHeight { get; }

    /// <summary>When the operator confirmed the panel, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>The state before the first calibration pass.</summary>
    public static InventoryPanelRoiCalibration Uncalibrated { get; } =
        new(false, new Dictionary<EquipmentSlot, InventorySlotRoi>(), 0, 0, null);

    /// <summary>
    /// A calibration the operator has confirmed against a real screenshot
    /// of their own equipment panel, one crop per <see cref="EquipmentSlot"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When <paramref name="rois"/> does not carry exactly one entry per
    /// declared <see cref="EquipmentSlot"/> value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When a crop has no extent, falls outside the client area, or the
    /// client area itself has no extent.
    /// </exception>
    public static InventoryPanelRoiCalibration Confirmed(
        IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois,
        int clientWidth,
        int clientHeight,
        DateTime calibratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rois);

        if (rois.Count != Slots.Length || Slots.Any(slot => !rois.ContainsKey(slot)))
        {
            throw new ArgumentException(
                $"A confirmed panel needs exactly one crop per {nameof(EquipmentSlot)} value "
                + $"({Slots.Length} total), no more and no fewer.",
                nameof(rois));
        }

        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth), "The client area has no extent.");

        foreach ((EquipmentSlot slot, InventorySlotRoi roi) in rois)
        {
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(rois), $"The crop for {slot} has no extent.");
            if (roi.X < 0 || roi.Y < 0 || roi.X + roi.Width > 1.0 || roi.Y + roi.Height > 1.0)
                throw new ArgumentOutOfRangeException(nameof(rois), $"The crop for {slot} falls outside the client area.");
        }

        return new InventoryPanelRoiCalibration(
            true, new Dictionary<EquipmentSlot, InventorySlotRoi>(rois), clientWidth, clientHeight, calibratedAtUtc);
    }

    /// <summary>
    /// Every slot's calibrated region in pixels, or null when there is no
    /// calibration or the supplied client area has no extent.
    /// </summary>
    public IReadOnlyDictionary<EquipmentSlot, PixelRect>? Resolve(PixelRect clientArea)
    {
        if (!IsCalibrated || clientArea.Width <= 0 || clientArea.Height <= 0)
            return null;

        var resolved = new Dictionary<EquipmentSlot, PixelRect>(_rois.Count);
        foreach ((EquipmentSlot slot, InventorySlotRoi roi) in _rois)
        {
            int rx = clientArea.X + (int)Math.Round(roi.X * clientArea.Width);
            int ry = clientArea.Y + (int)Math.Round(roi.Y * clientArea.Height);
            int rw = Math.Max(1, (int)Math.Round(roi.Width * clientArea.Width));
            int rh = Math.Max(1, (int)Math.Round(roi.Height * clientArea.Height));
            resolved[slot] = new PixelRect(rx, ry, rw, rh);
        }
        return resolved;
    }

    /// <summary>
    /// Loads the calibration at <paramref name="path"/>, or returns
    /// <see cref="Uncalibrated"/> with a reason.
    /// </summary>
    public static InventoryPanelRoiCalibration Load(string path, out string? failureReason)
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
            failureReason = $"inventory_panel_roi_unreadable:{ex.GetType().Name}";
            return Uncalibrated;
        }

        if (lines.Length < 2 + Slots.Length || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "inventory_panel_roi_header_unrecognised";
            return Uncalibrated;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "inventory_panel_roi_header_unrecognised";
            return Uncalibrated;
        }

        if (version != Version)
        {
            failureReason = $"inventory_panel_roi_version_unsupported:{version}";
            return Uncalibrated;
        }

        string[] meta = lines[1].Split(' ');
        if (meta.Length != 3
            || !int.TryParse(meta[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(meta[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !DateTime.TryParse(meta[2], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
        {
            failureReason = "inventory_panel_roi_entry_malformed";
            return Uncalibrated;
        }

        var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>(Slots.Length);
        for (int i = 0; i < Slots.Length; i++)
        {
            string[] fields = lines[2 + i].Split(' ');
            if (fields.Length != 5
                || !Enum.TryParse(fields[0], out EquipmentSlot slot)
                || !TryFraction(fields[1], out double x)
                || !TryFraction(fields[2], out double y)
                || !TryFraction(fields[3], out double width)
                || !TryFraction(fields[4], out double height))
            {
                failureReason = "inventory_panel_roi_entry_malformed";
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
            failureReason = "inventory_panel_roi_entry_malformed";
            return Uncalibrated;
        }
    }

    /// <summary>Writes the calibration, creating the directory if needed.</summary>
    /// <exception cref="InvalidOperationException">When there is nothing to write.</exception>
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

        foreach (EquipmentSlot slot in Slots)
        {
            InventorySlotRoi roi = _rois[slot];
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
}
