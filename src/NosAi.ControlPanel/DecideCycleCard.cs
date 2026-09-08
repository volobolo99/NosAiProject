using System;
using System.Globalization;

namespace NosAi.ControlPanel;

/// <summary>
/// Pure logic for the live decision-cycle card. The decision loop does not finish by itself:
/// it keeps running inside the Gate 1 host until the operator stops it, so the card has a
/// Start and a Stop action and shows rows while they arrive, not at the end.
/// </summary>
internal static class DecideCycleCard
{
    /// <summary>Classification for a line that reports no world state yet.</summary>
    public const string NoWorldStateKind = "NoWorldState";

    /// <summary>Classification for a line that reports a LIVE game observation being attached.</summary>
    public const string ObservationAttachedKind = "ObservationAttached";

    /// <summary>Classification for a line that reports execution being disabled at the Safety Gate.</summary>
    public const string ExecutionDisabledKind = "ExecutionDisabled";

    /// <summary>Classification for any other line.</summary>
    public const string OtherKind = "Other";

    private const string ObservationAttachedMarker = "Game observation attached";

    /// <summary>
    /// Text the card shows before the cycle starts: it explains that the cycle begins in
    /// NoWorldState, that a decision on LIVE readings is what the run must observe, and that
    /// ExecutionDisabled downstream of the Safety Gate is the intended outcome, not a fault.
    /// </summary>
    public static string DescribeExpectation()
    {
        return "Il ciclo parte e resta in ascolto finché non lo fermi tu. " +
               "All'avvio non c'è ancora uno stato di mondo: il ciclo rimane in NoWorldState finché non arrivano letture. " +
               "Quando arrivano letture LIVE il ciclo produce una decisione: è questo ciò che la prova deve osservare. " +
               "A valle del Safety Gate l'esito atteso è ExecutionDisabled, perché la politica tiene l'input disabilitato " +
               "e l'effector è DisabledActionEffector: non è un guasto, è il comportamento voluto.";
    }

    /// <summary>
    /// Validates the start arguments. The endpoint must have the form host:port with a port
    /// between 1 and 65535; the interval must be an integer between 500 and 60000 milliseconds,
    /// read with <see cref="CultureInfo.InvariantCulture"/>. Every refusal names the guilty
    /// field (endpoint or intervallo) and the rule it violated.
    /// </summary>
    public static bool TryValidateStart(string? endpoint, string? intervalMs, out int interval, out string? refusal)
    {
        interval = 0;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            refusal = "Endpoint mancante: il campo endpoint deve contenere host:porta.";
            return false;
        }

        int colonIndex = endpoint.LastIndexOf(':');
        if (colonIndex <= 0)
        {
            refusal = "Endpoint non valido: manca la porta, serve la forma host:porta.";
            return false;
        }

        string portText = endpoint.Substring(colonIndex + 1);
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
        {
            refusal = "Endpoint non valido: la porta non è un numero intero.";
            return false;
        }

        if (port < 1 || port > 65535)
        {
            refusal = "Endpoint non valido: la porta deve essere fra 1 e 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(intervalMs) ||
            !int.TryParse(intervalMs, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval))
        {
            refusal = "Intervallo non valido: deve essere un numero intero di millisecondi.";
            return false;
        }

        if (interval < 500)
        {
            refusal = "Intervallo non valido: deve essere almeno 500 millisecondi.";
            return false;
        }

        if (interval > 60000)
        {
            refusal = "Intervallo non valido: non può superare 60000 millisecondi.";
            return false;
        }

        refusal = null;
        return true;
    }

    // The four kinds are distinct facts and are not collapsed: a line that still has no world
    // state, a line that attaches a LIVE observation, a line that reports ExecutionDisabled
    // downstream of the Safety Gate, and any other line.
    public static string ClassifyDecideLine(string line)
    {
        var text = line ?? string.Empty;

        if (text.Contains(NoWorldStateKind, StringComparison.Ordinal))
        {
            return NoWorldStateKind;
        }

        if (text.Contains(ObservationAttachedMarker, StringComparison.Ordinal))
        {
            return ObservationAttachedKind;
        }

        if (text.Contains(ExecutionDisabledKind, StringComparison.Ordinal))
        {
            return ExecutionDisabledKind;
        }

        return OtherKind;
    }

    /// <summary>
    /// One-line summary of a finished run. It reports the counters and states whether the run
    /// finished by itself or was stopped by the operator. When no line was received it says so
    /// explicitly instead of reporting zeroes as if they were measurements.
    /// </summary>
    public static string SummariseRun(int noWorldState, int attached, int executionDisabled, int total, bool stoppedByOperator)
    {
        if (total == 0)
        {
            return stoppedByOperator
                ? "Nessuna riga ricevuta: la corsa è stata fermata dall'operatore prima che arrivasse qualsiasi riga."
                : "Nessuna riga ricevuta: la corsa è terminata da sola senza che il runtime stampasse nulla.";
        }

        return $"Righe ricevute: {total} totali, di cui {noWorldState} NoWorldState, " +
               $"{attached} osservazione collegata, {executionDisabled} ExecutionDisabled. " +
               $"Corsa {(stoppedByOperator ? "fermata dall'operatore" : "terminata da sola")}.";
    }
}
