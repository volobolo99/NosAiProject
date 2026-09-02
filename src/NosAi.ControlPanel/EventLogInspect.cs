using NosAi.Runtime.Gate2;

namespace NosAi.ControlPanel;

/// <summary>
/// Operator-facing event-log health. An incomplete log is shown as incomplete.
/// </summary>
internal static class EventLogInspect
{
    public const string IncompleteLabel = "INCOMPLETO";

    public static IReadOnlyList<DisplayField> Inspect(EventLogHealth? health)
    {
        if (health is null)
            return UnknownAll("event_log_not_read");

        if (!health.Readable)
        {
            string reason = health.FailureReason ?? "event_log_unreadable";
            return
            [
                new DisplayField("Salute registro", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Completo", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Eventi", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Sequenza", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Gap", $"UNKNOWN · {reason}", "UNKNOWN")
            ];
        }

        if (!health.Exists)
        {
            return
            [
                new DisplayField("Salute registro", "nessuno store ancora [DERIVED]", "DERIVED"),
                new DisplayField("Completo", "sì [DERIVED]", "DERIVED"),
                new DisplayField("Eventi", "0 [DERIVED]", "DERIVED"),
                new DisplayField("Sequenza", "vuoto [DERIVED]", "DERIVED"),
                new DisplayField("Gap", "nessuno [DERIVED]", "DERIVED")
            ];
        }

        DisplayField complete = health.IsComplete
            ? new DisplayField("Completo", "sì [DERIVED]", "DERIVED")
            : new DisplayField(
                "Completo",
                $"{IncompleteLabel} — {health.GapCount} interruzioni, {health.LostEventCount} eventi persi [DERIVED]",
                "DERIVED");

        string sequence = health.FirstSequence is null
            ? "vuoto [DERIVED]"
            : $"{health.FirstSequence}..{health.LastSequence} [DERIVED]";

        var fields = new List<DisplayField>
        {
            new DisplayField(
                "Salute registro",
                health.IsComplete ? "audit trail completo [DERIVED]" : $"{IncompleteLabel} — audit trail con buchi [DERIVED]",
                "DERIVED"),
            complete,
            new DisplayField("Eventi", $"{health.EventCount} [DERIVED]", "DERIVED"),
            new DisplayField("Sequenza", sequence, "DERIVED")
        };

        if (health.IsComplete)
        {
            fields.Add(new DisplayField("Gap", "nessuno [DERIVED]", "DERIVED"));
        }
        else
        {
            foreach (var gap in health.Gaps)
            {
                fields.Add(new DisplayField(
                    $"Gap dopo seq {gap.AfterSequence}",
                    $"{gap.LostCount} persi ({gap.Reason}) [DERIVED]",
                    "DERIVED"));
            }
        }

        return fields;
    }

    private static IReadOnlyList<DisplayField> UnknownAll(string reason) =>
    [
        new DisplayField("Salute registro", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Completo", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Eventi", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Sequenza", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Gap", $"UNKNOWN · {reason}", "UNKNOWN")
    ];
}
