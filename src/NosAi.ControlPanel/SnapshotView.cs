using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

public sealed record DisplayField(string Label, string Value, string Source);

/// <summary>One rendering shape for hosted snapshots and HTTP-attached ones.</summary>
public sealed class SnapshotView
{
    public string RuntimeStatus { get; init; } = "Stopped";
    public string Warning { get; init; } = "";
    public string CapturedAt { get; init; } = "";
    public IReadOnlyList<DisplayField> Client { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Guard { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Hardware { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Safety { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Resilience { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> GameObservation { get; init; } = Array.Empty<DisplayField>();
    /// <summary>
    /// Operator-facing line: actuating or not, the full reason, and whether the
    /// verdict is terminal. The panel never offers a retry from this line.
    /// </summary>
    public string SessionAuthorityLine { get; init; } = AuthorityStatus(null, null, null);
    public ClassifiedValue<int?> ClientProcessId { get; init; } = ClassifiedValue<int?>.Unknown("runtime_not_connected");
    public ClassifiedValue<int> ObservationLastHp { get; init; } = ClassifiedValue<int>.Unknown("runtime_not_connected");
    public ClassifiedValue<int> ObservationLastMaxHp { get; init; } = ClassifiedValue<int>.Unknown("runtime_not_connected");
    public string WireLabel { get; init; } = ChannelView.WireLabel;
    public string SlotLabel { get; init; } = "UNKNOWN";
    public string SlotHint { get; init; } = "";
    public string PhoneReminder { get; init; } = ChannelView.PhoneReminder;
    public string ContractVersion { get; init; } = "";

    public static SnapshotView Empty(string reason)
    {
        var (slot, hint) = ChannelView.Slot(null, null, null);
        return new SnapshotView
        {
            RuntimeStatus = "Offline",
            Warning = reason,
            Client = [new DisplayField("Stato", "UNKNOWN", "UNKNOWN")],
            Guard =
            [
                new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
                new DisplayField("Slot", slot, "UNKNOWN"),
                new DisplayField("Sessione", "UNKNOWN", "UNKNOWN")
            ],
            Hardware = [new DisplayField("Piattaforma", "UNKNOWN", "UNKNOWN")],
            Safety = [new DisplayField("Esecuzione", "UNKNOWN", "UNKNOWN")],
            Resilience = ResilienceInspect.Inspect(null),
            GameObservation = ObservationUnknown("runtime_not_connected"),
            SessionAuthorityLine = AuthorityStatus(null, null, null),
            ClientProcessId = ClassifiedValue<int?>.Unknown("runtime_not_connected"),
            ObservationLastHp = ClassifiedValue<int>.Unknown("runtime_not_connected"),
            ObservationLastMaxHp = ClassifiedValue<int>.Unknown("runtime_not_connected"),
            SlotLabel = slot,
            SlotHint = hint
        };
    }

    public static SnapshotView From(Gate1CanonicalSnapshot snapshot)
    {
        var (slot, hint) = ChannelView.Slot(
            ChannelView.Flag(snapshot.Guard.Connected),
            ChannelView.Flag(snapshot.Guard.Authenticated),
            ChannelView.Text(snapshot.Guard.TerminationReason));
        return new SnapshotView
        {
            RuntimeStatus = snapshot.RuntimeStatus.ToString(),
            Warning = snapshot.Warning ?? snapshot.Client.Warning ?? "",
            CapturedAt = snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
            ContractVersion = snapshot.ContractVersion,
            SlotLabel = slot,
            SlotHint = hint,
        Client =
        [
            Field("Stato", snapshot.Client.Status, snapshot.Client.Attached),
            Field("Processo", snapshot.Client.ProcessName),
            Field("PID", snapshot.Client.ProcessId),
            Field("Finestra", snapshot.Client.WindowTitle),
            Field("Handle", snapshot.Client.WindowHandle),
            Field("Risponde", snapshot.Client.ProcessResponding),
            Field("Visibile", snapshot.Client.WindowVisible),
            Field("Gameplay", snapshot.Client.GameplayBaseline)
        ],
        Guard =
        [
            new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
            new DisplayField("Slot", slot, slot == "UNKNOWN" ? "UNKNOWN" : "DERIVED"),
            Field("Collegato", snapshot.Guard.Connected),
            Field("Autenticato", snapshot.Guard.Authenticated),
            Field("Sessione", snapshot.Guard.SessionId),
            Field("Heartbeat", snapshot.Guard.LastHeartbeatUtc),
            Field("Chiusura", snapshot.Guard.TerminationReason)
        ],
        Hardware =
        [
            Field("Piattaforma", snapshot.Hardware.Platform),
            Field("CPU", snapshot.Hardware.Cpu),
            Field("Core", snapshot.Hardware.LogicalCores),
            Field("RAM processo (MB)", snapshot.Hardware.ProcessWorkingSetMb),
            Field("RAM sistema (MB)", snapshot.Hardware.SystemRamMb),
            Field("GPU", snapshot.Hardware.Gpu),
            Field("VRAM (MB)", snapshot.Hardware.GpuMemoryMb),
            Field("Refresh (Hz)", snapshot.Hardware.DisplayRefreshHz),
            Field("OS", snapshot.Hardware.OsVersion)
        ],
        Safety =
        [
            Field("Input live", snapshot.Safety.LiveInputEnabled),
            Field("Iniezione pacchetti", snapshot.Safety.PacketInjectionEnabled),
            Field("Client sano richiesto", snapshot.Safety.RequireClientHealthy),
            Field("Guard richiesto", snapshot.Safety.RequireGuardApproval),
            Field("Esecuzione", snapshot.Safety.ExecutionMode),
            Field("Sessione attuante", snapshot.Safety.SessionActuating),
            Field("Motivo autorità", snapshot.Safety.SessionAuthorityReason),
            Field("Verdetto terminale", snapshot.Safety.SessionAuthorityTerminal),
            Field("Integrità runtime", snapshot.Safety.RuntimeIntegrity),
            Field("Integrità client", snapshot.Safety.ClientIntegrity)
        ],
        Resilience = ResilienceInspect.Inspect(snapshot.Resilience),
        GameObservation = Observation(snapshot.GameObservation),
        SessionAuthorityLine = AuthorityStatus(
            snapshot.Safety.SessionActuating.HasValue ? snapshot.Safety.SessionActuating.Value : null,
            snapshot.Safety.SessionAuthorityReason.HasValue ? snapshot.Safety.SessionAuthorityReason.Value : snapshot.Safety.SessionAuthorityReason.FailureReason,
            snapshot.Safety.SessionAuthorityTerminal.HasValue ? snapshot.Safety.SessionAuthorityTerminal.Value : null),
        ClientProcessId = snapshot.Client.ProcessId,
        ObservationLastHp = snapshot.GameObservation.LastHp,
        ObservationLastMaxHp = snapshot.GameObservation.LastMaxHp
        };
    }

    private static IReadOnlyList<DisplayField> Observation(Gate1GameObservationView view) =>
    [
        Field("Canale osservazione gioco", view.Active),
        Field("Endpoint osservato", view.Endpoint),
        Field("Pacchetti osservati", view.PacketsObserved),
        Field("Pacchetti decodificati", view.PacketsDecoded),
        Field("Pacchetti non decodificabili", view.PacketsUndecodable),
        Field("Ultimo HP", view.LastHp),
        Field("Ultimo HP massimo", view.LastMaxHp),
        Field("Ultimo MP", view.LastMp),
        Field("Timestamp vitals", view.LastVitalsAtUtc)
    ];

    private static IReadOnlyList<DisplayField> ObservationUnknown(string reason) =>
    [
        new DisplayField("Canale osservazione gioco", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Endpoint osservato", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Pacchetti osservati", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Pacchetti decodificati", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Pacchetti non decodificabili", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo HP", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo HP massimo", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Ultimo MP", $"UNKNOWN · {reason}", "UNKNOWN"),
        new DisplayField("Timestamp vitals", $"UNKNOWN · {reason}", "UNKNOWN")
    ];

    /// <summary>
    /// One status line for the session verdict. Terminal is named because that is
    /// the difference between "try again in a moment" and "this will never work".
    /// The panel does not offer a retry from this line, and nothing here can mark
    /// a session as actuating.
    /// </summary>
    public static string AuthorityStatus(bool? actuating, string? reason, bool? terminal)
    {
        if (actuating is null)
            return "Sessione: UNKNOWN";
        if (actuating.Value)
            return "Sessione attuante";

        string named = string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : reason;
        return terminal == true
            ? $"Sessione non attuante, terminale: {named}"
            : $"Sessione non attuante: {named}";
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

    private static DisplayField Field<T>(string label, string raw, ClassifiedValue<T> provenance)
        => new(label, $"{raw} [{provenance.Source.ToWire()}]", provenance.Source.ToWire());
}
