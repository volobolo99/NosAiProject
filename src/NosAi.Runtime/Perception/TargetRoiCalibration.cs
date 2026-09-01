using System.Globalization;
using System.Text;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Where the target frame actually sits on this operator's client, confirmed by
/// looking at a crop of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="RoiSegmenter"/> places
/// <see cref="RoiKind.TargetHpBar"/> at fractions that were written as a plausible
/// guess and have never been checked against a running client. Only
/// <see cref="RoiKind.PlayerHpBar"/> has, by T-03, with the crop as evidence.
/// </para>
/// <para>
/// A bar reader aimed at the wrong region does not fail. It measures the wrong
/// pixels correctly, and <see cref="TargetFrameReader"/> cannot tell that from a
/// real reading — over empty HUD background it reports
/// <see cref="TargetFrameState.Absent"/>, which is a confident <i>no target</i> on
/// every frame. That is the worst of the three outcomes, worse than
/// <see cref="TargetFrameState.Unreadable"/>, which at least says so. Hence
/// ADR-0018: until this file exists, <c>HasTarget</c> stays UNKNOWN.
/// </para>
/// <para>
/// <b>Machine-specific, and therefore not committed.</b> The fractions are of one
/// client at one resolution on one display. It lives in gitignored <c>data/</c>
/// beside the glyph atlas and the T-03 crops, for the reason ADR-0017 gives for
/// the atlas. A fresh clone reads <c>target_roi_not_calibrated</c>, which is a
/// different state from broken and reports as one.
/// </para>
/// </remarks>
public sealed record TargetRoiCalibration
{
    /// <summary>Where the calibration lives, relative to the repository root.</summary>
    public const string RelativePath = "data/perception/target-roi.calibration";

    /// <summary>Reported by every consumer while no calibration exists.</summary>
    public const string NotCalibratedReason = "target_roi_not_calibrated";

    private const string Magic = "nosai-target-roi";
    private const int Version = 1;

    private TargetRoiCalibration(
        bool isCalibrated,
        double x,
        double y,
        double width,
        double height,
        int clientWidth,
        int clientHeight,
        DateTime? calibratedAtUtc)
    {
        IsCalibrated = isCalibrated;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        CalibratedAtUtc = calibratedAtUtc;
    }

    /// <summary>Whether a real calibration was loaded. False is not "use the guess".</summary>
    public bool IsCalibrated { get; }

    /// <summary>Left edge, as a fraction of the client area's width.</summary>
    public double X { get; }

    /// <summary>Top edge, as a fraction of the client area's height.</summary>
    public double Y { get; }

    /// <summary>Width, as a fraction of the client area's width.</summary>
    public double Width { get; }

    /// <summary>Height, as a fraction of the client area's height.</summary>
    public double Height { get; }

    /// <summary>The client area the fractions were measured against, in pixels.</summary>
    /// <remarks>
    /// Recorded so a later session at a different resolution can be recognised.
    /// The fractions themselves scale; a HUD that is laid out in pixels rather
    /// than proportionally does not, and this is what makes that checkable
    /// instead of silent.
    /// </remarks>
    public int ClientWidth { get; }

    /// <summary>The client area the fractions were measured against, in pixels.</summary>
    public int ClientHeight { get; }

    /// <summary>When the operator confirmed the crop, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>The state before the first calibration pass.</summary>
    public static TargetRoiCalibration Uncalibrated { get; } =
        new(false, 0, 0, 0, 0, 0, 0, null);

    /// <summary>
    /// A calibration the operator has confirmed against a crop of their own client.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When the region is not inside the client area. A region running off the
    /// edge would be clamped at read time into something the operator never
    /// looked at, which is the failure this whole type exists to prevent.
    /// </exception>
    public static TargetRoiCalibration Confirmed(
        double x, double y, double width, double height,
        int clientWidth, int clientHeight,
        DateTime calibratedAtUtc)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The region has no extent.");
        if (x < 0 || y < 0 || x + width > 1.0 || y + height > 1.0)
            throw new ArgumentOutOfRangeException(nameof(x), "The region falls outside the client area.");
        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth), "The client area has no extent.");

        return new TargetRoiCalibration(
            true, x, y, width, height, clientWidth, clientHeight, calibratedAtUtc);
    }

    /// <summary>
    /// The calibrated region in pixels, or null when there is no calibration.
    /// </summary>
    /// <remarks>
    /// Null rather than the <see cref="RoiSegmenter"/> guess: falling back to the
    /// guess is precisely how an unaimed reader starts publishing a confident
    /// <i>no target</i>.
    /// </remarks>
    public PixelRect? Resolve(PixelRect clientArea)
    {
        if (!IsCalibrated || clientArea.Width <= 0 || clientArea.Height <= 0)
            return null;

        int rx = clientArea.X + (int)Math.Round(X * clientArea.Width);
        int ry = clientArea.Y + (int)Math.Round(Y * clientArea.Height);
        int rw = Math.Max(1, (int)Math.Round(Width * clientArea.Width));
        int rh = Math.Max(1, (int)Math.Round(Height * clientArea.Height));
        return new PixelRect(rx, ry, rw, rh);
    }

    /// <summary>
    /// Loads the calibration at <paramref name="path"/>, or returns
    /// <see cref="Uncalibrated"/> with a reason.
    /// </summary>
    /// <remarks>
    /// A missing file is not an error: it is the state before the operator has
    /// aimed the reader, and it is reported as such so they are told to calibrate
    /// rather than told something is broken.
    /// </remarks>
    public static TargetRoiCalibration Load(string path, out string? failureReason)
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
            failureReason = $"target_roi_unreadable:{ex.GetType().Name}";
            return Uncalibrated;
        }

        if (lines.Length < 2 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "target_roi_header_unrecognised";
            return Uncalibrated;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "target_roi_header_unrecognised";
            return Uncalibrated;
        }

        if (version != Version)
        {
            failureReason = $"target_roi_version_unsupported:{version}";
            return Uncalibrated;
        }

        string[] fields = lines[1].Split(' ');
        if (fields.Length != 7
            || !TryFraction(fields[0], out double x)
            || !TryFraction(fields[1], out double y)
            || !TryFraction(fields[2], out double width)
            || !TryFraction(fields[3], out double height)
            || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !DateTime.TryParse(fields[6], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
        {
            failureReason = "target_roi_entry_malformed";
            return Uncalibrated;
        }

        try
        {
            return Confirmed(x, y, width, height, clientWidth, clientHeight, at);
        }
        catch (ArgumentOutOfRangeException)
        {
            // A file that says the region is off the client area is a file written
            // by something other than a confirmed calibration.
            failureReason = "target_roi_entry_malformed";
            return Uncalibrated;
        }
    }

    /// <summary>Writes the calibration, creating the directory if needed.</summary>
    /// <exception cref="InvalidOperationException">
    /// When there is nothing to write. Persisting the uncalibrated state would
    /// make the next load report a calibration that nobody confirmed.
    /// </exception>
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
            .Append(X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Width.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Height.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientWidth.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientHeight.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }

    private static bool TryFraction(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
