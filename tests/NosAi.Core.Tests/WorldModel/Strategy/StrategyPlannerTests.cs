using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Quests;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Strategy;

public sealed class StrategyPlannerTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;
    private static readonly MapId TestMapId = new("map-1");

    private static Player BuildPlayer(double? currentHp = null, double? maxHp = null)
    {
        EquatableArray<Resource> resources = currentHp is { } current && maxHp is { } max
            ? EquatableArray<Resource>.From(new[]
            {
                new Resource(ResourceKind.Health, WorldFact<double>.Live(current, 1d, Now), WorldFact<double>.Live(max, 1d, Now))
            })
            : EquatableArray<Resource>.Empty;

        return new Player(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(TestMapId, 1d, Now),
            new CombatantStatus(resources, EquatableArray<StatusEffect>.Empty),
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            EquatableArray<InventoryItem>.Empty,
            EquatableArray<EquipmentItem>.Empty);
    }

    // ---- AssessSurvivalUrgency ----

    [Fact]
    public void AssessSurvivalUrgency_NoHealthResource_ReturnsNull()
    {
        Player player = BuildPlayer();

        StrategicSignal? signal = StrategyPlanner.AssessSurvivalUrgency(player);

        Assert.Null(signal);
    }

    [Fact]
    public void AssessSurvivalUrgency_FullHealth_LowUrgency()
    {
        Player player = BuildPlayer(currentHp: 100, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessSurvivalUrgency(player);

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency, precision: 6);
    }

    [Fact]
    public void AssessSurvivalUrgency_LowHealth_HighUrgency()
    {
        Player player = BuildPlayer(currentHp: 10, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessSurvivalUrgency(player);

        Assert.NotNull(signal);
        Assert.Equal(0.9d, signal!.Urgency, precision: 6);
        Assert.Equal("health_critical", signal.Reason);
    }

    [Fact]
    public void AssessSurvivalUrgency_ZeroMaxHealth_ReturnsNull()
    {
        Player player = BuildPlayer(currentHp: 0, maxHp: 0);

        StrategicSignal? signal = StrategyPlanner.AssessSurvivalUrgency(player);

        Assert.Null(signal);
    }

    // ---- AssessRecoveryUrgency ----

    [Fact]
    public void AssessRecoveryUrgency_InCombatUnknown_ReturnsNull()
    {
        Player player = BuildPlayer(currentHp: 10, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessRecoveryUrgency(
            player, WorldFact<bool>.Unknown("no_combat_signal_yet", Now));

        Assert.Null(signal);
    }

    [Fact]
    public void AssessRecoveryUrgency_CurrentlyInCombat_ReturnsNull()
    {
        // Survival owns this regime unconditionally; Recovery must not also
        // fire on the same HP fraction while the fight is still on, or the
        // same fact would be double-counted under two names.
        Player player = BuildPlayer(currentHp: 10, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessRecoveryUrgency(
            player, WorldFact<bool>.Derived(true, 1d, Now));

        Assert.Null(signal);
    }

    [Fact]
    public void AssessRecoveryUrgency_NoHealthResource_ReturnsNull()
    {
        Player player = BuildPlayer();

        StrategicSignal? signal = StrategyPlanner.AssessRecoveryUrgency(
            player, WorldFact<bool>.Derived(false, 1d, Now));

        Assert.Null(signal);
    }

    [Fact]
    public void AssessRecoveryUrgency_SafeAndDamaged_HasUrgency()
    {
        Player player = BuildPlayer(currentHp: 10, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessRecoveryUrgency(
            player, WorldFact<bool>.Derived(false, 1d, Now));

        Assert.NotNull(signal);
        Assert.Equal(StrategicGoalKind.Recovery, signal!.Kind);
        Assert.Equal(0.9d, signal.Urgency, precision: 6);
        Assert.Equal("safe_to_recover", signal.Reason);
    }

    [Fact]
    public void AssessRecoveryUrgency_SafeAndFullHealth_NoUrgency()
    {
        Player player = BuildPlayer(currentHp: 100, maxHp: 100);

        StrategicSignal? signal = StrategyPlanner.AssessRecoveryUrgency(
            player, WorldFact<bool>.Derived(false, 1d, Now));

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency, precision: 6);
        Assert.Equal("health_full", signal.Reason);
    }

    // ---- AssessQuestUrgency ----

    private static Quest BuildQuest(string id, QuestObjectiveStatus? status, EquatableArray<QuestObjective>? objectives = null) =>
        new(
            new QuestId(id),
            WorldFact<string>.Live(id, 1d, Now),
            status is { } s ? WorldFact<QuestObjectiveStatus>.Live(s, 1d, Now) : WorldFact<QuestObjectiveStatus>.Unknown("r", Now),
            objectives ?? EquatableArray<QuestObjective>.Empty);

    private static QuestObjective BuildObjective(int current, int required) =>
        new(
            WorldFact<string>.Live("desc", 1d, Now),
            WorldFact<int>.Live(current, 1d, Now),
            WorldFact<int>.Live(required, 1d, Now),
            WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.InProgress, 1d, Now));

    [Fact]
    public void AssessQuestUrgency_NothingKnown_ReturnsNull()
    {
        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(QuestGraph.Empty, EquatableArray<Quest>.Empty);

        Assert.Null(signal);
    }

    [Fact]
    public void AssessQuestUrgency_StartableQuest_CountsIt()
    {
        var graph = new QuestGraph(EquatableArray<QuestNode>.From(new[]
        {
            new QuestNode(new QuestId("q1"), EquatableArray<QuestId>.Empty, EquatableArray<QuestReward>.Empty)
        }));

        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(graph, EquatableArray<Quest>.Empty);

        Assert.NotNull(signal);
        Assert.Equal(1d, signal!.Urgency);
    }

    [Fact]
    public void AssessQuestUrgency_InProgressWithNextObjective_CountsIt()
    {
        Quest quest = BuildQuest("q1", QuestObjectiveStatus.InProgress, EquatableArray<QuestObjective>.From(new[] { BuildObjective(0, 5) }));
        var quests = EquatableArray<Quest>.From(new[] { quest });

        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(QuestGraph.Empty, quests);

        Assert.NotNull(signal);
        Assert.Equal(1d, signal!.Urgency);
    }

    [Fact]
    public void AssessQuestUrgency_CompletedQuest_HasZeroUrgency()
    {
        Quest quest = BuildQuest("q1", QuestObjectiveStatus.Completed);
        var quests = EquatableArray<Quest>.From(new[] { quest });

        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(QuestGraph.Empty, quests);

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency);
    }

    // ---- AssessExplorationUrgency ----

    [Fact]
    public void AssessExplorationUrgency_UnknownFullyExplored_ReturnsNull()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "not_yet_observed", Now);

        StrategicSignal? signal = StrategyPlanner.AssessExplorationUrgency(footprint);

        Assert.Null(signal);
    }

    [Fact]
    public void AssessExplorationUrgency_NotFullyExplored_HasUrgency()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", Now) with
        {
            FullyExplored = WorldFact<bool>.Derived(false, 1d, Now)
        };

        StrategicSignal? signal = StrategyPlanner.AssessExplorationUrgency(footprint);

        Assert.NotNull(signal);
        Assert.Equal(1d, signal!.Urgency);
    }

    [Fact]
    public void AssessExplorationUrgency_FullyExplored_NoUrgency()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(TestMapId, "r", Now) with
        {
            FullyExplored = WorldFact<bool>.Derived(true, 1d, Now)
        };

        StrategicSignal? signal = StrategyPlanner.AssessExplorationUrgency(footprint);

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency);
    }

    // ---- SelectStrategicPlan ----

    [Fact]
    public void SelectStrategicPlan_NoSignals_ReturnsUnselected()
    {
        StrategicPlan plan = StrategyPlanner.SelectStrategicPlan(Array.Empty<StrategicSignal>(), Now);

        Assert.False(plan.HasSelection.HasValue);
    }

    [Fact]
    public void SelectStrategicPlan_PicksHighestUrgency()
    {
        var low = new StrategicSignal(StrategicGoalKind.Exploration, 0.2, "r");
        var high = new StrategicSignal(StrategicGoalKind.Survival, 0.9, "r");

        StrategicPlan plan = StrategyPlanner.SelectStrategicPlan(new[] { low, high }, Now);

        Assert.True(plan.HasSelection.Value);
        Assert.Equal(StrategicGoalKind.Survival, plan.SelectedKind);
    }

    [Fact]
    public void SelectStrategicPlan_TiedUrgency_PicksTheFirstOne()
    {
        var first = new StrategicSignal(StrategicGoalKind.Survival, 0.5, "r");
        var second = new StrategicSignal(StrategicGoalKind.QuestUrgency, 0.5, "r");

        StrategicPlan plan = StrategyPlanner.SelectStrategicPlan(new[] { first, second }, Now);

        Assert.Equal(StrategicGoalKind.Survival, plan.SelectedKind);
    }
}
