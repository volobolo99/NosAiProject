using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// <see cref="SkillCatalogue"/>: the reference skill read with a structural split
/// between fields whose in-game meaning the wire confirmed and fields that stay a
/// provisional read of the client's own table.
/// </summary>
public sealed class SkillCatalogueTests
{
    private static GameReferenceDatabase Imported(out string directory)
    {
        directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);
        Assert.True(importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "skill")).Ok);
        Assert.True(importer.ImportLanguage(db, "IT").Ok);
        return db;
    }

    [NosTaleClientFact]
    public void A_real_vnum_resolves_and_a_missing_one_does_not_resemble_it()
    {
        using GameReferenceDatabase db = Imported(out _);
        var catalogue = new SkillCatalogue(db);

        SkillCatalogueLookup found = catalogue.Lookup(226);
        SkillCatalogueLookup missing = catalogue.Lookup(999999);

        Assert.True(found.Ok);
        Assert.NotNull(found.Skill);
        Assert.Equal(226, found.Skill!.Vnum);

        Assert.False(missing.Ok);
        Assert.Null(missing.Skill);
        Assert.Equal(SkillCatalogue.SkillNotInCatalogueReason, missing.FailureReason);
        // The two answers do not resemble each other: one is a skill, the other a reason.
        Assert.NotEqual(found.Skill, missing.Skill);
    }

    [NosTaleClientFact]
    public void A_provisional_field_is_distinguishable_from_a_confirmed_one_by_its_type()
    {
        using GameReferenceDatabase db = Imported(out _);
        CataloguedSkill skill = new SkillCatalogue(db).Lookup(226).Skill!;

        // Confirmed fields carry a value with a real source; provisional ones do not.
        Assert.True(skill.CastId.HasValue);
        Assert.Equal(DataSourceKind.Derived, skill.CastId.Source);
        Assert.Equal(6, skill.CastId.Value);

        Assert.True(skill.CooldownTenths.HasValue);
        Assert.Equal(250, skill.CooldownTenths.Value);

        // CpCost and MpCost stay provisional: A1 could not decide which is the MP cost.
        Assert.False(skill.CpCost.HasValue);
        Assert.False(skill.MpCost.HasValue);
        Assert.Equal(DataSourceKind.Unknown, skill.CpCost.Source);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, skill.CpCost.FailureReason);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, skill.MpCost.FailureReason);

        // Range stays provisional: A4 could not measure it, the player's position
        // is never on the wire.
        Assert.False(skill.Range.HasValue);
        Assert.Equal(SkillCatalogue.RangeProvisionalReason, skill.Range.FailureReason);
    }

    [Fact]
    public void An_absent_catalogue_is_a_named_reason_not_an_empty_catalogue()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nosai-skillcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            GameReferenceLocation location = GameReferenceLocator.LocateIn(dir);

            Assert.False(location.Exists);
            Assert.StartsWith(GameReferenceLocator.DatabaseNotFound, location.FailureReason, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dir, GameReferenceLocator.FileName)));
            // No catalogue was created, and nothing was read as "zero skills".
            Assert.False(GameReferenceLocator.TryOpen(location, out _, out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The values the wire measures rest on are the client's own table; if an
    /// update changed them, every confirmed field would silently shift meaning, so
    /// the seven observed skills are pinned to the table's numbers here.
    /// </summary>
    [NosTaleClientFact]
    public void The_seven_observed_skills_hold_the_catalogues_values()
    {
        using GameReferenceDatabase db = Imported(out _);
        var catalogue = new SkillCatalogue(db);

        (int Vnum, int CastId, int CpCost, int MpCost, int Cooldown, int Area)[] expected =
        {
            (200, 0, 0, 0, 6, 0),
            (220, 0, 0, 0, 7, 0),
            (222, 2, 5, 15, 50, 0),
            (223, 3, 7, 25, 100, 0),
            (224, 4, 8, 30, 250, 1),
            (226, 6, 15, 28, 250, 3),
            (228, 8, 7, 22, 100, 0),
        };

        foreach ((int vnum, int castId, int cp, int mp, int cooldown, int area) in expected)
        {
            CataloguedSkill skill = catalogue.Lookup(vnum).Skill!;
            SkillReference raw = skill.Raw;

            Assert.Equal(castId, raw.CastId);
            Assert.Equal(cp, raw.CpCost);
            Assert.Equal(mp, raw.MpCost);
            Assert.Equal(cooldown, raw.CooldownRaw);
            Assert.Equal(area, raw.TargetRange);
        }
    }

    [NosTaleClientFact]
    public void The_two_cost_fields_stay_distinct_and_neither_is_declared_the_mp_cost()
    {
        using GameReferenceDatabase db = Imported(out _);
        CataloguedSkill skill = new SkillCatalogue(db).Lookup(226).Skill!;

        // A1 did not decide: the raw numbers differ (15 vs 28), and the classified
        // view presents neither as "the MP cost". Both stay Unknown with the same
        // named reason, so a caller cannot mistake either for a confirmed cost.
        Assert.NotEqual(skill.Raw.CpCost, skill.Raw.MpCost);
        Assert.False(skill.CpCost.HasValue);
        Assert.False(skill.MpCost.HasValue);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, skill.CpCost.FailureReason);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, skill.MpCost.FailureReason);
    }
}
