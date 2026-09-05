using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;

namespace NosAi.Runtime.Perception.Fusion;

/// <summary>
/// Turns existing Network / Memory / Screen / Local readings into the fusion
/// input set. Normalization never invents a value and never upgrades provenance.
/// </summary>
public static class SensorNormalizer
{
    /// <summary>The pointer could not be read. Not "no target".</summary>
    public const string TargetPointerUnreadReason = "target_pointer_not_read";

    /// <summary>
    /// Network channel as published by a wire-only provider. The observation is
    /// accepted as-is: this method does not fill gaps from other sensors.
    /// </summary>
    public static GameplayObservation? FromNetwork(GameplayObservation? observation) => observation;

    /// <summary>
    /// Memory channel as published by a memory-only (or memory-decorating) provider.
    /// Same rule: the classified fields stand, including their UNKNOWN reasons.
    /// </summary>
    public static GameplayObservation? FromMemory(GameplayObservation? observation) => observation;

    /// <summary>
    /// Screen vitals as published by <see cref="ScreenVitalReader"/>. Numeric
    /// HP/MP stay UNKNOWN until the glyph atlas recognises both current and
    /// maximum; a missing pair is "did not look", not a zero.
    /// </summary>
    public static ScreenWorldReading FromScreenVitals(
        ScreenVitalObservation vitals,
        ClassifiedValue<bool>? hasTarget = null,
        ClassifiedValue<MapPoint>? projectedPlayerPosition = null,
        DateTime? lastPlayerAttackAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        return FromScreen(vitals.Hp, vitals.Mp, hasTarget, projectedPlayerPosition, lastPlayerAttackAtUtc);
    }

    /// <summary>
    /// Builds the screen channel. <paramref name="hasTarget"/> must already be
    /// the ADR-0018 composition when a frame was read; pass null when the screen
    /// did not look. Screen vitals stay DERIVED — a LIVE pair is refused rather
    /// than relabelled.
    /// </summary>
    public static ScreenWorldReading FromScreen(
        ScreenVitalPair? hp,
        ScreenVitalPair? mp,
        ClassifiedValue<bool>? hasTarget,
        ClassifiedValue<MapPoint>? projectedPlayerPosition,
        DateTime? lastPlayerAttackAtUtc)
    {
        return new ScreenWorldReading(
            RefuseLiveVitals(hp),
            RefuseLiveVitals(mp),
            hasTarget,
            projectedPlayerPosition,
            lastPlayerAttackAtUtc);
    }

    /// <summary>
    /// Screen <c>HasTarget</c> from the existing composer. An uncalibrated ROI
    /// is UNKNOWN; a missing frame is "did not look" (null), not Absent.
    /// </summary>
    public static ScreenWorldReading FromScreenFrame(
        TargetRoiCalibration calibration,
        TargetFrameObservation? frame,
        DateTime? lastPlayerAttackAtUtc,
        ScreenVitalPair? hp = null,
        ScreenVitalPair? mp = null,
        ClassifiedValue<MapPoint>? projectedPlayerPosition = null)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        ClassifiedValue<bool>? hasTarget = frame is { } observed
            ? TargetStateComposer.Compose(calibration, observed, lastPlayerAttackAtUtc)
            : null;

        return FromScreen(hp, mp, hasTarget, projectedPlayerPosition, lastPlayerAttackAtUtc);
    }

    /// <summary>
    /// Memory <c>HasTarget</c> from the client's target pointer (ADR-0021).
    /// Non-zero is a target; zero is no target; a missing read is UNKNOWN.
    /// The boolean is DERIVED: the client stores a pointer, not a flag.
    /// </summary>
    public static ClassifiedValue<bool> HasTargetFromPointer(
        TargetPointerReading? reading,
        DateTime observedAtUtc,
        string? failureReason = null)
    {
        if (reading is not { } pointer)
        {
            return new ClassifiedValue<bool>(
                default,
                DataSourceKind.Unknown,
                observedAtUtc,
                false,
                null,
                failureReason ?? TargetPointerUnreadReason);
        }

        return new ClassifiedValue<bool>(
            pointer.HasTarget,
            DataSourceKind.Derived,
            observedAtUtc,
            true,
            null,
            null);
    }

    /// <summary>Local channel: attach + occupancy + navigation evidence.</summary>
    public static LocalWorldReading FromLocal(
        ClassifiedValue<bool>? clientAttached,
        OccupancyView? occupancy,
        ClassifiedValue<bool>? navigationPathFound,
        DateTime observedAtUtc)
        => new(clientAttached, occupancy, navigationPathFound, observedAtUtc);

    /// <summary>Assembles the four channels. Null means the sensor was not bound.</summary>
    public static SensorObservationSet Bundle(
        GameplayObservation? network,
        GameplayObservation? memory,
        ScreenWorldReading? screen,
        LocalWorldReading? local)
        => new(FromNetwork(network), FromMemory(memory), screen, local);

    private static ScreenVitalPair? RefuseLiveVitals(ScreenVitalPair? pair)
    {
        if (pair is not { } vitals)
            return null;

        if (vitals.Current.HasValue && vitals.Current.Source == DataSourceKind.Live)
        {
            DateTime at = vitals.Current.ObservedAtUtc;
            return new ScreenVitalPair(
                new ClassifiedValue<int>(default, DataSourceKind.Unknown, at, false, null, "screen_vitals_must_not_be_live"),
                new ClassifiedValue<int>(default, DataSourceKind.Unknown, at, false, null, "screen_vitals_must_not_be_live"),
                vitals.Confidence,
                "screen_vitals_must_not_be_live");
        }

        if (vitals.Maximum.HasValue && vitals.Maximum.Source == DataSourceKind.Live)
        {
            DateTime at = vitals.Maximum.ObservedAtUtc;
            return new ScreenVitalPair(
                new ClassifiedValue<int>(default, DataSourceKind.Unknown, at, false, null, "screen_vitals_must_not_be_live"),
                new ClassifiedValue<int>(default, DataSourceKind.Unknown, at, false, null, "screen_vitals_must_not_be_live"),
                vitals.Confidence,
                "screen_vitals_must_not_be_live");
        }

        return vitals;
    }
}
