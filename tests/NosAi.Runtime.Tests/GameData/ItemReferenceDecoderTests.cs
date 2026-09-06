using NosAi.Core.WorldModel;
using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class ItemReferenceDecoderTests
{
    private static NosField Field(string name, params string[] values) => new(name, values);

    private static NosRecord FullRecord() => new(
        1,
        new[]
        {
            Field("VNUM", "1", "70"),
            Field("NAME", "zts3e"),
            Field("INDEX", "0", "0", "0", "0", "1", "0"),
            Field("TYPE", "0", "1"),
            Field("FLAG",
                "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0",
                "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0"),
            Field("DATA", "1", "20", "28", "20", "4", "70", "0", "0", "0", "0",
                "0", "0", "0", "0", "0", "0", "0", "0", "0", "0"),
            Field("BUFF", "-1", "80", "0", "0", "0"),
            Field("LINEDESC", "1", "zts4e"),
        });

    [Fact]
    public void ADecodedRecord_ProducesEveryNamedField_FromItsOwnTagPositions()
    {
        ItemReference? item = ItemReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(item);
        Assert.Equal(1, item!.Vnum);
        Assert.Equal(70, item.Price);
        Assert.Equal("zts3e", item.NameKey);

        Assert.Equal(0, item.InventoryType);
        Assert.Equal(0, item.ItemType);
        Assert.Equal(0, item.ItemSubType);
        Assert.Equal(EquipmentSlot.Weapon, item.Slot);
        Assert.Equal(1, item.IconId);
        Assert.Equal(0, item.VisualChangeId);

        Assert.Equal(0, item.AttackType);
        Assert.Equal(1, item.RequiredClass);

        Assert.Equal("zts4e", item.DescriptionKey);
    }

    [Fact]
    public void EveryFlagPosition_DecodesFromItsOwnIndex_InTheDocumentedOrder()
    {
        var record = new NosRecord(2, new[]
        {
            Field("VNUM", "2", "0"),
            Field("FLAG",
                "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11",
                "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23"),
        });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Equal(1, item!.Flag1);
        Assert.Equal(2, item.Flag2);
        Assert.Equal(3, item.Flag3);
        Assert.Equal(4, item.NoSelling);
        Assert.Equal(5, item.NoDropping);
        Assert.Equal(6, item.NoTrading);
        Assert.Equal(7, item.UseableMinilandItem);
        Assert.Equal(8, item.UseableMinilandItem2);
        Assert.Equal(9, item.ShowWarningOnUse);
        Assert.Equal(10, item.IsTimespaceRewardBox);
        Assert.Equal(11, item.ShowDescriptionOwner);
        Assert.Equal(12, item.Flag4);
        Assert.Equal(13, item.FollowMouseOnUse);
        Assert.Equal(14, item.ShowSomethingOnHover);
        Assert.Equal(15, item.CanBeColored);
        Assert.Equal(16, item.FemaleCanWear);
        Assert.Equal(17, item.MaleCanWear);
        Assert.Equal(18, item.NotUsed);
        Assert.Equal(19, item.PlaySoundOnPickup);
        Assert.Equal(20, item.UseReputationAsPrice);
        Assert.Equal(21, item.IsHeroLevelEquip);
        Assert.Equal(22, item.Flag5);
        Assert.Equal(23, item.IsLimited);
    }

    [Theory]
    [InlineData(0, EquipmentSlot.Weapon)]
    [InlineData(17, EquipmentSlot.MiniPet)]
    public void ADocumentedSlotCode_MapsOntoTheProjectsOwnEquipmentSlot(int code, EquipmentSlot expected)
    {
        var record = new NosRecord(3, new[]
        {
            Field("VNUM", "3", "0"),
            Field("INDEX", "0", "0", "0", code.ToString(), "0", "0"),
        });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Equal(expected, item!.Slot);
    }

    [Fact]
    public void TheNotEquippableCode_DecodesToNoSlot_RatherThanAFabricatedMember()
    {
        var record = new NosRecord(4, new[]
        {
            Field("VNUM", "4", "0"),
            Field("INDEX", "0", "0", "0", "-1", "0", "0"),
        });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Null(item!.Slot);
    }

    [Fact]
    public void RecordWithNoVnum_DecodesToNull()
    {
        var record = new NosRecord(null, new[] { Field("NAME", "orphan") });

        Assert.Null(ItemReferenceDecoder.Decode(record));
    }

    [Fact]
    public void ARecordWithNoTagsAtAll_DecodesToAllNullFieldsAndAnEmptyNameAndNoCollections()
    {
        var record = new NosRecord(9, new[] { Field("VNUM", "9") });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Equal(string.Empty, item!.NameKey);
        Assert.Null(item.Price);
        Assert.Null(item.ItemType);
        Assert.Null(item.Slot);
        Assert.Empty(item.Data);
        Assert.Empty(item.Effects);
        Assert.Null(item.DescriptionKey);
    }

    [Fact]
    public void EveryDataValue_IsPreservedInOrder_UnaltredAndUninterpreted()
    {
        ItemReference? item = ItemReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(item);
        Assert.Equal(20, item!.Data.Count);
        Assert.Equal(1, item.Data[0]);
        Assert.Equal(20, item.Data[1]);
        Assert.Equal(28, item.Data[2]);
        Assert.Equal(70, item.Data[5]);
    }

    [Fact]
    public void EveryBuffEntry_DecodesToItsOwnBCardApplication_WithNoLeadingSlotIndex()
    {
        ItemReference? item = ItemReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(item);
        BCardApplication buff = Assert.Single(item!.Effects);
        Assert.Equal(-1, buff.BCardVnum);
        Assert.Equal(80, buff.EffectVal1);
        Assert.Equal(0, buff.EffectVal2);
        Assert.Equal(0, buff.BCardSub);
        Assert.Equal(0, buff.Target);
    }

    [Fact]
    public void ABuffEntryMissingAValue_IsSkipped_RatherThanAddedPartially()
    {
        var record = new NosRecord(5, new[]
        {
            Field("VNUM", "5", "0"),
            // Only 4 of the 5 values -- Target is absent.
            Field("BUFF", "-1", "80", "0", "0"),
        });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Empty(item!.Effects);
    }

    [Fact]
    public void ANonNumericValue_DecodesToNull_NeverAFabricatedNumber()
    {
        var record = new NosRecord(6, new[]
        {
            Field("VNUM", "6", "0"),
            Field("INDEX", NosDataTable.UnknownValue, "0", "0", "0", "0", "0"),
        });

        ItemReference? item = ItemReferenceDecoder.Decode(record);

        Assert.NotNull(item);
        Assert.Null(item!.InventoryType);
        Assert.Equal(0, item.ItemType);
    }

    [Fact]
    public void Decode_ThrowsOnNullRecord()
    {
        Assert.Throws<ArgumentNullException>(() => ItemReferenceDecoder.Decode(null!));
    }
}
