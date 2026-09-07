using System.Linq;
using System.Text;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Re-imports the reference catalogue and the broader file inventory from
/// the installed client, reporting what changed since the last run (CLI
/// <c>--client-updates</c>).
/// </summary>
/// <remarks>
/// Two independent channels answer "what did the last client update
/// change", each already able to say so on its own: <see cref="ReferenceImporter"/>'s
/// five known tables (monster/item/skill/card/bcard), each carrying its
/// own content hash in <see cref="GameReferenceDatabase.Import"/>'s diff;
/// and the broader, undifferentiated file inventory
/// <see cref="ClientDirectoryScanner"/> reads from every file under the
/// data directory, decoded or not, via
/// <see cref="GameReferenceDatabase.ImportClientInventory"/>. Both run
/// entirely inside this process, on the same archive reader
/// <see cref="ReferenceImporter"/> already trusts -- nothing external is
/// started. Running both on every invocation is what turns "run this
/// after each client update" into an actual answer rather than a manual
/// diff nobody performs.
/// </remarks>
/// <summary>
/// One run's outcome: the same formatted text <see cref="ClientUpdateCommand.Run(GameReferenceDatabase, string)"/>
/// returns, plus whether anything actually changed -- so a caller that
/// only cares about the second question (the automatic startup check)
/// never has to re-parse the first.
/// </summary>
public sealed record ClientUpdateReport(string Text, bool AnyChange);

public static class ClientUpdateCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--client-updates";

    /// <summary>
    /// Chiede anche i nomi, nella lingua indicata: <c>--with-language IT</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opzionale di proposito, e non parte del giro normale.</b> Questo
    /// comando gira anche da solo all'avvio (<c>Program.Main</c>), e le tabelle
    /// di lingua stanno in un archivio diverso da quello dei dati: farle leggere
    /// a ogni avvio aggiungerebbe un costo a un controllo che deve restare
    /// leggero. I nomi cambiano quando cambia il client, non fra un'esecuzione e
    /// l'altra.
    /// </para>
    /// <para>
    /// <see cref="ReferenceImporter.ImportLanguage"/> esiste da prima e non
    /// aveva alcun chiamante di produzione: la tabella <c>text</c> del catalogo
    /// era vuota, ogni <c>name_key</c> irrisolvibile, e <c>--reference-info</c>
    /// non lo diceva. Una lingua non installata viene riferita, mai sostituita
    /// con un'altra — il commento di quel metodo spiega perche'.
    /// </para>
    /// </remarks>
    public const string WithLanguageOption = "--with-language";

    /// <summary>Console entry: resolves the dedicated volume and the installed client.</summary>
    public static int Run() => Run(Array.Empty<string>());

    /// <summary>Console entry with the argument vector, for <c>--with-language</c>.</summary>
    public static int Run(string[] args)
    {
        if (!GameReferenceLocator.TryFindDedicatedDataDirectory(out string dataDirectory, out string? volumeReason))
        {
            Console.WriteLine($"reference database: (non risolvibile)");
            Console.WriteLine($"reason: {volumeReason ?? GameReferenceLocator.VolumeNotFound}");
            return 1;
        }

        string? language = ParseLanguage(args, out string? refusal);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            return 1;
        }

        GameReferenceLocation location = GameReferenceLocator.LocateIn(dataDirectory);
        using GameReferenceDatabase database = GameReferenceDatabase.Open(location.Path!);
        Console.Write(RunDetailed(database, ReferenceImporter.DefaultDataDirectory, language).Text);
        return 0;
    }

    /// <summary>
    /// Legge <c>--with-language &lt;LANG&gt;</c>. Null quando non c'e'; il
    /// rifiuto quando c'e' senza valore.
    /// </summary>
    internal static string? ParseLanguage(string[] args, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(args);
        refusal = null;

        int i = Array.FindIndex(args, a =>
            string.Equals(a, WithLanguageOption, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return null;

        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            refusal = "with_language_without_value";
            return null;
        }

        return args[i + 1];
    }

    /// <summary>
    /// The testable core: given an already-open database and a client
    /// directory, refreshes both channels and formats what changed.
    /// </summary>
    public static string Run(GameReferenceDatabase database, string clientDataDirectory) =>
        RunDetailed(database, clientDataDirectory).Text;

    /// <summary>
    /// Same refresh as <see cref="Run(GameReferenceDatabase, string)"/>, also
    /// reporting whether anything changed -- what the automatic startup
    /// check (<c>Program.Main</c>) needs to decide whether to say anything
    /// at all.
    /// </summary>
    public static ClientUpdateReport RunDetailed(
        GameReferenceDatabase database, string clientDataDirectory, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDataDirectory);

        var text = new StringBuilder();
        var importer = new ReferenceImporter(clientDataDirectory);

        if (!importer.ClientAvailable)
        {
            text.AppendLine($"client: non trovato in {clientDataDirectory}");
            return new ClientUpdateReport(text.ToString(), AnyChange: false);
        }

        text.AppendLine($"client: {clientDataDirectory}");
        bool anyChange = false;

        ImportReport report = importer.ImportAll(database);
        foreach (ImportOutcome outcome in report.Outcomes)
        {
            if (!outcome.Ok)
            {
                text.AppendLine($"  {outcome.Table.Kind}: fallito ({outcome.FailureReason})");
                continue;
            }

            ReferenceDiff diff = outcome.Diff;
            anyChange |= diff.AnyChange;
            text.AppendLine(
                $"  {outcome.Table.Kind}: +{diff.Added} ~{diff.Changed} -{diff.Removed} ={diff.Unchanged}");
        }

        IReadOnlyList<ClientInventoryEntry> files = ClientDirectoryScanner.Scan(clientDataDirectory);
        ReferenceDiff inventoryDiff = database.ImportClientInventory(files);
        anyChange |= inventoryDiff.AnyChange;
        text.AppendLine(
            $"inventario file: {files.Count} file, "
            + $"+{inventoryDiff.Added} ~{inventoryDiff.Changed} -{inventoryDiff.Removed} ={inventoryDiff.Unchanged}");
        foreach (string sample in inventoryDiff.Samples)
            text.AppendLine($"    {sample}");

        if (language is not null)
        {
            // Un fallimento qui non e' un fallimento del comando: i dati sono
            // gia' stati importati, e una lingua non installata e' una risposta.
            // ImportLanguage rifiuta di sostituirne un'altra, ed e' il motivo per
            // cui il rapporto stampa la ragione invece di un conteggio a zero.
            LanguageImportReport names = importer.ImportLanguage(database, language);
            if (names.Ok)
            {
                anyChange |= names.Total > 0;
                text.AppendLine(
                    $"nomi ({names.Language}): {names.Total} voci "
                    + string.Join(", ", names.EntriesByKind.Select(e => $"{e.Key}={e.Value}")));
            }
            else
            {
                text.AppendLine($"nomi ({names.Language}): non importati ({names.FailureReason})");
            }
        }

        return new ClientUpdateReport(text.ToString(), anyChange);
    }
}
