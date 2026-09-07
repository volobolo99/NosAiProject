using NosAi.Runtime.Autonomy;
using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The adapter that lets a per-cycle, pure consumer ask the reference
/// catalogue what a vnum is without holding its <c>SqliteConnection</c>.
/// </summary>
public sealed class CatalogueClassifierTests
{
    private static NosRecord Monster(int vnum) => new(vnum, new List<NosField>
    {
        new("VNUM", new[] { vnum.ToString() }),
        new("LEVEL", new[] { "10" }),
        new("RACE", new[] { "0", "1", "0" })
    });

    /// <summary>RaceType 8 is <c>monster.dat</c>'s own bucket of traps, teleporters and talkable NPCs.</summary>
    private static NosRecord SpecialEntity(int vnum) => new(vnum, new List<NosField>
    {
        new("VNUM", new[] { vnum.ToString() }),
        new("LEVEL", new[] { "1" }),
        new("RACE", new[] { "8", "3", "0" })
    });

    private static void Import(GameReferenceDatabase db, params NosRecord[] records) =>
        db.Import("monster", "test.NOS", "monster.dat", "C:/test", records,
            System.Text.Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid()}"));

    [Fact]
    public void NoCatalogue_AnswersNotLoadedForEveryVnum_AndRemembersNothing()
    {
        var classifier = new CatalogueClassifier(catalogue: null);

        Assert.Equal(CatalogueClass.CatalogueNotLoaded, classifier.Classify(36));
        Assert.Equal(CatalogueClass.CatalogueNotLoaded, classifier.Classify(37));
        Assert.Equal(0, classifier.RememberedCount);
    }

    [Fact]
    public void AnImportedMonster_IsClassifiedAsAMonster()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Monster(36));

        Assert.Equal(CatalogueClass.Monster, new CatalogueClassifier(db).Classify(36));
    }

    [Fact]
    public void ARaceType8Row_IsClassifiedAsASpecialNonMonsterEntity()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, SpecialEntity(900));

        Assert.Equal(CatalogueClass.SpecialNonMonsterEntity, new CatalogueClassifier(db).Classify(900));
    }

    [Fact]
    public void AVnumTheTableNeverHeardOf_IsAbsent_NotAMonster()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Monster(36));

        Assert.Equal(CatalogueClass.AbsentFromMonsterTable, new CatalogueClassifier(db).Classify(999));
    }

    /// <summary>
    /// The whole point of this type: the projector asks once per entity per
    /// fusion cycle, and one classification costs up to three SQL queries.
    /// </summary>
    [Fact]
    public void RepeatedQuestionsAboutTheSameVnum_AreRememberedOnce()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Monster(36), SpecialEntity(900));
        var classifier = new CatalogueClassifier(db);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(CatalogueClass.Monster, classifier.Classify(36));
            Assert.Equal(CatalogueClass.SpecialNonMonsterEntity, classifier.Classify(900));
            Assert.Equal(CatalogueClass.AbsentFromMonsterTable, classifier.Classify(999));
        }

        Assert.Equal(3, classifier.RememberedCount);
    }

    /// <summary>
    /// A closed connection makes every read throw. That is a transient I/O
    /// failure, so it answers Unreadable and remembers nothing -- caching it
    /// would turn one bad read into a permanent verdict for that vnum.
    /// </summary>
    [Fact]
    public void AnUnreadableCatalogue_AnswersUnreadable_AndIsNeverRemembered()
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Monster(36));
        var classifier = new CatalogueClassifier(db);
        Assert.Equal(CatalogueClass.Monster, classifier.Classify(36));

        db.Dispose();

        Assert.Equal(CatalogueClass.CatalogueUnreadable, classifier.Classify(77));
        Assert.Equal(1, classifier.RememberedCount);

        // The answer given before the failure is still remembered, and is
        // still answered from memory without touching the dead connection.
        Assert.Equal(CatalogueClass.Monster, classifier.Classify(36));
    }
}
