using System;

namespace NosAi.Runtime.Perception;

/// <summary>What the target frame's region said about itself.</summary>
public enum TargetFrameState : byte
{
    /// <summary>
    /// The pixels could not be read. <b>This is not "no target".</b> Every caller
    /// that collapses it into one is the failure this whole type exists to
    /// prevent: ADR-0016 skips the attack rules on an unknown target and runs the
    /// exploration rule on a known-absent one, so an unreadable region reported as
    /// absent walks the character away from a fight.
    /// </summary>
    Unreadable = 0,

    /// <summary>A target frame is on screen, with a measurable health bar.</summary>
    Present = 1,

    /// <summary>
    /// The region was readable and coherent, and holds no bar. See the remarks on
    /// <see cref="TargetFrameReader"/> for the one case this cannot separate from
    /// a present frame.
    /// </summary>
    Absent = 2
}

/// <summary>
/// One reading of the target frame's region.
/// </summary>
/// <param name="HpRatio">
/// The target's health as a fraction of its maximum, and non-null only when
/// <see cref="TargetFrameState.Present"/>. A ratio is all the pixels give: turning
/// it into absolute HP would need a maximum nobody read.
/// </param>
/// <param name="FailureReason">
/// Why the region could not be read, and non-null only when
/// <see cref="TargetFrameState.Unreadable"/>. An absent frame is an answer, not a
/// failure, so it carries no reason.
/// </param>
public readonly record struct TargetFrameReading(
    TargetFrameState State,
    double? HpRatio,
    double Confidence,
    string? FailureReason);

/// <summary>
/// Reads whether a target frame is on screen, from the pixels of its region.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>HasTarget</c> is the fact that separates reacting to
/// one's own health from fighting, and the wire never establishes it: no packet in
/// either capture carries it, and <c>ct</c> has no observed "target cleared"
/// counterpart, so a flag derived from it would be sticky and wrong with nothing
/// on the wire to correct it (<c>docs/PROTOCOLLO_NOSTALE.md</c>). The screen has
/// the property the wire lacks — the frame disappears — which is why the fact is
/// established here and only cross-checked against the wire.
/// </para>
/// <para>
/// <b>Not knowing is not knowing there is none.</b> The three states are kept
/// apart because the two failures they replace are opposites. The
/// <see cref="RoiSegmenter"/> proportions for <see cref="RoiKind.TargetHpBar"/>
/// have never been calibrated against a real client — only the player's HP bar
/// has, through T-03 — so this region may currently frame something else
/// entirely. A reader that answered <see cref="TargetFrameState.Absent"/> whenever
/// the pixels said nothing would report "no target" with total confidence while
/// pointed at the wrong part of the window.
/// </para>
/// <para>
/// <b>The one ambiguity that remains, stated rather than hidden.</b> An absent
/// frame and a present frame whose bar has drained to nothing produce the same
/// pixels: no fill anywhere. This reader calls both
/// <see cref="TargetFrameState.Absent"/>, because refusing them both would leave
/// <see cref="TargetFrameState.Absent"/> unreachable and the exploration rule
/// permanently unplannable — the reader would have half a job.
/// </para>
/// <para>
/// The case it costs is narrow and it is covered elsewhere: a target at zero
/// health is about to die, and the composition step that turns this reading into
/// <c>HasTarget</c> holds the wire's own evidence of the player attacking. A
/// recent hit against a screen that says absent is a disagreement between two
/// independent sources, and a disagreement yields UNKNOWN rather than either
/// answer. That check is where this ambiguity is paid for; it is not paid for
/// here, and a caller that uses this reading alone inherits it.
/// </para>
/// <para>
/// <b>What it does not do.</b> It does not locate the region, capture it, or
/// decide what the reading means: it is handed the pixels of one region and
/// answers about those pixels. Nothing here touches Win32, DXGI or the disk.
/// </para>
/// </remarks>
public static class TargetFrameReader
{
    /// <summary>
    /// The colour family of a target's health bar: red through green, the same arc
    /// the player's own bar walks as it drains.
    /// </summary>
    private const HudFillHue TargetBarHue = HudFillHue.RedOrGreen;

    /// <summary>
    /// How confident the underlying measure must be for a bar to count as a frame.
    /// </summary>
    /// <remarks>
    /// Set at the floor <see cref="HudBarFillReader"/> reports for a successful
    /// measure, so today it rejects nothing that reader accepts. It is not there to
    /// filter now — it is there so that a future measure which succeeds while
    /// saying it is unsure is refused rather than believed. Stating that plainly is
    /// better than implying a threshold does work it does not yet do.
    /// </remarks>
    public const double MinConfidence = 0.86;

    /// <summary>
    /// Reads the target frame's region from a BGRA crop.
    /// </summary>
    /// <param name="bgra">
    /// The pixels of the region alone, four bytes per pixel. A span longer than
    /// the region is accepted and its tail ignored, matching
    /// <see cref="HudBarFillReader"/>: two contradictory rules for the same pixels
    /// would be worse than one lenient one.
    /// </param>
    /// <returns>
    /// A reading that never throws. Every malformed input is
    /// <see cref="TargetFrameState.Unreadable"/> with a reason, because a caller
    /// handling an exception here would be deciding, under pressure, the very
    /// question this type is careful about.
    /// </returns>
    public static TargetFrameReading Read(ReadOnlySpan<byte> bgra, int width, int height)
    {
        HudBarMeasure measure = HudBarFillReader.Measure(bgra, width, height, TargetBarHue);

        if (measure.FailureReason is { } reason)
        {
            // The only refusal that is an answer rather than a failure. The bar
            // reader returns it when no pixel anywhere belongs to the family, which
            // for the player's own bar is genuinely unknown -- an empty bar and a
            // misaimed ROI look identical, and calling that zero HP would invent
            // the most dangerous reading in the system. The question asked here is
            // a different one: not how much health, but whether a frame is drawn.
            return reason == "no_bar_signature"
                ? new TargetFrameReading(TargetFrameState.Absent, null, measure.Confidence, null)
                : new TargetFrameReading(TargetFrameState.Unreadable, null, measure.Confidence, reason);
        }

        if (measure.Ratio is not { } ratio)
        {
            // A measure with neither a ratio nor a reason. Nothing produces this
            // today; if something ever does, it is a bug in the bar reader and must
            // not be read as an absent frame.
            return new TargetFrameReading(
                TargetFrameState.Unreadable, null, measure.Confidence, "bar_measure_incomplete");
        }

        if (measure.Confidence < MinConfidence)
        {
            return new TargetFrameReading(
                TargetFrameState.Unreadable, null, measure.Confidence, "bar_measure_low_confidence");
        }

        return new TargetFrameReading(
            TargetFrameState.Present, Math.Clamp(ratio, 0.0, 1.0), measure.Confidence, null);
    }
}
