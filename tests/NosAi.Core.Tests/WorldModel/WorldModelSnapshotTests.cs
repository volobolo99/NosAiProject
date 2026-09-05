using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldModelSnapshotTests
{
    private const long T0 = 1_757_073_600_000;

    private static Fact<T> Live<T>(T value, long at = T0, float confidence = 1f)
        => Fact<T>.Live(value, ObservationChannel.Network, confidence, at);

    private static WorldModelSnapshot WithVitals(WorldModelSnapshot snapshot, long at = T0)
        => snapshot with
        {
            Player = snapshot.Player with
            {
                Hp = Live(500, at),
                MaxHp = Live(1000, at),
                Mp = Live(200, at),
                Position = Live(new MapPosition(10, 12), at)
            }
        };

    [Fact]
    public void Unobserved_IsNotPlannableAndCarriesReason()
    {
        WorldModelSnapshot snapshot = WorldModelSnapshot.Unobserved("client_not_attached", T0, sessionId: 42);

        Assert.Equal(ContractVersion.Current, snapshot.Contract);
        Assert.Equal(0, snapshot.StateVersion);
        Assert.Equal(42, snapshot.SessionId);
        Assert.True(snapshot.IsContractReadable);
        Assert.False(snapshot.IsPlannable);
        Assert.Equal("client_not_attached", snapshot.UnplannableReason);
        Assert.False(snapshot.IsActionable(T0, 1000));

        FactSummary summary = snapshot.Summarize();
        Assert.Equal(0, summary.KnownCount);
        Assert.True(summary.UnknownCount > 0);
        Assert.False(summary.ContainsSimulated);
    }

    [Fact]
    public void IncompatibleContract_IsRefused()
    {
        WorldModelSnapshot snapshot = WithVitals(WorldModelSnapshot.Unobserved("r", T0)) with { Contract = new ContractVersion(2, 0) };

        Assert.False(snapshot.IsContractReadable);
        Assert.False(snapshot.IsPlannable);
        Assert.Equal("contract_version_incompatible", snapshot.UnplannableReason);
        Assert.False(snapshot.IsActionable(T0, 1000));
    }

    [Fact]
    public void KnownVitals_MakePlannable_FreshRealFactsMakeActionable()
    {
        WorldModelSnapshot snapshot = WithVitals(WorldModelSnapshot.Unobserved("r", T0));

        Assert.True(snapshot.IsPlannable);
        Assert.Null(snapshot.UnplannableReason);
        Assert.True(snapshot.IsActionable(T0 + 100, 500));
        Assert.False(snapshot.IsActionable(T0 + 600, 500));
    }

    [Fact]
    public void OneSimulatedObservation_BlocksActionButNotPlanning()
    {
        WorldModelSnapshot snapshot = WithVitals(WorldModelSnapshot.Unobserved("r", T0));
        MobState simulatedMob = MobState.Unknown(new EntityId(5), "r", T0) with { HpRatio = Fact<float>.Simulated(0.5f, 1f, T0) };
        WorldModelSnapshot withSimulated = snapshot with { Mobs = new[] { simulatedMob } };

        Assert.True(withSimulated.IsPlannable);
        Assert.True(withSimulated.ContainsSimulatedObservation);
        Assert.False(withSimulated.IsActionable(T0, 1000));
    }

    [Fact]
    public void SimulatedCandidatesAndDerivedGoals_DoNotMakeObservationsSimulated()
    {
        WorldModelSnapshot snapshot = WithVitals(WorldModelSnapshot.Unobserved("r", T0));
        ActionCandidate candidate = new(
            new ActionId(1), ActionKind.Move, Fact<EntityId>.Unknown("r"), Live(new MapPosition(1, 1)), SkillId.None, InventorySlotId.Unknown,
            Fact<float>.Derived(0.5f, ObservationChannel.Local, 1f, T0),
            Fact<PredictedEffect>.Simulated(new PredictedEffect(0.9f, 300, 0, 0, 0f), 0.5f, T0), T0);
        WorldModelSnapshot withCandidates = snapshot with { ActionCandidates = new[] { candidate } };

        Assert.True(withCandidates.Summarize().ContainsSimulated);
        Assert.False(withCandidates.SummarizeObserved().ContainsSimulated);
        Assert.False(withCandidates.ContainsSimulatedObservation);
        Assert.True(withCandidates.IsActionable(T0 + 10, 1000));
    }

    [Fact]
    public void OldestObservedFact_BoundsActionability()
    {
        WorldModelSnapshot snapshot = WithVitals(WorldModelSnapshot.Unobserved("r", T0));
        DropState oldDrop = DropState.Unknown(new EntityId(8), "r", T0) with { Position = Live(new MapPosition(3, 3), T0 - 5000) };
        WorldModelSnapshot withOld = snapshot with { Drops = new[] { oldDrop } };

        Assert.Equal(T0 - 5000, withOld.SummarizeObserved().OldestObservedAtUnixMillis);
        Assert.True(snapshot.IsActionable(T0 + 100, 1000));
        Assert.False(withOld.IsActionable(T0 + 100, 1000));
        Assert.True(withOld.IsActionable(T0 + 100, 6000));
    }

    [Fact]
    public void Lookups_FindByIdOrReportAbsence()
    {
        WorldModelSnapshot snapshot = WorldModelSnapshot.Unobserved("r", T0) with
        {
            Mobs = new[] { MobState.Unknown(new EntityId(1), "r", T0), MobState.Unknown(new EntityId(2), "r", T0) },
            Npcs = new[] { NpcState.Unknown(new EntityId(3), "r", T0) },
            Quests = new[] { QuestState.Unknown(new QuestId(4), "r", T0) },
            Cooldowns = new[] { CooldownState.Unknown(new SkillId(5), "r", T0) }
        };

        Assert.True(snapshot.TryFindMob(new EntityId(2), out MobState mob));
        Assert.Equal(new EntityId(2), mob.Id);
        Assert.False(snapshot.TryFindMob(new EntityId(9), out _));
        Assert.True(snapshot.TryFindNpc(new EntityId(3), out _));
        Assert.False(snapshot.TryFindNpc(new EntityId(1), out _));
        Assert.True(snapshot.TryFindQuest(new QuestId(4), out _));
        Assert.False(snapshot.TryFindQuest(new QuestId(1), out _));
        Assert.True(snapshot.TryFindCooldown(new SkillId(5), out _));
        Assert.False(snapshot.TryFindCooldown(new SkillId(1), out _));
    }

    [Fact]
    public void WithVersion_ProducesNewImmutableSnapshot()
    {
        WorldModelSnapshot original = WorldModelSnapshot.Unobserved("r", T0);
        WorldModelSnapshot next = original.WithVersion(7, T0 + 100);

        Assert.Equal(0, original.StateVersion);
        Assert.Equal(T0, original.AssembledAtUnixMillis);
        Assert.Equal(7, next.StateVersion);
        Assert.Equal(T0 + 100, next.AssembledAtUnixMillis);
        Assert.Same(original.Player, next.Player);
    }

    [Fact]
    public void Summarize_AggregatesEveryCollection()
    {
        WorldModelSnapshot snapshot = WorldModelSnapshot.Unobserved("r", T0) with
        {
            Mobs = new[] { MobState.Unknown(new EntityId(1), "r", T0) with { Position = Live(new MapPosition(1, 1)) } },
            Npcs = new[] { NpcState.Unknown(new EntityId(2), "r", T0) with { Position = Live(new MapPosition(2, 2)) } },
            Drops = new[] { DropState.Unknown(new EntityId(3), "r", T0) with { Position = Live(new MapPosition(3, 3)) } },
            Quests = new[] { QuestState.Unknown(new QuestId(4), "r", T0) with { Status = Live(QuestStatus.Active) } },
            Inventory = new[] { InventoryItemState.Unknown(new InventorySlotId(InventoryBag.Main, 0), "r", T0) with { Quantity = Live(3) } },
            Equipment = new[] { EquipmentItemState.Unknown(EquipmentSlot.Armor, "r", T0) with { Item = Live(new TemplateId(10)) } },
            Skills = new[] { SkillState.Unknown(new SkillId(5), "r", T0) with { Usable = Live(true) } },
            Buffs = new[] { BuffState.Unknown(new StatusEffectId(6), "r", T0) with { Level = Live((byte)1) } },
            Debuffs = new[] { DebuffState.Unknown(new StatusEffectId(7), "r", T0) with { Severity = Live(0.2f) } },
            Cooldowns = new[] { new CooldownState(new SkillId(5), Live(T0 + 1L)) },
            Resources = new[] { ResourceState.Unknown(ResourceKind.Gold, "r", T0) with { Current = Live(100L) } }
        };

        FactSummary observed = snapshot.SummarizeObserved();
        Assert.Equal(11, observed.KnownCount);
        Assert.Equal(T0, observed.OldestObservedAtUnixMillis);
        Assert.Equal(T0, observed.NewestObservedAtUnixMillis);
        Assert.Equal(1f, observed.MinConfidence);
    }
}
