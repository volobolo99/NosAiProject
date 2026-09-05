using NosAi.Core.Planning;
using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class DomainContractTests
{
    private const long T0 = 1_757_073_600_000;

    private static Fact<T> Live<T>(T value, long at = T0, float confidence = 1f)
        => Fact<T>.Live(value, ObservationChannel.Network, confidence, at);

    [Fact]
    public void ContractVersion_ReadableOnlyBySameMajorAndNewerOrEqualMinor()
    {
        ContractVersion produced = new(1, 0);

        Assert.True(produced.IsReadableBy(new ContractVersion(1, 0)));
        Assert.True(produced.IsReadableBy(new ContractVersion(1, 3)));
        Assert.False(produced.IsReadableBy(new ContractVersion(2, 0)));
        Assert.False(new ContractVersion(1, 4).IsReadableBy(new ContractVersion(1, 3)));
        Assert.False(ContractVersion.None.IsReadableBy(ContractVersion.Current));
        Assert.True(new ContractVersion(1, 2).CompareTo(new ContractVersion(1, 10)) < 0);
        Assert.True(new ContractVersion(2, 0).CompareTo(new ContractVersion(1, 10)) > 0);
        Assert.Equal("1.0", ContractVersion.Current.ToString());
    }

    [Fact]
    public void Identifiers_ZeroIsNone()
    {
        Assert.True(EntityId.None.IsNone);
        Assert.True(MapId.None.IsNone);
        Assert.True(TemplateId.None.IsNone);
        Assert.True(QuestId.None.IsNone);
        Assert.True(SkillId.None.IsNone);
        Assert.True(ActionId.None.IsNone);
        Assert.False(new EntityId(1).IsNone);
        Assert.Equal(new EntityId(5), new EntityId(5));
        Assert.True(InventorySlotId.Unknown.IsUnknown);
        Assert.False(new InventorySlotId(InventoryBag.Main, 3).IsUnknown);
        Assert.Equal("Main:3", new InventorySlotId(InventoryBag.Main, 3).ToString());
    }

    [Fact]
    public void MapPosition_Distances()
    {
        MapPosition a = new(0, 0);
        MapPosition b = new(3, -4);

        Assert.Equal(4, a.ChebyshevDistanceTo(b));
        Assert.Equal(7, a.ManhattanDistanceTo(b));
        Assert.Equal(5.0, a.EuclideanDistanceTo(b), 9);
    }

    [Fact]
    public void MapBounds_ValidatesContainsAndGrows()
    {
        MapBounds bounds = new(2, 3, 5, 7);

        Assert.Equal(4, bounds.Width);
        Assert.Equal(5, bounds.Height);
        Assert.Equal(20, bounds.Area);
        Assert.True(bounds.Contains(new MapPosition(5, 7)));
        Assert.False(bounds.Contains(new MapPosition(6, 7)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapBounds(5, 0, 4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapBounds(0, 5, 0, 4));

        MapBounds extended = bounds.Extend(new MapPosition(-1, 10));
        Assert.Equal(new MapBounds(-1, 3, 5, 10), extended);
        Assert.Equal(new MapBounds(-1, 0, 9, 10), extended.Union(new MapBounds(0, 0, 9, 1)));
    }

    [Fact]
    public void MapState_TileAt_IsUnknownWhenNotObserved()
    {
        TileState[] tiles =
        {
            new(new MapPosition(1, 1), Live(TileKind.Walkable)),
            new(new MapPosition(2, 1), Live(TileKind.Blocked))
        };
        MapState map = new(Live(new MapId(1)), 1, Live(new MapBounds(0, 0, 9, 9)), Live(0.02f), tiles, ReadOnlyMemory<PolygonRegion>.Empty, ReadOnlyMemory<PortalState>.Empty);

        Assert.Equal(TileKind.Blocked, map.TileAt(new MapPosition(2, 1)).Value);
        Fact<TileKind> missing = map.TileAt(new MapPosition(3, 3));
        Assert.True(missing.IsUnknown);
        Assert.Equal(UnknownReasons.NotObserved, missing.FailureReason);
        Assert.Equal(5, map.Summarize().KnownCount);
    }

    [Fact]
    public void PolygonRegion_ContainsUsesRayCasting()
    {
        MapPosition[] square = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        PolygonRegion region = new(square, Live(true), Live(1f));

        Assert.True(region.Contains(new MapPosition(5, 5)));
        Assert.False(region.Contains(new MapPosition(11, 5)));
        Assert.False(new PolygonRegion(new MapPosition[] { new(0, 0), new(1, 1) }, Live(true), Live(1f)).Contains(new MapPosition(0, 0)));
    }

    [Fact]
    public void PlayerState_RatiosRequireKnownVitals()
    {
        PlayerState unknown = PlayerState.Unknown("r", T0);
        Assert.False(unknown.HasVitals);
        Assert.False(unknown.TryGetHpRatio(out _));
        Assert.Equal(17, unknown.Summarize().UnknownCount);
        Assert.Equal(0, unknown.Summarize().KnownCount);

        PlayerState partial = unknown with { Hp = Live(250), MaxHp = Live(1000), Mp = Live(10), MaxMp = Live(0) };
        Assert.True(partial.HasVitals);
        Assert.True(partial.TryGetHpRatio(out float hp));
        Assert.Equal(0.25f, hp);
        Assert.False(partial.TryGetMpRatio(out _));
    }

    [Fact]
    public void MobState_EngagementOnlyWhenHostilityKnown()
    {
        MobState mob = MobState.Unknown(new EntityId(9), "r", T0);
        Assert.False(mob.IsEngagedWithPlayer);
        Assert.True((mob with { Hostility = Live(Hostility.EngagedWithPlayer) }).IsEngagedWithPlayer);
        Assert.False((mob with { Hostility = Live(Hostility.Aggressive) }).IsEngagedWithPlayer);
    }

    [Fact]
    public void DropState_UnknownExpiryIsNotExpired()
    {
        DropState drop = DropState.Unknown(new EntityId(3), "r", T0);
        Assert.False(drop.IsExpiredAt(long.MaxValue));
        Assert.True((drop with { ExpiresAtUnixMillis = Live(T0 + 10) }).IsExpiredAt(T0 + 10));
        Assert.False((drop with { ExpiresAtUnixMillis = Live(T0 + 10) }).IsExpiredAt(T0 + 9));
    }

    [Fact]
    public void QuestState_CompletionRequiresEveryObjectiveObservedComplete()
    {
        QuestObjective first = QuestObjective.Unknown(new ObjectiveId(1), "r", T0) with { Completed = Live(true) };
        QuestObjective second = QuestObjective.Unknown(new ObjectiveId(2), "r", T0) with
        {
            Completed = Live(false),
            Prerequisites = new[] { new ObjectiveId(1) }
        };
        QuestObjective third = QuestObjective.Unknown(new ObjectiveId(3), "r", T0) with
        {
            Prerequisites = new[] { new ObjectiveId(2) }
        };
        QuestState quest = QuestState.Unknown(new QuestId(7), "r", T0) with { Objectives = new[] { first, second, third } };

        Assert.False(quest.AllObjectivesObservedComplete);
        Assert.False(QuestState.Unknown(new QuestId(8), "r", T0).AllObjectivesObservedComplete);

        Span<ObjectiveId> ready = stackalloc ObjectiveId[4];
        int count = quest.CopyReadyObjectives(ready);
        Assert.Equal(1, count);
        Assert.Equal(new ObjectiveId(2), ready[0]);

        QuestState done = quest with { Objectives = new[] { first, second with { Completed = Live(true) }, third with { Completed = Live(true) } } };
        Assert.True(done.AllObjectivesObservedComplete);
        Assert.Equal(0, done.CopyReadyObjectives(ready));
    }

    [Fact]
    public void QuestObjective_ProgressRequiresKnownCounts()
    {
        QuestObjective objective = QuestObjective.Unknown(new ObjectiveId(1), "r", T0);
        Assert.False(objective.TryGetProgress(out _));
        Assert.True((objective with { RequiredCount = Live(4), CurrentCount = Live(1) }).TryGetProgress(out float progress));
        Assert.Equal(0.25f, progress);
    }

    [Fact]
    public void EquipmentItemState_DistinguishesUnreadFromObservedEmpty()
    {
        EquipmentItemState unread = EquipmentItemState.Unknown(EquipmentSlot.Ring, "r", T0);
        Assert.False(unread.IsObservedEmpty);
        Assert.True((unread with { Item = Live(TemplateId.None) }).IsObservedEmpty);
        Assert.False((unread with { Item = Live(new TemplateId(1)) }).IsObservedEmpty);
    }

    [Fact]
    public void Cooldown_UnknownIsNeverReady()
    {
        CooldownState unknown = CooldownState.Unknown(new SkillId(1), "r", T0);
        Assert.False(unknown.IsObservedReadyAt(long.MaxValue));
        Assert.Null(unknown.RemainingMillisAt(T0));

        CooldownState known = new(new SkillId(1), Live(T0 + 500L));
        Assert.False(known.IsObservedReadyAt(T0 + 499));
        Assert.True(known.IsObservedReadyAt(T0 + 500));
        Assert.Equal(500, known.RemainingMillisAt(T0));
        Assert.Equal(0, known.RemainingMillisAt(T0 + 900));
    }

    [Fact]
    public void BuffAndDebuff_RemainingIsNullWhenExpiryUnknown()
    {
        BuffState buff = BuffState.Unknown(new StatusEffectId(1), "r", T0);
        DebuffState debuff = DebuffState.Unknown(new StatusEffectId(2), "r", T0);

        Assert.Null(buff.RemainingMillisAt(T0));
        Assert.Null(debuff.RemainingMillisAt(T0));
        Assert.Equal(250, (buff with { ExpiresAtUnixMillis = Live(T0 + 250L) }).RemainingMillisAt(T0));
        Assert.Equal(0, (debuff with { ExpiresAtUnixMillis = Live(T0 - 1L) }).RemainingMillisAt(T0));
    }

    [Fact]
    public void ResourceState_RatioRequiresKnownMaximum()
    {
        ResourceState resource = ResourceState.Unknown(ResourceKind.Gold, "r", T0);
        Assert.False(resource.TryGetRatio(out _));
        Assert.True((resource with { Current = Live(50L), Maximum = Live(200L) }).TryGetRatio(out float ratio));
        Assert.Equal(0.25f, ratio);
    }

    [Fact]
    public void ActionCandidate_RejectsPredictionLabelledAsObservation()
    {
        ActionCandidate candidate = new(
            new ActionId(1),
            ActionKind.UseSkill,
            Live(new EntityId(9)),
            Fact<MapPosition>.Unknown("r"),
            new SkillId(4),
            InventorySlotId.Unknown,
            Fact<float>.Derived(0.8f, ObservationChannel.Local, 1f, T0),
            Fact<PredictedEffect>.Simulated(new PredictedEffect(0.7f, 800, -20, -15, 0.1f), 0.6f, T0),
            T0);

        Assert.True(candidate.HasWellFormedProvenance);
        Assert.False((candidate with { Prediction = Live(new PredictedEffect(1f, 0, 0, 0, 0f)) }).HasWellFormedProvenance);
        Assert.False((candidate with { Utility = Fact<float>.Cached(0.5f, ObservationChannel.Local, 1f, T0) }).HasWellFormedProvenance);
        Assert.True((candidate with { Prediction = Fact<PredictedEffect>.Unknown("r") }).HasWellFormedProvenance);
    }

    [Fact]
    public void ActionOutcome_SuccessRequiresObservedPostcondition()
    {
        ActionOutcome declared = new(new ActionId(1), ActionOutcomeStatus.Succeeded, Fact<bool>.Unknown("r"), T0, T0 + 300, null);
        Assert.False(declared.IsEvidencedSuccess);
        Assert.Equal(300, declared.DurationMillis);

        Assert.True((declared with { PostconditionSatisfied = Live(true) }).IsEvidencedSuccess);
        Assert.False((declared with { PostconditionSatisfied = Fact<bool>.Simulated(true, 1f, T0) }).IsEvidencedSuccess);
        Assert.False((declared with { Status = ActionOutcomeStatus.Failed, PostconditionSatisfied = Live(true) }).IsEvidencedSuccess);
    }

    [Fact]
    public void GoalState_ReusesPlannerIdsAndTreatsUnknownDeadlineAsNotOverdue()
    {
        GoalState goal = new(
            new GoalId(3),
            GoalClass.Survival,
            GoalKind.Recover,
            GoalStatus.Active,
            Fact<float>.Derived(0.9f, ObservationChannel.Local, 1f, T0),
            Fact<EntityId>.Unknown("r"),
            Fact<MapId>.Unknown("r"),
            Fact<MapPosition>.Unknown("r"),
            Fact<QuestId>.Unknown("r"),
            Fact<long>.Unknown("r"),
            T0);

        Assert.False(goal.IsObservedOverdueAt(long.MaxValue));
        Assert.False(goal.IsTerminal);
        Assert.True((goal with { DeadlineUnixMillis = Live(T0 + 1L) }).IsObservedOverdueAt(T0 + 2));
        Assert.True((goal with { Status = GoalStatus.Completed }).IsTerminal);
        Assert.Equal(new GoalId(3), goal.Id);
    }
}
