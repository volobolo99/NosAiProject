using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Perception.Network;

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
    /// <summary>
    /// Entities the runtime observed, or UNKNOWN when the list itself was never
    /// published. An empty list is a looked-at absence, not this default.
    /// </summary>
    public ClassifiedValue<IReadOnlyList<SelectableEntity>> Entities { get; init; }
        = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown("runtime_not_connected");
    /// <summary>Who last hit the character, with the hit instant on the value.</summary>
    public ClassifiedValue<Aggressor> HitBy { get; init; }
        = ClassifiedValue<Aggressor>.Unknown("runtime_not_connected");
    /// <summary>Target as classified bool. The inspect maps it to three drawings.</summary>
    public ClassifiedValue<bool> HasTarget { get; init; }
        = ClassifiedValue<bool>.Unknown("runtime_not_connected");
    /// <summary>
    /// Map id and standing cell as classified readings. The map view draws
    /// from this and never attaches to the client on its own.
    /// </summary>
    public MapWorldReading MapWorld { get; init; } = MapWorldReading.Unknown("runtime_not_connected");
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
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown("runtime_not_connected"),
            HitBy = ClassifiedValue<Aggressor>.Unknown("runtime_not_connected"),
            HasTarget = ClassifiedValue<bool>.Unknown("runtime_not_connected"),
            MapWorld = MapWorldReading.Unknown("runtime_not_connected"),
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
        ObservationLastMaxHp = snapshot.GameObservation.LastMaxHp,
        Entities = GameplayEntities(snapshot),
        HitBy = GameplayHitBy(snapshot),
        HasTarget = GameplayHasTarget(snapshot),
        MapWorld = GameplayMapWorld(snapshot)
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

    private static ClassifiedValue<IReadOnlyList<SelectableEntity>> GameplayEntities(Gate1CanonicalSnapshot snapshot)
        => snapshot.Client.Gameplay is { } gameplay
            ? gameplay.Entities
            : ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(GameplayUnreadReason(snapshot));

    private static ClassifiedValue<Aggressor> GameplayHitBy(Gate1CanonicalSnapshot snapshot)
        => snapshot.Client.Gameplay is { } gameplay
            ? gameplay.HitBy
            : ClassifiedValue<Aggressor>.Unknown(GameplayUnreadReason(snapshot));

    private static ClassifiedValue<bool> GameplayHasTarget(Gate1CanonicalSnapshot snapshot)
        => snapshot.Client.Gameplay is { } gameplay
            ? gameplay.HasTarget
            : ClassifiedValue<bool>.Unknown(GameplayUnreadReason(snapshot));

    private static MapWorldReading GameplayMapWorld(Gate1CanonicalSnapshot snapshot)
    {
        if (snapshot.Client.Gameplay is not { } gameplay)
            return MapWorldReading.Unknown(GameplayUnreadReason(snapshot));

        return Split(gameplay.MapId, gameplay.StandingCell);
    }

    /// <summary>
    /// Standing cell is one classified point on the snapshot and two classified
    /// integers on the view, so a missing axis cannot be invented from the other.
    /// </summary>
    internal static MapWorldReading Split(ClassifiedValue<int> mapId, ClassifiedValue<MapPoint> standing)
    {
        if (!standing.HasValue)
        {
            string reason = standing.FailureReason ?? GameplayObservation.StandingCellNotReadReason;
            return new MapWorldReading(
                mapId,
                ClassifiedValue<int>.Unknown(reason),
                ClassifiedValue<int>.Unknown(reason));
        }

        return new MapWorldReading(
            mapId,
            ClassifyAxis(standing.Value.X, standing),
            ClassifyAxis(standing.Value.Y, standing));
    }

    private static ClassifiedValue<int> ClassifyAxis(int value, ClassifiedValue<MapPoint> standing) => standing.Source switch
    {
        DataSourceKind.Live => ClassifiedValue<int>.Live(value, standing.ObservedAtUtc, standing.Warning),
        DataSourceKind.Derived => ClassifiedValue<int>.Derived(value, standing.ObservedAtUtc, standing.Warning),
        DataSourceKind.Cached => ClassifiedValue<int>.Cached(value, standing.ObservedAtUtc, standing.Warning),
        DataSourceKind.Simulated => ClassifiedValue<int>.Simulated(value, standing.ObservedAtUtc, standing.Warning),
        _ => ClassifiedValue<int>.Unknown(standing.FailureReason ?? GameplayObservation.StandingCellNotReadReason)
    };

    private static string GameplayUnreadReason(Gate1CanonicalSnapshot snapshot)
        => snapshot.Client.GameplayBaseline.FailureReason ?? GameplayObservation.NotPublishedReason;

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
