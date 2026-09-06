using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class SkillReferenceDecoderTests
{
    private static NosField Field(string name, params string[] values) => new(name, values);

    private static NosRecord FullRecord() => new(
        1177,
        new[]
        {
            Field("VNUM", "1177", "0"),
            Field("NAME", "sk1177n"),
            Field("TYPE", "0", "10", "1", "0", "0", "3"),
            Field("COST", "10", "0", "0"),
            Field("LEVEL", "0", "1", "0", "0", "0"),
            Field("EFFECT", "1177", "4979", "1000", "0", "0", "0", "0", "0", "0"),
            Field("TARGET", "1", "0", "5", "10", "2"),
            Field("DATA", "0", "0", "0", "0", "8", "180", "0", "0", "24", "0", "-1", "5", "10", "0", "0"),
            Field("BASIC", "0", "24", "0", "48", "280", "0"),
            Field("BASIC", "1", "-1", "0", "0", "0", "0"),
            Field("BASIC", "2", "-1", "0", "0", "0", "0"),
            Field("BASIC", "3", "-1", "0", "0", "0", "0"),
            Field("BASIC", "4", "-1", "0", "0", "0", "0"),
        });

    [Fact]
    public void ADecodedRecord_ProducesEveryNamedField_FromItsOwnTagPositions()
    {
        SkillReference? skill = SkillReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(skill);
        Assert.Equal(1177, skill!.Vnum);
        Assert.Equal("sk1177n", skill.NameKey);

        Assert.Equal(0, skill.SkillType);
        Assert.Equal(10, skill.CastId);
        Assert.Equal(1, skill.JobClass);
        Assert.Equal(0, skill.AttackType);
        Assert.Equal(0, skill.SecondaryWeapon);
        Assert.Equal(3, skill.Element);

        Assert.Equal(10, skill.CpCost);
        Assert.Equal(0, skill.GoldCost);
        Assert.Equal(0, skill.SpecialCost);

        Assert.Equal(0, skill.JobLevel);
        Assert.Equal(1, skill.AdventurerLevel);
        Assert.Equal(0, skill.SwordsmanLevel);
        Assert.Equal(0, skill.ArcherLevel);
        Assert.Equal(0, skill.MageLevel);

        Assert.Equal(1, skill.TargetType);
        Assert.Equal(0, skill.HitType);
        Assert.Equal(5, skill.Range);
        Assert.Equal(10, skill.TargetRange);
        Assert.Equal(2, skill.TargetGroup);

        Assert.Equal(0, skill.UpgradeSkill);
        Assert.Equal(0, skill.PartnerSkillId);
        Assert.Equal(8, skill.CastTimeRaw);
        Assert.Equal(180, skill.CooldownRaw);
        Assert.Equal(24, skill.MpCost);
        Assert.Equal(0, skill.DashSpeed);
        Assert.Equal(-1, skill.RequiredItemVnum);
        Assert.Equal(5, skill.DataRange);
        Assert.Equal(10, skill.DataTargetRange);
    }

    [Fact]
    public void RecordWithNoVnum_DecodesToNull()
    {
        var record = new NosRecord(null, new[] { Field("NAME", "orphan") });

        Assert.Null(SkillReferenceDecoder.Decode(record));
    }

    [Fact]
    public void MissingCostTag_LeavesCostFieldsNull_WithoutAffectingOtherTags()
    {
        var record = new NosRecord(5, new[]
        {
            Field("VNUM", "5", "0"),
            Field("LEVEL", "0", "1", "0", "0", "0"),
        });

        SkillReference? skill = SkillReferenceDecoder.Decode(record);

        Assert.NotNull(skill);
        Assert.Null(skill!.CpCost);
        Assert.Null(skill.GoldCost);
        Assert.Null(skill.SpecialCost);
        Assert.Equal(0, skill.JobLevel);
        Assert.Equal(1, skill.AdventurerLevel);
    }

    [Fact]
    public void ARecordWithNoTagsAtAll_DecodesToAllNullFieldsAndAnEmptyNameAndNoEffects()
    {
        var record = new NosRecord(9, new[] { Field("VNUM", "9", "0") });

        SkillReference? skill = SkillReferenceDecoder.Decode(record);

        Assert.NotNull(skill);
        Assert.Equal(string.Empty, skill!.NameKey);
        Assert.Null(skill.SkillType);
        Assert.Null(skill.CpCost);
        Assert.Null(skill.TargetType);
        Assert.Null(skill.CastTimeRaw);
        Assert.Null(skill.DataRange);
        Assert.Empty(skill.Effects);
    }

    [Fact]
    public void EveryBasicEntry_DecodesToItsOwnBCardApplication_SkippingItsOwnIndexSlot()
    {
        SkillReference? skill = SkillReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(skill);
        Assert.Equal(5, skill!.Effects.Count);

        BCardApplication first = skill.Effects[0];
        Assert.Equal(24, first.BCardVnum);
        Assert.Equal(0, first.BCardSub);
        Assert.Equal(48, first.EffectVal1);
        Assert.Equal(280, first.EffectVal2);
        Assert.Equal(0, first.Target);
    }

    [Fact]
    public void ABasicEntryMissingAValue_IsSkipped_RatherThanAddedPartially()
    {
        var record = new NosRecord(3, new[]
        {
            Field("VNUM", "3", "0"),
            // Only 4 of the 5 real values after the index slot -- Target is absent.
            Field("BASIC", "0", "24", "0", "48", "280"),
        });

        SkillReference? skill = SkillReferenceDecoder.Decode(record);

        Assert.NotNull(skill);
        Assert.Empty(skill!.Effects);
    }

    [Fact]
    public void ANonNumericValue_DecodesToNull_NeverAFabricatedNumber()
    {
        // NosDataTable itself renders an undecodable packed number as
        // "UNKNOWN" (NosDataTable.UnknownValue) -- this must survive as a
        // null typed value here, not silently become 0.
        var record = new NosRecord(7, new[]
        {
            Field("VNUM", "7", "0"),
            Field("COST", NosDataTable.UnknownValue, "0", "0"),
        });

        SkillReference? skill = SkillReferenceDecoder.Decode(record);

        Assert.NotNull(skill);
        Assert.Null(skill!.CpCost);
        Assert.Equal(0, skill.GoldCost);
    }

    [Fact]
    public void Decode_ThrowsOnNullRecord()
    {
        Assert.Throws<ArgumentNullException>(() => SkillReferenceDecoder.Decode(null!));
    }
}
