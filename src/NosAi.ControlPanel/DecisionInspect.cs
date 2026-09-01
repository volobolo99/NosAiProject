using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;

namespace NosAi.ControlPanel;

/// <summary>Operator-facing Gate 3 loop. Unknown is never a quiet zero.</summary>
internal static class DecisionInspect
{
    public const string AttachedUnavailable = "decision_loop_only_when_hosted";

    public static IReadOnlyList<DisplayField> Inspect(SessionKind kind, Gate3LoopView? view)
    {
        if (kind != SessionKind.Hosted)
            return UnknownAll(AttachedUnavailable);

        if (view is null)
            return UnknownAll("decision_loop_not_configured");

        var fields = new List<DisplayField>
        {
            Field("In esecuzione", view.Running),
            Field("Cicli eseguiti", view.CyclesRun),
            Field("Ultimo esito", view.LastOutcome),
            Field("Ultima azione", view.LastAction),
            Field("Ultimo riepilogo", view.LastSummary),
            Field("Ultimo HP", view.LastHp),
            Field("Ultimo HP massimo", view.LastMaxHp),
            Field("Età osservazione (s)", view.LastObservationAgeSeconds),
            Acting(view.ActingEnabled)
        };

        if (view.OutcomeCounts.IsDefaultOrEmpty)
        {
            fields.Add(new DisplayField("Conteggi esito", "UNKNOWN · nessun ciclo ancora", "UNKNOWN"));
        }
        else
        {
            foreach (var entry in view.OutcomeCounts)
                fields.Add(Field($"Esito {entry.Key}", ClassifiedValue<long>.Derived(entry.Value)));
        }

        return fields;
    }

    private static DisplayField Acting(ClassifiedValue<bool> enabled)
    {
        if (!enabled.HasValue)
        {
            var reason = string.IsNullOrWhiteSpace(enabled.FailureReason)
                ? "UNKNOWN"
                : $"UNKNOWN · {enabled.FailureReason}";
            return new DisplayField("Azione", reason, "UNKNOWN");
        }

        // ADR-0016: the safe policy decides and does not act. That is the intended
        // first run, not a fault in the loop.
        return enabled.Value
            ? new DisplayField("Azione", "agisce [DERIVED]", "DERIVED")
            : new DisplayField("Azione", "decide, non agisce [DERIVED]", "DERIVED");
    }

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
        new DisplayField("In esecuzione", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Cicli eseguiti", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo esito", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultima azione", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo riepilogo", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo HP", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo HP massimo", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Età osservazione (s)", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Azione", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Conteggi esito", $"UNKNOWN · {reason}", "UNKNOWN")
    ];
}
