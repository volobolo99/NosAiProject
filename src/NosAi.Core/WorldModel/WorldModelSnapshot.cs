namespace NosAi.Core.WorldModel;

/// <summary>
/// Snapshot immutabile e versionato dell'intero World Model (AP-01): l'unico
/// stato semantico consumato dalla pianificazione. Ogni collezione è una
/// <see cref="ReadOnlyMemory{T}"/> di proprietà dello snapshot: il produttore non
/// deve riutilizzare il buffer dopo la pubblicazione.
/// </summary>
/// <param name="Contract">Versione del contratto con cui lo snapshot è stato costruito.</param>
/// <param name="StateVersion">Contatore monotono per sorgente: due snapshot con la stessa versione sono lo stesso stato.</param>
/// <param name="AssembledAtUnixMillis">Istante di assemblaggio. Non è l'età dei fatti: quella si legge da <see cref="Summarize"/>.</param>
/// <param name="SessionId">Sessione di gioco/replay a cui appartiene lo snapshot, per replay deterministico.</param>
public sealed record WorldModelSnapshot(
    ContractVersion Contract,
    long StateVersion,
    long AssembledAtUnixMillis,
    long SessionId,
    PlayerState Player,
    MapState Map,
    ReadOnlyMemory<MobState> Mobs,
    ReadOnlyMemory<NpcState> Npcs,
    ReadOnlyMemory<DropState> Drops,
    ReadOnlyMemory<QuestState> Quests,
    ReadOnlyMemory<InventoryItemState> Inventory,
    ReadOnlyMemory<EquipmentItemState> Equipment,
    ReadOnlyMemory<SkillState> Skills,
    ReadOnlyMemory<BuffState> Buffs,
    ReadOnlyMemory<DebuffState> Debuffs,
    ReadOnlyMemory<CooldownState> Cooldowns,
    ReadOnlyMemory<ResourceState> Resources,
    ReadOnlyMemory<ActionCandidate> ActionCandidates,
    ReadOnlyMemory<GoalState> Goals) : IFactCarrier
{
    /// <summary>Riassunto di tutti i fatti dello snapshot, senza allocazioni.</summary>
    public FactSummary Summarize()
    {
        FactSummary summary = Player.Summarize();
        FactSummary map = Map.Summarize();
        summary.Merge(in map);
        summary.AddAll(Mobs.Span);
        summary.AddAll(Npcs.Span);
        summary.AddAll(Drops.Span);
        summary.AddAll(Quests.Span);
        summary.AddAll(Inventory.Span);
        summary.AddAll(Equipment.Span);
        summary.AddAll(Skills.Span);
        summary.AddAll(Buffs.Span);
        summary.AddAll(Debuffs.Span);
        summary.AddAll(Cooldowns.Span);
        summary.AddAll(Resources.Span);
        summary.AddAll(ActionCandidates.Span);
        summary.AddAll(Goals.Span);
        return summary;
    }

    /// <summary>
    /// Riassunto dei soli fatti osservati (giocatore, mappa, entità, missioni,
    /// inventario, equipaggiamento, skill, effetti, cooldown, risorse). Esclude
    /// candidati e obiettivi, che sono per definizione derivati/simulati e non
    /// devono far apparire "simulato" uno stato del mondo interamente reale.
    /// </summary>
    public FactSummary SummarizeObserved()
    {
        FactSummary summary = Player.Summarize();
        FactSummary map = Map.Summarize();
        summary.Merge(in map);
        summary.AddAll(Mobs.Span);
        summary.AddAll(Npcs.Span);
        summary.AddAll(Drops.Span);
        summary.AddAll(Quests.Span);
        summary.AddAll(Inventory.Span);
        summary.AddAll(Equipment.Span);
        summary.AddAll(Skills.Span);
        summary.AddAll(Buffs.Span);
        summary.AddAll(Debuffs.Span);
        summary.AddAll(Cooldowns.Span);
        summary.AddAll(Resources.Span);
        return summary;
    }

    /// <summary>Vero quando lo snapshot è leggibile da un consumer compilato contro <see cref="ContractVersion.Current"/>.</summary>
    public bool IsContractReadable => Contract.IsReadableBy(ContractVersion.Current);

    /// <summary>Vero quando c'è abbastanza per pianificare: contratto leggibile e vitali del giocatore conosciuti.</summary>
    public bool IsPlannable => IsContractReadable && Player.HasVitals;

    /// <summary>Vero quando almeno un fatto osservato è SIMULATED. Uno basta: si può pianificare, non agire.</summary>
    public bool ContainsSimulatedObservation => SummarizeObserved().ContainsSimulated;

    /// <summary>
    /// Condizione necessaria (non autorizzazione) perché lo stato sostenga
    /// un'azione reale: contratto leggibile, vitali conosciuti, nessun fatto
    /// osservato simulato, fatto osservato più vecchio entro <paramref name="maxAgeMillis"/>.
    /// </summary>
    public bool IsActionable(long nowUnixMillis, long maxAgeMillis, float minConfidence = 0f)
        => IsPlannable && SummarizeObserved().IsActionable(nowUnixMillis, maxAgeMillis, minConfidence);

    /// <summary>Il motivo per cui non si può pianificare, o null quando si può.</summary>
    public string? UnplannableReason
    {
        get
        {
            if (!IsContractReadable) return "contract_version_incompatible";
            if (IsPlannable) return null;
            return Player.Hp.FailureReason ?? Player.MaxHp.FailureReason ?? Player.Mp.FailureReason ?? "world_state_incomplete";
        }
    }

    /// <summary>Copia con nuova versione di stato e istante di assemblaggio.</summary>
    public WorldModelSnapshot WithVersion(long stateVersion, long assembledAtUnixMillis)
        => this with { StateVersion = stateVersion, AssembledAtUnixMillis = assembledAtUnixMillis };

    public bool TryFindMob(EntityId id, out MobState mob)
    {
        ReadOnlySpan<MobState> mobs = Mobs.Span;
        for (int i = 0; i < mobs.Length; i++)
            if (mobs[i].Id == id) { mob = mobs[i]; return true; }
        mob = null!;
        return false;
    }

    public bool TryFindNpc(EntityId id, out NpcState npc)
    {
        ReadOnlySpan<NpcState> npcs = Npcs.Span;
        for (int i = 0; i < npcs.Length; i++)
            if (npcs[i].Id == id) { npc = npcs[i]; return true; }
        npc = null!;
        return false;
    }

    public bool TryFindQuest(QuestId id, out QuestState quest)
    {
        ReadOnlySpan<QuestState> quests = Quests.Span;
        for (int i = 0; i < quests.Length; i++)
            if (quests[i].Id == id) { quest = quests[i]; return true; }
        quest = null!;
        return false;
    }

    public bool TryFindCooldown(SkillId skill, out CooldownState cooldown)
    {
        ReadOnlySpan<CooldownState> cooldowns = Cooldowns.Span;
        for (int i = 0; i < cooldowns.Length; i++)
            if (cooldowns[i].Skill == skill) { cooldown = cooldowns[i]; return true; }
        cooldown = default;
        return false;
    }

    /// <summary>Snapshot in cui nulla è conosciuto. Pianificare su di esso è rifiutato, non indovinato.</summary>
    public static WorldModelSnapshot Unobserved(string reason, long assembledAtUnixMillis, long sessionId = 0) => new(
        ContractVersion.Current,
        0,
        assembledAtUnixMillis,
        sessionId,
        PlayerState.Unknown(reason, assembledAtUnixMillis),
        MapState.Unknown(reason, assembledAtUnixMillis),
        ReadOnlyMemory<MobState>.Empty,
        ReadOnlyMemory<NpcState>.Empty,
        ReadOnlyMemory<DropState>.Empty,
        ReadOnlyMemory<QuestState>.Empty,
        ReadOnlyMemory<InventoryItemState>.Empty,
        ReadOnlyMemory<EquipmentItemState>.Empty,
        ReadOnlyMemory<SkillState>.Empty,
        ReadOnlyMemory<BuffState>.Empty,
        ReadOnlyMemory<DebuffState>.Empty,
        ReadOnlyMemory<CooldownState>.Empty,
        ReadOnlyMemory<ResourceState>.Empty,
        ReadOnlyMemory<ActionCandidate>.Empty,
        ReadOnlyMemory<GoalState>.Empty);
}

/// <summary>
/// Sorgente in sola lettura dello snapshot corrente. Il produttore (fusione A2,
/// wiring A4) pubblica snapshot completi; i consumer non mutano mai lo stato.
/// </summary>
public interface IWorldModelSnapshotSource
{
    /// <summary>Lo snapshot più recente pubblicato. Mai null: in assenza di osservazioni è <see cref="WorldModelSnapshot.Unobserved"/>.</summary>
    WorldModelSnapshot Current { get; }
}
