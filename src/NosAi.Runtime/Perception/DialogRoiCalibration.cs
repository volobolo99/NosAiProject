using System.Globalization;
using System.Text;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Where a dialog-window panel sits on this operator's client, confirmed by
/// an operator against a real crop, plus the panel's own known-empty color
/// baseline -- the reference <see cref="DialogWindowReader"/> compares every
/// later reading against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a baseline instead of a signature like <see cref="TargetFrameReader"/>'s.</b>
/// <see cref="TargetFrameReader"/> can recognise a target's HP bar by its own
/// color family (<see cref="HudBarFillReader"/>'s red/green hue match)
/// because a target frame's bar has a fixed, game-defined look wherever it
/// appears. A dialog-window panel has no such fixed color signature this
/// repository can name without guessing at NosTale's actual UI art --
/// something no session without a real running client should do
/// (docs/agents/DEEPSEEK_TASKS.md "Candidati da investigare", the
/// investigation this type answers). What every panel appearing over a
/// previously empty region *does* share, regardless of its own colors, is
/// that the region's own pixels change from whatever they looked like when
/// nothing was open -- so calibration here records that empty state once
/// (a real crop, confirmed by an operator, exactly like
/// <see cref="TargetRoiCalibration"/>'s own crop-confirmation discipline),
/// and <see cref="DialogWindowReader"/> only ever asks "does this still look
/// like the confirmed-empty baseline, or not."
/// </para>
/// <para>
/// <b>Machine-specific, and therefore not committed.</b> Same reasoning as
/// <see cref="TargetRoiCalibration"/>: the fractions and the baseline colors
/// are of one client's UI theme at one resolution. Lives in gitignored
/// <c>data/</c>. A fresh clone reads <see cref="NotCalibratedReason"/>,
/// distinct from a broken read.
/// </para>
/// </remarks>
public sealed record DialogRoiCalibration
{
    /// <summary>Where the calibration lives, relative to the repository root.</summary>
    public const string RelativePath = "data/perception/dialog-roi.calibration";

    /// <summary>Reported by every consumer while no calibration exists.</summary>
    public const string NotCalibratedReason = "dialog_roi_not_calibrated";

    private const string Magic = "nosai-dialog-roi";
    private const int Version = 1;

    private DialogRoiCalibration(
        bool isCalibrated,
        double x,
        double y,
        double width,
        double height,
        int clientWidth,
        int clientHeight,
        DateTime? calibratedAtUtc,
        double baselineMeanB,
        double baselineMeanG,
        double baselineMeanR)
    {
        IsCalibrated = isCalibrated;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        CalibratedAtUtc = calibratedAtUtc;
        BaselineMeanB = baselineMeanB;
        BaselineMeanG = baselineMeanG;
        BaselineMeanR = baselineMeanR;
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
    public int ClientWidth { get; }

    /// <summary>The client area the fractions were measured against, in pixels.</summary>
    public int ClientHeight { get; }

    /// <summary>When the operator confirmed the crop, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>Mean blue channel value (0-255) of the confirmed-empty ROI crop.</summary>
    public double BaselineMeanB { get; }

    /// <summary>Mean green channel value (0-255) of the confirmed-empty ROI crop.</summary>
    public double BaselineMeanG { get; }

    /// <summary>Mean red channel value (0-255) of the confirmed-empty ROI crop.</summary>
    public double BaselineMeanR { get; }

    /// <summary>The state before the first calibration pass.</summary>
    public static DialogRoiCalibration Uncalibrated { get; } =
        new(false, 0, 0, 0, 0, 0, 0, null, 0, 0, 0);

    /// <summary>
    /// A calibration the operator has confirmed against a crop of their own
    /// client, taken while no dialog window was open -- the baseline mean
    /// channel values are that crop's own average color, not a guessed
    /// constant.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When the region is not inside the client area, or a baseline channel
    /// mean falls outside a valid 0-255 pixel range.
    /// </exception>
    public static DialogRoiCalibration Confirmed(
        double x, double y, double width, double height,
        int clientWidth, int clientHeight,
        DateTime calibratedAtUtc,
        double baselineMeanB, double baselineMeanG, double baselineMeanR)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The region has no extent.");
        if (x < 0 || y < 0 || x + width > 1.0 || y + height > 1.0)
            throw new ArgumentOutOfRangeException(nameof(x), "The region falls outside the client area.");
        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth), "The client area has no extent.");
        if (baselineMeanB is < 0 or > 255 || baselineMeanG is < 0 or > 255 || baselineMeanR is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(baselineMeanB), "A baseline channel mean must be a valid 0-255 pixel value.");

        return new DialogRoiCalibration(
            true, x, y, width, height, clientWidth, clientHeight, calibratedAtUtc,
            baselineMeanB, baselineMeanG, baselineMeanR);
    }

    /// <summary>
    /// The calibrated region in pixels, or null when there is no calibration.
    /// </summary>
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
    public static DialogRoiCalibration Load(string path, out string? failureReason)
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
            failureReason = $"dialog_roi_unreadable:{ex.GetType().Name}";
            return Uncalibrated;
        }

        if (lines.Length < 2 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "dialog_roi_header_unrecognised";
            return Uncalibrated;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "dialog_roi_header_unrecognised";
            return Uncalibrated;
        }

        if (version != Version)
        {
            failureReason = $"dialog_roi_version_unsupported:{version}";
            return Uncalibrated;
        }

        string[] fields = lines[1].Split(' ');
        if (fields.Length != 10
            || !TryFraction(fields[0], out double x)
            || !TryFraction(fields[1], out double y)
            || !TryFraction(fields[2], out double width)
            || !TryFraction(fields[3], out double height)
            || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !DateTime.TryParse(fields[6], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)
            || !TryFraction(fields[7], out double baselineMeanB)
            || !TryFraction(fields[8], out double baselineMeanG)
            || !TryFraction(fields[9], out double baselineMeanR))
        {
            failureReason = "dialog_roi_entry_malformed";
            return Uncalibrated;
        }

        try
        {
            return Confirmed(x, y, width, height, clientWidth, clientHeight, at, baselineMeanB, baselineMeanG, baselineMeanR);
        }
        catch (ArgumentOutOfRangeException)
        {
            failureReason = "dialog_roi_entry_malformed";
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
            .Append(X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Width.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Height.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientWidth.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientHeight.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append(' ')
            .Append(BaselineMeanB.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(BaselineMeanG.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(BaselineMeanR.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }

    private static bool TryFraction(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
