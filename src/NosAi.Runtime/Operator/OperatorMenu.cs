using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Navigation;

namespace NosAi.Runtime.Operator;

/// <summary>Where the hunt for the map id currently stands, as one reading.</summary>
/// <param name="MapsReady">Whether the extracted grids were found.</param>
/// <param name="Grids">How many of them.</param>
/// <param name="HasFile">Whether a candidate file exists at all.</param>
/// <param name="Candidates">How many candidates survive.</param>
/// <param name="Anchored">How many of those are measured from a base, not a bare address.</param>
/// <param name="Passes">How many different maps the set has tracked.</param>
/// <param name="Restarts">How many client restarts it has survived.</param>
/// <param name="Winner">The single proven candidate, when there is one.</param>
/// <param name="PlayerX">Where the character stood on the last pass.</param>
/// <param name="PlayerY">Where the character stood on the last pass.</param>
/// <param name="BestAnchored">The first candidate measured from a base, if any.</param>
public readonly record struct MapIdProgress(
    bool MapsReady,
    int Grids,
    bool HasFile,
    int Candidates,
    int Anchored,
    int Passes,
    int Restarts,
    string? Winner,
    int PlayerX = -1,
    int PlayerY = -1,
    string? BestAnchored = null);

/// <summary>
/// One screen the operator can open and keep open, instead of a list of flags to
/// remember and a directory to be in.
/// </summary>
/// <remarks>
/// <para>
/// This adds no capability. Every entry calls the same command the flag calls,
/// and the entries that actuate are deliberately absent: arming input stays an
/// explicit flag, because a menu item is exactly the shape a bypass has
/// (<c>CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 2.1). What the menu adds is the
/// state — where the files are, what the client is, how far the proof has got —
/// and the single next step that follows from it.
/// </para>
/// <para>
/// <b>The next step is computed, never remembered.</b> It is read back from the
/// candidate file and the live client on every draw, so it cannot describe a
/// situation that has since changed.
/// </para>
/// </remarks>
public static class OperatorMenu
{
    /// <summary>
    /// Where every action's output is kept, verbatim.
    /// </summary>
    /// <remarks>
    /// A console scrolls and then it is gone, so the outcome of a run existed
    /// only for as long as the operator could see it - and reporting it meant
    /// retyping it. The transcript is what makes an outcome readable after the
    /// fact by anyone, including whoever is writing the code it came from.
    /// </remarks>
    public const string TranscriptPath = "data/banco.log";

    /// <summary>Past this size the transcript starts again, keeping nothing.</summary>
    private const long TranscriptLimitBytes = 2 * 1024 * 1024;

    /// <summary>
    /// The one thing to do next, given where the proof has got to.
    /// </summary>
    /// <remarks>
    /// Pure, and separated from the drawing for that reason: an operator who is
    /// told the wrong next step performs the wrong experiment, and this is the
    /// sentence they act on.
    /// </remarks>
    public static string NextStep(MapIdProgress progress)
    {
        if (!progress.MapsReady)
        {
            return "Collega il volume NOSAI-SSD e usa la voce 5 (estrai le mappe dal client). "
                 + "Senza le griglie estratte non c'e' oracolo con cui riconoscere il codice mappa.";
        }

        if (!progress.HasFile)
        {
            return "Apri NosTale, ferma il personaggio, e usa la voce 1. "
                 + "Questa e' la prima passata: cerca in memoria tutte le parole che valgono un id di mappa che ti contiene.";
        }

        if (progress.Candidates == 0)
        {
            return "Nessun candidato e' sopravvissuto. Resta su questa mappa e rilancia la voce 1: "
                 + "riparte da una scansione pulita. Se si ripete, il problema e' a monte, nelle griglie estratte.";
        }

        if (progress.Winner is { } winner)
        {
            return $"TROVATO: {winner} - due prove su due, ed e' scritto in NosTaleClientLayout. "
                 + "Ora la voce 2: fermo il personaggio, la cella sotto di te deve risultare "
                 + "calpestabile. E' la prova che dice se i bit della griglia significano quello "
                 + "che il layout pretende.";
        }

        // A restart drops every bare address at once, and it is the proof that is
        // missing anyway. When something is anchored and something else is not, it
        // narrows harder than another portal and costs the same.
        if (progress.Restarts < 1 && progress.Anchored >= 1 && progress.Anchored < progress.Candidates)
        {
            string bare = (progress.Candidates - progress.Anchored).ToString(CultureInfo.InvariantCulture);
            string kept = progress.Anchored.ToString(CultureInfo.InvariantCulture);
            return $"Chiudi NosTale, riaprilo, rientra con lo stesso personaggio e rilancia la voce 1. "
                 + $"I {bare} candidati che sono solo indirizzi muoiono con il processo e cadono da soli; "
                 + $"restano i {kept} ancorati. E' anche la prova che manca: un offset sopravvive al "
                 + "riavvio, un indirizzo no.";
        }

        if (progress.Candidates > 1)
        {
            string count = progress.Candidates.ToString(CultureInfo.InvariantCulture);
            return $"Restano {count} candidati. Attraversa un portale verso una mappa di dimensioni "
                 + "diverse e rilancia la voce 1: chi non cambia valore non e' il codice mappa."
                 + WeakFilterHint(progress.PlayerX, progress.PlayerY);
        }

        if (progress.Passes < 2)
        {
            return "Un solo candidato, ma su una mappa sola. Attraversa un portale e rilancia la voce 1: "
                 + "il valore deve cambiare, e diventare l'id di una griglia che ti contiene ancora.";
        }

        if (progress.Anchored == 0)
        {
            return "Il superstite e' un indirizzo nudo, non una distanza da una base che il runtime ritrova: "
                 + "al riavvio del client non vuol dire piu' niente. Riavvia il client e rilancia la voce 1: "
                 + "se e' davvero il campo, una nuova scansione lo ritrova.";
        }

        return "Un candidato ancorato che ha seguito due mappe. Ora chiudi NosTale, riaprilo, rientra con lo "
             + "stesso personaggio e rilancia la voce 1: un offset sopravvive al riavvio, un indirizzo no.";
    }

    /// <summary>
    /// Says so when the character is standing where the oracle barely filters.
    /// </summary>
    /// <remarks>
    /// A cell near the origin is inside almost every rectangle, so nearly every
    /// map is plausible and the pass discards nothing. Where the character stands
    /// is part of the experiment, and it is the part nobody thinks of as one.
    /// </remarks>
    internal static string WeakFilterHint(int x, int y)
    {
        const int Narrow = 50;
        if (x < 0 || y < 0 || (x >= Narrow && y >= Narrow))
            return string.Empty;

        string cell = x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
        return $" E fermati lontano dall'angolo 0,0: a {cell} quasi ogni griglia ti contiene, "
             + "e un filtro che accetta tutti non scarta nessuno.";
    }

    /// <summary>Reads the state the menu shows, from the disk and the live client.</summary>
    public static MapIdProgress ReadProgress(string candidatePath)
    {
        int grids = 0;
        if (MapGridExtractor.TryResolveDedicatedMapsDirectory(out string mapsDirectory, out _)
            && MapGridExtractor.TryLoadCatalog(mapsDirectory, out IReadOnlyList<MapGridSize> catalog, out _))
        {
            grids = catalog.Count;
        }

        bool mapsReady = grids > 0;

        if (!MapIdFinder.TryLoadCandidates(candidatePath, out MapIdCandidates? file) || file is null)
            return new MapIdProgress(mapsReady, grids, HasFile: false, 0, 0, 0, 0, null);

        int anchored = 0;
        string? bestAnchored = null;
        foreach (MapIdHit hit in file.Hits)
        {
            if (!hit.IsDurable)
                continue;

            anchored++;
            bestAnchored ??= hit.Describe();
        }

        string? winner = MapIdFinder.Proven(file.Hits, file.Passes, file.Restarts)
            ? file.Hits[0].Describe()
            : null;

        return new MapIdProgress(
            mapsReady, grids, HasFile: true, file.Hits.Count, anchored, file.Passes, file.Restarts, winner,
            file.PlayerX, file.PlayerY, bestAnchored);
    }

    /// <summary>Runs the menu until the operator leaves it.</summary>
    public static int Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Il banco di prova legge un client Windows.");
            return 2;
        }

        while (true)
        {
            Draw();
            Console.Write("Scelta: ");
            string? choice = Console.ReadLine();
            if (choice is null)
                return 0;

            switch (choice.Trim())
            {
                case "1":
                    Perform("Cerca il codice mappa", () => MapIdFinder.Run());
                    break;
                case "2":
                    Perform("Prova della cella su cui sei", () => MapGridCheck.Run());
                    break;
                case "3":
                    Perform("Sonda del personaggio", () => PlayerObjectProbe.Run(0, null));
                    break;
                case "4":
                    Perform("Finestra, DPI, monitor", () => Perception.ClientWindowDpiProbe.Run());
                    break;
                case "5":
                    Perform("Estrai le mappe dal client", () => MapGridExtractor.RunExtract());
                    break;
                case "6":
                    Perform("Scheda di una mappa", RunMapInfo);
                    break;
                case "7":
                    Perform("SendInput arriva alla coda?", () => LowLevel.InputEnvironmentProbe.RunConsoleProbe());
                    break;
                case "8":
                    Perform("Stato delle guardie d'input", () => LowLevel.InputGuardsProbe.Run());
                    break;
                case "0":
                case "q":
                case "Q":
                    return 0;
                default:
                    Console.WriteLine("Non e' una voce del menu.");
                    Pause();
                    break;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Draw()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Output redirected: the screen is a log, and a log does not clear.
        }

        string candidatePath = Path.GetFullPath(MapIdFinder.CandidatePath);
        MapIdProgress progress = ReadProgress(candidatePath);

        Console.WriteLine("================ NosAi — banco di prova ================");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cartella: {Directory.GetCurrentDirectory()}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"privilegi: {(IsElevated() ? "amministratore" : "utente normale")}"));
        Console.WriteLine();

        Console.WriteLine("-- stato ------------------------------------------------");
        Console.WriteLine(DescribeClient());
        Console.WriteLine(progress.MapsReady
            ? string.Create(CultureInfo.InvariantCulture, $"griglie:  {progress.Grids} mappe estratte")
            : "griglie:  NON TROVATE (serve il volume NOSAI-SSD e la voce 5)");
        Console.WriteLine(DescribeHunt(progress));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"file:     {candidatePath}"));
        Console.WriteLine();

        Console.WriteLine("-- cosa fare adesso -------------------------------------");
        foreach (string line in Wrap(NextStep(progress), 56))
            Console.WriteLine("  " + line);
        Console.WriteLine();

        Console.WriteLine("-- voci -------------------------------------------------");
        Console.WriteLine("  1  Cerca il codice mappa        (una passata dell'oracolo)");
        Console.WriteLine("  2  Prova della cella su cui sei (serve il codice mappa)");
        Console.WriteLine("  3  Sonda del personaggio        (id e posizione dal client)");
        Console.WriteLine("  4  Finestra, DPI, monitor");
        Console.WriteLine("  5  Estrai le mappe dal client   (una volta per build)");
        Console.WriteLine("  6  Scheda di una mappa          (dimensioni e hash)");
        Console.WriteLine("  7  SendInput arriva alla coda?  (non muove niente)");
        Console.WriteLine("  8  Stato delle guardie d'input  (commit point, sola lettura)");
        Console.WriteLine("  0  Esci");
        Console.WriteLine();
        Console.WriteLine("Tutto qui dentro e' in sola lettura: niente muove il personaggio.");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Ogni esito finisce in {Path.GetFullPath(TranscriptPath)}"));
        Console.WriteLine();
    }

    private static string DescribeHunt(MapIdProgress progress)
    {
        if (!progress.HasFile)
            return "codice mappa: caccia non ancora iniziata";

        string candidates = progress.Candidates.ToString(CultureInfo.InvariantCulture);
        string anchored = progress.Anchored.ToString(CultureInfo.InvariantCulture);
        string passes = progress.Passes.ToString(CultureInfo.InvariantCulture);
        string restarts = progress.Restarts.ToString(CultureInfo.InvariantCulture);
        string tail = progress.BestAnchored is { } best ? $"  [{best}]" : string.Empty;
        return $"codice mappa: {candidates} candidati ({anchored} ancorati) - "
             + $"mappe seguite {passes}/2, riavvii superati {restarts}/1{tail}";
    }

    [SupportedOSPlatform("windows")]
    private static string DescribeClient()
    {
        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? failure))
            return string.Create(CultureInfo.InvariantCulture, $"client:   NON LEGGIBILE ({failure})");

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"client:   pid {session.ProcessId}, ma il personaggio non si legge ({readFailure})");
            }

            return string.Create(CultureInfo.InvariantCulture,
                $"client:   pid {session.ProcessId}, personaggio {player.CharacterId} in {player.X},{player.Y}");
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static int RunMapInfo()
    {
        Console.Write("Numero della mappa: ");
        string? typed = Console.ReadLine();
        if (!int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
        {
            Console.WriteLine("Non e' un numero.");
            return 2;
        }

        return MapGridExtractor.RunInfo(mapId);
    }

    private static void Perform(string title, Func<int> action)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");

        TextWriter console = Console.Out;
        TextWriter error = Console.Error;
        var captured = new StringWriter(CultureInfo.InvariantCulture);
        int code;
        try
        {
            Console.SetOut(new TeeTextWriter(console, captured));
            Console.SetError(new TeeTextWriter(error, captured));
            code = action();
        }
        catch (Exception ex)
        {
            // A tool that throws must not take the menu with it: the operator is
            // mid-procedure and the next step still has to be readable.
            Console.WriteLine($"[ERRORE] {ex.GetType().Name}: {ex.Message}");
            code = 3;
        }
        finally
        {
            Console.SetOut(console);
            Console.SetError(error);
        }

        Console.WriteLine();
        Console.WriteLine(code == 0 ? "esito: riuscito (0)" : $"esito: non concluso ({code})");
        Record(title, code, captured.ToString());
        Pause();
    }

    /// <summary>One transcript entry: what was run, when, what it said, how it ended.</summary>
    internal static string FormatEntry(string title, int code, string output, DateTime at)
    {
        var entry = new StringBuilder();
        entry.Append("=== ")
             .Append(at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
             .Append(" - ").Append(title).AppendLine(" ===");
        entry.Append(output);
        if (output.Length > 0 && !output.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            entry.AppendLine();
        entry.Append("esito: ").AppendLine(code.ToString(CultureInfo.InvariantCulture));
        entry.AppendLine();
        return entry.ToString();
    }

    private static void Record(string title, int code, string output)
    {
        try
        {
            string? directory = Path.GetDirectoryName(TranscriptPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Starting again beats trimming: a half-truncated entry reads like a
            // run that stopped, which is a thing that can actually happen.
            if (File.Exists(TranscriptPath) && new FileInfo(TranscriptPath).Length > TranscriptLimitBytes)
                File.Delete(TranscriptPath);

            File.AppendAllText(TranscriptPath, FormatEntry(title, code, output, DateTime.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The transcript is a convenience. Losing it must not lose the run.
            Console.WriteLine($"(esito non registrato: {ex.GetType().Name})");
        }
    }

    /// <summary>Writes to the console and to the transcript at the same time.</summary>
    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _capture;

        public TeeTextWriter(TextWriter console, TextWriter capture)
        {
            _console = console;
            _capture = capture;
        }

        public override Encoding Encoding => _console.Encoding;

        public override void Write(char value)
        {
            _console.Write(value);
            _capture.Write(value);
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            _capture.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _console.WriteLine(value);
            _capture.WriteLine(value);
        }

        public override void Flush()
        {
            _console.Flush();
            _capture.Flush();
        }
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.Write("Premi INVIO per tornare al menu.");
        Console.ReadLine();
    }

    /// <summary>Breaks a sentence at spaces so a narrow console does not cut words.</summary>
    internal static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return lines;
    }
}
