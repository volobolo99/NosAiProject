using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// Whether <see cref="CardReferenceDecoder"/> maps the client's own
/// <c>BCard.dat</c> <c>VNUM</c>/<c>NAME</c> tags correctly, and — the check
/// this decoder's remarks say matters — whether the above-zero
/// <see cref="BCardApplication.BCardVnum"/> values a real <c>Skill.dat</c>
/// skill applies resolve inside the BCard table. The real-client checks
/// skip where no installation is present
/// (<see cref="NosTaleClientFactAttribute"/>); a skip is never evidence
/// the mapping was checked.
/// </summary>
public sealed class CardReferenceDecoderTests
{
    private readonly ITestOutputHelper _output;

    public CardReferenceDecoderTests(ITestOutputHelper output) => _output = output;

    private static NosField Field(string name, params string[] values) => new(name, values);

    private static NosRecord FullRecord() => new(
        19,
        new[]
        {
            Field("VNUM", "19"),
            Field("ICON", "-1"),
            Field("NAME", "zts23e"),
            Field("DESC", "0", "1", "1", "1", "0"),
            Field("SUBJ1", "zts946e"),
            Field("SUBJ2", "zts650e"),
            Field("LIST1-1", "zts318e"),
            Field("LIST1-2", "zts318f"),
            Field("END"),
        });

    // ---------------------------------------------------------- synthetic

    [Fact]
    public void ADecodedRecord_ProducesEveryNamedField_FromItsOwnTag()
    {
        CardReference? card = CardReferenceDecoder.Decode(FullRecord());

        Assert.NotNull(card);
        Assert.Equal(19, card!.Vnum);
        Assert.Equal("zts23e", card.NameKey);
    }

    [Fact]
    public void RecordWithNoVnum_DecodesToNull()
    {
        var record = new NosRecord(null, new[] { Field("NAME", "zts23e") });

        Assert.Null(CardReferenceDecoder.Decode(record));
    }

    [Fact]
    public void ANonNumericVnum_DecodesToNull_NeverAFabricatedNumber()
    {
        // NosDataTable itself renders an undecodable packed number as
        // "UNKNOWN" (NosDataTable.UnknownValue), which then fails the VNUM
        // int parse and leaves the record with a null Vnum. This must stay
        // a null result, not silently become a fabricated vnum.
        var record = new NosRecord(null, new[]
        {
            Field("VNUM", NosDataTable.UnknownValue),
            Field("NAME", "zts23e"),
        });

        Assert.Null(CardReferenceDecoder.Decode(record));
    }

    [Fact]
    public void ARecordWithNoNameTag_DecodesToAnEmptyNameKey()
    {
        var record = new NosRecord(19, new[] { Field("VNUM", "19") });

        CardReference? card = CardReferenceDecoder.Decode(record);

        Assert.NotNull(card);
        Assert.Equal(19, card!.Vnum);
        Assert.Equal(string.Empty, card.NameKey);
    }

    [Fact]
    public void UndocumentedTags_AreIgnored_WithoutFailingTheRest()
    {
        // ICON/DESC/SUBJ/LIST/END are the tags the source documents but this
        // decoder deliberately does not promote (see CardReference remarks):
        // a record carrying them must still decode VNUM/NAME, and an extra,
        // entirely undocumented tag must not break it either.
        var record = new NosRecord(24, new[]
        {
            Field("VNUM", "24"),
            Field("NAME", "zts3222e"),
            Field("SOME_UNDOCUMENTED_TAG", "1", "2", "3"),
        });

        CardReference? card = CardReferenceDecoder.Decode(record);

        Assert.NotNull(card);
        Assert.Equal(24, card!.Vnum);
        Assert.Equal("zts3222e", card.NameKey);
    }

    [Fact]
    public void Decode_ThrowsOnNullRecord()
    {
        Assert.Throws<ArgumentNullException>(() => CardReferenceDecoder.Decode(null!));
    }

    // ------------------------------------------------------ real client

    [NosTaleClientFact]
    public void BcardTable_ImportsAtRealVolume()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        ImportOutcome outcome = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "bcard"));
        Assert.True(outcome.Ok, outcome.FailureReason);

        int count = db.Count("bcard");
        Evidence.Live(_output, "bcardRecords", count);

        // Measured against the installed client (2026-09-07): 131 records,
        // vnums 1..131 (see the evidence line above). The threshold is low
        // on purpose — it proves the table decoded at real volume, not a
        // specific catalogue size this project has never independently
        // confirmed.
        Assert.True(count > 100, $"attese molte bcard, contate {count}");
    }

    [NosTaleClientFact]
    public void EveryInRangeBCardVnumARealSkillApplies_ResolvesInTheBcardTable()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        ImportOutcome bcard = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "bcard"));
        Assert.True(bcard.Ok, bcard.FailureReason);

        ImportOutcome card = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "card"));
        Assert.True(card.Ok, card.FailureReason);

        ImportOutcome skill = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "skill"));
        Assert.True(skill.Ok, skill.FailureReason);

        int skillsWithEffects = 0;
        int effectsTotal = 0;
        int emptySlots = 0;
        int resolved = 0;
        int outOfRange = 0;
        var outOfRangeVnums = new HashSet<int>();
        var samples = new List<string>();

        for (int vnum = 0; vnum <= 20_000; vnum++)
        {
            IReadOnlyList<NosField>? fields = db.Lookup("skill", vnum);
            if (fields is null)
                continue;

            SkillReference? skillRef = SkillReferenceDecoder.Decode(new NosRecord(vnum, fields));
            if (skillRef is null || skillRef.Effects.Count == 0)
                continue;

            skillsWithEffects++;
            foreach (BCardApplication effect in skillRef.Effects)
            {
                effectsTotal++;
                if (effect.BCardVnum <= 0)
                {
                    // Zero and negative BCardVnum are the empty-slot sentinel
                    // the other decoders already accept (Item.dat's BUFF and
                    // Skill.dat's BASIC write -1 or 0 for a slot with no
                    // effect) — not references into the catalogue.
                    emptySlots++;
                    continue;
                }

                IReadOnlyList<NosField>? bcardFields = db.Lookup("bcard", effect.BCardVnum);
                CardReference? cardRef = bcardFields is null
                    ? null
                    : CardReferenceDecoder.Decode(new NosRecord(effect.BCardVnum, bcardFields));

                if (cardRef is not null)
                {
                    resolved++;
                    if (samples.Count < 20)
                        samples.Add($"skill[{vnum}] -> bcard[{effect.BCardVnum}] = {cardRef.NameKey}");
                }
                else
                {
                    outOfRange++;
                    outOfRangeVnums.Add(effect.BCardVnum);
                }
            }
        }

        Evidence.Live(_output, "bcardCrossCheck",
            $"skillsWithEffects={skillsWithEffects} effectsTotal={effectsTotal} "
            + $"emptySlots={emptySlots} resolved={resolved} outOfRange={outOfRange} "
            + $"distinctOutOfRange={outOfRangeVnums.Count}");
        Evidence.Live(_output, "bcardOutOfRangeVnums",
            string.Join(",", outOfRangeVnums.OrderBy(v => v)));
        foreach (string sample in samples)
            Evidence.Live(_output, "bcardResolved", sample);

        // The decoder's mapping is proven by the volume that resolves at
        // real scale: 3 674 of 3 766 above-zero references (2026-09-07). A
        // wrong VNUM/NAME position would collapse this to near zero.
        Assert.True(skillsWithEffects > 100, $"attese molte skill con effetti, trovate {skillsWithEffects}");
        Assert.True(resolved > 3_000, $"attesi migliaia di rimandi bcard risolti, risolti {resolved}");
    }
}
