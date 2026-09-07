using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class MonsterReferenceDecoderTests
{
    private static NosField Field(string name, params string[] values) => new(name, values);

    private static NosRecord FullRecord() => new(
        0,
        new[]
        {
            Field("VNUM", "0", "0"),
            Field("NAME", "zts1e"),
            Field("LEVEL", "16"),
            Field("RACE", "0", "1", "0"),
            Field("ATTRIB", "0", "0", "13", "0", "1", "0"),
            Field("HP/MP", "500", "0"),
            Field("EXP", "960", "120"),
            Field("PREATT", "0", "0", "8", "8", "40"),
            Field("SETTING", "8000", "0", "0", "0", "0", "0"),
            Field("ETC", "0", "0", "1", "0", "0", "1", "0", "0"),
            Field("PETINFO", "0", "0", "0", "0", "0"),
            Field("EFF", "0", "0", "0"),
            Field("ZSKILL", "0", "1", "5", "0", "0", "0", "0"),
            Field("WINFO", "0", "0", "0"),
            Field("WEAPON", "1", "1", "1", "2", "0", "0", "0"),
            Field("AINFO", "0", "0"),
            Field("ARMOR", "1", "0", "0", "0", "0"),
            Field("SKILL", "0", "0", "0"),
            Field("SKILL", "-1", "0", "0"),
            Field("SKILL", "-1", "0", "0"),
            Field("SKILL", "-1", "0", "0"),
            Field("BASIC", "27", "120", "0", "0", "1"),
            Field("CARD", "27", "120", "0", "0", "1"),
            Field("ITEM", "2000", "9000", "1"),
            Field("ITEM", "16", "800", "1"),
        });

    [Fact]
    public void ADecodedRecord_ProducesEveryNamedField_FromItsOwnTagPositions()
    {
        MonsterReference? monster = MonsterReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(monster);
        Assert.Equal(0, monster!.Vnum);
        Assert.Equal("zts1e", monster.NameKey);
        Assert.Equal(16, monster.Level);

        Assert.Equal(0, monster.RaceType);
        Assert.Equal(1, monster.RaceSubType);
        Assert.Equal(0, monster.HeroLevel);

        Assert.Equal(0, monster.Element);
        Assert.Equal(0, monster.ElementRate);
        Assert.Equal(13, monster.FireResistance);
        Assert.Equal(0, monster.WaterResistance);
        Assert.Equal(1, monster.LightResistance);
        Assert.Equal(0, monster.DarkResistance);

        Assert.Equal(500, monster.MaxHpBonus);
        Assert.Equal(0, monster.MaxMpBonus);
        Assert.Equal(960, monster.XpBonus);
        Assert.Equal(120, monster.JobXpBonus);

        Assert.Equal(0, monster.Hostility);
        Assert.Equal(0, monster.GroupAttack);
        Assert.Equal(8, monster.SeekRange);
        Assert.Equal(8, monster.MovementSpeed);
        Assert.Equal(40, monster.RespawnTimeDeciseconds);

        Assert.Equal(8000, monster.IconId);
        Assert.Equal(1, monster.IsPercentileDamage);
        Assert.Equal(1, monster.VisibleOnMinimapAsGreenDot);

        Assert.Equal(0, monster.EffectIdOnAttack);
        Assert.Equal(0, monster.BasicAttackType);
        Assert.Equal(5, monster.BasicAttackHitChance);
        Assert.Equal(1, monster.WeaponRange);
        Assert.Equal(0, monster.ArmorInfoDefenseType);
        Assert.Equal(1, monster.ArmorLevel);
    }

    [Fact]
    public void RecordWithNoVnum_DecodesToNull()
    {
        var record = new NosRecord(null, new[] { Field("NAME", "orphan") });

        Assert.Null(MonsterReferenceDecoder.Decode(record));
    }

    [Fact]
    public void ARecordWithNoTagsAtAll_DecodesToAllNullFieldsAndAnEmptyNameAndNoCollections()
    {
        var record = new NosRecord(9, new[] { Field("VNUM", "9", "0") });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.NotNull(monster);
        Assert.Equal(string.Empty, monster!.NameKey);
        Assert.Null(monster.Level);
        Assert.Null(monster.MaxHpBonus);
        Assert.Null(monster.Element);
        Assert.Empty(monster.Skills);
        Assert.Empty(monster.BasicEffects);
        Assert.Empty(monster.CardEffects);
        Assert.Empty(monster.Drops);
    }

    [Fact]
    public void EveryItemSlot_DecodesToItsOwnDrop_ItemVnumChanceAmountInThatOrder()
    {
        MonsterReference? monster = MonsterReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(monster);
        Assert.Equal(2, monster!.Drops.Count);

        MonsterDrop first = monster.Drops[0];
        Assert.Equal(2000, first.ItemVnum);
        Assert.Equal(9000, first.Chance);
        Assert.Equal(1, first.Amount);

        MonsterDrop second = monster.Drops[1];
        Assert.Equal(16, second.ItemVnum);
        Assert.Equal(800, second.Chance);
        Assert.Equal(1, second.Amount);
    }

    [Fact]
    public void ADropSlotMissingAValue_IsSkipped_RatherThanAddedPartially()
    {
        var record = new NosRecord(3, new[]
        {
            Field("VNUM", "3", "0"),
            // ItemVnum absent entirely -- not even a stray token.
            Field("ITEM"),
        });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.NotNull(monster);
        Assert.Empty(monster!.Drops);
    }

    [Fact]
    public void EverySkillSlot_DecodesToItsOwnEntry_VnumChanceForceInThatOrder()
    {
        MonsterReference? monster = MonsterReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(monster);
        Assert.Equal(4, monster!.Skills.Count);
        Assert.Equal(0, monster.Skills[0].Vnum);
        Assert.Equal(-1, monster.Skills[1].Vnum);
    }

    [Fact]
    public void EveryBasicAndCardEntry_DecodesWithNoLeadingSlotIndex_UnlikeSkillDat()
    {
        MonsterReference? monster = MonsterReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(monster);

        BCardApplication basic = Assert.Single(monster!.BasicEffects);
        Assert.Equal(27, basic.BCardVnum);
        Assert.Equal(120, basic.EffectVal1);
        Assert.Equal(0, basic.EffectVal2);
        Assert.Equal(0, basic.BCardSub);
        Assert.Equal(1, basic.Target);

        BCardApplication card = Assert.Single(monster.CardEffects);
        Assert.Equal(27, card.BCardVnum);
    }

    [Fact]
    public void ABasicEntryMissingAValue_IsSkipped_RatherThanAddedPartially()
    {
        var record = new NosRecord(3, new[]
        {
            Field("VNUM", "3", "0"),
            // Only 4 of the 5 values -- Target is absent.
            Field("BASIC", "27", "120", "0", "0"),
        });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.NotNull(monster);
        Assert.Empty(monster!.BasicEffects);
    }

    [Fact]
    public void ANonNumericValue_DecodesToNull_NeverAFabricatedNumber()
    {
        var record = new NosRecord(7, new[]
        {
            Field("VNUM", "7", "0"),
            Field("HP/MP", NosDataTable.UnknownValue, "0"),
        });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.NotNull(monster);
        Assert.Null(monster!.MaxHpBonus);
        Assert.Equal(0, monster.MaxMpBonus);
    }

    [Fact]
    public void Decode_ThrowsOnNullRecord()
    {
        Assert.Throws<ArgumentNullException>(() => MonsterReferenceDecoder.Decode(null!));
    }

    // ---- IsSpecialNonMonsterEntity ----

    [Fact]
    public void RaceType8_IsReportedAsASpecialNonMonsterEntity()
    {
        var record = new NosRecord(1, new[] { Field("VNUM", "1", "0"), Field("RACE", "8", "3", "0") });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.True(monster!.IsSpecialNonMonsterEntity);
    }

    [Fact]
    public void ARealMonsterRaceType_IsNotReportedAsSpecial()
    {
        MonsterReference? monster = MonsterReferenceDecoder.Decode(FullRecord());

        Assert.False(monster!.IsSpecialNonMonsterEntity);
    }

    [Fact]
    public void MissingRaceTag_ReportsUnknown_NeverAssumedToBeAMonster()
    {
        var record = new NosRecord(2, new[] { Field("VNUM", "2", "0") });

        MonsterReference? monster = MonsterReferenceDecoder.Decode(record);

        Assert.Null(monster!.IsSpecialNonMonsterEntity);
    }
}
