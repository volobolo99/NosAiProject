using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;

namespace NosAi.ControlPanel;

/// <summary>Operator-facing combat row: last hit and the three-valued target.</summary>
internal sealed class CombatView
{
    public string LastHitLine { get; init; } = "";
    public string TargetLine { get; init; } = "";
    public string SelectedTargetLine { get; init; } = "";
    public TargetFrameState TargetState { get; init; }
    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
}

/// <summary>
/// Last aggressor and the target frame as ADR-0018 names them: present, absent,
/// or unreadable with the reason. A classified bool is never printed as true/false.
/// </summary>
internal static class CombatInspect
{
    public const string PresentLabel = "presente";
    public const string AbsentLabel = "assente";
    public const string UnreadableLabel = "illeggibile";
    public const string NoObservationLabel = SurroundingsInspect.NoObservationLabel;

    /// <summary>
    /// Why the "which" row is UNKNOWN when the caller passed no selected target:
    /// the panel's snapshot view does not carry <c>selectedTarget</c> (yet), so
    /// nothing here claims the provider did not publish it.
    /// </summary>
    public const string SelectedTargetNotSurfacedReason = "selected_target_not_on_panel_snapshot";

    /// <summary>
    /// Formats the combat row. Ages are measured against <paramref name="nowUtc"/>
    /// the same way surroundings ages are: the number is the drawing.
    /// </summary>
    public static CombatView Inspect(
        ClassifiedValue<Aggressor>? hitBy,
        ClassifiedValue<bool>? hasTarget,
        DateTime nowUtc,
        ClassifiedValue<TargetedEntity>? selectedTarget = null)
    {
        DisplayField hitField = HitField(hitBy, nowUtc, out string hitLine);
        DisplayField targetField = TargetField(hasTarget, out string targetLine, out TargetFrameState state);
        DisplayField selectedField = SelectedTargetField(selectedTarget, out string selectedLine);
        return new CombatView
        {
            LastHitLine = hitLine,
            TargetLine = targetLine,
            SelectedTargetLine = selectedLine,
            TargetState = state,
            Fields = [hitField, targetField, selectedField]
        };
    }

    private static DisplayField HitField(
        ClassifiedValue<Aggressor>? hitBy,
        DateTime nowUtc,
        out string line)
    {
        if (hitBy is null || !hitBy.HasValue)
        {
            string reason = hitBy?.FailureReason ?? GameplayObservation.NotPublishedReason;
            line = $"Ultimo colpo: {NoObservationLabel} · {reason}";
            return new DisplayField("Ultimo colpo", $"UNKNOWN · {NoObservationLabel} · {reason}", "UNKNOWN");
        }

        Aggressor who = hitBy.Value;
        double age = SurroundingsInspect.AgeSeconds(hitBy.ObservedAtUtc, nowUtc);
        string ageLabel = SurroundingsInspect.AgeLabel(age);
        string source = hitBy.Source.ToWire();
        line = string.Create(CultureInfo.InvariantCulture,
            $"Ultimo colpo: id={who.EntityId} type={who.EntityType} età={ageLabel}");
        return new DisplayField("Ultimo colpo", $"{line} [{source}]", source);
    }

    private static DisplayField TargetField(
        ClassifiedValue<bool>? hasTarget,
        out string line,
        out TargetFrameState state)
    {
        if (hasTarget is null || !hasTarget.HasValue)
        {
            string reason = hasTarget?.FailureReason ?? GameplayObservation.NotPublishedReason;
            state = TargetFrameState.Unreadable;
            line = $"Bersaglio: {UnreadableLabel} · {reason}";
            return new DisplayField("Bersaglio", $"UNKNOWN · {UnreadableLabel} · {reason}", "UNKNOWN");
        }

        string source = hasTarget.Source.ToWire();
        if (hasTarget.Value)
        {
            state = TargetFrameState.Present;
            line = $"Bersaglio: {PresentLabel}";
            return new DisplayField("Bersaglio", $"{PresentLabel} [{source}]", source);
        }

        state = TargetFrameState.Absent;
        line = $"Bersaglio: {AbsentLabel}";
        return new DisplayField("Bersaglio", $"{AbsentLabel} [{source}]", source);
    }

    /// <summary>
    /// The wire's <i>which</i> beside the screen's <i>whether</i> (ADR-0018).
    /// <c>SelectedTarget</c> is sticky by nature -- it names the last entity the
    /// character acted on, not whether one is still selected -- so this row is
    /// labelled "ultimo" and never claims a live selection.
    /// </summary>
    private static DisplayField SelectedTargetField(
        ClassifiedValue<TargetedEntity>? selectedTarget,
        out string line)
    {
        if (selectedTarget is null || !selectedTarget.HasValue)
        {
            string reason = selectedTarget?.FailureReason ?? SelectedTargetNotSurfacedReason;
            line = $"Ultimo bersaglio: UNKNOWN · {reason}";
            return new DisplayField("Ultimo bersaglio", $"UNKNOWN · {reason}", "UNKNOWN");
        }

        TargetedEntity who = selectedTarget.Value;
        string source = selectedTarget.Source.ToWire();
        line = string.Create(CultureInfo.InvariantCulture,
            $"Ultimo bersaglio: id={who.EntityId} type={who.EntityType} [{source}]");
        return new DisplayField("Ultimo bersaglio", line, source);
    }
}
