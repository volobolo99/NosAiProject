namespace NosAi.Core.WorldModel;

/// <summary>Classe del personaggio come la espone il client.</summary>
public enum CharacterClass : byte
{
    Unknown = 0,
    Adventurer = 1,
    Swordsman = 2,
    Archer = 3,
    Mage = 4,
    MartialArtist = 5
}

/// <summary>Fase del tracking di un'entità osservata. Estende <see cref="NosAi.Core.EntitySnapshot.Phase"/> con semantica esplicita.</summary>
public enum TrackPhase : byte
{
    Unknown = 0,
    Tentative = 1,
    Confirmed = 2,
    Coasting = 3,
    Lost = 4
}

/// <summary>Atteggiamento di un mob verso il giocatore.</summary>
public enum Hostility : byte
{
    Unknown = 0,
    Passive = 1,
    Aggressive = 2,
    EngagedWithPlayer = 3,
    EngagedWithOther = 4
}

/// <summary>Ruolo funzionale di un NPC.</summary>
public enum NpcRole : byte
{
    Unknown = 0,
    Merchant = 1,
    QuestGiver = 2,
    Teleporter = 3,
    Storage = 4,
    Trainer = 5,
    Craftsman = 6,
    Decorative = 7
}

/// <summary>Indicatore di missione mostrato sopra un NPC.</summary>
public enum QuestMarker : byte
{
    Unknown = 0,
    None = 1,
    Available = 2,
    InProgress = 3,
    ReadyToTurnIn = 4
}

/// <summary>Cinematica osservata o derivata di un'entità.</summary>
public readonly record struct Kinematics(MapPosition Position, Velocity2 Velocity);

/// <summary>
/// Stato del giocatore controllato. Ogni campo è un fatto indipendente: la
/// posizione può essere LIVE dalla rete mentre l'HP è CACHED dallo schermo.
/// </summary>
public sealed record PlayerState(
    EntityId Id,
    Fact<MapId> Map,
    Fact<MapPosition> Position,
    Fact<Direction8> Facing,
    Fact<Velocity2> Velocity,
    Fact<CharacterClass> Class,
    Fact<ushort> Level,
    Fact<ushort> JobLevel,
    Fact<ushort> HeroLevel,
    Fact<int> Hp,
    Fact<int> MaxHp,
    Fact<int> Mp,
    Fact<int> MaxMp,
    Fact<bool> Alive,
    Fact<bool> InCombat,
    Fact<EntityId> Target,
    Fact<byte> Speed,
    Fact<long> Gold) : IFactCarrier
{
    /// <summary>Vero quando HP e MaxHp sono conosciuti e MaxHp &gt; 0.</summary>
    public bool TryGetHpRatio(out float ratio)
    {
        if (Hp.TryGetValue(out int hp) && MaxHp.TryGetValue(out int max) && max > 0)
        {
            ratio = Math.Clamp((float)hp / max, 0f, 1f);
            return true;
        }

        ratio = 0f;
        return false;
    }

    public bool TryGetMpRatio(out float ratio)
    {
        if (Mp.TryGetValue(out int mp) && MaxMp.TryGetValue(out int max) && max > 0)
        {
            ratio = Math.Clamp((float)mp / max, 0f, 1f);
            return true;
        }

        ratio = 0f;
        return false;
    }

    /// <summary>Vitali minimi per pianificare: HP, MaxHp, MP conosciuti (parità con <c>Gate3WorldState.HasVitals</c>).</summary>
    public bool HasVitals => Hp.HasValue && MaxHp.HasValue && Mp.HasValue;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Map);
        summary.Add(Position);
        summary.Add(Facing);
        summary.Add(Velocity);
        summary.Add(Class);
        summary.Add(Level);
        summary.Add(JobLevel);
        summary.Add(HeroLevel);
        summary.Add(Hp);
        summary.Add(MaxHp);
        summary.Add(Mp);
        summary.Add(MaxMp);
        summary.Add(Alive);
        summary.Add(InCombat);
        summary.Add(Target);
        summary.Add(Speed);
        summary.Add(Gold);
        return summary;
    }

    public static PlayerState Unknown(string reason, long observedAtUnixMillis = 0) => new(
        EntityId.None,
        Fact<MapId>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<Direction8>.Unknown(reason, observedAtUnixMillis),
        Fact<Velocity2>.Unknown(reason, observedAtUnixMillis),
        Fact<CharacterClass>.Unknown(reason, observedAtUnixMillis),
        Fact<ushort>.Unknown(reason, observedAtUnixMillis),
        Fact<ushort>.Unknown(reason, observedAtUnixMillis),
        Fact<ushort>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis),
        Fact<EntityId>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>Mob osservato o inseguito dal tracker. <see cref="ThreatEstimate"/> è per costruzione DERIVED o SIMULATED, mai LIVE.</summary>
public sealed record MobState(
    EntityId Id,
    Fact<TemplateId> Template,
    Fact<MapPosition> Position,
    Fact<Velocity2> Velocity,
    Fact<float> HpRatio,
    Fact<ushort> Level,
    Fact<Hostility> Hostility,
    Fact<EntityId> Target,
    Fact<bool> Alive,
    Fact<TrackPhase> Tracking,
    Fact<float> ThreatEstimate) : IFactCarrier
{
    public bool IsEngagedWithPlayer => Hostility.TryGetValue(out Hostility h) && h == WorldModel.Hostility.EngagedWithPlayer;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Template);
        summary.Add(Position);
        summary.Add(Velocity);
        summary.Add(HpRatio);
        summary.Add(Level);
        summary.Add(Hostility);
        summary.Add(Target);
        summary.Add(Alive);
        summary.Add(Tracking);
        summary.Add(ThreatEstimate);
        return summary;
    }

    public static MobState Unknown(EntityId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<Velocity2>.Unknown(reason, observedAtUnixMillis),
        Fact<float>.Unknown(reason, observedAtUnixMillis),
        Fact<ushort>.Unknown(reason, observedAtUnixMillis),
        Fact<Hostility>.Unknown(reason, observedAtUnixMillis),
        Fact<EntityId>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis),
        Fact<TrackPhase>.Unknown(reason, observedAtUnixMillis),
        Fact<float>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>NPC osservato. <see cref="Name"/> è un fatto perché arriva da OCR o da un catalogo, con la propria confidenza.</summary>
public sealed record NpcState(
    EntityId Id,
    Fact<TemplateId> Template,
    Fact<string> Name,
    Fact<MapPosition> Position,
    Fact<NpcRole> Role,
    Fact<QuestMarker> Marker,
    Fact<bool> Interactable) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Template);
        summary.Add(Name);
        summary.Add(Position);
        summary.Add(Role);
        summary.Add(Marker);
        summary.Add(Interactable);
        return summary;
    }

    public static NpcState Unknown(EntityId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<string>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<NpcRole>.Unknown(reason, observedAtUnixMillis),
        Fact<QuestMarker>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>Oggetto a terra. <see cref="Owner"/> è l'entità con diritto di raccolta; <see cref="ExpiresAtUnixMillis"/> è la scadenza stimata.</summary>
public sealed record DropState(
    EntityId Id,
    Fact<TemplateId> Item,
    Fact<int> Quantity,
    Fact<MapPosition> Position,
    Fact<EntityId> Owner,
    Fact<long> ExpiresAtUnixMillis,
    Fact<bool> Lootable) : IFactCarrier
{
    /// <summary>Vero solo se la scadenza è conosciuta ed è già passata. Scadenza UNKNOWN non significa "ancora presente".</summary>
    public bool IsExpiredAt(long nowUnixMillis)
        => ExpiresAtUnixMillis.TryGetValue(out long expires) && nowUnixMillis >= expires;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Item);
        summary.Add(Quantity);
        summary.Add(Position);
        summary.Add(Owner);
        summary.Add(ExpiresAtUnixMillis);
        summary.Add(Lootable);
        return summary;
    }

    public static DropState Unknown(EntityId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<EntityId>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}
