using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// Whether <see cref="SkillReferenceDecoder"/>'s tuple-position mapping
/// survives contact with a real client -- the check this decoder's own
/// remarks say it still needs. Skipped where no real installation is
/// present (<see cref="NosTaleClientFactAttribute"/>), same convention as
/// <see cref="ReferenceCoverageTests"/>: a skip is never evidence the
/// mapping was checked.
/// </summary>
public sealed class SkillReferenceDecoderRealClientTests
{
    private readonly ITestOutputHelper _output;

    public SkillReferenceDecoderRealClientTests(ITestOutputHelper output) => _output = output;

    [NosTaleClientFact]
    public void EveryNamedSkillDecodesToACostLevelTargetAndDataReadableForComparison()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        ImportOutcome outcome = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "skill"));
        Assert.True(outcome.Ok, outcome.FailureReason);

        LanguageImportReport language = importer.ImportLanguage(db, "IT");
        Assert.True(language.Ok, language.FailureReason);

        // Prints one line per decodable skill: its real vnum, the client's own
        // display name where the language table resolves one, and every
        // typed field this decoder produced. Compare any row's MpCost/
        // CooldownRaw/CastTimeRaw against the same skill's MP cost/cooldown
        // as shown in the client's own UI -- a match on even one row is
        // enough to move this decoder's tuple positions from provisional to
        // confirmed; a mismatch names exactly which position is wrong.
        int decoded = 0;
        for (int vnum = 0; vnum <= 20_000; vnum++)
        {
            IReadOnlyList<NosField>? fields = db.Lookup("skill", vnum);
            if (fields is null)
                continue;

            SkillReference? skill = SkillReferenceDecoder.Decode(new NosRecord(vnum, fields));
            if (skill is null)
                continue;

            string name = db.DisplayName("skill", vnum, "IT") ?? skill.NameKey;
            Evidence.Live(_output, $"skill[{vnum}]", name,
                $"CpCost={skill.CpCost} GoldCost={skill.GoldCost} SpecialCost={skill.SpecialCost} "
                + $"MpCost={skill.MpCost} CastTimeRaw={skill.CastTimeRaw} CooldownRaw={skill.CooldownRaw} "
                + $"Range={skill.Range} TargetRange={skill.TargetRange} TargetGroup={skill.TargetGroup} "
                + $"JobLevel={skill.JobLevel} Effetti={skill.Effects.Count}");
            decoded++;
        }

        Evidence.Live(_output, "skillDecodificate", decoded);

        // Loose on purpose: this proves the decoder ran against real records
        // at real volume, not a specific catalogue size this project has
        // never independently confirmed.
        Assert.True(decoded > 100, $"attese molte skill decodificate, contate {decoded}");
    }
}
