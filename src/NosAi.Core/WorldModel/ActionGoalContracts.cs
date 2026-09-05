using NosAi.Core.Planning;

namespace NosAi.Core.WorldModel;

/// <summary>
/// Tipo di azione proponibile. Superinsieme di
/// <see cref="NosAi.Core.CharacterControl.CharacterActionKind"/> (stessi nomi
/// per le voci comuni) esteso con le azioni di missione, inventario e mappa.
/// </summary>
public enum ActionKind : byte
{
    Unknown = 0,
    Move = 1,
    Stop = 2,
    BasicAttack = 3,
    UseSkill = 4,
    Interact = 5,
    Pickup = 6,
    UseItem = 7,
    Equip = 8,
    Unequip = 9,
    Talk = 10,
    AcceptQuest = 11,
    TurnInQuest = 12,
    TraversePortal = 13,
    Wait = 14,
    Recover = 15
}

/// <summary>Esito osservato di un'azione dopo Verify.</summary>
public enum ActionOutcomeStatus : byte
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2,
    TimedOut = 3,
    Rejected = 4,
    Interrupted = 5
}

/// <summary>Conseguenza prevista di un'azione. Esiste solo come SIMULATED o DERIVED: un valore LIVE qui è un errore di costruzione.</summary>
public readonly record struct PredictedEffect(
    float SuccessProbability,
    int ExpectedDurationMillis,
    int ExpectedHpDelta,
    int ExpectedMpDelta,
    float ExpectedRisk);

/// <summary>
/// Proposta di azione prodotta dalla cognizione (ranking, planner). È
/// deliberatamente priva di qualsiasi handle di esecuzione, tier di trust o flag di
/// bypass: la cognizione propone, il runtime (Guard → Trust → Safety) autorizza ed
/// esegue. Il mapping verso <see cref="ActionIntent"/> è responsabilità del runtime.
/// </summary>
/// <param name="Utility">Utilità stimata, DERIVED dal ranking.</param>
/// <param name="Prediction">Effetto previsto, SIMULATED dalla simulazione locale o DERIVED da storico.</param>
public sealed record ActionCandidate(
    ActionId Id,
    ActionKind Kind,
    Fact<EntityId> TargetEntity,
    Fact<MapPosition> TargetPosition,
    SkillId Skill,
    InventorySlotId ItemSlot,
    Fact<float> Utility,
    Fact<PredictedEffect> Prediction,
    long ProposedAtUnixMillis) : IFactCarrier
{
    /// <summary>
    /// Vero quando la proposta rispetta il contratto di provenienza: previsione e
    /// utilità mai LIVE o CACHED. Un candidato che dichiara una previsione come
    /// osservazione reale è etichettatura di dati simulati come live ed è respinto.
    /// </summary>
    public bool HasWellFormedProvenance
        => (Prediction.IsUnknown || Prediction.Source is FactSourceKind.Simulated or FactSourceKind.Derived)
           && (Utility.IsUnknown || Utility.Source is FactSourceKind.Simulated or FactSourceKind.Derived);

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(TargetEntity);
        summary.Add(TargetPosition);
        summary.Add(Utility);
        summary.Add(Prediction);
        return summary;
    }
}

/// <summary>Esito verificato di un'azione: l'evidenza che chiude il ciclo Execute → Verify → Re-observe.</summary>
/// <param name="PostconditionSatisfied">Fatto osservato (LIVE/CACHED) sulla post-condizione; UNKNOWN quando nessun sensore ha potuto verificarla.</param>
public sealed record ActionOutcome(
    ActionId Action,
    ActionOutcomeStatus Status,
    Fact<bool> PostconditionSatisfied,
    long StartedAtUnixMillis,
    long CompletedAtUnixMillis,
    string? Reason) : IFactCarrier
{
    public long DurationMillis => Math.Max(0, CompletedAtUnixMillis - StartedAtUnixMillis);

    /// <summary>Vero solo con stato Succeeded e post-condizione osservata vera. Un successo dichiarato senza evidenza non è un successo.</summary>
    public bool IsEvidencedSuccess
        => Status == ActionOutcomeStatus.Succeeded && PostconditionSatisfied.IsReal && PostconditionSatisfied.Value;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(PostconditionSatisfied);
        return summary;
    }
}

/// <summary>Famiglia strategica di un obiettivo (roadmap AP-08).</summary>
public enum GoalKind : byte
{
    Unknown = 0,
    Survive = 1,
    Recover = 2,
    Explore = 3,
    Navigate = 4,
    Kill = 5,
    Collect = 6,
    Quest = 7,
    Progress = 8,
    Farm = 9,
    Optimize = 10,
    Idle = 11
}

public enum GoalStatus : byte
{
    Unknown = 0,
    Proposed = 1,
    Active = 2,
    Suspended = 3,
    Completed = 4,
    Abandoned = 5
}

/// <summary>
/// Obiettivo strategico. Riusa <see cref="GoalId"/> e <see cref="GoalClass"/> del
/// planner esistente così l'orchestratore lessicografico non richiede conversioni.
/// </summary>
/// <param name="Priority">Priorità ∈ [0,1], DERIVED dalla utility strategica.</param>
/// <param name="DeadlineUnixMillis">Scadenza; UNKNOWN = nessuna scadenza conosciuta, non "nessuna scadenza".</param>
public sealed record GoalState(
    GoalId Id,
    GoalClass Class,
    GoalKind Kind,
    GoalStatus Status,
    Fact<float> Priority,
    Fact<EntityId> TargetEntity,
    Fact<MapId> TargetMap,
    Fact<MapPosition> TargetPosition,
    Fact<QuestId> Quest,
    Fact<long> DeadlineUnixMillis,
    long CreatedAtUnixMillis) : IFactCarrier
{
    /// <summary>Vero solo con scadenza conosciuta e superata.</summary>
    public bool IsObservedOverdueAt(long nowUnixMillis)
        => DeadlineUnixMillis.TryGetValue(out long deadline) && nowUnixMillis > deadline;

    public bool IsTerminal => Status is GoalStatus.Completed or GoalStatus.Abandoned;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Priority);
        summary.Add(TargetEntity);
        summary.Add(TargetMap);
        summary.Add(TargetPosition);
        summary.Add(Quest);
        summary.Add(DeadlineUnixMillis);
        return summary;
    }
}
