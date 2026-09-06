namespace NosAi.Runtime.Perception;

/// <summary>
/// One reading of the vision channel: the vision-analog of
/// <c>NosAi.LiveIntegration.GameplayObservation</c> for the network channel.
/// AP-02/A1 (docs/ROADMAP_ESECUTIVA.md S:AP-02): unifies what the existing
/// Gate-era perception pipeline already produces --
/// <see cref="PerceptionPipeline"/>'s tracked entities,
/// <see cref="ScreenVitalReader"/>'s HUD bar/numeric readings, and
/// <see cref="TargetStateComposer"/>'s screen-established target presence --
/// into one typed observation, so a fusion step has a single vision-side
/// input to combine with the network side (<c>GameplayObservation</c>)
/// instead of three independently-shaped readers.
/// </summary>
/// <remarks>
/// This is a contract, not a new reader: every field here is produced by
/// pre-existing, already-tested Gate-era code (<see cref="PerceptionResult"/>,
/// <see cref="ScreenVitalObservation"/>, <see cref="TargetStateComposer.Compose"/>).
/// None of those readers are modified by this type.
/// </remarks>
/// <param name="Frame">The tracked-entity result of this cycle's vision pipeline run. <see cref="PerceptionResult.FrameAcquired"/> false means nothing below should be trusted as current.</param>
/// <param name="Vitals">HUD-derived HP/MP (bar fill and, once a glyph atlas is trained, numeric current/maximum). Always DERIVED or UNKNOWN per <see cref="ScreenVitalReader"/>'s own contract -- never LIVE.</param>
/// <param name="HasTarget">Screen-established target-frame presence (ADR-0018), already classified by <see cref="TargetStateComposer.Compose"/>.</param>
/// <param name="HasDialogWindow">
/// Screen-established dialog-window-panel presence, already classified by
/// <see cref="DialogWindowStateComposer.Compose"/>. No wire-side channel
/// exists to reconcile this against (see that composer's own remarks) --
/// the screen is the only source there is for this fact.
/// </param>
/// <param name="ObservedAtUtc">The instant this cycle's frame was processed.</param>
public sealed record VisualObservation(
    PerceptionResult Frame,
    ScreenVitalObservation Vitals,
    NosAi.Runtime.Contracts.ClassifiedValue<bool> HasTarget,
    NosAi.Runtime.Contracts.ClassifiedValue<bool> HasDialogWindow,
    DateTime ObservedAtUtc)
{
    /// <summary>Nothing was read this cycle (no frame, capture unavailable, ...). Every field says why -- never a fabricated reading.</summary>
    public static VisualObservation Unobserved(string reason, DateTime? observedAtUtc = null)
    {
        DateTime at = observedAtUtc ?? DateTime.UtcNow;
        NosAi.Runtime.Contracts.ClassifiedValue<double> unknownRatio = NosAi.Runtime.Contracts.ClassifiedValue<double>.Unknown(reason);
        var unknownBar = new ScreenBarFill(unknownRatio, Confidence: 0, FailureReason: reason);
        NosAi.Runtime.Contracts.ClassifiedValue<int> unknownCount = NosAi.Runtime.Contracts.ClassifiedValue<int>.Unknown(reason);
        var unknownPair = new ScreenVitalPair(unknownCount, unknownCount, Confidence: 0, FailureReason: reason);

        return new VisualObservation(
            new PerceptionResult(
                FrameIndex: 0,
                Source: NosAi.Runtime.Contracts.DataSourceKind.Unknown,
                FrameAcquired: false,
                Regions: System.Collections.Immutable.ImmutableArray<RegionOfInterest>.Empty,
                Entities: System.Collections.Immutable.ImmutableArray<TrackedEntity>.Empty,
                UnavailableReason: reason),
            new ScreenVitalObservation(
                HpRoi: new PixelRect(0, 0, 0, 0),
                MpRoi: new PixelRect(0, 0, 0, 0),
                HpBar: unknownBar,
                MpBar: unknownBar,
                Hp: unknownPair,
                Mp: unknownPair,
                HpGlyphs: 0,
                MpGlyphs: 0,
                TrainedGlyphs: 0),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown(reason),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown(reason),
            at);
    }
}
