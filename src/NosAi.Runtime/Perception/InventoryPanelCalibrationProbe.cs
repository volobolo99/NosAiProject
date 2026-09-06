using System.Globalization;
using System.Text;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Real-environment probe for <see cref="InventoryPanelRoiCalibration"/>:
/// captures the desktop, resolves the client area the way
/// <see cref="HudProbe"/> does, writes a whole-client-area preview bitmap
/// the operator reads the eight slot fractions off, and records one crop
/// per <see cref="EquipmentSlot"/> when the operator supplies all eight.
/// </summary>
/// <remarks>
/// <para>
/// Answers one question and refuses to answer more: <b>where are the eight
/// equipment slots on this operator's panel?</b> Equipping in NosTale is a
/// drag/click on a fixed UI panel, not a hotkey, and no code in this
/// repository can name where those slots are without an operator confirming
/// a real crop (AP-07: the screen-space panel layout is the missing piece
/// before an Equip/Unequip click can target a real coordinate).
/// </para>
/// <para>
/// Same recipe as <see cref="HudProbe"/>'s own target-frame calibration
/// (<c>TargetRoiCalibration</c>, ADR-0018), extended from one region to the
/// panel's eight slots; <see cref="InventoryPanelRoiCalibration"/> is all
/// eight together or none, because the panel is either fully open or not.
/// A crop of the wrong pixels is exactly what this calibration exists to
/// rule out, and nothing inside any reader can tell a correct crop from a
/// wrong one -- so the whole client area goes to disk as
/// <c>inventory_panel_latest.bmp</c> and a person looks at it.
/// </para>
/// <para>
/// This probe never clicks, never Equips/Unequips, and never reads what
/// currently occupies a slot. Building <c>--equip</c>/<c>--unequip</c>
/// themselves is future work, gated on this calibration having been
/// confirmed by an operator against a real client <i>and</i> on a dedicated
/// capture confirming which <see cref="GameTrafficObserver.InventorySlotReading.InventoryKind"/>
/// value a real equip produces.
/// </para>
/// </remarks>
public static class InventoryPanelCalibrationProbe
{
    /// <summary>Frames to wait for, same budget as <see cref="HudProbe"/>.</summary>
    private const int AcquireAttempts = 40;

    /// <summary>
    /// The operator passes each slot as one token, <c>&lt;EquipmentSlot&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;</c>
    /// -- fractions of the client area, packed one slot per token because there
    /// are eight of them.
    /// </summary>
    /// <remarks>
    /// Does not call <see cref="InventoryPanelRoiCalibration.Confirmed"/>:
    /// that can throw for a region-outside-client-area or zero-extent reason
    /// this method cannot know about before the client area is resolved, so
    /// the caller does, per the console command below.
    /// </remarks>
    /// <param name="failureReason">
    /// Which token was refused and why, or null when the tokens parsed.
    /// Never silently drops or defaults a slot.
    /// </param>
    public static bool TryParseSlots(
        IReadOnlyList<string> tokens,
        out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois,
        out string? failureReason)
    {
        rois = new Dictionary<EquipmentSlot, InventorySlotRoi>();
        failureReason = null;

        if (tokens is null)
        {
            failureReason = "no_slot_tokens_given";
            return false;
        }

        EquipmentSlot[] declared = (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));
        if (tokens.Count != declared.Length)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture,
                $"expected_exactly_{declared.Length}_slot_tokens_got_{tokens.Count}");
            return false;
        }

        var parsed = new Dictionary<EquipmentSlot, InventorySlotRoi>(declared.Length);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!TryParseOne(tokens[i], out EquipmentSlot slot, out InventorySlotRoi roi, out string? tokenReason))
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
    /// reports the current calibration state; with all eight tokens present it
    /// attaches to the client, captures one frame, writes the preview bitmap,
    /// and records the calibration the operator just confirmed.
    /// </summary>
    /// <param name="repoRoot">Repository root, so <see cref="InventoryPanelRoiCalibration.RelativePath"/> and the crops land in the same gitignored <c>data/</c> tree.</param>
    /// <param name="processName">Client process to locate the client area from, same default as <see cref="HudProbe.FindClientWindow"/>.</param>
    /// <param name="slots">All eight slot fractions, in any order, or null to only report.</param>
    public static int Run(
        string? repoRoot = null,
        string processName = "NostaleClientX",
        IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi>? slots = null)
    {
        repoRoot ??= Directory.GetCurrentDirectory();
        string path = Path.Combine(repoRoot, InventoryPanelRoiCalibration.RelativePath);

        // Same "client not found is a hard failure when recording" choice as
        // HudProbe's crop workflow: a calibration is fractions OF the client
        // area, and writing one without that area would be fractions of nothing.
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
            Console.WriteLine("  Open the client with its equipment panel before running this command again.");
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

            // Without the client window the area is the whole desktop, which is
            // only the client area when the client is fullscreen; a windowed
            // client would read the wrong pixels. Same fallback and same warning
            // HudProbe prints for the same reason.
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
                $"  Preview: {Path.Combine(repoRoot, HudCropWriter.RelativeDirectory, HudCropWriter.PanelPreviewFileName)}");
            Console.WriteLine();
            Console.WriteLine("Open inventory_panel_latest.bmp with the equipment panel open. Read off where");
            Console.WriteLine("each of the eight slots (Weapon, Shield, Helmet, Armor, Gloves, Boots,");
            Console.WriteLine("Accessory1, Accessory2) sits, and record all eight as fractions of the area:");
            Console.WriteLine();
            Console.WriteLine($"    --calibrate-inventory-panel Weapon:<x>,<y>,<w>,<h> Shield:<x>,<y>,<w>,<h> Helmet:<x>,<y>,<w>,<h> Armor:<x>,<y>,<w>,<h> Gloves:<x>,<y>,<w>,<h> Boots:<x>,<y>,<w>,<h> Accessory1:<x>,<y>,<w>,<h> Accessory2:<x>,<y>,<w>,<h>");
            Console.WriteLine();
            Console.WriteLine("  All eight are required in one invocation, in any order. Nothing is recorded");
            Console.WriteLine("  until every slot has a confirmed crop.");
            Console.WriteLine();

            ReportCalibrationState(path, slots, area);
            return slots is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Reports the current calibration state, and records a new one when the
    /// operator supplied all eight slots.
    /// </summary>
    /// <remarks>
    /// The confirmation is the operator's act, not the probe's: the file is
    /// written only when they pass the fractions, which they do after looking
    /// at <c>inventory_panel_latest.bmp</c> with the equipment panel open.
    /// Nothing here infers a calibration from a reading.
    /// </remarks>
    private static void ReportCalibrationState(
        string path,
        IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi>? proposed,
        PixelRect area)
    {
        if (proposed is { } p)
        {
            try
            {
                InventoryPanelRoiCalibration confirmed = InventoryPanelRoiCalibration.Confirmed(
                    p, area.Width, area.Height, DateTime.UtcNow);
                confirmed.Save(path);
                Console.WriteLine($"Inventory-panel ROI calibration written: {path}");
                Console.WriteLine("  Each slot is only as right as its crop: if inventory_panel_latest.bmp");
                Console.WriteLine("  is not the equipment panel, delete the file and redo this.");
                Console.WriteLine("  This records where a slot is; it does not record --equip/--unequip,");
                Console.WriteLine("  which remain blocked on a real client pass and a capture confirming");
                Console.WriteLine("  which InventoryKind a real equip produces.");
                return;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[REFUSED] {ex.Message}");
                Console.WriteLine("  Nothing was written. A calibration is all eight valid crops inside");
                Console.WriteLine("  the client area, or it is not a calibration.");
                return;
            }
        }

        InventoryPanelRoiCalibration existing = InventoryPanelRoiCalibration.Load(path, out string? reason);
        if (existing.IsCalibrated)
        {
            Console.WriteLine(
                $"Inventory-panel ROI calibrated on {existing.CalibratedAtUtc:O} against " +
                $"{existing.ClientWidth}x{existing.ClientHeight}.");
            return;
        }

        Console.WriteLine($"Inventory-panel ROI: {reason}. No slot crop exists yet (AP-07).");
    }

    private static bool TryParseOne(
        string token,
        out EquipmentSlot slot,
        out InventorySlotRoi roi,
        out string? reason)
    {
        slot = default;
        roi = default;
        reason = null;

        int colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1)
        {
            reason = "expected_slot_name_colon_x_comma_y_comma_w_comma_h";
            return false;
        }

        if (!Enum.TryParse(token.AsSpan(0, colon), ignoreCase: false, out slot)
            || !Enum.IsDefined(slot))
        {
            reason = $"unknown_equipment_slot_{token[..colon]}";
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
    /// Writes the whole-client-area preview, named
    /// <c>inventory_panel_latest.bmp</c>, via the same writer HudProbe uses
    /// for its other named crops. No preview bitmap is written when the crop
    /// writer cannot (nothing to draw on, or no pixels).
    /// </summary>
    private static void WritePreview(string repoRoot, CaptureFrame frame, PixelRect area)
        => HudCropWriter.TrySavePanelPreview(repoRoot, frame, area);
}
