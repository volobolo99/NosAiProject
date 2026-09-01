using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Real-environment probe for the HUD reader: captures the desktop, runs
/// <see cref="ScreenVitalReader"/> over it, and writes the crops it worked from.
/// </summary>
/// <remarks>
/// <para>
/// T-03 in <c>docs/TEST_RIMANDATI.md</c>. The Control Panel has had this behind a
/// button since the beginning; the same thing runs here so the test can be
/// repeated, scripted and quoted in a report rather than clicked and described.
/// </para>
/// <para>
/// It answers one question and refuses to answer more: <b>is the ROI on the
/// HUD?</b> A bar ratio read from the wrong hundred pixels is a real measurement
/// of the wrong thing, and nothing inside the reader can tell that from a correct
/// one -- so the crops go to disk and a person looks at them. Numeric HP and MP
/// stay UNKNOWN regardless, because the glyphs are untrained; a ratio is not an
/// HP, which is the distinction ADR-0012 turns on.
/// </para>
/// </remarks>
public static class HudProbe
{
    /// <summary>Frames to wait for. A still desktop can take a moment to produce one.</summary>
    private const int AcquireAttempts = 40;

    /// <param name="processName">Client process to locate the client area from.</param>
    /// <param name="calibrateTarget">
    /// Fractions of the client area to record as the target-frame region
    /// (ADR-0018), or null to only report the current state. Passing the region
    /// is the operator's act of confirmation: it is written only after they have
    /// looked at <c>target_latest.bmp</c> with a target selected and seen the
    /// target frame in it.
    /// </param>
    public static int RunConsoleProbe(
        string? repoRoot = null,
        string processName = "NostaleClientX",
        (double X, double Y, double Width, double Height)? calibrateTarget = null)
    {
        repoRoot ??= Directory.GetCurrentDirectory();

        // Where the game actually draws. Without this the regions are fractions of
        // the whole desktop, which is only the client area when the client is
        // fullscreen -- T-03 measured the editor behind it instead.
        PixelRect? clientArea = null;
        if (OperatingSystem.IsWindows())
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    ClientWindow? window = ClientWindowLocator.TryFind(process.Id, out string? why);
                    if (window is null)
                        continue;

                    clientArea = window.ClientArea;
                    Console.WriteLine(
                        $"Client window: 0x{window.Handle.ToInt64():X} class={window.ClassName} " +
                        $"client={window.ClientArea.Width}x{window.ClientArea.Height}" +
                        $"@{window.ClientArea.X},{window.ClientArea.Y}");
                    break;
                }
            }
        }

        if (clientArea is null)
        {
            Console.WriteLine($"Client window: not found for '{processName}'.");
            Console.WriteLine("  Regions fall back to fractions of the whole frame, which is right only");
            Console.WriteLine("  for a fullscreen client. A windowed one will read the wrong pixels.");
        }

        if (!DxgiDesktopDuplicationSource.TryCreate(out DxgiDesktopDuplicationSource? capture, out var unavailable))
        {
            Console.WriteLine($"[UNKNOWN] DXGI unavailable: {unavailable?.Reason ?? "dxgi_unavailable"}");
            Console.WriteLine("  No pixels are invented. HP and MP stay UNKNOWN.");
            return 1;
        }

        using (capture)
        {
            Console.WriteLine($"Desktop duplication open: {capture!.Width}x{capture.Height} [LIVE]");

            for (int attempt = 1; attempt <= AcquireAttempts; attempt++)
            {
                if (!capture.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
                {
                    Thread.Sleep(50);
                    continue;
                }

                ScreenVitalObservation observation = new ScreenVitalReader().Read(frame, clientArea: clientArea);

                PixelRect area = clientArea ?? new PixelRect(0, 0, frame.Width, frame.Height);
                PixelRect targetRoi = TargetRegion(area, calibrateTarget, frame);
                string? directory = HudCropWriter.TrySave(repoRoot, frame, observation, targetRoi);

                Console.WriteLine($"Frame: {frame.Width}x{frame.Height} [{frame.Source.ToWire()}] after {attempt} attempt(s)");
                Console.WriteLine($"  HP roi={Describe(observation.HpRoi)} bar={Describe(observation.HpBar)}");
                Console.WriteLine($"  MP roi={Describe(observation.MpRoi)} bar={Describe(observation.MpBar)}");
                Console.WriteLine($"  Target roi={Describe(targetRoi)} {DescribeTarget(frame, targetRoi)}");
                Console.WriteLine($"  Crops: {directory ?? "not written"}");
                Console.WriteLine();
                Console.WriteLine("Open hp_latest.bmp and mp_latest.bmp. The reading is DERIVED only if the");
                Console.WriteLine("crop is actually the HUD bar; if it is anything else the number is a");
                Console.WriteLine("measurement of the wrong pixels and the honest answer stays UNKNOWN.");
                Console.WriteLine();
                ReportTargetCalibration(repoRoot, area, calibrateTarget);

                // The probe cannot tell whether the ROI is right -- that is what the
                // crops are for -- so it reports success only for having captured
                // and read, never for the reading being correct.
                return 0;
            }

            Console.WriteLine("[UNKNOWN] Duplication opened but produced no frame within the budget.");
            Console.WriteLine("  A completely static desktop can do this. HP and MP stay UNKNOWN.");
            return 1;
        }
    }

    /// <summary>
    /// The region to crop for the target frame: the operator's fractions when
    /// they supplied them, the existing calibration when there is one, and
    /// otherwise the uninspected <see cref="RoiSegmenter"/> guess — which is what
    /// the crop is for.
    /// </summary>
    private static PixelRect TargetRegion(
        PixelRect area,
        (double X, double Y, double Width, double Height)? proposed,
        CaptureFrame frame)
    {
        if (proposed is { } p)
        {
            return new PixelRect(
                area.X + (int)Math.Round(p.X * area.Width),
                area.Y + (int)Math.Round(p.Y * area.Height),
                Math.Max(1, (int)Math.Round(p.Width * area.Width)),
                Math.Max(1, (int)Math.Round(p.Height * area.Height)));
        }

        TargetRoiCalibration existing = TargetRoiCalibration.Load(
            Path.Combine(Directory.GetCurrentDirectory(), TargetRoiCalibration.RelativePath), out _);
        if (existing.Resolve(area) is { } calibrated)
            return calibrated;

        foreach (RegionOfInterest region in RoiSegmenter.Segment(frame.Width, frame.Height, area))
        {
            if (region.Kind == RoiKind.TargetHpBar)
                return region.Rect;
        }

        return new PixelRect(0, 0, 0, 0);
    }

    /// <summary>What the reader makes of the target region, without believing it.</summary>
    private static string DescribeTarget(CaptureFrame frame, PixelRect roi)
    {
        if (roi.Width <= 0 || roi.Height <= 0 || !roi.IsWithin(frame.Width, frame.Height))
            return "unreadable (target_roi_outside_frame)";

        byte[] crop = ScreenVitalReader.Crop(frame, roi);
        TargetFrameReading reading = TargetFrameReader.Read(crop, roi.Width, roi.Height);
        return reading.State switch
        {
            TargetFrameState.Present => $"present ratio={reading.HpRatio!.Value:P1} confidence={reading.Confidence:P0}",
            TargetFrameState.Absent => "absent (no bar in the region)",
            _ => $"unreadable ({reading.FailureReason})",
        };
    }

    /// <summary>
    /// Reports the calibration state, and records one when the operator supplied
    /// the region.
    /// </summary>
    /// <remarks>
    /// The confirmation is the operator's act, not the probe's: the file is
    /// written only when they pass the fractions, which they do after looking at
    /// <c>target_latest.bmp</c> with a target selected. Nothing here infers a
    /// calibration from a reading, because a reading of the wrong pixels is
    /// exactly what a calibration exists to rule out (ADR-0018).
    /// </remarks>
    private static void ReportTargetCalibration(
        string repoRoot,
        PixelRect area,
        (double X, double Y, double Width, double Height)? proposed)
    {
        string path = Path.Combine(repoRoot, TargetRoiCalibration.RelativePath);

        if (proposed is { } p)
        {
            try
            {
                TargetRoiCalibration confirmed = TargetRoiCalibration.Confirmed(
                    p.X, p.Y, p.Width, p.Height, area.Width, area.Height, DateTime.UtcNow);
                confirmed.Save(path);
                Console.WriteLine($"Target ROI calibration written: {path}");
                Console.WriteLine("  HasTarget can now become DERIVED. It is only as right as the crop:");
                Console.WriteLine("  if target_latest.bmp is not the target frame, delete the file and redo this.");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine($"Target ROI calibration refused: {ex.Message}");
            }
            return;
        }

        TargetRoiCalibration existing = TargetRoiCalibration.Load(path, out string? reason);
        if (existing.IsCalibrated)
        {
            Console.WriteLine(
                $"Target ROI calibrated on {existing.CalibratedAtUtc:O} against " +
                $"{existing.ClientWidth}x{existing.ClientHeight}.");
            return;
        }

        Console.WriteLine($"Target ROI: {reason}. HasTarget stays UNKNOWN (ADR-0018).");
        Console.WriteLine("  Select a target in the client, look at target_latest.bmp, and when the crop");
        Console.WriteLine("  is the target frame record it with:");
        Console.WriteLine("    --hud-probe --calibrate-target <x> <y> <width> <height>");
        Console.WriteLine("  as fractions of the client area. An uncalibrated region reads a confident");
        Console.WriteLine("  'no target' over empty HUD, which is worse than reporting nothing.");
    }

    private static string Describe(PixelRect rect) =>
        rect.Width <= 0 || rect.Height <= 0
            ? "empty"
            : $"{rect.Width}x{rect.Height}@{rect.X},{rect.Y}";

    private static string Describe(ScreenBarFill bar) =>
        bar.Ratio.HasValue
            ? $"{bar.Ratio.Value:P1} [{bar.Ratio.Source.ToWire()}] confidence={bar.Confidence:P0}"
            : $"UNKNOWN ({bar.FailureReason ?? bar.Ratio.FailureReason})";
}
