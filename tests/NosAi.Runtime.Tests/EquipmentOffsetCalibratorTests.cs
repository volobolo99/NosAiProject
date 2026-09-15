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

    /// <summary>A sparse byte-addressable fake memory, for testing a window read without a real process.</summary>
    private sealed class FakeMemory
    {
        private readonly Dictionary<long, byte> _bytes = new();

        public void WriteInt32(long address, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
                _bytes[address + i] = bytes[i];
        }

        public void WriteInt16(long address, short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
                _bytes[address + i] = bytes[i];
        }

        public byte[] ReadWindow(IntPtr address, int length)
        {
            var window = new byte[length];
            for (int i = 0; i < length; i++)
                window[i] = _bytes.TryGetValue(address.ToInt64() + i, out byte b) ? b : (byte)0;
            return window;
        }
    }

    [Fact]
    public void KeepMatchingArray_KeepsOnlyAnchorsWhereEveryOtherKnownValueIsNearby()
    {
        // Anchor vnum 100 (slot 0) was found by the scan at both 0x1000 and 0x2000.
        // Only near 0x1000 does the other known value (slot 5 = 42) actually sit
        // somewhere in the search window; near 0x2000 it is simply not there.
        var memory = new FakeMemory();
        memory.WriteInt32(0x1000 + 20, 42);
        memory.WriteInt32(0x2000 + 20, 999); // unrelated value: must not satisfy slot 5

        var expected = new Dictionary<int, int> { [0] = 100, [5] = 42 }.ToImmutableDictionary();
        var anchorAddresses = new List<IntPtr> { new(0x1000), new(0x2000) };

        List<EquipmentArrayHit> hits = EquipmentOffsetCalibrator.KeepMatchingArray(
            anchorAddresses, anchorSlot: 0, expected, memory.ReadWindow);

        Assert.Single(hits);
        Assert.Equal(new IntPtr(0x1000), hits[0].BaseAddress);
    }

    [Fact]
    public void KeepMatchingArray_FindsAKnownValueStoredAsA2ByteShort()
    {
        // WeaponSkin/WingSkin are declared `short` on the wire (Rutherther/NosSmooth's
        // InEquipmentSubPacket), so a matching field in memory may be 2 bytes wide
        // instead of 4 like every other slot.
        var memory = new FakeMemory();
        memory.WriteInt16(0x1000 + 20, 12345);

        var expected = new Dictionary<int, int> { [0] = 100, [8] = 12345 }.ToImmutableDictionary();

        List<EquipmentArrayHit> hits = EquipmentOffsetCalibrator.KeepMatchingArray(
            new List<IntPtr> { new(0x1000) }, anchorSlot: 0, expected, memory.ReadWindow);

        Assert.Single(hits);
    }

    [Fact]
    public void KeepMatchingArray_NullWindow_SkipsThatCandidate()
    {
        var expected = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();
        Func<IntPtr, int, byte[]?> readWindow = (_, _) => null;

        List<EquipmentArrayHit> hits = EquipmentOffsetCalibrator.KeepMatchingArray(
            new List<IntPtr> { new(0x1000) }, anchorSlot: 0, expected, readWindow);

        Assert.Empty(hits);
    }

    [Fact]
    public void Confirm_KeepsSurvivorOnlyWhenNewValuesAreAlsoNearby()
    {
        var memory = new FakeMemory();
        memory.WriteInt32(0x1000 + 20, 77); // the new round's value for slot 5

        var previous = new List<EquipmentArrayHit>
        {
            new(new IntPtr(0x1000), ImmutableDictionary<int, int>.Empty),
        };
        var expected = new Dictionary<int, int> { [0] = 100, [5] = 77 }.ToImmutableDictionary();
        memory.WriteInt32(0x1000 + 40, 100);

        List<EquipmentArrayHit> survivors = EquipmentOffsetCalibrator.Confirm(previous, expected, memory.ReadWindow);

        Assert.Single(survivors);
        Assert.Equal(new IntPtr(0x1000), survivors[0].BaseAddress);
    }

    [Fact]
    public void Confirm_ValueNoLongerNearby_DropsTheSurvivor()
    {
        var memory = new FakeMemory(); // deliberately empty: nothing matches
        var previous = new List<EquipmentArrayHit>
        {
            new(new IntPtr(0x1000), ImmutableDictionary<int, int>.Empty),
        };
        var expected = new Dictionary<int, int> { [0] = 100 }.ToImmutableDictionary();

        List<EquipmentArrayHit> survivors = EquipmentOffsetCalibrator.Confirm(previous, expected, memory.ReadWindow);

        Assert.Empty(survivors);
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
