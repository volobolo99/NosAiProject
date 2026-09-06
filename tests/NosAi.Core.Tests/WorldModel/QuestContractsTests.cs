using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class QuestContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UngroundedQuest_StatusIsExplicitlyUnknown_NeverNotStartedByDefault()
    {
        var quest = new Quest(new QuestId("q1"), WorldFact<string>.Unknown("not_yet_read"), WorldFact<QuestObjectiveStatus>.Unknown("not_yet_read"), EquatableArray<QuestObjective>.Empty);

        Assert.False(quest.OverallStatus.HasValue);
    }

    [Fact]
    public void ObjectiveProgress_TracksCurrentAgainstRequired()
    {
        var objective = new QuestObjective(
            WorldFact<string>.Live("Collect wolf pelts", 1.0, Now),
            WorldFact<int>.Live(3, 1.0, Now),
            WorldFact<int>.Live(10, 1.0, Now),
            WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.InProgress, 1.0, Now));

        Assert.Equal(3, objective.CurrentCount.Value);
        Assert.Equal(10, objective.RequiredCount.Value);
        Assert.Equal(QuestObjectiveStatus.InProgress, objective.Status.Value);
    }

    [Fact]
    public void Quest_PreservesObjectiveOrder()
    {
        var objectives = EquatableArray<QuestObjective>.From(new[]
        {
            new QuestObjective(WorldFact<string>.Live("A", 1.0, Now), WorldFact<int>.Live(0, 1.0, Now), WorldFact<int>.Live(1, 1.0, Now), WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.NotStarted, 1.0, Now)),
            new QuestObjective(WorldFact<string>.Live("B", 1.0, Now), WorldFact<int>.Live(0, 1.0, Now), WorldFact<int>.Live(1, 1.0, Now), WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.NotStarted, 1.0, Now))
        });

        var quest = new Quest(new QuestId("q1"), WorldFact<string>.Live("Test Quest", 1.0, Now), WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.InProgress, 1.0, Now), objectives);

        Assert.Equal("A", quest.Objectives[0].Description.Value);
        Assert.Equal("B", quest.Objectives[1].Description.Value);
    }
}
