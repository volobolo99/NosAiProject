using System.Buffers.Binary;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Writes the HP and MP crops the vital reader worked from, so the ROI can be
/// checked by looking at it.
/// </summary>
/// <remarks>
/// <para>
/// The ROI is the whole question in T-03: a reader pointed a hundred pixels off
/// the HUD returns a bar reading that is a measurement of the wrong thing, and no
/// amount of internal confidence distinguishes that from a correct one. The crop
/// is the evidence, and it is why it is written to disk rather than merely
/// summarised.
/// </para>
/// <para>
/// Not a provider, and nothing here reaches the Gate 1 snapshot: this is how an
/// operator confirms where the reader is looking, not a source of observations.
/// </para>
/// </remarks>
public static class HudCropWriter
{
    /// <summary>Where the crops land, relative to the repository root.</summary>
    public const string RelativeDirectory = "data/perception/crops";

    /// <summary>
    /// Writes the HP and MP crops, and returns the directory, or null when there
    /// was nothing to write.
    /// </summary>
    /// <param name="targetRoi">
    /// The candidate target-frame region, written as <c>target_latest.bmp</c>. It
    /// is the evidence for ADR-0018's calibration, and for the same reason as the
    /// HP crop: nothing inside the reader can tell a correct region from a real
    /// measurement of the wrong pixels, so a person looks at it.
    /// </param>
    /// <param name="clientArea">
    /// The whole client area, written as <c>client_latest.bmp</c>. A crop alone
    /// cannot say which way to move a region that is pointed at the wrong place:
    /// it shows empty HUD either way. The full picture is what turns the
    /// calibration from guesswork into a measurement, because the operator can
    /// read the frame's two corners off it in pixels.
    /// </param>
    public static string? TrySave(
        string? repoRoot,
        CaptureFrame frame,
        ScreenVitalObservation observation,
        PixelRect? targetRoi = null,
        PixelRect? clientArea = null)
    {
        IReadOnlyList<HudCropWrite> written = SaveCrops(repoRoot, frame, observation, targetRoi, clientArea);
        return written.Count > 0 && repoRoot is { } root
            ? Path.Combine(root, RelativeDirectory)
            : null;
    }

    /// <summary>
    /// Writes the crops that actually have pixels and returns each one with the
    /// instant its file was written. A crop whose region falls outside the frame
    /// is not written, and is not reported as written.
    /// </summary>
    public static IReadOnlyList<HudCropWrite> SaveCrops(
        string? repoRoot,
        CaptureFrame frame,
        ScreenVitalObservation observation,
        PixelRect? targetRoi = null,
        PixelRect? clientArea = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !frame.HasPixels)
            return Array.Empty<HudCropWrite>();

        string directory = Path.Combine(repoRoot, RelativeDirectory);
        Directory.CreateDirectory(directory);

        var written = new List<HudCropWrite>(4);
        WriteIfNotEmpty(directory, "hp_latest.bmp", frame, observation.HpRoi, written);
        WriteIfNotEmpty(directory, "mp_latest.bmp", frame, observation.MpRoi, written);
        if (targetRoi is { } target)
            WriteIfNotEmpty(directory, "target_latest.bmp", frame, target, written);
        if (clientArea is { } client)
            WriteIfNotEmpty(directory, "client_latest.bmp", frame, client, written);
        return written;
    }

    private static void WriteIfNotEmpty(string directory, string fileName, CaptureFrame frame, PixelRect rect, List<HudCropWrite> written)
    {
        string path = Path.Combine(directory, fileName);
        if (!WriteBmp(path, frame, rect))
            return;
        written.Add(new HudCropWrite(fileName, File.GetLastWriteTimeUtc(path)));
    }

    /// <summary>The panel preview bitmap's name, alongside the other named crops.</summary>
    public const string PanelPreviewFileName = "inventory_panel_latest.bmp";

    /// <summary>
    /// Writes the whole-client-area preview the inventory-panel calibration
    /// is read off (<c>inventory_panel_latest.bmp</c>), and returns the
    /// directory, or null when there was nothing to write.
    /// </summary>
    /// <remarks>
    /// Same evidence discipline as the crops above: slot crops are only
    /// as right as the picture they were measured off, and the whole panel is
    /// what shows the operator which way a mis-placed crop is wrong. The
    /// caller (the inventory-panel calibration probe) decides which area is
    /// the panel's; this only writes the pixels.
    /// </remarks>
    public static string? TrySavePanelPreview(string? repoRoot, CaptureFrame frame, PixelRect panelArea)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !frame.HasPixels)
            return null;

        string directory = Path.Combine(repoRoot, RelativeDirectory);
        Directory.CreateDirectory(directory);
        WriteBmp(Path.Combine(directory, PanelPreviewFileName), frame, panelArea);
        return directory;
    }

    /// <summary>The bag-panel preview bitmap's name (C-313) -- a separate file from
    /// <see cref="PanelPreviewFileName"/> so calibrating one panel never overwrites
    /// the other's evidence.</summary>
    public const string BagPanelPreviewFileName = "bag_panel_latest.bmp";

    /// <summary>
    /// Writes the whole-client-area preview the bag-panel calibration is read off
    /// (<c>bag_panel_latest.bmp</c>), and returns the directory, or null when there
    /// was nothing to write. Same recipe as <see cref="TrySavePanelPreview"/>, kept
    /// as its own method because the two panels are calibrated independently
    /// (<c>BagPanelRoiCalibration</c>, C-312) and must never share a preview file.
    /// </summary>
    public static string? TrySaveBagPanelPreview(string? repoRoot, CaptureFrame frame, PixelRect panelArea)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !frame.HasPixels)
            return null;

        string directory = Path.Combine(repoRoot, RelativeDirectory);
        Directory.CreateDirectory(directory);
        WriteBmp(Path.Combine(directory, BagPanelPreviewFileName), frame, panelArea);
        return directory;
    }

    /// <summary>
    /// Writes one crop as a 32-bit BMP.
    /// </summary>
    /// <remarks>
    /// A hand-written header rather than an imaging dependency: the runtime already
    /// holds the pixels as BGRA, which is exactly what a 32-bit BMP stores, so the
    /// format costs 54 bytes of header and no package. Rows are written bottom-up
    /// because that is the order a BMP with positive height is read back in; upside
    /// down crops would make the ROI look wrong when it was right.
    /// </remarks>
    private static bool WriteBmp(string path, CaptureFrame frame, PixelRect rect)
    {
        byte[] bgra = ScreenVitalReader.Crop(frame, rect);
        if (bgra.Length == 0 || rect.Width <= 0 || rect.Height <= 0)
            return false;

        int rowStride = rect.Width * 4;
        int pixelBytes = rowStride * rect.Height;
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), 54 + pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), rect.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), rect.Height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 32);

        using FileStream stream = File.Create(path);
        stream.Write(header);
        for (int y = rect.Height - 1; y >= 0; y--)
            stream.Write(bgra, y * rowStride, rowStride);
        return true;
    }
}

/// <summary>One crop written in a pass, and the instant its file was last written.</summary>
public readonly record struct HudCropWrite(string FileName, DateTime WrittenAtUtc);
