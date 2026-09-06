using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class ClientUpdateCommandTests
{
    [Fact]
    public void MissingClientDirectory_IsReported_WithoutTouchingTheDatabase()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        string missing = Path.Combine(Path.GetTempPath(), $"nosai-missing-{Guid.NewGuid():N}");

        string report = ClientUpdateCommand.Run(database, missing);

        Assert.Contains($"client: non trovato in {missing}", report);
    }

    [Fact]
    public void AnExistingButEmptyClientDirectory_ReportsEveryKnownTableAsFailed()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        string empty = Path.Combine(Path.GetTempPath(), $"nosai-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            string report = ClientUpdateCommand.Run(database, empty);

            foreach (ReferenceTable table in ReferenceImporter.Tables)
                Assert.Contains($"{table.Kind}: fallito", report);
            Assert.Contains("inventario file: 0 file", report);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public void AnUnrecognizedLooseFile_IsCountedInTheInventory_WithoutBeingMistakenForAnArchive()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        string directory = Path.Combine(Path.GetTempPath(), $"nosai-loose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "not an archive");

            string report = ClientUpdateCommand.Run(database, directory);

            Assert.Contains("inventario file: 1 file, +1 ~0 -0 =0", report);
            ClientInventoryEntry stored = Assert.Single(database.ClientInventory());
            Assert.Equal("readme.txt", stored.File);
            Assert.Null(stored.ArchiveType);
            Assert.NotNull(stored.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
