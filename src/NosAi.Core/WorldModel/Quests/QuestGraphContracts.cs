namespace NosAi.Core.WorldModel.Quests;

/// <summary>
/// What a <see cref="QuestObjective"/> concretely asks the player to do
/// (docs/ROADMAP_ESECUTIVA.md S:AP-06: "Supportare travel, dialogue,
/// collect, kill, interact, deliver, quantities, prerequisites, rewards
/// e catene multi-step"). <see cref="QuestObjective"/> (AP-01) already
/// tracks progress (<c>CurrentCount</c>/<c>RequiredCount</c>/<c>Status</c>);
/// this enum plus <see cref="QuestObjectiveTarget"/> name what the count is
/// counting and where/who it concerns, which AP-01's shape deliberately
/// left unspecified.
/// </summary>
public enum QuestObjectiveKind
{
    /// <summary>Reach a position. Needs <see cref="QuestObjectiveTarget.Position"/>.</summary>
    Travel = 0,

    /// <summary>Talk to an NPC. Needs <see cref="QuestObjectiveTarget.Npc"/>.</summary>
    Dialogue = 1,

    /// <summary>Obtain a quantity of an item (drop, purchase, craft). Needs <see cref="QuestObjectiveTarget.Item"/>.</summary>
    Collect = 2,

    /// <summary>Defeat a quantity of a mob species. Needs <see cref="QuestObjectiveTarget.MobSpecies"/>.</summary>
    Kill = 3,

    /// <summary>Interact with a world object/NPC without necessarily conversing (a lever, a shrine, a gathering node). Needs <see cref="QuestObjectiveTarget.Npc"/>.</summary>
    Interact = 4,

    /// <summary>Hand a quantity of an item to an NPC. Needs both <see cref="QuestObjectiveTarget.Npc"/> and <see cref="QuestObjectiveTarget.Item"/>.</summary>
    Deliver = 5
}

/// <summary>
/// The kind-specific identity of one <see cref="QuestObjective"/> --
/// where to go, who to talk to, which item, which mob species. Which
/// fields are required depends on <see cref="Kind"/>; the constructor
/// enforces exactly the combination that kind needs, the same discipline
/// <see cref="Combat.CombatActionCandidate"/> already applies for
/// <see cref="Combat.CombatActionKind"/>.
/// </summary>
public sealed record QuestObjectiveTarget
{
    public QuestObjectiveKind Kind { get; init; }
    public WorldPosition? Position { get; init; }
    public EntityId? Npc { get; init; }
    public ItemId? Item { get; init; }
    public string? MobSpecies { get; init; }

    public QuestObjectiveTarget(
        QuestObjectiveKind kind,
        WorldPosition? position = null,
        EntityId? npc = null,
        ItemId? item = null,
        string? mobSpecies = null)
    {
        switch (kind)
        {
            case QuestObjectiveKind.Travel:
                if (position is null)
                    throw new ArgumentException($"{nameof(position)} is required for {kind} objectives.", nameof(position));
                RequireAbsent(npc, nameof(npc), kind);
                RequireAbsent(item, nameof(item), kind);
                if (mobSpecies is not null)
                    throw new ArgumentException($"{nameof(mobSpecies)} must not be set for {kind} objectives.", nameof(mobSpecies));
                break;

            case QuestObjectiveKind.Dialogue:
            case QuestObjectiveKind.Interact:
                if (npc is null)
                    throw new ArgumentException($"{nameof(npc)} is required for {kind} objectives.", nameof(npc));
                RequireAbsent(position, nameof(position), kind);
                RequireAbsent(item, nameof(item), kind);
                if (mobSpecies is not null)
                    throw new ArgumentException($"{nameof(mobSpecies)} must not be set for {kind} objectives.", nameof(mobSpecies));
                break;

            case QuestObjectiveKind.Collect:
                if (item is null)
                    throw new ArgumentException($"{nameof(item)} is required for {kind} objectives.", nameof(item));
                RequireAbsent(position, nameof(position), kind);
                RequireAbsent(npc, nameof(npc), kind);
                if (mobSpecies is not null)
                    throw new ArgumentException($"{nameof(mobSpecies)} must not be set for {kind} objectives.", nameof(mobSpecies));
                break;

            case QuestObjectiveKind.Kill:
                if (string.IsNullOrWhiteSpace(mobSpecies))
                    throw new ArgumentException($"{nameof(mobSpecies)} is required for {kind} objectives.", nameof(mobSpecies));
                RequireAbsent(position, nameof(position), kind);
                RequireAbsent(npc, nameof(npc), kind);
                RequireAbsent(item, nameof(item), kind);
                break;

            case QuestObjectiveKind.Deliver:
                if (npc is null)
                    throw new ArgumentException($"{nameof(npc)} is required for {kind} objectives.", nameof(npc));
                if (item is null)
                    throw new ArgumentException($"{nameof(item)} is required for {kind} objectives.", nameof(item));
                RequireAbsent(position, nameof(position), kind);
                if (mobSpecies is not null)
                    throw new ArgumentException($"{nameof(mobSpecies)} must not be set for {kind} objectives.", nameof(mobSpecies));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown QuestObjectiveKind.");
        }

        Kind = kind;
        Position = position;
        Npc = npc;
        Item = item;
        MobSpecies = mobSpecies;
    }

    private static void RequireAbsent<T>(T? value, string parameterName, QuestObjectiveKind kind) where T : struct
    {
        if (value is not null)
        {
            throw new ArgumentException(
                $"{parameterName} must not be set for {kind} objectives.", parameterName);
        }
    }
}

/// <summary>
/// One <see cref="QuestObjective"/> (AP-01's progress-tracking shape)
/// paired with the kind-specific target AP-06 adds. Kept as a separate
/// pairing rather than adding fields to <see cref="QuestObjective"/>
/// itself, so AP-01's contract stays the minimal shape it was published
/// as and every existing construction site keeps compiling.
/// </summary>
public sealed record EnrichedQuestObjective(
    QuestObjective Objective,
    QuestObjectiveTarget Target);

/// <summary>What completing a <see cref="Quest"/> grants.</summary>
public enum QuestRewardKind
{
    Item = 0,
    Currency = 1,
    Experience = 2
}

/// <summary>
/// One reward granted on quest completion. <see cref="Quantity"/> is the
/// item count for <see cref="QuestRewardKind.Item"/>, or the raw
/// currency/experience amount otherwise.
/// </summary>
public sealed record QuestReward
{
    public QuestRewardKind Kind { get; init; }
    public ItemId? Item { get; init; }
    public double Quantity { get; init; }

    public QuestReward(QuestRewardKind kind, double quantity, ItemId? item = null)
    {
        if (kind == QuestRewardKind.Item && item is null)
            throw new ArgumentException($"{nameof(item)} is required for {kind} rewards.", nameof(item));
        if (kind != QuestRewardKind.Item && item is not null)
            throw new ArgumentException($"{nameof(item)} must not be set for {kind} rewards.", nameof(item));

        Kind = kind;
        Item = item;
        Quantity = quantity;
    }
}

/// <summary>
/// One quest's place in the Quest Graph (docs/ROADMAP_ESECUTIVA.md
/// S:AP-06's "Quest Graph"): which other quests must already be
/// <see cref="QuestObjectiveStatus.Completed"/> before this one can start,
/// and what it grants when it is. The quest's own name/objectives/progress
/// stay on <see cref="Quest"/> (AP-01); this is the graph edge and the
/// reward list layered on top of it.
/// </summary>
/// <param name="Id">The quest this node describes.</param>
/// <param name="Prerequisites">Quest ids that must be Completed first. Empty for a quest with no prerequisite.</param>
/// <param name="Rewards">What completing this quest grants.</param>
public sealed record QuestNode(
    QuestId Id,
    EquatableArray<QuestId> Prerequisites,
    EquatableArray<QuestReward> Rewards);

/// <summary>The full set of known quests and their prerequisite edges.</summary>
public sealed record QuestGraph(EquatableArray<QuestNode> Nodes)
{
    /// <summary>No quests known yet.</summary>
    public static QuestGraph Empty { get; } = new(EquatableArray<QuestNode>.Empty);
}
