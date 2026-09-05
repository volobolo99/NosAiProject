namespace NosAi.Core.Testing;

public enum ObservationSource { Network, Memory, Screen, Local, Operator, Unknown }

public enum ObservationState { Observed, Derived, Predicted, Cached, Unknown }

public readonly record struct LiveObservationMetadata(
    ObservationSource Source,
    ObservationState State,
    DateTime ObservedAtUtc,
    double Confidence,
    long WorldStateVersion)
{
    public long AgeMs(DateTime nowUtc)
    {
        DateTime now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        DateTime observed = ObservedAtUtc.Kind == DateTimeKind.Utc ? ObservedAtUtc : ObservedAtUtc.ToUniversalTime();
        return Math.Max(0, (long)(now - observed).TotalMilliseconds);
    }
}

public enum PracticalTestKind
{
    AttachObservation,
    ScreenVision,
    NetworkObservation,
    WorldModel,
    Navigation,
    Combat,
    QuestInteraction,
    CharacterInventory,
    AutonomousLoop,
    ResilienceSafety
}

public enum PracticalTestResult { NotRun, Running, Pass, Fail, Unknown, Blocked }

public sealed record PracticalTestDefinition(
    string Id,
    PracticalTestKind Kind,
    string Name,
    string Preconditions,
    string OperatorAction,
    TimeSpan Timeout,
    string ExpectedObservation,
    bool RequiresLiveClient);

public sealed record PracticalTestRun(
    string RunId,
    string TestId,
    PracticalTestResult Result,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string SessionId,
    string RuntimeVersion,
    string EvidenceReference,
    string FailureReason);

/// <summary>Catalog consumed by the Dashboard Test Center. Runtime owns execution and safety.</summary>
public static class PracticalTestCatalog
{
    public static IReadOnlyList<PracticalTestDefinition> All { get; } =
    [
        new("T1", PracticalTestKind.AttachObservation, "Attach & Live Observation", "Client privato di test avviato", "Nessuna, salvo richiesta esplicita", TimeSpan.FromMinutes(2), "Snapshot fresco con provenance", true),
        new("T2", PracticalTestKind.ScreenVision, "Screen / Vision", "Finestra client visibile", "Esegui l'azione mostrata dalla Dashboard se richiesta", TimeSpan.FromMinutes(3), "Frame/ROI/detection coerenti", true),
        new("T3", PracticalTestKind.NetworkObservation, "Network Observation", "Client con traffico osservabile", "Genera traffico normale del client", TimeSpan.FromMinutes(3), "Traffico correlato e timestampato", true),
        new("T4", PracticalTestKind.WorldModel, "World Model", "Almeno una sorgente live disponibile", "Muovi o cambia stato solo quando richiesto", TimeSpan.FromMinutes(3), "WorldState aggiornato e provenance", true),
        new("T5", PracticalTestKind.Navigation, "Navigation", "Posizione osservabile", "Porta il personaggio in una zona di test quando richiesto", TimeSpan.FromMinutes(5), "Percorso, avanzamento e replan verificabili", true),
        new("T6", PracticalTestKind.Combat, "Combat", "Scenario di combattimento nel server privato", "Entra in combattimento quando richiesto", TimeSpan.FromMinutes(5), "Target, decisione, Guard, Execute e Verify", true),
        new("T7", PracticalTestKind.QuestInteraction, "Quest / Interaction", "Obiettivo/interazione osservabile", "Esegui l'interazione indicata", TimeSpan.FromMinutes(5), "Cambio stato osservato e verificato", true),
        new("T8", PracticalTestKind.CharacterInventory, "Character / Inventory", "Dati personaggio osservabili", "Apri schermate o usa item solo quando richiesto", TimeSpan.FromMinutes(5), "Stato inventario/progressione coerente", true),
        new("T9", PracticalTestKind.AutonomousLoop, "Autonomous Loop", "T1-T8 rilevanti disponibili", "Intervento solo se richiesto dal test", TimeSpan.FromMinutes(10), "Catena Observe-to-Verify completa", true),
        new("T10", PracticalTestKind.ResilienceSafety, "Resilience / Safety", "Runtime attivo e scenario controllato", "Esegui la perturbazione indicata", TimeSpan.FromMinutes(5), "Fail-closed, watchdog e recovery verificati", true)
    ];
}
