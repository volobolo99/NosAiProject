using System;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Combat;

/// <summary>
/// An empty candidate list cannot tell peace from blindness. "No enemy here", "I do not know
/// where I am" and "they are too far" all produce nothing, and they ask for opposite things:
/// rest, look, close the distance. These tests pin the verdict that separates them.
/// </summary>
public sealed class CombatOpportunityVerdictTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    [Fact]
    public void AnUnknownPlayerPosition_IsBlindnessNotPeace()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: null),
            EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f)) }));

        Assert.Equal(CombatOpportunityVerdict.PlayerPositionUnknown, verdict);
    }

    [Fact]
    public void AHostileInReach_IsAnOpportunity()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: new WorldPosition(0f, 0f)),
            EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f)) }));

        Assert.Equal(CombatOpportunityVerdict.CandidatesAvailable, verdict);
    }

    [Fact]
    public void NoMobAtAll_IsNoViableTarget()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: new WorldPosition(0f, 0f)),
            EquatableArray<Mob>.Empty);

        Assert.Equal(CombatOpportunityVerdict.NoViableTarget, verdict);
    }

    [Fact]
    public void AMobThatIsNotHostile_IsNoViableTarget()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: new WorldPosition(0f, 0f)),
            EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(1f, 0f), hostile: false) }));

        Assert.Equal(CombatOpportunityVerdict.NoViableTarget, verdict);
    }

    /// <summary>
    /// Distance is actionable information: the player can walk. Reporting "no target" for an
    /// enemy standing fifty tiles away would throw that away.
    /// </summary>
    [Fact]
    public void AHostileTooFarAway_AsksToCloseTheDistance()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: new WorldPosition(0f, 0f)),
            EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(50f, 0f)) }));

        Assert.Equal(CombatOpportunityVerdict.TargetsOutOfReach, verdict);
    }

    /// <summary>
    /// Standing four tiles away the basic attack cannot reach, and the skill list was never
    /// observed, so nothing can be done to a target that is nonetheless right there. That is a
    /// gap in what has been read, not an absence of enemies.
    /// </summary>
    [Fact]
    public void AReachableTargetWithUnreadSkills_IsNoUsableAction()
    {
        CombatOpportunityVerdict verdict = CombatPlanner.ExplainCandidates(
            BuildPlayer(position: new WorldPosition(0f, 0f)),
            EquatableArray<Mob>.From(new[] { BuildMob(position: new WorldPosition(4f, 0f)) }));

        Assert.Equal(CombatOpportunityVerdict.NoUsableAction, verdict);
    }

    private static Player BuildPlayer(
        WorldPosition? position = null,
        EquatableArray<Skill>? skills = null) =>
        new(
            new EntityId("player-1"),
            position is { } p ? WorldFact<WorldPosition>.Live(p, 1d, Now) : WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            CombatantStatus.Empty,
            skills is { } observedSkills
                ? WorldFact<EquatableArray<Skill>>.Live(observedSkills, 1d, Now)
                : WorldFact<EquatableArray<Skill>>.Unknown("skill_list_never_read", Now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, Now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, Now));

    private static Mob BuildMob(
        string id = "mob-1",
        WorldPosition? position = null,
        bool? hostile = true,
        bool? alive = true) =>
        new(
            new EntityId(id),
            position is { } p ? WorldFact<WorldPosition>.Live(p, 1d, Now) : WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<string>.Live("wolf", 1d, Now),
            hostile is { } h ? WorldFact<bool>.Live(h, 1d, Now) : WorldFact<bool>.Unknown("r", Now),
            alive is { } a ? WorldFact<bool>.Live(a, 1d, Now) : WorldFact<bool>.Unknown("r", Now),
            CombatantStatus.Empty);
}
