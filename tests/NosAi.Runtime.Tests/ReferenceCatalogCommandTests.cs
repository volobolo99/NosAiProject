using NosAi.Runtime.GameData;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Observability;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The reference catalogue reaches <c>--world-replay</c> and <c>--reference-info</c>
/// without inventing a name when the file or the vnum is missing.
/// </summary>
public sealed class ReferenceCatalogCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-reference-catalog-" + Guid.NewGuid().ToString("N"));

    public ReferenceCatalogCommandTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // --------------------------------------------------------------- locator

    [Fact]
    public void TheLocatorFollowsTheDedicatedVolumeTheMapsAlreadyUse()
    {
        Assert.Equal(MapGridExtractor.VolumeLabel, GameReferenceLocator.VolumeLabel);
        Assert.Equal(MapGridExtractor.VolumeNotFound, GameReferenceLocator.VolumeNotFound);
        Assert.Equal("reference.db", GameReferenceLocator.FileName);
    }

    [Fact]
    public void AMissingFileInAKnownDirectoryIsNamedAndNotCreated()
    {
        GameReferenceLocation location = GameReferenceLocator.LocateIn(_dir);

        Assert.False(location.Exists);
        Assert.False(string.IsNullOrWhiteSpace(location.Path));
        Assert.StartsWith(GameReferenceLocator.DatabaseNotFound, location.FailureReason, StringComparison.Ordinal);
        Assert.Contains(location.Path!, location.FailureReason, StringComparison.Ordinal);
        Assert.False(File.Exists(location.Path));
        Evidence.Live(_output, "motivoAssente", location.FailureReason);
    }

    [Fact]
    public void AnExistingFileInAKnownDirectoryIsReturned()
    {
        string path = Path.Combine(_dir, GameReferenceLocator.FileName);
        using (GameReferenceDatabase created = GameReferenceDatabase.Open(path))
            created.CheckIntegrity();

        GameReferenceLocation location = GameReferenceLocator.LocateIn(_dir);

        Assert.True(location.Exists);
        Assert.Null(location.FailureReason);
        Assert.True(File.Exists(location.Path));
        Assert.Equal(Path.GetFullPath(path), location.Path);
        Evidence.Live(_output, "percorso", location.Path);
    }

    [Fact]
    public void OpeningAKnownFileDoesNotInventRows()
    {
        string path = Path.Combine(_dir, GameReferenceLocator.FileName);
        using (GameReferenceDatabase created = GameReferenceDatabase.Open(path))
        {
            created.Import(
                "monster", "test.NOS", "monster.dat", "C:/test",
                [Monster(9, "zts9e")],
                "payload"u8.ToArray());
        }

        Assert.True(GameReferenceLocator.TryOpen(
            GameReferenceLocator.LocateIn(_dir),
            out GameReferenceDatabase? opened,
            out string? reason),
            reason);
        using (opened)
        {
            Assert.NotNull(opened);
            Assert.Equal(1, opened!.Count("monster"));
            Assert.True(opened.Exists("monster", 9));
            Assert.False(opened.Exists("monster", 1));
        }
    }

    [Fact]
    public void AMissingLocationDoesNotOpenADatabase()
    {
        Assert.False(GameReferenceLocator.TryOpen(
            GameReferenceLocator.LocateIn(_dir),
            out GameReferenceDatabase? database,
            out string? reason));
        Assert.Null(database);
        Assert.StartsWith(GameReferenceLocator.DatabaseNotFound, reason, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- three names

    [Fact]
    public void TheThreeNameStatesStayDistinct()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        db.Import(
            "monster", "test.NOS", "monster.dat", "C:/test",
            [Monster(221, "zts1e")],
            "payload"u8.ToArray());
        db.ImportText("IT", "monster", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zts1e"] = "Volpe piccola"
        });

        string named = WorldReplayCommand.ResolveEntityName("Monster", 221, db);
        string absent = WorldReplayCommand.ResolveEntityName("Monster", null, db);
        string unknown = WorldReplayCommand.ResolveEntityName("Monster", 9, db);

        Assert.Equal("Volpe piccola", named);
        Assert.Equal(WorldReplayCommand.NameVnumAbsent, absent);
        Assert.Contains(WorldReplayCommand.VnumAbsent, absent, StringComparison.Ordinal);
        Assert.Equal(WorldReplayCommand.VnumNotInCatalog(9), unknown);
        Assert.Equal("vnum 9 non nel catalogo", unknown);

        Assert.NotEqual(named, absent);
        Assert.NotEqual(named, unknown);
        Assert.NotEqual(absent, unknown);
        Evidence.Live(_output, "conosciuto", named);
        Evidence.Live(_output, "vnumAssente", absent);
        Evidence.Live(_output, "vnumIgnoto", unknown);
    }

    [Fact]
    public void ACatalogKeyIsNotPrintedAsAName()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        db.Import(
            "monster", "test.NOS", "monster.dat", "C:/test",
            [Monster(221, "zts1e")],
            "payload"u8.ToArray());

        string name = WorldReplayCommand.ResolveEntityName("monster", 221, db);

        Assert.Equal(WorldReplayCommand.CatalogUnknown, name);
        Assert.DoesNotContain("zts1e", name, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCatalogIsAFourthStateNotAGuessedName()
    {
        Assert.Equal(
            WorldReplayCommand.CatalogNotLoaded,
            WorldReplayCommand.ResolveEntityName("monster", 9, catalog: null));
    }

    // ------------------------------------------------------- reference-info

    [Fact]
    public void ReferenceInfoOnAnInMemoryDatabasePrintsCountsProvenanceAndImportTime()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        DateTime imported = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        db.Import(
            "monster", "NSgtdData.NOS", "monster.dat", @"C:\Program Files (x86)\Nostale\NostaleData",
            [Monster(1, "zts1e"), Monster(2, "zts2e")],
            "payload"u8.ToArray(),
            imported);

        string text = ReferenceInfoCommand.Format(db);

        Assert.Contains("reference database: :memory:", text, StringComparison.Ordinal);
        Assert.Contains("exists: yes", text, StringComparison.Ordinal);
        Assert.Contains("monster: 2", text, StringComparison.Ordinal);
        Assert.Contains("item: 0", text, StringComparison.Ordinal);
        Assert.Contains("skill: 0", text, StringComparison.Ordinal);
        Assert.Contains("card: 0", text, StringComparison.Ordinal);
        Assert.Contains("archive=NSgtdData.NOS", text, StringComparison.Ordinal);
        Assert.Contains("table=monster.dat", text, StringComparison.Ordinal);
        Assert.Contains("imported=2026-09-01T12:00:00.0000000Z", text, StringComparison.Ordinal);
        Assert.Contains(@"client=C:\Program Files (x86)\Nostale\NostaleData", text, StringComparison.Ordinal);
        Evidence.Live(_output, "referenceInfo", text.ReplaceLineEndings(" | "));
    }

    [Fact]
    public void ReferenceInfoOnAMissingFilePrintsTheNamedRefusal()
    {
        GameReferenceLocation location = GameReferenceLocator.LocateIn(_dir);
        string text = ReferenceInfoCommand.Format(location);

        Assert.Contains("exists: no", text, StringComparison.Ordinal);
        Assert.Contains(GameReferenceLocator.DatabaseNotFound, text, StringComparison.Ordinal);
        Assert.DoesNotContain("exists: yes", text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, GameReferenceLocator.FileName)));
    }

    [Fact]
    public void TheRuntimeWiresWorldReplayToTheLocatorAndExposesReferenceInfo()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("GameReferenceLocator.TryOpen", program, StringComparison.Ordinal);
        Assert.Contains("WorldReplayCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("ReferenceInfoCommand", program, StringComparison.Ordinal);
        Assert.Contains("\"--reference-info\"", program, StringComparison.Ordinal);
    }

    private static NosRecord Monster(int vnum, string nameKey) =>
        new(vnum, [
            new NosField("VNUM", [vnum.ToString()]),
            new NosField("LEVEL", ["1"]),
            new NosField("NAME", [nameKey])
        ]);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
