using System.Text;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Re-imports the reference catalogue and the broader file inventory from
/// the installed client, reporting what changed since the last run (CLI
/// <c>--client-updates</c>).
/// </summary>
/// <remarks>
/// <para>
/// Two independent channels answer "what did the last client update
/// change", each already able to say so on its own:
/// <see cref="ReferenceImporter"/>'s five known tables (monster/item/skill/
/// card/bcard), each carrying its own content hash in
/// <see cref="GameReferenceDatabase.Import"/>'s diff; and the broader,
/// undifferentiated file inventory <see cref="TaletoolInvoker"/> reads from
/// every file under the data directory, decoded or not, via
/// <see cref="GameReferenceDatabase.ImportClientInventory"/>. Running both
/// on every invocation is what turns "run this after each client update"
/// into an actual answer rather than a manual diff nobody performs.
/// </para>
/// <para>
/// The taletool channel degrades on its own when the executable is absent:
/// the known-table diff still runs and is still useful without it, the
/// same "partial capability, not a hard failure" shape
/// <see cref="ReferenceImporter"/> already applies per table.
/// </para>
/// </remarks>
public static class ClientUpdateCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--client-updates";

    /// <summary>Console entry: resolves the dedicated volume and the installed client.</summary>
    public static int Run()
    {
        if (!GameReferenceLocator.TryFindDedicatedDataDirectory(out string dataDirectory, out string? volumeReason))
        {
            Console.WriteLine($"reference database: (non risolvibile)");
            Console.WriteLine($"reason: {volumeReason ?? GameReferenceLocator.VolumeNotFound}");
            return 1;
        }

        GameReferenceLocation location = GameReferenceLocator.LocateIn(dataDirectory);
        using GameReferenceDatabase database = GameReferenceDatabase.Open(location.Path!);
        Console.Write(Run(database, ReferenceImporter.DefaultDataDirectory));
        return 0;
    }

    /// <summary>
    /// The testable core: given an already-open database and a client
    /// directory, refreshes both channels and formats what changed.
    /// </summary>
    public static string Run(GameReferenceDatabase database, string clientDataDirectory, string? taletoolPath = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDataDirectory);

        var text = new StringBuilder();
        var importer = new ReferenceImporter(clientDataDirectory);

        if (!importer.ClientAvailable)
        {
            text.AppendLine($"client: non trovato in {clientDataDirectory}");
            return text.ToString();
        }

        text.AppendLine($"client: {clientDataDirectory}");

        ImportReport report = importer.ImportAll(database);
        foreach (ImportOutcome outcome in report.Outcomes)
        {
            if (!outcome.Ok)
            {
                text.AppendLine($"  {outcome.Table.Kind}: fallito ({outcome.FailureReason})");
                continue;
            }

            ReferenceDiff diff = outcome.Diff;
            text.AppendLine(
                $"  {outcome.Table.Kind}: +{diff.Added} ~{diff.Changed} -{diff.Removed} ={diff.Unchanged}");
        }

        TaletoolScanResult scan = TaletoolInvoker.Scan(clientDataDirectory, taletoolPath);
        if (!scan.Ok)
        {
            text.AppendLine($"inventario file (taletool): non disponibile ({scan.FailureReason})");
            return text.ToString();
        }

        ReferenceDiff inventoryDiff = database.ImportClientInventory(scan.Files);
        text.AppendLine(
            $"inventario file (taletool): {scan.Files.Count} file, "
            + $"+{inventoryDiff.Added} ~{inventoryDiff.Changed} -{inventoryDiff.Removed} ={inventoryDiff.Unchanged}");
        foreach (string sample in inventoryDiff.Samples)
            text.AppendLine($"    {sample}");

        return text.ToString();
    }
}
