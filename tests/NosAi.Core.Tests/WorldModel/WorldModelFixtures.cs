using System.Collections.Immutable;
using NosAi.Core.WorldModel;

namespace NosAi.Core.Tests.WorldModel;

/// <summary>
/// Deterministic fixtures for AP-01 tests. Every timestamp is a fixed constant
/// so two runs on different days produce byte-identical canonical text; that is
/// the property the replay tests pin. These are test inputs, not observations,
/// and never reach a production path.
/// </summary>
public static class WorldModelFixtures
{
    /// <summary>2026-09-05T10:00:00Z.</summary>
    public const long T0 = 1_788_602_400_000L;
    public const long T1 = T0 + 250;
    public const long T2 = T0 + 1_000;

    public static WorldFact<T> Net<T>(T value, long at = T1, float confidence = 1f)
        => WorldFact<T>.Live(value, SensorChannel.Network, confidence, at);

    public static WorldFact<T> Screen<T>(T value, long at = T1, float confidence = 0.8f)
        => WorldFact<T>.Derived(value, SensorChannel.Screen, confidence, at, "ocr");

    public static WorldFact<T> Unknown<T>(string reason = "fixture unknown", long at = T1)
        => WorldFact<T>.Unknown(reason, at);

    public static PlayerState Player() => new(
        Net(new EntityId(4242)),
        Net("Volo"),
        Net(37),
        Net(12),
        Net(new MapCell(41, 17)),
        Net(Orientation.East),
        Net(new Vital(812, 1000)),
        Net(new Vital(300, 420)),
        Net(true),
        Net(false),
        Unknown<EntityId>("no target packet yet"),
        Unknown<EntityId>("never hit"));

    public static MapState Map() => new(
        Net(new MapId(1)),
        Screen("NosVille"),
        Net(new MapBounds(120, 90)),
        [
            new TileObservation(new MapCell(41, 17), Net(TileState.Walkable)),
            new TileObservation(new MapCell(42, 17), Net(TileState.Walkable)),
            new TileObservation(new MapCell(43, 17), Net(TileState.Blocked))
        ],
        [
            new MapPolygon([new MapCell(40, 16), new MapCell(44, 16), new MapCell(44, 19), new MapCell(40, 19)], Screen(TileState.Walkable))
        ],
        [
            new PortalState(new EntityId(9001), Net(new MapCell(0, 45)), Net(new MapId(2)), Unknown<MapCell>("never traversed"))
        ]);

    public static ImmutableArray<MobState> Mobs() => EntityCollections.SortById(
    [
        new MobState(new EntityId(501), Net(12), Net("Fox"), Net(5), Net(new MapCell(45, 18)), Net(new Vital(60, 60)), Net(true), Net(false), Net(EntityPresence.Visible)),
        new MobState(new EntityId(377), Net(15), Net("Wolf"), Net(9), Net(new MapCell(50, 20)), Unknown<Vital>("hp not in packet"), Net(true), Net(true), Net(EntityPresence.Visible))
    ]);

    public static ImmutableArray<NpcState> Npcs() =>
    [
        new NpcState(new EntityId(2001), Net(300), Net("Quest Giver"), Net(new MapCell(30, 10)), Net(true), Net(EntityPresence.Visible))
    ];

    public static ImmutableArray<DropState> Drops() =>
    [
        new DropState(new EntityId(7001), Net(new ItemId(1012)), Net(3), Net(new MapCell(46, 18)), Net(new EntityId(4242)), Net(EntityPresence.Visible))
    ];

    public static ImmutableArray<QuestState> Quests() =>
    [
        new QuestState(new QuestId("q-fox-hunt"), Net("Fox Hunt"), Net(QuestStatus.Active),
        [
            new QuestObjective("kill-fox", Net("Kill 5 foxes"), Net(2), Net(5))
        ])
    ];

    public static ImmutableArray<InventoryItem> Inventory() =>
    [
        new InventoryItem(InventoryBag.Main, 0, Net(new ItemId(1012)), Net(10), Net(0), Net(0)),
        new InventoryItem(InventoryBag.Etc, 3, Net(new ItemId(2000)), Net(1), Unknown<int>("rarity not shown"), Unknown<int>("upgrade not shown"))
    ];

    public static ImmutableArray<EquipmentItem> Equipment() =>
    [
        new EquipmentItem(EquipmentSlot.MainWeapon, Net(new ItemId(1)), Net(3), Net(5), Unknown<int>("durability not shown"))
    ];

    public static ImmutableArray<SkillState> Skills() =>
    [
        new SkillState(new SkillId(20), Net("Slash"), Net(10), Net(1), Net(300), Net(2_000), Net(true), Net(T0)),
        new SkillState(new SkillId(21), Net("Charge"), Net(25), Net(6), Net(500), Net(8_000), Net(false), Net(T1 + 5_000))
    ];

    public static ImmutableArray<StatusEffectState> StatusEffects() =>
    [
        new StatusEffectState(new EffectId(3), StatusEffectPolarity.Buff, Net("Haste"), Net(1), Net(T0), Net(T0 + 30_000)),
        new StatusEffectState(new EffectId(8), StatusEffectPolarity.Debuff, Net("Poison"), Net(2), Net(T0), Unknown<long>("expiry not shown"))
    ];

    public static ImmutableArray<CooldownState> Cooldowns() =>
    [
        new CooldownState(CooldownScope.Skill, 21, Net(T1 - 3_000), Net(T1 + 5_000)),
        new CooldownState(CooldownScope.Global, 0, Net(T1), Net(T1 + 200))
    ];

    public static ImmutableArray<ResourceState> Resources() =>
    [
        new ResourceState(ResourceKind.Gold, Net(15_320L), Unknown<long>("no cap")),
        new ResourceState(ResourceKind.Experience, Net(120_000L), Net(250_000L))
    ];

    public static ImmutableArray<ActionRecord> Actions() =>
    [
        new ActionRecord(new ActionId("attack-target"), ActionCategory.Combat, Net(ActionAvailability.Available), Net(T0), Net(ActionOutcome.Succeeded), Net(0)),
        new ActionRecord(new ActionId("walk-to"), ActionCategory.Movement, Unknown<ActionAvailability>("path not evaluated"), Unknown<long>("never attempted"), Unknown<ActionOutcome>("never attempted"), Net(0))
    ];

    public static ImmutableArray<GoalRecord> Goals() =>
    [
        new GoalRecord(new WorldGoalId("g-fox-hunt"), GoalKind.Quest, Net(GoalStatus.Active), 10, Screen(0.4f), Unknown<long>("no deadline"), new QuestId("q-fox-hunt")),
        new GoalRecord(new WorldGoalId("g-stay-alive"), GoalKind.Survival, Net(GoalStatus.Active), 100, Unknown<float>("not measurable"), Unknown<long>("no deadline"), QuestId.None)
    ];

    /// <summary>A fully populated, valid snapshot at revision 7.</summary>
    public static WorldModelSnapshot Populated() => new(
        WorldModelContract.SchemaVersion,
        7,
        T2,
        Player(),
        Map(),
        Mobs(),
        Npcs(),
        Drops(),
        Quests(),
        Inventory(),
        Equipment(),
        Skills(),
        StatusEffects(),
        Cooldowns(),
        Resources(),
        Actions(),
        Goals());
}
