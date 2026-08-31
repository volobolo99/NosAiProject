namespace NosAi.ControlPanel;

/// <summary>Operator-facing network facts from settings and session kind. Not a probe of the LAN.</summary>
internal static class NetworkInspect
{
    public static IReadOnlyList<DisplayField> Inspect(
        OperatorSettings settings,
        SessionKind kind,
        string? sessionDetail,
        string? lastFailure,
        bool? apiListening,
        bool? guardListening)
    {
        var discovery = settings.Discovery ? "attiva (UDP discovery, il telefono trova il PC)" : "disattivata";
        var bind = settings.GuardLoopbackOnly ? "solo loopback (USB/tunnel)" : "LAN consentita";
        return
        [
            new DisplayField("Sessione console", Mode(kind), "DERIVED"),
            new DisplayField("Dettaglio sessione", string.IsNullOrWhiteSpace(sessionDetail) ? "UNKNOWN" : sessionDetail, string.IsNullOrWhiteSpace(sessionDetail) ? "UNKNOWN" : "DERIVED"),
            new DisplayField("API operatore", settings.DashboardPort.ToString(), "DERIVED"),
            Listen("API in ascolto (127.0.0.1)", apiListening),
            new DisplayField("Porta Guard", settings.GuardPort.ToString(), "DERIVED"),
            Listen("Guard in ascolto (127.0.0.1)", guardListening),
            new DisplayField("Timeout operazione (ms)", settings.OperationTimeoutMs.ToString(), "DERIVED"),
            new DisplayField("Discovery", discovery, "DERIVED"),
            new DisplayField("Bind Guard", bind, "DERIVED"),
            new DisplayField("Processo client cercato", settings.ClientProcessName, "DERIVED"),
            new DisplayField("Ultimo errore rete", string.IsNullOrWhiteSpace(lastFailure) ? "nessuno in questa sessione" : lastFailure, "DERIVED")
        ];
    }

    private static DisplayField Listen(string label, bool? listening)
        => listening is null
            ? new DisplayField(label, "UNKNOWN · non ancora sondato", "UNKNOWN")
            : new DisplayField(label, listening.Value ? "sì" : "no", "LIVE");

    private static string Mode(SessionKind kind) => kind switch
    {
        SessionKind.Hosted => "OSPITATO",
        SessionKind.Attached => "COLLEGATO",
        _ => "OFFLINE"
    };
}
