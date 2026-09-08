using System;
using System.Globalization;

namespace NosAi.ControlPanel;

/// <summary>
/// Pure logic behind the target-ROI calibration card. It lives here, not in the window
/// handler, so unit tests can call it without instantiating the window and without any
/// WPF dependency: everything it needs is passed in as plain values.
/// </summary>
internal static class TargetRoiCalibrationCard
{
    /// <summary>Location of the latest target crop, relative to the repository root.</summary>
    public const string CropRelativePath = "data/perception/crops/target_latest.bmp";

    /// <summary>
    /// Validates the four box fractions edited on the card. On success returns true and
    /// fills the out values; on failure returns false and <paramref name="refusal"/> names
    /// the guilty fraction (x, y, w or h) and the violated rule.
    /// </summary>
    public static bool TryValidateFractions(
        string? xText,
        string? yText,
        string? wText,
        string? hText,
        out double x,
        out double y,
        out double w,
        out double h,
        out string? refusal)
    {
        x = 0d;
        y = 0d;
        w = 0d;
        h = 0d;
        refusal = null;

        // Each of the four must be a decimal readable with Float + InvariantCulture.
        if (!double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
        {
            refusal = $"x non è un numero valido: '{xText}'.";
            return false;
        }

        if (!double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
        {
            refusal = $"y non è un numero valido: '{yText}'.";
            return false;
        }

        if (!double.TryParse(wText, NumberStyles.Float, CultureInfo.InvariantCulture, out w))
        {
            refusal = $"w non è un numero valido: '{wText}'.";
            return false;
        }

        if (!double.TryParse(hText, NumberStyles.Float, CultureInfo.InvariantCulture, out h))
        {
            refusal = $"h non è un numero valido: '{hText}'.";
            return false;
        }

        // x and y must lie in [0, 1).
        if (x < 0d || x >= 1d)
        {
            refusal = "x deve essere compreso tra 0 (incluso) e 1 (escluso).";
            return false;
        }

        if (y < 0d || y >= 1d)
        {
            refusal = "y deve essere compreso tra 0 (incluso) e 1 (escluso).";
            return false;
        }

        // w and h must lie in (0, 1].
        if (w <= 0d || w > 1d)
        {
            refusal = "w deve essere maggiore di 0 e minore o uguale a 1.";
            return false;
        }

        if (h <= 0d || h > 1d)
        {
            refusal = "h deve essere maggiore di 0 e minore o uguale a 1.";
            return false;
        }

        // The box must stay inside the unit square.
        if (x + w > 1d)
        {
            refusal = "x + w supera 1: il riquadro esce a destra.";
            return false;
        }

        if (y + h > 1d)
        {
            refusal = "y + h supera 1: il riquadro esce in basso.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Produces the line shown on the card about the candidate crop: nothing written yet,
    /// a crop older than the run (not this run's crop, with its date), or the fresh crop
    /// with its instant. Instants are formatted in InvariantCulture as yyyy-MM-dd HH:mm:ss UTC.
    /// </summary>
    public static string DescribeCrop(bool exists, DateTime writtenUtc, DateTime runStartedUtc)
    {
        if (!exists)
        {
            return $"Nessun ritaglio è stato scritto: manca {CropRelativePath}.";
        }

        if (writtenUtc < runStartedUtc)
        {
            return $"Il ritaglio non è di questa corsa: risale a {FormatInstant(writtenUtc)}, prima dell'avvio della corsa.";
        }

        return $"Ritaglio nuovo di questa corsa: {FormatInstant(writtenUtc)}.";
    }

    private static string FormatInstant(DateTime utc)
    {
        return utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
    }
}
