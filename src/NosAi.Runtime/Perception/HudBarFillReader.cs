using System;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Colour family of a HUD bar's fill. Grey, white and black never count: those
/// are the numerals drawn over the bar, its outline, or an empty region.
/// </summary>
public enum HudFillHue : byte
{
    /// <summary>
    /// The HP bar's family: red, orange, yellow and green. NosTale walks the bar
    /// across that whole arc as health drains, so a predicate that accepted only
    /// red or only green would stop reading the bar partway down.
    /// </summary>
    RedOrGreen = 0,

    /// <summary>The MP bar's family: blue through cyan.</summary>
    Blue = 1
}

/// <summary>Unsigned measure from pixels, before the classification gate.</summary>
public readonly record struct HudBarMeasure(double? Ratio, double Confidence, string? FailureReason);

/// <summary>
/// Reads a horizontal HUD bar from a BGRA crop.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a NosTale bar actually looks like</b>, measured on the real client
/// (<c>data/perception/crops/</c>, captured 1 Sep 2026 with the ROI on the HUD,
/// and kept as fixtures under <c>tests/NosAi.Runtime.Tests/Fixtures/</c>):
/// </para>
/// <list type="bullet">
/// <item>The fill is a <b>two-axis gradient</b> — bright at the top, dark at the
/// bottom, and yellow-green at the left running to deep green at the right.
/// Nothing about it is one flat colour.</item>
/// <item>The current and maximum are drawn <b>over the middle of the bar</b>, in
/// white with a black outline. Those glyphs punch holes through the fill that are
/// up to ten of the bar's twelve rows tall.</item>
/// </list>
/// <para>
/// The previous model asked each column to be at least 70% fill-coloured and
/// allowed the profile exactly one filled/empty transition. A full bar with
/// <c>7305/7305</c> written across it produced three transitions and was refused
/// as <c>noisy_bar_profile</c>; on the earlier whole-desktop ROI the same code
/// refused with <c>no_bar_signature</c>. Both refusals were the text, not the aim,
/// and T-03 had no way to tell the two apart from the reason string alone.
/// </para>
/// <para>
/// <b>The model used instead.</b> A column belongs to the fill if <i>any</i> of
/// its rows is in the family — the numerals cannot hide a bar that is still
/// visible above and below them — and the fill is the run of such columns from
/// the left edge, measured by its right-hand end. Interior holes are then
/// irrelevant by construction, which is the point: the text is interior, and the
/// thing being measured is the edge.
/// </para>
/// <para>
/// <b>What still separates a bar from noise.</b> A column of bar is one solid
/// vertical run, or two when a glyph splits it. Speckle — a checkerboard, dithered
/// wallpaper, a screenshot of foliage — breaks into many short runs in every
/// column. So a column is bar-like only when its family pixels form at most
/// <see cref="MaxRunsPerColumn"/> runs, and a crop whose columns mostly fail that
/// is refused. On the real crops not one column exceeds three runs; a checkerboard
/// gives four in every column of an eight-row crop.
/// </para>
/// <para>
/// <b>An empty bar reads UNKNOWN, not 0.</b> A crop with no family pixel anywhere
/// is indistinguishable from a crop that is not a bar, and calling that zero HP
/// would invent the most dangerous reading in the system out of a missing one.
/// Everything above roughly one column of fill is measurable, so this costs only
/// the case where the character is already dead.
/// </para>
/// <para>
/// <b>Still unconfirmed on the real client:</b> both fixture crops are of a full
/// bar, so the edge itself has been exercised only against painted partials. What
/// T-03 has to show is a drained bar reading a ratio that matches the numerals
/// beside it — and the empty groove staying below <see cref="MinChannel"/>, which
/// is the one assumption a full bar cannot check.
/// </para>
/// </remarks>
public static class HudBarFillReader
{
    public const int MinWidth = 24;
    public const int MinHeight = 2;

    /// <summary>
    /// Vertical runs of fill a single column may have and still be bar-like: one
    /// for a plain column, two where a numeral splits it, and a third for the
    /// anti-aliased edge of that numeral. Measured maximum on the real client: 3.
    /// </summary>
    public const int MaxRunsPerColumn = 3;

    /// <summary>
    /// How many columns may be speckle before the crop is not a bar. None of the
    /// real crops' 249 columns are; a checkerboard is all of them.
    /// </summary>
    public const double MaxSpeckleColumnRatio = 0.15;

    /// <summary>
    /// How much of the run up to the fill edge must actually be fill. The numerals
    /// can black out whole columns inside the fill, but they sit in the middle and
    /// cannot account for most of it.
    /// </summary>
    public const double MinFillCoverage = 0.55;

    /// <summary>
    /// Family pixels that may appear beyond the fill edge, as a fraction of the
    /// crop, before the profile is refused. Beyond the edge the bar is dark, so a
    /// scattering there means something else is in the ROI.
    /// </summary>
    public const double MaxBleedRatio = 0.05;

    /// <summary>Below this the pixel is outline or groove, not fill.</summary>
    internal const int MinChannel = 80;

    /// <summary>How decisively the losing channel must lose for the hue to count.</summary>
    internal const int ChannelMargin = 40;

    public static HudBarMeasure Measure(ReadOnlySpan<byte> bgra, int width, int height, HudFillHue hue)
    {
        if (width < MinWidth || height < MinHeight)
            return new HudBarMeasure(null, 0, "crop_too_small");

        var expected = width * height * 4;
        if (bgra.Length < expected)
            return new HudBarMeasure(null, 0, "crop_truncated");

        var hit = new bool[width];
        var speckle = 0;
        var edge = -1;

        for (var x = 0; x < width; x++)
        {
            var runs = 0;
            var previous = false;
            for (var y = 0; y < height; y++)
            {
                var i = (y * width + x) * 4;
                var matches = MatchesHue(bgra[i + 2], bgra[i + 1], bgra[i], hue);
                if (matches && !previous)
                    runs++;
                previous = matches;
            }

            if (runs == 0)
                continue;

            // Many short runs is what dithering and foliage look like; a bar is a
            // band, whole or split by a numeral.
            if (runs > MaxRunsPerColumn)
            {
                speckle++;
                continue;
            }

            hit[x] = true;
            edge = x;
        }

        if (speckle > width * MaxSpeckleColumnRatio)
            return new HudBarMeasure(null, 0, "noisy_bar_profile");

        if (edge < 0)
            return new HudBarMeasure(null, 0, "no_bar_signature");

        var filled = 0;
        for (var x = 0; x <= edge; x++)
        {
            if (hit[x])
                filled++;
        }

        var coverage = filled / (double)(edge + 1);
        if (coverage < MinFillCoverage)
            return new HudBarMeasure(null, 0, "noisy_bar_profile");

        var ratio = (edge + 1) / (double)width;

        // Confidence tracks how cleanly the fill held together, since that is the
        // only thing the pixels say about how far to trust the edge.
        var confidence = 0.86 + 0.04 * Math.Min(1.0, (coverage - MinFillCoverage) / (1.0 - MinFillCoverage));
        return new HudBarMeasure(ratio, Math.Round(confidence, 4), null);
    }

    /// <summary>
    /// Whether a pixel belongs to the bar's colour family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated as "which channel loses", not "which channel wins", because that is
    /// what survives a gradient. Across the HP bar's whole arc — red, orange,
    /// yellow, green — blue is the channel that stays down; across the MP bar's,
    /// blue is the channel that stays up. The previous predicate asked green to
    /// beat red by 20, which the measured yellow-green at the left of a full HP bar
    /// (215,238,51) cleared by three, and a client rendering a shade warmer would
    /// not have cleared at all.
    /// </para>
    /// <para>
    /// <see cref="MinChannel"/> keeps the outline and the empty groove out: both
    /// are dark, and a dark pixel carries no hue worth trusting.
    /// </para>
    /// </remarks>
    internal static bool MatchesHue(byte r, byte g, byte b, HudFillHue hue)
    {
        return hue switch
        {
            // Blue clearly loses to whichever of red and green is leading.
            HudFillHue.RedOrGreen => Math.Max(r, g) >= MinChannel && b + ChannelMargin <= Math.Max(r, g),
            // Blue clearly leads red, and is not merely riding a bright cyan.
            HudFillHue.Blue => b >= MinChannel && b >= r + ChannelMargin && b + 20 >= g,
            _ => false
        };
    }
}
