using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class ClientInventoryTests
{
    [Fact]
    public void FirstImport_ReportsEveryEntryAsAdded()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();

        ReferenceDiff diff = database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("Item.dat", "text", "records=4979", null),
            new ClientInventoryEntry("Skill.dat", "text", "records=1958", null),
        });

        Assert.Equal(2, diff.Added);
        Assert.Equal(0, diff.Changed);
        Assert.Equal(0, diff.Removed);
        Assert.Equal(0, diff.Unchanged);
    }

    [Fact]
    public void ReimportingTheSameEntries_ReportsThemUnchanged()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        var entries = new[] { new ClientInventoryEntry("Item.dat", "text", "records=4979", null) };

        database.ImportClientInventory(entries);
        ReferenceDiff diff = database.ImportClientInventory(entries);

        Assert.Equal(0, diff.Added);
        Assert.Equal(0, diff.Changed);
        Assert.Equal(1, diff.Unchanged);
    }

    [Fact]
    public void AChangedDetailString_IsReportedAsChanged_NotAddedOrUnchanged()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("Item.dat", "text", "records=4979", null),
        });

        ReferenceDiff diff = database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("Item.dat", "text", "records=4980", null),
        });

        Assert.Equal(0, diff.Added);
        Assert.Equal(1, diff.Changed);
        Assert.Equal(0, diff.Unchanged);
    }

    [Fact]
    public void AFileAbsentFromTheNewScan_IsReportedAsRemoved()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("Item.dat", "text", "records=4979", null),
            new ClientInventoryEntry("Skill.dat", "text", "records=1958", null),
        });

        ReferenceDiff diff = database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("Item.dat", "text", "records=4979", null),
        });

        Assert.Equal(0, diff.Added);
        Assert.Equal(1, diff.Removed);
        Assert.Equal(1, diff.Unchanged);
    }

    [Fact]
    public void ClientInventory_RoundTripsNullFields_WithoutFabricatingAValue()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        database.ImportClientInventory(new[]
        {
            new ClientInventoryEntry("broken.NOS", null, null, "no_line_terminator"),
        });

        ClientInventoryEntry stored = Assert.Single(database.ClientInventory());
        Assert.Equal("broken.NOS", stored.File);
        Assert.Null(stored.ArchiveType);
        Assert.Null(stored.Details);
        Assert.Equal("no_line_terminator", stored.Error);
    }
}
