namespace NosAi.Core.WorldModel;

/// <summary>
/// Identificatori fortemente tipizzati del World Model. Sono tutti <c>uint</c>
/// per compatibilità con <see cref="NosAi.Core.EntitySnapshot.EntityId"/> e con
/// <see cref="NosAi.Core.Planning.PlanStep.TargetEntityId"/>; il valore 0 è
/// sempre "nessuno". Un id non è un fatto: identifica, non afferma.
/// </summary>
public readonly record struct EntityId(uint Value)
{
    public static EntityId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Identità di una mappa così come la conosce il client (map id di rete).</summary>
public readonly record struct MapId(uint Value)
{
    public static MapId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Identità di un portale all'interno di una mappa.</summary>
public readonly record struct PortalId(uint Value)
{
    public static PortalId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identità di catalogo (vnum) di mob, NPC, oggetto, skill o effetto. Distingue
/// "che cosa è" (template) da "quale istanza è" (<see cref="EntityId"/>).
/// </summary>
public readonly record struct TemplateId(uint Value)
{
    public static TemplateId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

public readonly record struct QuestId(uint Value)
{
    public static QuestId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

public readonly record struct ObjectiveId(uint Value)
{
    public static ObjectiveId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

public readonly record struct SkillId(uint Value)
{
    public static SkillId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Istanza di buff/debuff applicata al giocatore (non il template).</summary>
public readonly record struct StatusEffectId(uint Value)
{
    public static StatusEffectId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Identità di una proposta di azione. Univoca per snapshot; non è un handle di esecuzione.</summary>
public readonly record struct ActionId(uint Value)
{
    public static ActionId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Borse dell'inventario osservabili dal client. Gli ordinali seguono il tipo borsa del protocollo <c>ivn</c>.</summary>
public enum InventoryBag : byte
{
    Equipment = 0,
    Main = 1,
    Etc = 2,
    Miniland = 3,
    Specialist = 6,
    Costume = 7,
    Unknown = 255
}

/// <summary>Posizione di uno slot inventario: borsa + indice. Immutabile, 3 byte.</summary>
public readonly record struct InventorySlotId(InventoryBag Bag, ushort Index)
{
    public static InventorySlotId Unknown => new(InventoryBag.Unknown, 0);
    public bool IsUnknown => Bag == InventoryBag.Unknown;
    public override string ToString() => $"{Bag}:{Index}";
}
