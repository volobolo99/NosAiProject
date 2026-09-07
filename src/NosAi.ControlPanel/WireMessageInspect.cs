using System.Globalization;
using System.IO;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;

namespace NosAi.ControlPanel;

/// <summary>One <c>sayi</c> the recording carried, as the panel shows it.</summary>
/// <param name="Opcode">The packet that carried it. Today always <c>sayi</c>.</param>
/// <param name="MessageId">Field 4 — the message id. Confirmed on two recordings.</param>
/// <param name="ArgumentType">Field 5 — <c>2</c> when the argument is an item vnum.</param>
/// <param name="Argument">Field 6 — the argument itself.</param>
/// <param name="ArgumentName">
/// What the catalogue calls the argument, or a stated reason. Never blank, and
/// never the number repeated as if it were a name.
/// </param>
internal readonly record struct WireMessageRow(
    string Opcode,
    int MessageId,
    int ArgumentType,
    int Argument,
    string ArgumentName);

/// <summary>What the panel found in a recording and in the catalogue, side by side.</summary>
/// <param name="Ok">False when the recording could not be read; <paramref name="Reason"/> says why.</param>
internal readonly record struct WireMessageRead(
    bool Ok,
    string Reason,
    IReadOnlyList<WireMessageRow> Messages,
    IReadOnlyList<GameTextRow> CatalogueMatches,
    IReadOnlyList<string> SearchedWords);

/// <summary>
/// Legge dalla registrazione appena scritta i messaggi che il filo ha portato, e
/// dal catalogo le righe di testo che somigliano a quello che l'operatore ha
/// annotato.
/// </summary>
/// <remarks>
/// <para>
/// <b>Perché sta nel pannello.</b> T-16 è aperto su un punto solo: <c>sayi</c>
/// porta un id di messaggio e il catalogo porta il testo, e il legame fra i due
/// non esiste — verificato due volte che l'id non indicizza <c>conststring</c>,
/// né direttamente né a scarto costante. La prova che lo stabilirebbe è una
/// coppia osservata: cosa c'era a schermo, e quale riga della cattura. Il
/// pannello raccoglieva già le due metà in file separati; qui le mette una
/// accanto all'altra, nello stesso momento in cui esistono entrambe.
/// </para>
/// <para>
/// <b>Cosa questa classe non fa.</b> Non collega niente. Mostra gli id che il
/// filo ha detto e le righe del catalogo che contengono le parole della nota, e
/// chi guarda decide se sono la stessa cosa. Un accostamento non è una prova, e
/// promuoverlo a lookup è esattamente ciò che è stato rifiutato due volte.
/// </para>
/// </remarks>
internal static class WireMessageInspect
{
    /// <summary>Field 5 when the argument is an item vnum. Measured: 20 packets out of 20.</summary>
    private const int ItemArgumentType = 2;

    /// <summary>The catalogue table that holds the client's own message texts.</summary>
    private const string MessageTable = "conststring";

    /// <summary>The only opcode whose layout is measured. See <see cref="ReadMessages"/>.</summary>
    private const string MessageOpcode = "sayi";

    /// <summary>How many catalogue rows to offer. More than one because one is not a confirmation.</summary>
    private const int MaxMatches = 12;

    /// <summary>
    /// Le parole più corte di questo non si cercano: «di», «il», «un» pescano
    /// mezzo catalogo e non restringono niente.
    /// </summary>
    private const int MinimumWordLength = 5;

    /// <summary>How many words of the note to search with, longest first.</summary>
    private const int MaxWords = 4;

    /// <summary>
    /// Reads the recording's message packets and, from the note, the catalogue
    /// rows that might be what the operator saw.
    /// </summary>
    /// <param name="capturePath">The recording just written.</param>
    /// <param name="note">What the operator typed while it ran. May be empty.</param>
    /// <param name="language">The client language whose texts were imported.</param>
    public static WireMessageRead Read(string capturePath, string note, string language)
    {
        if (!File.Exists(capturePath))
            return Failed($"la cattura non è sul disco: {capturePath}");

        List<WireMessageRow> messages;
        try
        {
            messages = ReadMessages(capturePath, language);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Failed($"la cattura non si è aperta: {ex.Message}");
        }

        IReadOnlyList<string> words = WordsOf(note);
        IReadOnlyList<GameTextRow> matches = SearchCatalogue(words, language);

        return new WireMessageRead(true, "", messages, matches, words);
    }

    private static WireMessageRead Failed(string reason) => new(
        false, reason, Array.Empty<WireMessageRow>(), Array.Empty<GameTextRow>(), Array.Empty<string>());

    /// <summary>Every <c>sayi</c> in the recording, in the order it arrived.</summary>
    /// <remarks>
    /// <para>
    /// Il decoder del filo non legge questo opcode — non c'è niente da pubblicare
    /// finché il legame col testo non esiste — quindi qui si guardano i campi
    /// direttamente. È lettura diagnostica: niente di quel che esce di qui entra
    /// nel World Model.
    /// </para>
    /// <para>
    /// <b>Perché solo <c>sayi</c>.</b> <c>msgi</c> porta quasi certamente la
    /// stessa cosa, ma il suo tracciato non è stato misurato: su
    /// <c>messaggi.noscap</c> ha sette campi contro i nove di <c>sayi</c>, e in
    /// quei due pacchetti l'unico campo non nullo è il secondo. Leggerlo con gli
    /// indici di <c>sayi</c> darebbe un id di messaggio pari a zero e lo
    /// mostrerebbe accanto a quelli veri, indistinguibile. Si aggiunge quando
    /// qualcuno lo misura.
    /// </para>
    /// </remarks>
    private static List<WireMessageRow> ReadMessages(string capturePath, string language)
    {
        var rows = new List<WireMessageRow>();
        GameReferenceLocation location = GameReferenceLocator.Locate();
        using GameReferenceDatabase? catalogue = location.Exists && location.Path is { } path
            ? GameReferenceDatabase.OpenExisting(path)
            : null;

        // La stessa catena di `--wire-inspect`, chiamata e non riscritta: il corpo
        // dei pacchetti NosTale e' codificato, e il framer piu' `NosTaleWorldDecoder`
        // sono cio' che lo rende leggibile. Una seconda copia di quel percorso qui
        // dentro divergerebbe dalla prima al primo cambiamento del formato.
        using IPacketSource packets = CaptureFile.Open(capturePath);
        IReadOnlyList<string> lines = WireInspectCommand.RawLines(
            packets, DataSourceKind.Cached, MessageOpcode, maxLines: 0);

        foreach (string line in lines)
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 7)
                continue;
            if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argumentType)
                || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argument))
                continue;

            rows.Add(new WireMessageRow(
                MessageOpcode, id, argumentType, argument,
                NameOfArgument(catalogue, language, argumentType, argument)));
        }

        return rows;
    }

    /// <summary>
    /// What the catalogue calls the argument — only when the packet said it is
    /// an item.
    /// </summary>
    /// <remarks>
    /// Il campo 5 dice di che genere è l'argomento, e vale <c>2</c> quando è il
    /// vnum di un oggetto — 20 pacchetti su 20. Con qualunque altro valore il
    /// numero non è un oggetto, e cercarlo nel catalogo degli oggetti darebbe un
    /// nome plausibile e falso.
    /// </remarks>
    private static string NameOfArgument(
        GameReferenceDatabase? catalogue, string language, int argumentType, int argument)
    {
        if (argumentType != ItemArgumentType)
            return $"il campo 5 vale {argumentType}: l'argomento non è dichiarato un oggetto";
        if (catalogue is null)
            return "catalogo non sul disco";

        return catalogue.DisplayName("item", argument, language)
            ?? $"vnum {argument} non nel catalogo degli oggetti";
    }

    /// <summary>
    /// The words of the note worth searching with: the longest ones, deduplicated.
    /// </summary>
    /// <remarks>
    /// Le parole lunghe restringono, le corte no. Ordinate dalla più lunga perché
    /// è quella che ha più probabilità di comparire in una riga sola del
    /// catalogo, che è esattamente il risultato utile.
    /// </remarks>
    internal static IReadOnlyList<string> WordsOf(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return Array.Empty<string>();

        return note
            .Split(
                new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= MinimumWordLength)
            .Select(w => w.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(w => w.Length)
            .ThenBy(w => w, StringComparer.Ordinal)
            .Take(MaxWords)
            .ToArray();
    }

    /// <summary>Catalogue rows containing any of the words, shortest text first.</summary>
    private static IReadOnlyList<GameTextRow> SearchCatalogue(IReadOnlyList<string> words, string language)
    {
        if (words.Count == 0)
            return Array.Empty<GameTextRow>();

        GameReferenceLocation location = GameReferenceLocator.Locate();
        if (!location.Exists || location.Path is not { } path)
            return Array.Empty<GameTextRow>();

        using GameReferenceDatabase catalogue = GameReferenceDatabase.OpenExisting(path);

        var seen = new Dictionary<string, GameTextRow>(StringComparer.Ordinal);
        foreach (string word in words)
        {
            foreach (GameTextRow row in catalogue.SearchText(language, MessageTable, word, MaxMatches))
                seen.TryAdd(row.Key, row);
        }

        return seen.Values
            .OrderBy(r => r.Value.Length)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(MaxMatches)
            .ToArray();
    }

    /// <summary>The block the panel prints under the recording, ready to read.</summary>
    /// <remarks>
    /// Un blocco solo e non due elenchi separati: il punto è vederli insieme.
    /// Quando la nota è vuota lo dice, perché una registrazione senza nota non
    /// chiude T-16 e va rifatta finché il messaggio è ancora fresco.
    /// </remarks>
    public static string Describe(WireMessageRead read)
    {
        if (!read.Ok)
            return $"Il filo non si è potuto rileggere: {read.Reason}";

        var lines = new StringBuilder();

        if (read.Messages.Count == 0)
        {
            lines.AppendLine(
                "Il filo non ha portato nessun messaggio di sistema in questa registrazione. "
                + "Provocane uno — raccogli un oggetto da terra — e registra di nuovo.");
        }
        else
        {
            lines.AppendLine(CultureInfo.InvariantCulture, $"Il filo ha detto ({read.Messages.Count}):");
            foreach (WireMessageRow row in read.Messages.Take(20))
            {
                lines.AppendLine(CultureInfo.InvariantCulture,
                    $"  {row.Opcode} messaggio {row.MessageId}, argomento {row.Argument} — {row.ArgumentName}");
            }
        }

        if (read.SearchedWords.Count == 0)
        {
            lines.AppendLine(
                "Nessuna parola nella nota: senza il testo visto a schermo la registrazione non chiude T-16.");
            return lines.ToString().TrimEnd();
        }

        lines.AppendLine(CultureInfo.InvariantCulture,
            $"Righe del catalogo che contengono {string.Join(", ", read.SearchedWords)}:");

        if (read.CatalogueMatches.Count == 0)
        {
            lines.AppendLine("  nessuna. Prova con le parole esatte che erano a schermo.");
            return lines.ToString().TrimEnd();
        }

        foreach (GameTextRow row in read.CatalogueMatches)
            lines.AppendLine(CultureInfo.InvariantCulture, $"  chiave {row.Key} — {row.Value}");

        lines.AppendLine(
            "Un accostamento non è una prova: serve che lo stesso id e lo stesso testo tornino "
            + "insieme almeno due volte, su messaggi diversi.");

        return lines.ToString().TrimEnd();
    }
}
