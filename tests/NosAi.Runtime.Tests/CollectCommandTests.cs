using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Safety;
using Xunit;
using RuntimeDataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-06/A4: <see cref="CollectCommand.ExecuteOneRound"/> -- one operator-named
/// Collect attempt, walked and verified through the <c>WalkCommandTests</c> rig
/// style (<c>WalkRig</c>/<c>ChainRig</c>/<c>RecordingInputBackend</c>/<c>Fast</c>
/// verifier) with already-captured <see cref="GameplayObservation"/> before/after
/// inventory reads. Covers: an inventory count that rises across the walk, an
/// item absent from both inventories (counts Live and 0 when the channel is
/// confirmed by other slots), and an unobserved/empty inventory (both counts
/// Unknown).
/// </summary>
public sealed class CollectCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 14, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7400;
    private static readonly ActuationAuthority CollectAuthority = ActuationAuthority.Commanded(CollectCommand.Flag);
    private static readonly EntityId PlayerId = new("player-1");
    private static readonly ItemId Item = new("201");

    // ------------------------------------------------------------- rig

    private sealed class ChainRig
    {
        public string? AuthorityRefusal { get; set; }
        public bool Armed { get; set; } = true;
        public ProjectionStandIn Projection { get; } = new();

        public StepGuardChain Build() => new(
            () => AuthorityRefusal,
            () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Armed },
            Projection);
    }

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public bool Works { get; set; } = true;
        public string? Failure { get; set; }
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = Works ? null : Failure ?? "projection_refused";
            return Works;
        }
    }

    private sealed class WalkRig
    {
        public ChainRig Chain { get; } = new();
        public RecordingInputBackend Recorder { get; } = new();
        public PathWalkController Controller { get; } = new();

        public GatedInputBackend Gate() => new(
            Recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Chain.Armed });
    }

    private static readonly MovementVerifier Fast =
        new(window: TimeSpan.FromMilliseconds(60), tolerance: TimeSpan.FromMilliseconds(10),
            pollInterval: TimeSpan.FromMilliseconds(2));

    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    /// <summary>Open ground, 10x10 -- the same shape <c>WalkCommandTests</c> walks on.</summary>
    private static MapGrid OpenMap() => new(mapId: 1, width: 10, height: 10, new byte[100]);

    private static OccupancyView FreshView() => new(Array.Empty<SelectableEntity>(), Now);

    /// <summary>Serves readings that follow the walk's clicks, as the real client resolves them.</summary>
    private sealed class FollowingPosition
    {
        private readonly RecordingInputBackend _recorder;
        private MapPoint _current;

        public FollowingPosition(MapPoint start, RecordingInputBackend recorder)
        {
            _current = start;
            _recorder = recorder;
        }

        public PositionReading? Read()
        {
            foreach (string last in _recorder.Events.Reverse())
            {
                if (last.StartsWith("move-absolute:", StringComparison.Ordinal))
                {
                    string[] parts = last["move-absolute:".Length..].Split(',');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int x)
                        && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int y))
                    {
                        _current = new MapPoint((x - 100) / 32, (y - 200) / 16);
                        break;
                    }
                }
            }

            return new PositionReading(_current, Now.AddYears(1), RuntimeDataSourceKind.Live);
        }
    }

    // ------------------------------------------------- gameplay observations

    /// <summary>A gameplay observation carrying only the given inventory slots (or none when unobserved).</summary>
    private static GameplayObservation ObservationWithInventory(
        IReadOnlyList<InventorySlotReading>? slots,
        string unknownReason = "not_published_by_provider")
    {
        var observation = GameplayObservation.Unobserved("test_base", Now);
        if (slots is null)
            return observation; // Inventory stays Unknown

        return observation with
        {
            Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(slots, Now)
        };
    }

    private static InventorySlotReading Slot(int vnum, int amount, int slotIndex = 0) =>
        new(InventoryKind: 2, Slot: slotIndex, Vnum: vnum, Amount: amount, Rarity: 0, Now, RuntimeDataSourceKind.Live);

    private static (WalkRun Walk, WorldFact<int> Before, WorldFact<int> After) RunRound(
        GameplayObservation before,
        GameplayObservation after,
        MapPoint? destination = null,
        MapPoint? origin = null)
    {
        var rig = new WalkRig();
        MapPoint from = origin ?? new MapPoint(2, 1);
        MapPoint to = destination ?? new MapPoint(3, 1);
        var follower = new FollowingPosition(from, rig.Recorder);
        StepGuardChain chain = rig.Chain.Build();
        SingleStepExecutor executor = new(
            chain, rig.Gate(), () => Session, Fast, readGeometry: StatedGeometry);
        MapGrid grid = OpenMap();

        (WalkRun walk, WorldFact<int> beforeCount, WorldFact<int> afterCount) = CollectCommand.ExecuteOneRound(
            to,
            from,
            Item,
            in grid,
            FreshView(),
            rig.Controller,
            chain,
            executor,
            in CollectAuthority,
            follower.Read,
            before,
            after,
            PlayerId,
            Now);

        return (walk, beforeCount, afterCount);
    }

    // ------------------------------------------------- count rises

    [Fact]
    public void InventoryCountRisingAcrossTheWalk_ReportsBothCounts()
    {
        GameplayObservation before = ObservationWithInventory(new[] { Slot(201, amount: 2), Slot(99, amount: 1) });
        GameplayObservation after = ObservationWithInventory(new[] { Slot(201, amount: 5), Slot(99, amount: 1) });

        (WalkRun walk, WorldFact<int> beforeCount, WorldFact<int> afterCount) = RunRound(before, after);

        Assert.Equal(WalkCommand.ExitArrived, walk.ExitCode);
        Assert.True(beforeCount.HasValue);
        Assert.True(afterCount.HasValue);
        Assert.Equal(2, beforeCount.Value);
        Assert.Equal(5, afterCount.Value);
        Assert.True(afterCount.Value > beforeCount.Value);
    }

    [Fact]
    public void ItemAbsentFromBothInventories_ReportsLiveZeroCounts()
    {
        // The channel is confirmed live (another slot is present) and the tracked
        // item is simply not there: AssessCollectProgress's documented reasoning
        // says this is a known zero, never Unknown.
        GameplayObservation before = ObservationWithInventory(new[] { Slot(99, amount: 3) });
        GameplayObservation after = ObservationWithInventory(new[] { Slot(99, amount: 3) });

        (WalkRun? walk, WorldFact<int> beforeCount, WorldFact<int> afterCount) = RunRound(before, after);

        Assert.NotNull(walk);
        Assert.True(beforeCount.HasValue);
        Assert.True(afterCount.HasValue);
        Assert.Equal(0, beforeCount.Value);
        Assert.Equal(0, afterCount.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, beforeCount.Source);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, afterCount.Source);
    }

    // ------------------------------------------------- unobserved

    [Fact]
    public void UnobservedInventoryInBothReadings_ReportsUnknownCounts()
    {
        // An empty/unobserved Inventory ClassifiedValue means "never observed",
        // which AssessCollectProgress refuses to read as zero.
        GameplayObservation before = ObservationWithInventory(slots: null);
        GameplayObservation after = ObservationWithInventory(slots: null);

        (WalkRun? walk, WorldFact<int> beforeCount, WorldFact<int> afterCount) = RunRound(before, after);

        Assert.NotNull(walk);
        Assert.False(beforeCount.HasValue);
        Assert.False(afterCount.HasValue);
    }

    // ------------------------------------------------- stackable in two slots

    [Fact]
    public void ItemSpreadAcrossTwoSlots_IsSummed()
    {
        GameplayObservation before = ObservationWithInventory(new[] { Slot(201, amount: 2, slotIndex: 0), Slot(201, amount: 3, slotIndex: 4) });
        GameplayObservation after = ObservationWithInventory(new[] { Slot(201, amount: 2, slotIndex: 0), Slot(201, amount: 3, slotIndex: 4), Slot(201, amount: 1, slotIndex: 5) });

        (WalkRun? walk, WorldFact<int> beforeCount, WorldFact<int> afterCount) = RunRound(before, after);

        Assert.NotNull(walk);
        Assert.Equal(5, beforeCount.Value);
        Assert.Equal(6, afterCount.Value);
    }

    // ---------------------------------------- Run(...) argument validation

    // Program.cs's dispatch only checks argument *count*, not content, so a
    // caller that resolves a vnum to an empty string can reach Run with a
    // present-but-blank argument. It must be refused cleanly, not crash the
    // process with an unhandled exception -- same fix and same reasoning as
    // AP-05/A6's EngageCommand.Run.

    [Fact]
    public void Run_BlankVnum_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = CollectCommand.Run(x: 10, y: 20, vnum: "   ", requiredCount: null);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_ZeroRounds_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = CollectCommand.Run(x: 10, y: 20, vnum: "201", requiredCount: null, rounds: 0);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    // ------------------------------------------------- the wiring

    [Fact]
    public void TheRuntimeWiresTheCollectFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(CollectCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("CollectCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + CollectCommand.Flag + "\"", program, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
