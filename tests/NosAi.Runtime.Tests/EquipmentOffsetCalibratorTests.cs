using System.Collections.Immutable;
using System.Linq;
using NosAi.LiveIntegration;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class EquipmentOffsetCalibratorTests
{
    private static WornEquipment Equip(params (int Slot, int Vnum)[] slots) =>
        new(EntityId: 1, Slots: slots.Select(s => new WornEquipmentSlot(s.Slot, s.Vnum)).ToImmutableArray(),
            Opcode: EquipmentWireOpcode.Equip);

    [Fact]
    public void KeepOnlyNonZero_DropsZeroVnumSlots()
    {
        WornEquipment reading = Equip((0, 100), (1, 0), (5, 42));

        ImmutableDictionary<int, int> kept = EquipmentOffsetCalibrator.KeepOnlyNonZero(reading);

        Assert.Equal(2, kept.Count);
        Assert.Equal(100, kept[0]);
        Assert.Equal(42, kept[5]);
        Assert.False(kept.ContainsKey(1));
    }

    [Fact]
    public void TryPickAnchor_PicksLowestSlotNumber()
    {
        var slots = new Dictionary<int, int> { [5] = 42, [0] = 100, [3] = 7 }.ToImmutableDictionary();

        bool found = EquipmentOffsetCalibrator.TryPickAnchor(slots, out int anchorSlot, out int anchorVnum);

        Assert.True(found);
        Assert.Equal(0, anchorSlot);
        Assert.Equal(100, anchorVnum);
    }

    [Fact]
    public void TryPickAnchor_EmptyMap_ReturnsFalse()
    {
        bool found = EquipmentOffsetCalibrator.TryPickAnchor(ImmutableDictionary<int, int>.Empty, out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void KeepMatchingArray_KeepsOnlyAddressesWhereEverySlotMatches()
    {
        // Two array bases at 0x1000 and 0x2000, each 18 slots of 4 bytes.
        // Only the base at 0x1000 actually holds slot 0 = 100 AND slot 5 = 42.
        var memory = new Dictionary<long, int>
        {
            [0x1000 + 0 * 4] = 100,
            [0x1000 + 5 * 4] = 42,
            [0x2000 + 0 * 4] = 100,
            [0x2000 + 5 * 4] = 999, // mismatch: this base must be rejected
        };
        Func<IntPtr, int?> readInt32 = addr => memory.TryGetValue(addr.ToInt64(), out int v) ? v : null;

        var expected = new Dictionary<int, int> { [0] = 100, [5] = 42 }.ToImmutableDictionary();
        // Anchor addresses are where slot 0 (the anchor slot) was found: base + 0.
        var anchorAddresses = new List<IntPtr> { new(0x1000), new(0x2000) };

        List<EquipmentArrayHit> hits = EquipmentOffsetCalibrator.KeepMatchingArray(anchorAddresses, anchorSlot: 0, expected, readInt32);

        Assert.Single(hits);
        Assert.Equal(new IntPtr(0x1000), hits[0].BaseAddress);
    }

    [Fact]
    public void CanConfirm_SameSlotsAndValues_ReturnsFalse()
    {
        var before = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();
        var after = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();

        Assert.False(EquipmentOffsetCalibrator.CanConfirm(before, after));
    }

    [Fact]
    public void CanConfirm_DifferentValueAtSameSlot_ReturnsTrue()
    {
        var before = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();
        var after = new Dictionary<int, int> { [0] = 200 }.ToImmutableDictionary();

        Assert.True(EquipmentOffsetCalibrator.CanConfirm(before, after));
    }

    [Fact]
    public void CanConfirm_DifferentSlotSameCount_ReturnsTrue()
    {
        var before = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();
        var after = new Dictionary<int, int> { [1] = 100 }.ToImmutableDictionary();

        Assert.True(EquipmentOffsetCalibrator.CanConfirm(before, after));
    }

    [Fact]
    public void UnchangedReason_WhenUnchanged_NamesTheReason()
    {
        var same = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();

        Assert.Equal(EquipmentOffsetCalibrator.UnchangedPrefix, EquipmentOffsetCalibrator.UnchangedReason(same, same));
    }

    [Fact]
    public void UnchangedReason_WhenChanged_ReturnsNull()
    {
        var before = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();
        var after = new Dictionary<int, int> { [0] = 200 }.ToImmutableDictionary();

        Assert.Null(EquipmentOffsetCalibrator.UnchangedReason(before, after));
    }

    [Fact]
    public void Verdict_NoSurvivors_NamesNoCandidate()
    {
        Assert.Equal(EquipmentOffsetCalibrator.NoCandidatePrefix,
            EquipmentOffsetCalibrator.Verdict(new List<EquipmentArrayHit>()));
    }

    [Fact]
    public void Verdict_OneSurvivor_ReturnsNull()
    {
        var hit = new EquipmentArrayHit(new IntPtr(0x1000), ImmutableDictionary<int, int>.Empty);

        Assert.Null(EquipmentOffsetCalibrator.Verdict(new List<EquipmentArrayHit> { hit }));
    }

    [Fact]
    public void Verdict_MultipleSurvivors_NamesAmbiguous()
    {
        var hits = new List<EquipmentArrayHit>
        {
            new(new IntPtr(0x1000), ImmutableDictionary<int, int>.Empty),
            new(new IntPtr(0x2000), ImmutableDictionary<int, int>.Empty),
        };

        string? verdict = EquipmentOffsetCalibrator.Verdict(hits);

        Assert.StartsWith(EquipmentOffsetCalibrator.AmbiguousPrefix, verdict);
    }
}
