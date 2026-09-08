using System;
using System.Linq;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// Quest chains arrive from observation, and observation can be wrong. These tests pin what the
/// builder does with a malformed chain: it reports the defect instead of quietly repairing it,
/// because a cycle left undetected makes <see cref="QuestGraphPlanner.GetStartableQuests"/>
/// return an empty list that reads exactly like "everything is already done".
/// </summary>
public sealed class QuestGraphBuilderTests
{
    [Fact]
    public void EmptyObservation_ProducesSoundEmptyGraph()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(Array.Empty<QuestNode>());
        Assert.True(result.IsSound);
        Assert.Empty(result.Graph.Nodes);
    }

    [Fact]
    public void IndependentQuests_AreAllAccepted()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(new[] { Node("a"), Node("b") });
        Assert.True(result.IsSound);
        Assert.Equal(2, result.Graph.Nodes.Count);
    }

    [Fact]
    public void DuplicateQuest_IsReportedAndOnlyFirstKept()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(new[] { Node("a"), Node("a") });
        Assert.False(result.IsSound);
        Assert.Single(result.DuplicateIds);
        Assert.Single(result.Graph.Nodes);
    }

    [Fact]
    public void PrerequisiteOutsideTheSet_IsReportedAsDangling()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(new[] { Node("a", "missing") });
        Assert.False(result.IsSound);
        Assert.Single(result.DanglingPrerequisites);
        Assert.Equal(Q("a"), result.DanglingPrerequisites[0].Quest);
        Assert.Equal(Q("missing"), result.DanglingPrerequisites[0].MissingPrerequisite);
    }

    [Fact]
    public void TwoQuestCycle_IsDetected()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(new[] { Node("a", "b"), Node("b", "a") });
        Assert.False(result.IsSound);
        Assert.Single(result.Cycles);
    }

    [Fact]
    public void ThreeQuestCycle_IsDetected()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(
            new[] { Node("a", "b"), Node("b", "c"), Node("c", "a") });
        Assert.False(result.IsSound);
        Assert.Single(result.Cycles);
    }

    [Fact]
    public void SelfPrerequisite_IsACycle()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(new[] { Node("a", "a") });
        Assert.False(result.IsSound);
        Assert.Single(result.Cycles);
    }

    [Fact]
    public void LinearChain_IsSound()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(
            new[] { Node("a"), Node("b", "a"), Node("c", "b") });
        Assert.True(result.IsSound);
        Assert.Empty(result.Cycles);
        Assert.Equal(3, result.Graph.Nodes.Count);
    }

    [Fact]
    public void ADefectiveChainDoesNotHideTheHealthyOne()
    {
        QuestGraphBuildResult result = QuestGraphBuilder.Build(
            new[] { Node("a"), Node("b", "a"), Node("x", "y"), Node("y", "x") });

        Assert.False(result.IsSound);
        Assert.Single(result.Cycles);
        Assert.Equal(4, result.Graph.Nodes.Count);
        Assert.Contains(result.Graph.Nodes, node => node.Id == Q("b"));
    }

    [Fact]
    public void ALongChainIsWalkedWithoutExhaustingTheStack()
    {
        QuestNode[] chain = Enumerable.Range(0, 5000)
            .Select(i => i == 0 ? Node("q0") : Node($"q{i}", $"q{i - 1}"))
            .ToArray();

        QuestGraphBuildResult result = QuestGraphBuilder.Build(chain);

        Assert.True(result.IsSound);
        Assert.Equal(5000, result.Graph.Nodes.Count);
    }

    [Fact]
    public void NullObservation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => QuestGraphBuilder.Build(null!));
    }

    private static QuestId Q(string id) => new(id);

    private static QuestNode Node(string id, params string[] prerequisites) =>
        new(
            Q(id),
            EquatableArray<QuestId>.From(prerequisites.Select(Q)),
            EquatableArray<QuestReward>.Empty);
}
