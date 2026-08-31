using System.Buffers.Binary;
using System.IO;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>
/// Writes the last HUD crops so the operator can check the ROI. Not a provider
/// and not part of the Gate 1 snapshot. <c>data/</c> is gitignored.
/// </summary>
internal static class HudCropStore
{
    public const string RelativeDirectory = "data/perception/crops";

    public static string? TrySave(string? repoRoot, CaptureFrame frame, ScreenVitalObservation observation)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !frame.HasPixels)
            return null;

        var dir = Path.Combine(repoRoot, RelativeDirectory);
        Directory.CreateDirectory(dir);

        WriteBmp(Path.Combine(dir, "hp_latest.bmp"), frame, observation.HpRoi);
        WriteBmp(Path.Combine(dir, "mp_latest.bmp"), frame, observation.MpRoi);
        return dir;
    }

    private static void WriteBmp(string path, CaptureFrame frame, PixelRect rect)
    {
        var bgra = ScreenVitalReader.Crop(frame, rect);
        if (bgra.Length == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        var rowStride = rect.Width * 4;
        var pixelBytes = rowStride * rect.Height;
        var fileSize = 54 + pixelBytes;
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), rect.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), rect.Height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 32);

        using var stream = File.Create(path);
        stream.Write(header);
        for (var y = rect.Height - 1; y >= 0; y--)
            stream.Write(bgra, y * rowStride, rowStride);
    }
}
