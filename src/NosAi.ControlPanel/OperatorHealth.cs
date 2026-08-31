namespace NosAi.ControlPanel;

/// <summary>
/// Operator health derived from a snapshot already captured. Does not invent an
/// event stream: that endpoint is not exposed.
/// </summary>
internal static class OperatorHealth
{
    public static IReadOnlyList<DisplayField> From(SnapshotView snapshot, SessionKind kind)
    {
        if (kind == SessionKind.Idle
            || string.Equals(snapshot.RuntimeStatus, "Offline", StringComparison.OrdinalIgnoreCase)
            || snapshot.Warning.StartsWith("runtime_unreachable", StringComparison.Ordinal))
        {
            return
            [
                new DisplayField("Contratto snapshot", string.IsNullOrWhiteSpace(snapshot.ContractVersion) ? "UNKNOWN" : snapshot.ContractVersion, "UNKNOWN"),
                new DisplayField("API ok", "UNKNOWN", "UNKNOWN"),
                new DisplayField("Stream eventi", "UNKNOWN · nessun endpoint EventBus esposto", "UNKNOWN")
            ];
        }

        var ok = snapshot.RuntimeStatus is "Healthy" or "Degraded";
        var contract = string.IsNullOrWhiteSpace(snapshot.ContractVersion)
            ? new DisplayField("Contratto snapshot", "UNKNOWN", "UNKNOWN")
            : new DisplayField("Contratto snapshot", snapshot.ContractVersion, "LIVE");

        return
        [
            contract,
            new DisplayField("API ok", ok ? "sì" : "no", "LIVE"),
            new DisplayField("Stato runtime", snapshot.RuntimeStatus, "LIVE"),
            new DisplayField("Stream eventi", "UNKNOWN · nessun endpoint EventBus esposto", "UNKNOWN")
        ];
    }
}
