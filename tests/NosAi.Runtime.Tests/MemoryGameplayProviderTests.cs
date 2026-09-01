using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The player's own position from memory, and the three checks that decide
/// whether a reading is one.
/// </summary>
/// <remarks>
/// F1-10. A moved offset does not fail — it returns four readable bytes and a
/// plausible number — so the validity check is the provider and reading the bytes
/// is the easy half. These pin that LIVE requires all three checks, that each
/// failure names itself, and above all that a failed check never falls back to
/// the last good value, which is the case ADR-0014 names in full.
/// </remarks>
public sealed class MemoryGameplayProviderTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr ModuleBase = new(0x400000);

    private static PlayerPositionOffsets Usable(int? mapIdOffset = null)
        => PlayerPositionOffsets.Found("NostaleClientX.exe", 0x10, 0x14, mapIdOffset, 2, Start);

    /// <summary>Serves coordinates by address, the way the real reader would.</summary>
    private sealed class FakeMemory
    {
        private readonly Dictionary<long, int> _values = new();

        public FakeMemory(int x, int y)
        {
            Set(x, y);
        }

        public void Set(int x, int y)
        {
            _values[ModuleBase.ToInt64() + 0x10] = x;
            _values[ModuleBase.ToInt64() + 0x14] = y;
        }

        /// <summary>Fails the read outright, the way an unmapped page does.</summary>
        public string? FailWith { get; set; }

        public ClassifiedValue<int?> Read(IntPtr address, DateTime at)
        {
            if (FailWith is { } reason)
                return ClassifiedValue<int?>.Unknown(reason);

            return _values.TryGetValue(address.ToInt64(), out int value)
                ? ClassifiedValue<int?>.Live(value, at)
                : ClassifiedValue<int?>.Unknown("read_failed:5");
        }
    }

    private sealed class Harness
    {
        public FakeMemory Memory { get; }
        public FakeTimeProvider Clock { get; }
        public MemoryGameplayProvider Provider { get; }
        public int? Speed { get; set; } = 11;
        public MapBounds? Bounds { get; set; }
        public PlayerPositionOffsets Offsets { get; set; }
        public IntPtr? ModuleBaseAddress { get; set; } = ModuleBase;

        public Harness(int x, int y, PlayerPositionOffsets? offsets = null)
        {
            Memory = new FakeMemory(x, y);
            Clock = new FakeTimeProvider(Start);
            Offsets = offsets ?? Usable();
            Provider = new MemoryGameplayProvider(
                () => ModuleBaseAddress,
                () => Offsets,
                Memory.Read,
                () => Speed,
                () => Bounds,
                Clock);
        }
    }

    /// <summary>A clock the test moves by hand, so continuity can be exercised.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTime start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    // ------------------------------------------------------------- a reading

    [Fact]
    public void A_coordinate_that_passes_every_check_is_live()
    {
        var harness = new Harness(121, 110);

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.True(position.HasValue);
        Assert.Equal(new MapPoint(121, 110), position.Value);
        Assert.Equal(DataSourceKind.Live, position.Source);
    }

    /// <summary>
    /// The first reading has nothing to be continuous with, so continuity does not
    /// apply. That is the start of a series, not a check that failed.
    /// </summary>
    [Fact]
    public void The_first_reading_needs_no_previous_one()
    {
        var harness = new Harness(121, 110) { Speed = null };

        Assert.True(harness.Provider.ReadPosition().HasValue);
    }

    [Fact]
    public void A_character_that_walked_a_plausible_distance_stays_live()
    {
        var harness = new Harness(121, 110);
        Assert.True(harness.Provider.ReadPosition().HasValue);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Memory.Set(124, 112);

        ClassifiedValue<MapPoint> moved = harness.Provider.ReadPosition();

        Assert.True(moved.HasValue);
        Assert.Equal(new MapPoint(124, 112), moved.Value);
    }

    // ------------------------------------------------------- 1. the range check

    /// <summary>
    /// A value this far out is a different field — a pointer, a timestamp, a
    /// count — not a character a long way off.
    /// </summary>
    [Theory]
    [InlineData(1_400_000, 110)]
    [InlineData(121, -5)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void A_value_outside_the_plausible_range_is_unknown(int x, int y)
    {
        var harness = new Harness(x, y);

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.StartsWith("position_out_of_range", position.FailureReason, StringComparison.Ordinal);
    }

    // -------------------------------------------------- 2. the continuity check

    /// <summary>
    /// Not a character that ran: an offset that moved. The distance is impossible
    /// in the time, and that is the only way a plausible number gives itself away.
    /// </summary>
    [Fact]
    public void A_step_larger_than_the_speed_allows_is_unknown()
    {
        var harness = new Harness(121, 110);
        Assert.True(harness.Provider.ReadPosition().HasValue);

        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));
        harness.Memory.Set(900, 900);

        ClassifiedValue<MapPoint> jumped = harness.Provider.ReadPosition();

        Assert.False(jumped.HasValue);
        Assert.StartsWith("position_moved_too_far", jumped.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case ADR-0014 names in full: a failed check must never publish the last
    /// good value. A retained coordinate is exactly what makes a moved offset
    /// invisible.
    /// </summary>
    [Fact]
    public void A_failed_check_never_falls_back_to_the_last_good_position()
    {
        var harness = new Harness(121, 110);
        ClassifiedValue<MapPoint> good = harness.Provider.ReadPosition();
        Assert.Equal(new MapPoint(121, 110), good.Value);

        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));
        harness.Memory.Set(900, 900);

        ClassifiedValue<MapPoint> after = harness.Provider.ReadPosition();

        Assert.False(after.HasValue);
        Assert.NotEqual(DataSourceKind.Cached, after.Source);
        Assert.Equal(DataSourceKind.Unknown, after.Source);
        Assert.Equal(default, after.Value);
    }

    /// <summary>
    /// The suspect reading is dropped along with the one before it: keeping it
    /// would let the next reading be continuous with a value already in doubt,
    /// which is how a moved offset settles into looking correct.
    /// </summary>
    [Fact]
    public void After_a_break_in_continuity_the_next_reading_starts_a_new_series()
    {
        var harness = new Harness(121, 110);
        harness.Provider.ReadPosition();
        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));
        harness.Memory.Set(900, 900);
        Assert.False(harness.Provider.ReadPosition().HasValue);

        // Same impossible jump again. It is accepted only because the history was
        // dropped, which is the intended behaviour and worth pinning.
        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));

        ClassifiedValue<MapPoint> restarted = harness.Provider.ReadPosition();

        Assert.True(restarted.HasValue);
        Assert.Equal(new MapPoint(900, 900), restarted.Value);
    }

    /// <summary>
    /// A check that cannot run has not passed. Two checks out of three is not the
    /// bar ADR-0014 sets for LIVE, and saying so is what keeps the bar meaningful.
    /// </summary>
    [Fact]
    public void Without_a_movement_speed_continuity_cannot_run_and_the_reading_is_unknown()
    {
        var harness = new Harness(121, 110);
        Assert.True(harness.Provider.ReadPosition().HasValue);

        harness.Speed = null;
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Memory.Set(122, 111);

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal("movement_speed_unknown", position.FailureReason);
    }

    // ------------------------------------------------ 3. the map coherence check

    [Fact]
    public void A_coordinate_outside_a_known_map_is_unknown()
    {
        var harness = new Harness(121, 110) { Bounds = new MapBounds(80, 80) };

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.StartsWith("position_outside_map", position.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>The card makes this check conditional: an unknown map skips it.</summary>
    [Fact]
    public void An_unknown_map_skips_the_coherence_check_rather_than_failing_it()
    {
        var harness = new Harness(121, 110) { Bounds = null };

        Assert.True(harness.Provider.ReadPosition().HasValue);
    }

    [Fact]
    public void A_coordinate_inside_a_known_map_passes()
    {
        var harness = new Harness(121, 110) { Bounds = new MapBounds(200, 200) };

        Assert.True(harness.Provider.ReadPosition().HasValue);
    }

    // ------------------------------------------------------- what it refuses on

    [Fact]
    public void Without_offsets_nothing_is_read_at_all()
    {
        var harness = new Harness(121, 110, PlayerPositionOffsets.Missing);

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal(PlayerPositionOffsets.NotFoundReason, position.FailureReason);
    }

    /// <summary>
    /// F1-9's rule, enforced rather than written down: an offset that has not
    /// survived a restart is an address that worked once.
    /// </summary>
    [Fact]
    public void Offsets_never_reverified_after_a_restart_are_refused()
    {
        PlayerPositionOffsets once = PlayerPositionOffsets.Found(
            "NostaleClientX.exe", 0x10, 0x14, null, verifiedRestarts: 0, Start);
        var harness = new Harness(121, 110, once);

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal(PlayerPositionOffsets.NotReverifiedReason, position.FailureReason);
    }

    [Fact]
    public void Without_the_module_attached_there_is_no_address_to_read()
    {
        var harness = new Harness(121, 110) { ModuleBaseAddress = null };

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal("client_module_not_attached", position.FailureReason);
    }

    /// <summary>A read that fails carries its own reason out rather than a generic one.</summary>
    [Fact]
    public void A_failed_read_reports_the_readers_reason()
    {
        var harness = new Harness(121, 110);
        harness.Memory.FailWith = "read_failed:299";

        ClassifiedValue<MapPoint> position = harness.Provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal("read_failed:299", position.FailureReason);
    }
}
