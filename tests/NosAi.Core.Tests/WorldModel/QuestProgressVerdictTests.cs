using System;
using System.Linq;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// An empty startable list cannot tell "every quest is done" from "the chain is stuck", and the
/// two demand opposite behaviour: move on, or unblock something. These tests pin the verdict that
/// separates them, including the case where the answer must stay Unknown because a status was
/// never read.
/// </summary>
public sealed class QuestProgressVerdictTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    [Fact]
    public void EmptyGraph_IsNothingKnown()
    {
        Assert.Equal(
            QuestProgressVerdict.NothingKnown,
            QuestGraphPlanner.ExplainProgress(QuestGraph.Empty, EquatableArray<Quest>.Empty));
    }

    [Fact]
    public void AQuestWithoutPrerequisites_IsStartable()
    {
        Assert.Equal(
            QuestProgressVerdict.Startable,
            QuestGraphPlanner.ExplainProgress(Graph(Node("a")), EquatableArray<Quest>.Empty));
    }

    [Fact]
    public void EveryQuestCompleted_IsAllCompleted()
    {
        Assert.Equal(
            QuestProgressVerdict.AllCompleted,
            QuestGraphPlanner.ExplainProgress(
                Graph(Node("a")),
                Known(BuildQuest("a", QuestObjectiveStatus.Completed))));
    }

    [Fact]
    public void AQuestUnderWay_IsInProgress()
    {
        Assert.Equal(
            QuestProgressVerdict.InProgress,
            QuestGraphPlanner.ExplainProgress(
                Graph(Node("a")),
                Known(BuildQuest("a", QuestObjectiveStatus.InProgress))));
    }

    [Fact]
    public void AFailedQuestBlockingItsSuccessor_IsBlocked()
    {
        QuestGraph graph = Graph(Node("a"), Node("b", "a"));
        EquatableArray<Quest> known = Known(
            BuildQuest("a", QuestObjectiveStatus.Failed),
            BuildQuest("b", QuestObjectiveStatus.NotStarted));

        Assert.Equal(QuestProgressVerdict.Blocked, QuestGraphPlanner.ExplainProgress(graph, known));
    }

    /// <summary>
    /// The unread status must not be reported as a blockage. Here "b" is held back by a failed
    /// prerequisite, so nothing is startable, and "b"'s own status was never observed: claiming
    /// the chain is stuck would state as fact something that was never read.
    /// </summary>
    [Fact]
    public void AnUnreadStatus_IsUnknownNotBlocked()
    {
        QuestGraph graph = Graph(Node("a"), Node("b", "a"));
        EquatableArray<Quest> known = Known(
            BuildQuest("a", QuestObjectiveStatus.Failed),
            BuildQuest("b", status: null));

        Assert.Equal(QuestProgressVerdict.Unknown, QuestGraphPlanner.ExplainProgress(graph, known));
    }

    /// <summary>
    /// A quest actually seen under way outranks the doubt raised by another quest whose status
    /// was never read: one is an observation, the other only an absence of one.
    /// </summary>
    [Fact]
    public void AnObservedInProgressOutranksAnUnreadStatus()
    {
        QuestGraph graph = Graph(Node("a"), Node("b", "a"), Node("c", "a"));
        EquatableArray<Quest> known = Known(
            BuildQuest("a", QuestObjectiveStatus.InProgress),
            BuildQuest("b", status: null),
            BuildQuest("c", QuestObjectiveStatus.NotStarted));

        Assert.Equal(QuestProgressVerdict.InProgress, QuestGraphPlanner.ExplainProgress(graph, known));
    }

    /// <summary>
    /// Zero urgency must not be mute. A stuck chain and a finished one both count nothing
    /// startable, so the strategic signal has to carry which of the two produced the zero.
    /// </summary>
    [Fact]
    public void ABlockedChainSaysSoInTheStrategicSignal()
    {
        QuestGraph graph = Graph(Node("a"), Node("b", "a"));
        EquatableArray<Quest> known = Known(
            BuildQuest("a", QuestObjectiveStatus.Failed),
            BuildQuest("b", QuestObjectiveStatus.NotStarted));

        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(graph, known);

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency);
        Assert.Equal("quest_chain_blocked", signal.Reason);
    }

    [Fact]
    public void AFinishedChainIsNotReportedAsBlocked()
    {
        StrategicSignal? signal = StrategyPlanner.AssessQuestUrgency(
            Graph(Node("a")),
            Known(BuildQuest("a", QuestObjectiveStatus.Completed)));

        Assert.NotNull(signal);
        Assert.Equal(0d, signal!.Urgency);
        Assert.Equal("all_quests_completed", signal.Reason);
    }

    [Fact]
    public void NullGraph_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => QuestGraphPlanner.ExplainProgress(null!, EquatableArray<Quest>.Empty));
    }

    private static QuestGraph Graph(params QuestNode[] nodes) =>
        new(EquatableArray<QuestNode>.From(nodes));

    private static QuestNode Node(string id, params string[] prerequisites) =>
        new(
            new QuestId(id),
            EquatableArray<QuestId>.From(prerequisites.Select(p => new QuestId(p))),
            EquatableArray<QuestReward>.Empty);

    private static EquatableArray<Quest> Known(params Quest[] quests) =>
        EquatableArray<Quest>.From(quests);

    private static Quest BuildQuest(string id, QuestObjectiveStatus? status) =>
        new(
            new QuestId(id),
            WorldFact<string>.Live(id, 1d, Now),
            status is { } value
                ? WorldFact<QuestObjectiveStatus>.Live(value, 1d, Now)
                : WorldFact<QuestObjectiveStatus>.Unknown("never_read", Now),
            EquatableArray<QuestObjective>.Empty);
}
