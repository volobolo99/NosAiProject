using Microsoft.Data.Sqlite;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Runtime.WorldModel.Fusion;
using NosAi.Storage;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-09/A2: <see cref="ActionOutcomeRecorder"/> -- records one
/// already-executed act's evidence into a real temp-file
/// <see cref="ActionOutcomeLedgerStore"/>, or is a documented no-op when
/// the store is <see langword="null"/>. The expected ledger entry is
/// computed the same way <c>WorldActionProjectorTests</c> does -- through
/// <see cref="NosAi.Core.WorldModel.WorldActionProjector"/> itself -- and
/// asserted for equality, never re-derived a second, independent way.
/// </summary>
public sealed class ActionOutcomeRecorderTests : IDisposable
{
    private readonly string _databasePath;

    public ActionOutcomeRecorderTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-action-outcome-recorder-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static readonly DateTime Now = new(2026, 9, 6, 12, 30, 0, DateTimeKind.Utc);
    private static readonly ActionId AnyActionId = new("action-1");

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static CombatActionCandidate UseConsumableCandidate() =>
        new(CombatActionKind.UseConsumable, item: new ItemId("2"));

    private static CombatExecutionEvidence CombatEvidence() =>
        new(
            UseConsumableCandidate(),
            ResourceKind.Health,
            WorldFact<double>.Live(40, 1d, Now),
            WorldFact<double>.Live(90, 1d, Now),
            CombatExecutionResult.ResourceGainConfirmed,
            null,
            Now);

    private static MovementExecutionEvidence MovementEvidence() =>
        new(
            new MapId("map-1"),
            new TileCoordinate(1, 1),
            WorldFact<TileCoordinate>.Live(new TileCoordinate(1, 1), 1d, Now),
            MovementExecutionResult.Succeeded,
            null,
            Now);

    // -- null store: documented no-op -----------------------------------------

    [Fact]
    public void RecordCombat_WithANullStore_DoesNothing_NoFileCreated()
    {
        ActionOutcomeRecorder.RecordCombat(
            null,
            AnyActionId,
            UseConsumableCandidate(),
            Now,
            CombatEvidence(),
            MemoryType.Combat,
            "consumable-slot:2",
            Now);

        Assert.False(File.Exists(_databasePath));
    }

    [Fact]
    public void RecordMovement_WithANullStore_DoesNothing_NoFileCreated()
    {
        ActionOutcomeRecorder.RecordMovement(
            null,
            AnyActionId,
            "scout-step",
            Now,
            MovementEvidence(),
            MemoryType.Spatial,
            "scout:map-1",
            Now);

        Assert.False(File.Exists(_databasePath));
    }

    // -- real store: entry matches the projector's own mapping ----------------

    [Fact]
    public void RecordCombat_WithARealStore_AppendsExactlyWhatTheProjectorProduces()
    {
        const string context = "consumable-slot:2";
        using var store = new ActionOutcomeLedgerStore(_databasePath, Options());

        ActionOutcomeRecorder.RecordCombat(
            store,
            AnyActionId,
            UseConsumableCandidate(),
            Now,
            CombatEvidence(),
            MemoryType.Combat,
            context,
            Now);

        ActionOutcomeLedgerEntry entry = Assert.Single(store.LoadByContext(context));

        // The expected value, computed the same way WorldActionProjectorTests
        // computes its own: FromCombat + ToLedgerEntry on the same inputs.
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseConsumableCandidate(), Now, CombatEvidence());
        ActionOutcomeLedgerEntry expected = WorldActionProjector.ToLedgerEntry(action, MemoryType.Combat, context, Now);

        Assert.Equal(AnyActionId, entry.ActionId);
        Assert.Equal(MemoryType.Combat, entry.Category);
        Assert.Equal(context, entry.Context);
        Assert.Equal(expected.Outcome, entry.Outcome); // never re-derived
        Assert.Equal(expected.RecordedAtUtc, entry.RecordedAtUtc);
    }

    [Fact]
    public void RecordMovement_WithARealStore_AppendsExactlyWhatTheProjectorProduces()
    {
        const string context = "scout:map-1";
        using var store = new ActionOutcomeLedgerStore(_databasePath, Options());

        ActionOutcomeRecorder.RecordMovement(
            store,
            AnyActionId,
            "scout-step",
            Now,
            MovementEvidence(),
            MemoryType.Spatial,
            context,
            Now);

        ActionOutcomeLedgerEntry entry = Assert.Single(store.LoadByContext(context));

        WorldAction action = WorldActionProjector.FromMovement(AnyActionId, "scout-step", Now, MovementEvidence());
        ActionOutcomeLedgerEntry expected = WorldActionProjector.ToLedgerEntry(action, MemoryType.Spatial, context, Now);

        Assert.Equal(AnyActionId, entry.ActionId);
        Assert.Equal(MemoryType.Spatial, entry.Category);
        Assert.Equal(context, entry.Context);
        Assert.Equal(expected.Outcome, entry.Outcome); // never re-derived
        Assert.Equal(expected.RecordedAtUtc, entry.RecordedAtUtc);
    }
}
