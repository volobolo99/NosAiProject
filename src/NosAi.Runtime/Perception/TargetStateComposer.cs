using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>One reading of the target frame, and when the pixels were captured.</summary>
/// <param name="ObservedAtUtc">
/// When the frame was captured, not when the composer ran. The wire's contribution
/// is decided by which of the two sources is more recent, so a poll-time stamp
/// here would make an old screen reading look newer than the hit that contradicts
/// it (ADR-0016 makes the same argument for vitals).
/// </param>
public readonly record struct TargetFrameObservation(
    TargetFrameReading Reading,
    DateTime ObservedAtUtc);

/// <summary>Whatever can say when the player was last seen attacking, on the wire.</summary>
/// <remarks>
/// A timestamp rather than a flag: the wire never establishes the target here, and
/// the only question asked of it is whether a hit happened after the screen looked.
/// </remarks>
public interface IPlayerAttackObserver
{
    /// <summary>
    /// When the player was last observed attacking, or null when no such hit has
    /// been seen. Null is not "the player is not attacking".
    /// </summary>
    DateTime? LastPlayerAttackAtUtc { get; }
}

/// <summary>
/// Establishes <c>HasTarget</c> from the screen, checked against the wire.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0018. Neither source answers this alone. The wire has <c>ct</c> and
/// <c>su</c> but <b>no observed counterpart that clears a target</b>, so a flag
/// derived from it would go true once and stay true with nothing to correct it.
/// The screen has the opposite: the frame disappears, so it is the only source
/// that can say <i>no</i>.
/// </para>
/// <para>
/// So the screen establishes and the wire enters only as a contradiction. A hit
/// by the player more recent than a screen reading of
/// <see cref="TargetFrameState.Absent"/> means the two disagree, and the result is
/// UNKNOWN rather than a choice between them. A hit while the screen says
/// <see cref="TargetFrameState.Present"/> is agreement; a hit older than the
/// reading is history, and a target dropped after a hit is ordinary.
/// </para>
/// <para>
/// Pure and total: every combination returns a classified value, so each refusal
/// is testable without a client.
/// </para>
/// </remarks>
public static class TargetStateComposer
{
    /// <summary>The two sources do not agree, and neither is chosen over the other.</summary>
    public const string SourcesDisagreeReason = "target_sources_disagree";

    /// <summary>The reader failed and said why, but the reason was lost on the way.</summary>
    public const string UnreadableReason = "target_frame_unreadable";

    /// <summary>
    /// Composes the target state.
    /// </summary>
    /// <param name="calibration">
    /// Checked before anything is read. An uncalibrated ROI produces a self-assured
    /// <c>false</c>, which is worse than any failure that reports itself.
    /// </param>
    /// <param name="screen">The target-frame reading and when its pixels were captured.</param>
    /// <param name="lastPlayerAttackAtUtc">
    /// When the player was last seen attacking on the wire, or null. Only used to
    /// contradict; it never establishes the fact.
    /// </param>
    public static ClassifiedValue<bool> Compose(
        TargetRoiCalibration calibration,
        TargetFrameObservation screen,
        DateTime? lastPlayerAttackAtUtc)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        // Before the reading, not after: a region nobody aimed reports Absent over
        // empty HUD background, and that is a confident wrong answer on every
        // frame rather than an occasional failure.
        if (!calibration.IsCalibrated)
            return ClassifiedValue<bool>.Unknown(TargetRoiCalibration.NotCalibratedReason);

        TargetFrameReading reading = screen.Reading;

        switch (reading.State)
        {
            case TargetFrameState.Present:
                // A hit here agrees with the screen and changes nothing.
                return ClassifiedValue<bool>.Derived(true, screen.ObservedAtUtc);

            case TargetFrameState.Absent:
                // Only a hit that landed after the screen looked is a
                // disagreement. An older one is the target being dropped after a
                // hit, which is the ordinary course of a fight.
                return lastPlayerAttackAtUtc is { } attackedAt && attackedAt > screen.ObservedAtUtc
                    ? ClassifiedValue<bool>.Unknown(SourcesDisagreeReason)
                    : ClassifiedValue<bool>.Derived(false, screen.ObservedAtUtc);

            default:
                // Never false. ADR-0016's planner would walk to an exploration
                // waypoint mid-fight, which is the case that ADR exists to
                // prevent. The reader's own reason is carried through, because
                // "crop too small" and "ratio out of range" are different repairs.
                return ClassifiedValue<bool>.Unknown(reading.FailureReason ?? UnreadableReason);
        }
    }
}
