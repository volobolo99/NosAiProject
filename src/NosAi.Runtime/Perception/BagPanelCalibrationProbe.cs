using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Real-environment probe for <see cref="BagPanelRoiCalibration"/> (C-313):
/// captures the desktop, resolves the client area the way
/// <see cref="InventoryPanelCalibrationProbe"/> does for the equipment panel,
/// writes a whole-client-area preview bitmap the operator reads the slot
/// fractions off, and records one crop per bag slot index the operator
/// supplies.
/// </summary>
/// <remarks>
/// Unlike the equipment panel's 18 fixed <see cref="EquipmentSlot"/> values,
/// the bag scrolls: <see cref="BagPanelRoiCalibration"/> is never
/// all-or-nothing, so this probe accepts any number of slot tokens (at least
/// one) instead of requiring a fixed declared count.
/// </remarks>
public static class BagPanelCalibrationProbe
{
    /// <summary>Frames to wait for, same budget as <see cref="InventoryPanelCalibrationProbe"/>.</summary>
    private const int AcquireAttempts = 40;

    /// <summary>
    /// The operator passes each slot as one token, <c>&lt;slotIndex&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
    /// -- fractions of the client area. At least one token is required; there
    /// is no fixed count to match, unlike the equipment panel's declared
    /// <see cref="EquipmentSlot"/> values.
    /// </summary>
    public static bool TryParseSlots(IReadOnlyList<string> tokens, out IReadOnlyDictionary<int, InventorySlotRoi> rois, out string? failureReason)
    {
        rois = new Dictionary<int, InventorySlotRoi>();
        failureReason = null;

        if (tokens is null || tokens.Count == 0)
        {
            failureReason = "no_slot_tokens_given";
            return false;
        }

        var parsed = new Dictionary<int, InventorySlotRoi>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!TryParseOne(tokens[i], out int slot, out InventorySlotRoi roi, out string? tokenReason))
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"token_{i + 1}_('{tokens[i]}')_rejected:{tokenReason}");
                return false;
            }

            if (parsed.ContainsKey(slot))
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"token_{i + 1}_('{tokens[i]}')_names_{slot}_twice");
                return false;
            }

            parsed[slot] = roi;
        }

        rois = parsed;
        return true;
    }

    /// <summary>
    /// Runs the console calibration. With <paramref name="slots"/> null it only
    /// reports the current calibration state; with at least one slot token
    /// present it attaches to the client, captures one frame, writes the
    /// preview bitmap, and records the calibration the operator just confirmed.
    /// </summary>
    public static int Run(string? repoRoot = null, string processName = "NostaleClientX", IReadOnlyDictionary<int, InventorySlotRoi>? slots = null)
    {
        repoRoot ??= Directory.GetCurrentDirectory();
        string path = Path.Combine(repoRoot, BagPanelRoiCalibration.RelativePath);

        ClientWindow? window = null;
        if (OperatingSystem.IsWindows() && HudProbe.FindClientWindow(processName) is { } found)
        {
            window = found;
            Console.WriteLine(
                $"Client window: 0x{window.Handle.ToInt64():X} class={window.ClassName} " +
                $"client={window.ClientArea.Width}x{window.ClientArea.Height}" +
                $"@{window.ClientArea.X},{window.ClientArea.Y}");
        }
        else
        {
            Console.WriteLine($"Client window: not found for '{processName}'.");
            Console.WriteLine("  Open the client with its bag panel before running this command again.");
        }

        if (!DxgiDesktopDuplicationSource.TryCreate(out DxgiDesktopDuplicationSource? capture, out var unavailable))
        {
            Console.WriteLine($"[UNKNOWN] DXGI unavailable: {unavailable?.Reason ?? "dxgi_unavailable"}");
            Console.WriteLine("  No pixels are captured. No calibration is recorded.");
            return 1;
        }

        using (capture)
        {
            Console.WriteLine($"Desktop duplication open: {capture!.Width}x{capture.Height} [LIVE]");

            CaptureFrame? frame = AcquireFrame(capture);
            if (frame is null)
            {
                Console.WriteLine("[UNKNOWN] Duplication opened but produced no frame within the budget.");
                Console.WriteLine("  A completely static desktop can do this. No calibration is recorded.");
                return 1;
            }

            PixelRect area = window?.ClientArea ?? new PixelRect(0, 0, frame.Width, frame.Height);
            if (window is null)
            {
                Console.WriteLine("  Regions fall back to fractions of the whole frame, which is right only");
                Console.WriteLine("  for a fullscreen client. A windowed one would read the wrong pixels.");
            }

            WritePreview(repoRoot, frame, area);

            Console.WriteLine($"Frame: {frame.Width}x{frame.Height} [{frame.Source.ToWire()}]");
            Console.WriteLine($"Area: {area.Width}x{area.Height} px (le frazioni sono di questa)");
            Console.WriteLine(
                $"  Preview: {Path.Combine(repoRoot, HudCropWriter.RelativeDirectory, HudCropWriter.BagPanelPreviewFileName)}");
            Console.WriteLine();
            Console.WriteLine("Open bag_panel_latest.bmp with the bag open. Read off where each of the");
            Console.WriteLine("slots you want to calibrate sits, as fractions of the area (any number,");
            Console.WriteLine("in any order -- the bag scrolls, so there is no fixed count to match).");
            Console.WriteLine();
            Console.WriteLine("Re-run the command with one token per slot, like:");
            Console.WriteLine("    --calibrate-bag-panel 0:<x>,<y>,<w>,<h> 1:<x>,<y>,<w>,<h> ...");
            Console.WriteLine();

            ReportCalibrationState(path, slots, area);
            return slots is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Reports the current calibration state, and records a new one when the
    /// operator supplied at least one bag-slot crop.
    /// </summary>
    private static void ReportCalibrationState(string path, IReadOnlyDictionary<int, InventorySlotRoi>? proposed, PixelRect area)
    {
        if (proposed is { } p)
        {
            try
            {
                BagPanelRoiCalibration confirmed = BagPanelRoiCalibration.Confirmed(
                    p, area.Width, area.Height, DateTime.UtcNow);
                confirmed.Save(path);
                Console.WriteLine($"Bag-panel ROI calibration written: {path}");
                Console.WriteLine("  Each slot is only as right as its crop: if bag_panel_latest.bmp");
                Console.WriteLine("  is not the bag panel, delete the file and redo this.");
                return;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[REFUSED] {ex.Message}");
                Console.WriteLine("  Nothing was written. A calibration is one or more valid crops, all");
                Console.WriteLine("  inside the client area with a non-negative slot index, or it is not");
                Console.WriteLine("  a calibration.");
                return;
            }
        }

        BagPanelRoiCalibration existing = BagPanelRoiCalibration.Load(path, out string? reason);
        if (existing.IsCalibrated)
        {
            Console.WriteLine(
                $"Bag-panel ROI calibrated on {existing.CalibratedAtUtc:O} against " +
                $"{existing.ClientWidth}x{existing.ClientHeight}.");
            return;
        }

        Console.WriteLine($"Bag-panel ROI: {reason}. No slot crop exists yet (C-313).");
    }

    private static bool TryParseOne(string token, out int slot, out InventorySlotRoi roi, out string? reason)
    {
        slot = default;
        roi = default;
        reason = null;

        int colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1)
        {
            reason = "expected_slot_index_colon_x_comma_y_comma_w_comma_h";
            return false;
        }

        if (!int.TryParse(token.AsSpan(0, colon), out slot) || slot < 0)
        {
            reason = $"invalid_or_negative_slot_index_{token[..colon]}";
            return false;
        }

        string[] parts = token[(colon + 1)..].Split(',');
        if (parts.Length != 4
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
        {
            reason = "expected_four_invariant_fractions";
            return false;
        }

        roi = new InventorySlotRoi(x, y, w, h);
        return true;
    }

    private static CaptureFrame? AcquireFrame(DxgiDesktopDuplicationSource capture)
    {
        for (int attempt = 1; attempt <= AcquireAttempts; attempt++)
        {
            if (capture.TryAcquire(out CaptureFrame frame) && frame.HasPixels)
                return frame;
            Thread.Sleep(50);
        }

        return null;
    }

    /// <summary>
    /// Writes the whole-client-area preview, named <c>bag_panel_latest.bmp</c>,
    /// via <see cref="HudCropWriter.TrySaveBagPanelPreview"/> -- never
    /// <see cref="HudCropWriter.TrySavePanelPreview"/>, which is the other panel's.
    /// </summary>
    private static void WritePreview(string repoRoot, CaptureFrame frame, PixelRect area)
        => HudCropWriter.TrySaveBagPanelPreview(repoRoot, frame, area);
}
