using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Quests;

public sealed class QuestGraphPlannerTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static Quest BuildQuest(string id, QuestObjectiveStatus? status, EquatableArray<QuestObjective>? objectives = null) =>
        new(
            new QuestId(id),
            WorldFact<string>.Live(id, 1d, Now),
            status is { } s ? WorldFact<QuestObjectiveStatus>.Live(s, 1d, Now) : WorldFact<QuestObjectiveStatus>.Unknown("r", Now),
            objectives ?? EquatableArray<QuestObjective>.Empty);

    private static QuestObjective BuildObjective(int? current, int? required, QuestObjectiveStatus? status = null) =>
        new(
            WorldFact<string>.Live("desc", 1d, Now),
            current is { } c ? WorldFact<int>.Live(c, 1d, Now) : WorldFact<int>.Unknown("r", Now),
            required is { } r ? WorldFact<int>.Live(r, 1d, Now) : WorldFact<int>.Unknown("r", Now),
            status is { } s ? WorldFact<QuestObjectiveStatus>.Live(s, 1d, Now) : WorldFact<QuestObjectiveStatus>.Unknown("r", Now));

    // ---- IsStartable ----

    [Fact]
    public void IsStartable_NoPrerequisites_QuestNotObserved_IsStartable()
    {
        var node = new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty);

        bool startable = QuestGraphPlanner.IsStartable(node, EquatableArray<Quest>.Empty);

        Assert.True(startable);
    }

    [Fact]
    public void IsStartable_AlreadyInProgress_IsNotStartable()
    {
        var node = new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty);
        var quests = EquatableArray<Quest>.From(new[] { BuildQuest("q1", QuestObjectiveStatus.InProgress) });

        bool startable = QuestGraphPlanner.IsStartable(node, quests);

        Assert.False(startable);
    }

    [Fact]
    public void IsStartable_AlreadyCompleted_IsNotStartable()
    {
        var node = new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty);
        var quests = EquatableArray<Quest>.From(new[] { BuildQuest("q1", QuestObjectiveStatus.Completed) });

        bool startable = QuestGraphPlanner.IsStartable(node, quests);

        Assert.False(startable);
    }

    [Fact]
    public void IsStartable_PrerequisiteCompleted_IsStartable()
    {
        var node = new QuestNode(new QuestId("q2"), EquatableArray<QuestId>.From(new[] { new QuestId("q1") }), EquatableArray<QuestReward>.Empty);
        var quests = EquatableArray<Quest>.From(new[] { BuildQuest("q1", QuestObjectiveStatus.Completed) });

        bool startable = QuestGraphPlanner.IsStartable(node, quests);

        Assert.True(startable);
    }

    [Fact]
    public void IsStartable_PrerequisiteNotCompleted_IsNotStartable()
    {
        var node = new QuestNode(new QuestId("q2"), EquatableArray<QuestId>.From(new[] { new QuestId("q1") }), EquatableArray<QuestReward>.Empty);
        var quests = EquatableArray<Quest>.From(new[] { BuildQuest("q1", QuestObjectiveStatus.InProgress) });

        bool startable = QuestGraphPlanner.IsStartable(node, quests);

        Assert.False(startable);
    }

    [Fact]
    public void IsStartable_PrerequisiteNeverObserved_IsNotStartable()
    {
        var node = new QuestNode(new QuestId("q2"), EquatableArray<QuestId>.From(new[] { new QuestId("q1") }), EquatableArray<QuestReward>.Empty);

        bool startable = QuestGraphPlanner.IsStartable(node, EquatableArray<Quest>.Empty);

        Assert.False(startable);
    }

    [Fact]
    public void IsStartable_PrerequisiteStatusUnknown_IsNotStartable()
    {
        var node = new QuestNode(new QuestId("q2"), EquatableArray<QuestId>.From(new[] { new QuestId("q1") }), EquatableArray<QuestReward>.Empty);
        var quests = EquatableArray<Quest>.From(new[] { BuildQuest("q1", status: null) });

        bool startable = QuestGraphPlanner.IsStartable(node, quests);

        Assert.False(startable);
    }

    // ---- GetStartableQuests ----

    [Fact]
    public void GetStartableQuests_ReturnsOnlyStartableNodesInOrder()
    {
        var graph = new QuestGraph(EquatableArray<QuestNode>.From(new[]
        {
            new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty),
            new QuestNode(new QuestId("q2"), EquatableArray<QuestId>.From(new[] { new QuestId("q1") }), EquatableArray<QuestReward>.Empty),
        }));
        var quests = EquatableArray<Quest>.Empty;

        IReadOnlyList<QuestId> startable = QuestGraphPlanner.GetStartableQuests(graph, quests);

        Assert.Single(startable);
        Assert.Equal(new QuestId("q1"), startable[0]);
    }

    // ---- IsObjectiveSatisfied ----

    [Fact]
    public void IsObjectiveSatisfied_StatusCompleted_IsSatisfied()
    {
        QuestObjective objective = BuildObjective(current: null, required: null, status: QuestObjectiveStatus.Completed);

        Assert.True(QuestGraphPlanner.IsObjectiveSatisfied(objective));
    }

    [Fact]
    public void IsObjectiveSatisfied_CountReached_IsSatisfied()
    {
        QuestObjective objective = BuildObjective(current: 10, required: 10);

        Assert.True(QuestGraphPlanner.IsObjectiveSatisfied(objective));
    }

    [Fact]
    public void IsObjectiveSatisfied_CountBelowRequired_IsNotSatisfied()
    {
        QuestObjective objective = BuildObjective(current: 3, required: 10);

        Assert.False(QuestGraphPlanner.IsObjectiveSatisfied(objective));
    }

    [Fact]
    public void IsObjectiveSatisfied_UnknownCounts_IsNotSatisfied()
    {
        QuestObjective objective = BuildObjective(current: null, required: null);

        Assert.False(QuestGraphPlanner.IsObjectiveSatisfied(objective));
    }

    // ---- NextIncompleteObjective ----

    [Fact]
    public void NextIncompleteObjective_ReturnsFirstUnsatisfied()
    {
        var objectives = EquatableArray<QuestObjective>.From(new[]
        {
            BuildObjective(current: 1, required: 1),
            BuildObjective(current: 0, required: 5),
            BuildObjective(current: 0, required: 3),
        });
        Quest quest = BuildQuest("q1", QuestObjectiveStatus.InProgress, objectives);

        QuestObjective? next = QuestGraphPlanner.NextIncompleteObjective(quest);

        Assert.NotNull(next);
        Assert.Equal(objectives[1], next);
    }

    [Fact]
    public void NextIncompleteObjective_AllSatisfied_ReturnsNull()
    {
        var objectives = EquatableArray<QuestObjective>.From(new[]
        {
            BuildObjective(current: 1, required: 1),
            BuildObjective(current: 2, required: 2),
        });
        Quest quest = BuildQuest("q1", QuestObjectiveStatus.InProgress, objectives);

        QuestObjective? next = QuestGraphPlanner.NextIncompleteObjective(quest);

        Assert.Null(next);
    }

    [Fact]
    public void NextIncompleteObjective_NoObjectives_ReturnsNull()
    {
        Quest quest = BuildQuest("q1", QuestObjectiveStatus.InProgress);

        QuestObjective? next = QuestGraphPlanner.NextIncompleteObjective(quest);

        Assert.Null(next);
    }

    // ---- AssessCollectProgress ----

    private static readonly ItemId TargetItem = new("1234");
    private static readonly QuestObjectiveTarget CollectTarget = new(QuestObjectiveKind.Collect, item: TargetItem);

    private static InventoryItem BuildSlot(string itemId, int? quantity, int slot) =>
        new(
            new ItemId(itemId),
            WorldFact<string>.Unknown("item_name_catalog_not_available", Now),
            quantity is { } q ? WorldFact<int>.Live(q, 1d, Now) : WorldFact<int>.Unknown("r", Now),
            WorldFact<int>.Live(slot, 1d, Now));

    [Fact]
    public void AssessCollectProgress_RejectsNonCollectTarget()
    {
        var travelTarget = new QuestObjectiveTarget(QuestObjectiveKind.Travel, position: new WorldPosition(1f, 1f));

        Assert.Throws<ArgumentException>(() =>
            QuestGraphPlanner.AssessCollectProgress(travelTarget, EquatableArray<InventoryItem>.Empty, Now));
    }

    [Fact]
    public void AssessCollectProgress_EmptyInventory_IsUnknown_NotZero()
    {
        WorldFact<int> result = QuestGraphPlanner.AssessCollectProgress(CollectTarget, EquatableArray<InventoryItem>.Empty, Now);

        Assert.False(result.HasValue);
    }

    [Fact]
    public void AssessCollectProgress_NonEmptyInventory_ItemAbsent_IsKnownZero()
    {
        EquatableArray<InventoryItem> inventory = EquatableArray<InventoryItem>.From(new[]
        {
            BuildSlot("9999", quantity: 3, slot: 0),
        });

        WorldFact<int> result = QuestGraphPlanner.AssessCollectProgress(CollectTarget, inventory, Now);

        Assert.True(result.HasValue);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void AssessCollectProgress_SingleMatchingSlot_ReturnsItsQuantity()
    {
        EquatableArray<InventoryItem> inventory = EquatableArray<InventoryItem>.From(new[]
        {
            BuildSlot(TargetItem.Value, quantity: 5, slot: 0),
        });

        WorldFact<int> result = QuestGraphPlanner.AssessCollectProgress(CollectTarget, inventory, Now);

        Assert.True(result.HasValue);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void AssessCollectProgress_MultipleMatchingSlots_SumsQuantities()
    {
        EquatableArray<InventoryItem> inventory = EquatableArray<InventoryItem>.From(new[]
        {
            BuildSlot(TargetItem.Value, quantity: 5, slot: 0),
            BuildSlot(TargetItem.Value, quantity: 2, slot: 4),
            BuildSlot("9999", quantity: 1, slot: 7),
        });

        WorldFact<int> result = QuestGraphPlanner.AssessCollectProgress(CollectTarget, inventory, Now);

        Assert.True(result.HasValue);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void AssessCollectProgress_MatchingSlotWithUnknownQuantity_IsUnknown()
    {
        EquatableArray<InventoryItem> inventory = EquatableArray<InventoryItem>.From(new[]
        {
            BuildSlot(TargetItem.Value, quantity: 5, slot: 0),
            BuildSlot(TargetItem.Value, quantity: null, slot: 4),
        });

        WorldFact<int> result = QuestGraphPlanner.AssessCollectProgress(CollectTarget, inventory, Now);

        Assert.False(result.HasValue);
    }
}
