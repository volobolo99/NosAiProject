using System.Globalization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Teaches the glyph atlas from a captured frame, using a reading that did not
/// come from the screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>The loop this closes.</b> The world channel reads the player's HP from the
/// wire and publishes it LIVE. The screen shows the same number, printed over the
/// HP bar. Pairing the two teaches the atlas what each numeral looks like, once —
/// and from then on the screen reads HP by itself.
/// </para>
/// <para>
/// That independence is the point, and it is worth being precise about why. The
/// wire is authoritative for what the <i>server</i> knows and silent about what
/// only the client knows: the player's own position never arrives from the server,
/// and neither does whether the player has a target
/// (<c>docs/PROTOCOLLO_NOSTALE.md</c>). Those need a second source that watches
/// the client itself. A screen reader that can only produce ratios is not that
/// source — ADR-0012 wants numbers, and Gate 3 plans on HP and max HP as integers.
/// Training is what turns the one into the other, and the wire is the only label
/// available that was checked against this exact HUD.
/// </para>
/// <para>
/// <b>It is supervision, not agreement.</b> After training, a screen reading that
/// disagrees with the wire is a real disagreement worth surfacing, because the two
/// paths no longer share anything but the client. Before training there was
/// nothing to disagree.
/// </para>
/// </remarks>
public static class HudGlyphTraining
{
    /// <summary>
    /// Trains the HP numerals from one frame, labelled by a reading of the same
    /// vitals taken somewhere other than the screen.
    /// </summary>
    /// <param name="atlas">Trained in place; untouched when the pass is refused.</param>
    /// <param name="frame">The captured frame the HUD is in.</param>
    /// <param name="current">Current HP, from the wire.</param>
    /// <param name="maximum">Maximum HP, from the wire.</param>
    /// <param name="clientArea">
    /// Where the game draws inside the frame. Null means the client fills it,
    /// which is true only of a fullscreen client — and training against the wrong
    /// hundred pixels writes permanent nonsense into the atlas, so this matters
    /// more here than anywhere else in the perception path.
    /// </param>
    public static HudGlyphTrainingResult TrainHpFromFrame(
        HudGlyphAtlas atlas,
        CaptureFrame frame,
        int current,
        int maximum,
        PixelRect? clientArea = null)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(frame);

        if (maximum <= 0 || current < 0 || current > maximum)
            return HudGlyphTrainingResult.Refused($"label_out_of_range:{current}_of_{maximum}");

        IReadOnlyList<byte[]> glyphs = ScreenVitalReader.ExtractGlyphs(frame, RoiKind.PlayerHpBar, clientArea);
        return atlas.Train(glyphs, FormatVitalText(current, maximum));
    }

    /// <summary>
    /// Trains from a classified pair, refusing any label that was not actually
    /// observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>LIVE, and nothing else.</b> The atlas is persistent, so a wrong lesson
    /// outlives the run that taught it, and each classification fails here for its
    /// own reason:
    /// </para>
    /// <list type="bullet">
    /// <item><c>SIMULATED</c> and <c>UNKNOWN</c> are not readings of this HUD at
    /// all.</item>
    /// <item><c>CACHED</c> is a real reading of an earlier moment. <c>stat</c>
    /// arrives on change rather than on a schedule, so a retained value is usually
    /// still current — but "usually" is the wrong standard for a label that is
    /// written to disk and believed thereafter, and a single dropped packet during
    /// combat is enough to pair the frame with the previous HP.</item>
    /// <item><c>DERIVED</c> is refused because it is what the screen reader itself
    /// publishes. Training the screen on a label the screen produced would teach it
    /// to agree with itself, and the whole value of this path is that the two
    /// sources are independent afterwards.</item>
    /// </list>
    /// </remarks>
    public static HudGlyphTrainingResult TrainHpFromObservedVitals(
        HudGlyphAtlas atlas,
        CaptureFrame frame,
        ClassifiedValue<int> current,
        ClassifiedValue<int> maximum,
        PixelRect? clientArea = null)
    {
        ArgumentNullException.ThrowIfNull(atlas);

        if (!current.HasValue || !maximum.HasValue)
            return HudGlyphTrainingResult.Refused(
                $"label_not_observed:{current.FailureReason ?? maximum.FailureReason ?? "unknown"}");

        if (current.Source != DataSourceKind.Live || maximum.Source != DataSourceKind.Live)
            return HudGlyphTrainingResult.Refused($"label_not_live:{current.Source.ToWire()}");

        return TrainHpFromFrame(atlas, frame, current.Value, maximum.Value, clientArea);
    }

    /// <summary>How the client prints a vital pair: <c>current/maximum</c>.</summary>
    public static string FormatVitalText(int current, int maximum) =>
        string.Create(CultureInfo.InvariantCulture, $"{current}/{maximum}");
}
