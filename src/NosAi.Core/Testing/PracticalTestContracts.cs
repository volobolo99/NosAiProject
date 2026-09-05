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
    ResilienceSafety,
    HardwareRuntime,
    SafetyGate,
    GuardTrust,
    RuntimeHealth,
    SnapshotFreshness,
    ProvenanceIntegrity,
    EvidenceJournal,
    RecoveryReconnect,
    OperatorControl,
    EndToEndCertification
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

/// <summary>Canonical operator test catalog. Runtime owns execution and safety.</summary>
public static class PracticalTestCatalog
{
    public static IReadOnlyList<PracticalTestDefinition> All { get; } =
    [
        new("T1", PracticalTestKind.AttachObservation, "Attach & Live Observation", "Client privato di test avviato", "Nessuna, salvo richiesta esplicita", TimeSpan.FromMinutes(2), "Snapshot fresco con provenance", true),
        new("T2", PracticalTestKind.ScreenVision, "Screen / Vision", "Finestra client visibile", "Mantieni il client visibile e svolgi lo scenario richiesto", TimeSpan.FromMinutes(3), "Frame reale + ROI/detection coerenti", true),
        new("T3", PracticalTestKind.NetworkObservation, "Network Observation", "Client con traffico osservabile", "Genera traffico normale del client", TimeSpan.FromMinutes(3), "Traffico client-side correlato e timestampato", true),
        new("T4", PracticalTestKind.WorldModel, "World Model", "Almeno una sorgente live disponibile", "Cambia uno stato visibile solo quando richiesto", TimeSpan.FromMinutes(3), "WorldState aggiornato con provenance", true),
        new("T5", PracticalTestKind.Navigation, "Navigation", "Posizione osservabile e area di test controllata", "Porta il personaggio nella destinazione indicata", TimeSpan.FromMinutes(5), "Posizione, percorso, avanzamento e replan osservabili", true),
        new("T6", PracticalTestKind.Combat, "Combat", "Scenario di combattimento controllato", "Entra nel combattimento quando richiesto", TimeSpan.FromMinutes(5), "Target, decisione, Guard, Execute, Verify e re-observe", true),
        new("T7", PracticalTestKind.QuestInteraction, "Quest / Interaction", "Obiettivo o interazione osservabile", "Esegui l'interazione indicata", TimeSpan.FromMinutes(5), "Cambio di stato osservato e verificato", true),
        new("T8", PracticalTestKind.CharacterInventory, "Character / Inventory", "Dati personaggio/inventario osservabili", "Apri o modifica lo stato solo quando richiesto", TimeSpan.FromMinutes(5), "Delta di stato con provenance", true),
        new("T9", PracticalTestKind.AutonomousLoop, "Autonomous Loop", "Prerequisiti live delle capacità necessarie", "Nessun intervento salvo richiesta", TimeSpan.FromMinutes(10), "Observe → plan → guard → safety → execute → verify → re-observe", true),
        new("T10", PracticalTestKind.ResilienceSafety, "Resilience / Safety", "Runtime attivo e perturbazione controllata", "Esegui la perturbazione esplicitamente indicata", TimeSpan.FromMinutes(5), "Fail-closed, watchdog e recovery", true),
        new("T11", PracticalTestKind.HardwareRuntime, "Hardware / Runtime", "Runtime attivo", "Nessuna", TimeSpan.FromMinutes(2), "CPU/RAM/GPU/VRAM e runtime health osservati senza valori sintetici", false),
        new("T12", PracticalTestKind.SafetyGate, "Safety Gate", "Runtime attivo", "Nessuna", TimeSpan.FromMinutes(2), "Safety policy coerente e fail-closed", false),
        new("T13", PracticalTestKind.GuardTrust, "Guard / Trust", "Runtime attivo", "Nessuna", TimeSpan.FromMinutes(2), "Stato Guard/trust osservato e non aggirabile", false),
        new("T14", PracticalTestKind.RuntimeHealth, "Runtime Health", "Runtime attivo", "Nessuna", TimeSpan.FromMinutes(2), "Stato runtime e correlazione snapshot coerenti", false),
        new("T15", PracticalTestKind.SnapshotFreshness, "Snapshot Freshness", "Endpoint runtime raggiungibile", "Nessuna", TimeSpan.FromMinutes(2), "Timestamp fresco entro soglia e correlazione presente", false),
        new("T16", PracticalTestKind.ProvenanceIntegrity, "Provenance Integrity", "Snapshot disponibile", "Nessuna", TimeSpan.FromMinutes(2), "Valori osservati classificati con sorgente/stato; UNKNOWN preservato", false),
        new("T17", PracticalTestKind.EvidenceJournal, "Evidence Journal", "Runtime/event log disponibile", "Nessuna", TimeSpan.FromMinutes(2), "Evidenza persistente leggibile senza gap non dichiarati", false),
        new("T18", PracticalTestKind.RecoveryReconnect, "Recovery / Reconnect", "Runtime controllato", "Scollega/ricollega solo quando richiesto", TimeSpan.FromMinutes(5), "Perdita osservazione → stato sicuro → reconnect senza dati inventati", false),
        new("T19", PracticalTestKind.OperatorControl, "Operator Control", "Dashboard attiva", "Esegui esclusivamente i comandi operatore richiesti", TimeSpan.FromMinutes(5), "Comando autenticato e soggetto a Guard/Trust/Safety", false),
        new("T20", PracticalTestKind.EndToEndCertification, "End-to-End Certification", "T1-T19 rilevanti superati con evidenza", "Esegui la procedura fisica finale nel server privato", TimeSpan.FromMinutes(15), "Catena completa, riproducibile e senza canali privilegiati", true)
    ];
}
