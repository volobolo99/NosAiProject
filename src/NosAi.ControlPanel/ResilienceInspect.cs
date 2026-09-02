using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

/// <summary>Operator-facing recovery breaker. Unknown is never a quiet zero.</summary>
internal static class ResilienceInspect
{
    public static IReadOnlyList<DisplayField> Inspect(Gate1ResilienceView? view)
    {
        if (view is null)
            return UnknownAll(Gate1ResilienceView.NotConfiguredReason);

        return
        [
            Field("Stato breaker", view.State),
            Field("Fallimenti in finestra", view.FailuresInWindow),
            Field("Attesa prossimo tentativo (s)", view.CooldownRemainingSeconds),
            Field("Budget finestra", view.WindowSize),
            Field("Prove per chiudere", view.ProbeSuccessesToClose),
            Field("Cooldown base (s)", view.BaseCooldownSeconds),
            Field("Cooldown massimo (s)", view.MaxCooldownSeconds),
            Field("Cooldown in vigore (s)", view.CurrentCooldownSeconds),
            Field("Arresti", view.Halts)
        ];
    }

    public static IReadOnlyList<DisplayField> FromSnapshot(SnapshotView snapshot)
        => snapshot.Resilience.Count > 0
            ? snapshot.Resilience
            : UnknownAll(Gate1ResilienceView.NotConfiguredReason);

    private static DisplayField Field<T>(string label, ClassifiedValue<T> classified)
    {
        var source = classified.Source.ToWire();
        if (!classified.HasValue)
        {
            var reason = classified.FailureReason;
            return new DisplayField(label, string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : $"UNKNOWN · {reason}", "UNKNOWN");
        }

        return new DisplayField(label, $"{classified.Value} [{source}]", source);
    }

    private static IReadOnlyList<DisplayField> UnknownAll(string reason) =>
    [
        new DisplayField("Stato breaker", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Fallimenti in finestra", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Attesa prossimo tentativo (s)", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Budget finestra", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Prove per chiudere", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Cooldown base (s)", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Cooldown massimo (s)", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Cooldown in vigore (s)", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Arresti", $"UNKNOWN · {reason}", "UNKNOWN")
    ];
}
