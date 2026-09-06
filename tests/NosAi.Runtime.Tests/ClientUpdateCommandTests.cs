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
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public void WhenTaletoolCannotBeStarted_TheFailureIsNamed_AndNoInventoryIsWritten()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        string empty = Path.Combine(Path.GetTempPath(), $"nosai-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            string report = ClientUpdateCommand.Run(
                database, empty, taletoolPath: Path.Combine(empty, "no-such-taletool.exe"));

            Assert.Contains("inventario file (taletool): non disponibile (process_start_failed:", report);
            Assert.Empty(database.ClientInventory());
        }
        finally
        {
            Directory.Delete(empty);
        }
    }
}
