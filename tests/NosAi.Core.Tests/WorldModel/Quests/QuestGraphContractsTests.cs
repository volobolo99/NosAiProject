using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Quests;

public sealed class QuestObjectiveTargetTests
{
    private static readonly EntityId Npc = new("npc-1");
    private static readonly ItemId Item = new("item-1");
    private static readonly WorldPosition Position = new(1f, 2f);

    [Fact]
    public void Travel_RequiresPosition()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Travel));
    }

    [Fact]
    public void Travel_WithPosition_Constructs()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Travel, position: Position);

        Assert.Equal(Position, target.Position);
    }

    [Fact]
    public void Dialogue_RequiresNpc()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Dialogue));
    }

    [Fact]
    public void Dialogue_WithNpc_Constructs()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Dialogue, npc: Npc);

        Assert.Equal(Npc, target.Npc);
    }

    [Fact]
    public void Interact_RequiresNpc()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Interact));
    }

    [Fact]
    public void Collect_RequiresItem()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Collect));
    }

    [Fact]
    public void Collect_WithItem_Constructs()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Collect, item: Item);

        Assert.Equal(Item, target.Item);
    }

    [Fact]
    public void Kill_RequiresMobSpecies()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Kill));
    }

    [Fact]
    public void Kill_WithMobSpecies_Constructs()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Kill, mobSpecies: "wolf");

        Assert.Equal("wolf", target.MobSpecies);
    }

    [Fact]
    public void Deliver_RequiresNpcAndItem()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Deliver, npc: Npc));
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Deliver, item: Item));
    }

    [Fact]
    public void Deliver_WithNpcAndItem_Constructs()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Deliver, npc: Npc, item: Item);

        Assert.Equal(Npc, target.Npc);
        Assert.Equal(Item, target.Item);
    }

    [Fact]
    public void Travel_RejectsExtraFields()
    {
        Assert.Throws<ArgumentException>(() => new QuestObjectiveTarget(QuestObjectiveKind.Travel, position: Position, npc: Npc));
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var first = new QuestObjectiveTarget(QuestObjectiveKind.Travel, position: Position);
        var second = new QuestObjectiveTarget(QuestObjectiveKind.Travel, position: Position);

        Assert.Equal(first, second);
    }
}

public sealed class QuestRewardTests
{
    [Fact]
    public void Item_RequiresItemId()
    {
        Assert.Throws<ArgumentException>(() => new QuestReward(QuestRewardKind.Item, 1d));
    }

    [Fact]
    public void Item_WithItemId_Constructs()
    {
        var reward = new QuestReward(QuestRewardKind.Item, 5d, new ItemId("potion"));

        Assert.Equal(new ItemId("potion"), reward.Item);
        Assert.Equal(5d, reward.Quantity);
    }

    [Fact]
    public void Currency_RejectsItemId()
    {
        Assert.Throws<ArgumentException>(() => new QuestReward(QuestRewardKind.Currency, 100d, new ItemId("gold")));
    }

    [Fact]
    public void Currency_Constructs()
    {
        var reward = new QuestReward(QuestRewardKind.Currency, 100d);

        Assert.Null(reward.Item);
        Assert.Equal(100d, reward.Quantity);
    }

    [Fact]
    public void Experience_Constructs()
    {
        var reward = new QuestReward(QuestRewardKind.Experience, 250d);

        Assert.Equal(QuestRewardKind.Experience, reward.Kind);
    }
}

public sealed class QuestGraphTests
{
    [Fact]
    public void Empty_HasNoNodes()
    {
        Assert.Empty(QuestGraph.Empty.Nodes);
    }

    [Fact]
    public void RecordEquality_ComparesNodesStructurally()
    {
        var nodes = EquatableArray<QuestNode>.From(new[]
        {
            new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty)
        });

        var first = new QuestGraph(nodes);
        var second = new QuestGraph(nodes);

        Assert.Equal(first, second);
    }
}
