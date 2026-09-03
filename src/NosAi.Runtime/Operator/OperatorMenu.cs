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
    /// La misura che manca perche' il runtime possa combattere, o null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pura e separata da <see cref="NextStep"/> per la stessa ragione per cui
    /// quella lo e': la caccia al codice mappa e' chiusa, e la frase su cui
    /// l'operatore agisce adesso e' un'altra.
    /// </para>
    /// <para>
    /// L'ordine non e' arbitrario. Finche' il riquadro bersaglio non e'
    /// calibrato <c>HasTarget</c> resta UNKNOWN, e ADR-0016 salta ogni regola
    /// che lo legge: nessuna riga scritta a tavolino puo' produrre un
    /// combattimento prima di quella misura. I tasti vengono dopo perche'
    /// servono a eseguire una decisione che senza il bersaglio non viene
    /// nemmeno presa.
    /// </para>
    /// </remarks>
    public static string? NextCalibration(bool targetRoiCalibrated, bool keybindsPresent)
    {
        if (!targetRoiCalibrated)
        {
            return "Il riquadro bersaglio non e' calibrato, quindi HasTarget resta UNKNOWN e ogni "
                 + "regola d'attacco viene saltata. Seleziona un mostro nel client, usa la voce 10 e "
                 + "guarda data/perception/crops/target_latest.bmp; quando il ritaglio e' il riquadro, "
                 + "registralo con la voce 11.";
        }

        if (!keybindsPresent)
        {
            return "data/keybinds.json non esiste, quindi ogni skill e ogni pozione rifiutano con "
                 + "keybind_not_configured. La voce 14 dice quali intenti il runtime sa chiedere e "
                 + "quali non hanno un tasto.";
        }

        return null;
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
                case "9":
                    Perform("Autorita' di sessione", () => LowLevel.InputAuthorityProbe.Run());
                    break;
                case "10":
                    Perform("Barre e riquadro bersaglio", () => Perception.HudProbe.RunConsoleProbe());
                    break;
                case "11":
                    Perform("Calibra il riquadro bersaglio", RunTargetCalibration);
                    break;
                case "12":
                    Perform("Replay di una registrazione",
                        () => RunReplay(path => Observability.WorldReplayCommand.Run(path)));
                    break;
                case "13":
                    Perform("Replay della decisione",
                        () => RunReplay(path =>
                            Observability.DecideReplayCommand.RunAsync(path).GetAwaiter().GetResult()));
                    break;
                case "14":
                    Perform("Tasti configurati", () => LowLevel.KeybindsCheck.Run());
                    break;
                case "16":
                    Perform("Catena del bersaglio", () => Navigation.TargetChainProbe.Run());
                    break;
                case "17":
                    Perform("Nomi delle entita'", RunEntityNames);
                    break;
                case "18":
                    Perform("HP e MP del personaggio", RunPlayerVitals);
                    break;
                case "19":
                    Perform("Cooldown di un'abilita'", RunSkillCooldowns);
                    break;
                case "15":
                    Perform("Cerca il bersaglio in memoria", RunTargetHunt);
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
        Console.WriteLine(DescribeTargetRoi());
        Console.WriteLine(DescribeKeybinds());
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"file:     {candidatePath}"));
        Console.WriteLine();

        Console.WriteLine("-- cosa fare adesso -------------------------------------");
        foreach (string line in Wrap(NextStep(progress), 56))
            Console.WriteLine("  " + line);

        if (NextCalibration(LoadTargetRoi().IsCalibrated, KeybindsPresent()) is { } calibration)
        {
            Console.WriteLine();
            foreach (string line in Wrap(calibration, 56))
                Console.WriteLine("  " + line);
        }

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
        Console.WriteLine("  9  Autorita' di sessione        (si puo' guidare questo client?)");
        Console.WriteLine(" 10  Barre e riquadro bersaglio   (scrive i ritagli da guardare)");
        Console.WriteLine(" 11  Calibra il riquadro bersaglio (chiede le quattro frazioni)");
        Console.WriteLine(" 12  Replay di una registrazione  (cosa vede l'osservazione, senza client)");
        Console.WriteLine(" 13  Replay della decisione       (la scala del ciclo, senza client)");
        Console.WriteLine(" 14  Tasti configurati            (quali intenti hanno un tasto)");
        Console.WriteLine(" 15  Cerca il bersaglio in memoria (un giro dell'oracolo)");
        Console.WriteLine(" 16  Catena del bersaglio         (manager -> puntatore -> id, contro ct)");
        Console.WriteLine(" 17  Nomi delle entita'           (memoria e filo affiancati, candidati)");
        Console.WriteLine(" 18  HP e MP del personaggio      (scan sulle basi, candidati UNKNOWN)");
        Console.WriteLine(" 19  Cooldown di un'abilita'      (tu la usi, il filo dice quando torna)");
        Console.WriteLine("  0  Esci");
        Console.WriteLine();
        Console.WriteLine("Niente qui dentro muove il personaggio. Le voci 12 e 13 non toccano");
        Console.WriteLine("nemmeno il client: leggono una registrazione. La voce 9 e' l'unica che");
        Console.WriteLine("emette qualcosa - sposta il puntatore e lo rimette dov'era, perche' e'");
        Console.WriteLine("cosi' che l'autorita' di sessione si verifica.");
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

    /// <summary>
    /// One pass of the target-id oracle, told what the operator has selected.
    /// </summary>
    /// <remarks>
    /// The single bit a person supplies, and it is a keypress rather than a
    /// measurement: nothing here reads a pixel, and nothing asks anybody to aim.
    /// It matters because a pass without a target is the only one that tells the
    /// selection apart from the client's own entity list (`ADR-0021`).
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int RunTargetHunt()
    {
        Console.WriteLine("Il giro e' uno solo, e alterni tu mentre gira: seleziona un mostro,");
        Console.WriteLine("poi toglilo, poi selezionane un altro. Ogni passo ti dice cosa fare e");
        Console.WriteLine("aspetta INVIO; 'x' si ferma e salva quello che e' rimasto.");
        Console.WriteLine();
        Console.WriteLine("La deselezione non e' un di piu': una parola qualunque cambia, ma solo la");
        Console.WriteLine("selezione TORNA ALLO STESSO valore ogni volta che togli il bersaglio.");
        Console.WriteLine();
        Console.WriteLine("Il riavvio del client resta una seconda esecuzione: rilancia questa voce");
        Console.WriteLine("dopo aver chiuso e riaperto NosTale, e riparte dai superstiti.");
        Console.WriteLine();

        return TargetIdFinder.Run();
    }

    /// <summary>The recorded target-frame region, or the uncalibrated one.</summary>
    private static Perception.TargetRoiCalibration LoadTargetRoi()
        => Perception.TargetRoiCalibration.Load(
            Path.GetFullPath(Perception.TargetRoiCalibration.RelativePath), out _);

    private static string DescribeTargetRoi()
    {
        Perception.TargetRoiCalibration roi = LoadTargetRoi();
        return roi.IsCalibrated
            ? string.Create(CultureInfo.InvariantCulture,
                $"bersaglio: calibrato il {roi.CalibratedAtUtc:yyyy-MM-dd} su {roi.ClientWidth}x{roi.ClientHeight}")
            : "bersaglio: NON CALIBRATO (HasTarget resta UNKNOWN)";
    }

    /// <summary>Whether the operator has declared which key means what.</summary>
    private static bool KeybindsPresent()
        => File.Exists(Path.GetFullPath(Gate3.InputActionEffector.KeybindsRelativePath));

    private static string DescribeKeybinds()
        => KeybindsPresent()
            ? "tasti:    data/keybinds.json presente"
            : "tasti:    data/keybinds.json NON ESISTE (skill e pozioni rifiutano)";

    /// <summary>
    /// Records the four fractions of the client area the target frame occupies.
    /// </summary>
    /// <remarks>
    /// The probe writes the calibration the moment it is handed fractions, so
    /// there is no preview and no confirmation step to offer: every attempt
    /// overwrites the last, and the crop is the only judge of whether the
    /// region was right. Saying so is the whole point of this prompt.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int RunTargetCalibration()
    {
        if (Perception.HudProbe.FindClientWindow() is not { } window)
        {
            Console.WriteLine("La finestra di NosTale non c'e'. Apri il client e riprova:");
            Console.WriteLine("senza di essa le frazioni sarebbero dell'intero desktop, non del gioco.");
            return 2;
        }

        int clientWidth = window.ClientArea.Width;
        int clientHeight = window.ClientArea.Height;

        Console.WriteLine("PASSO 1 - fotografo l'area client.");
        Console.WriteLine("Seleziona un mostro nel client: senza bersaglio il riquadro non c'e' da");
        Console.WriteLine("misurare. Poi torna qui e premi INVIO.");
        Console.Write("INVIO quando il bersaglio e' selezionato.");
        Console.ReadLine();

        // Pressing ENTER brought this console to the front, so the client is now
        // behind it and the capture would photograph whatever covers it. The
        // countdown is the operator's time to click back on the game; the check
        // below is what makes the refusal honest when they do not.
        Console.WriteLine();
        Console.WriteLine("Ora porta NosTale davanti. Clicca sulla sua BARRA DEL TITOLO, non dentro il");
        Console.WriteLine("gioco: un clic nel mondo muove il personaggio e puo' togliere il bersaglio.");
        Console.WriteLine("Scatto fra:");
        for (int second = 5; second >= 1; second--)
        {
            Console.Write(string.Create(CultureInfo.InvariantCulture, $" {second}"));
            Thread.Sleep(1000);
        }

        Console.WriteLine();
        Console.WriteLine();

        if (!ClientIsInFront(window, out string? covered))
        {
            Console.WriteLine($"[RIFIUTATO] {covered}");
            Console.WriteLine("La foto sarebbe di quello che copre il gioco, e una misura presa li'");
            Console.WriteLine("produce una regione plausibile del rettangolo sbagliato. Riprova.");
            return 2;
        }

        int shot = Perception.HudProbe.RunConsoleProbe();
        if (shot != 0)
            return shot;

        string image = Path.GetFullPath(
            Path.Combine(Perception.HudCropWriter.RelativeDirectory, "client_latest.bmp"));

        Console.WriteLine();
        Console.WriteLine("PASSO 2 - misura il riquadro sulla foto.");
        Console.WriteLine($"  {image}");
        Console.WriteLine();
        Console.WriteLine("Aprila con Paint. Passa il mouse sull'angolo IN ALTO A SINISTRA del riquadro");
        Console.WriteLine("bersaglio: in basso a sinistra Paint scrive due numeri, sono i pixel. Poi");
        Console.WriteLine("fai lo stesso sull'angolo IN BASSO A DESTRA. Servono quei quattro numeri.");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"L'immagine e' {clientWidth}x{clientHeight}: alle frazioni ci penso io."));
        Console.WriteLine();

        if (!TryReadPixel("sinistra", clientWidth, out int left)) return 2;
        if (!TryReadPixel("alto    ", clientHeight, out int top)) return 2;
        if (!TryReadPixel("destra  ", clientWidth, out int right)) return 2;
        if (!TryReadPixel("basso   ", clientHeight, out int bottom)) return 2;

        if (right <= left || bottom <= top)
        {
            Console.WriteLine("Destra deve stare oltre sinistra, e basso oltre alto:");
            Console.WriteLine("sono i due angoli opposti, non due punti qualsiasi.");
            return 2;
        }

        double x = (double)left / clientWidth;
        double y = (double)top / clientHeight;
        double width = (double)(right - left) / clientWidth;
        double height = (double)(bottom - top) / clientHeight;

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Frazioni: {x:F4} {y:F4} {width:F4} {height:F4}"));
        Console.WriteLine("Registro e ritaglio quella regione. Riguarda target_latest.bmp: se non e' il");
        Console.WriteLine("riquadro, rifai questa voce - ogni giro sovrascrive il precedente.");
        Console.WriteLine();

        return Perception.HudProbe.RunConsoleProbe(calibrateTarget: (x, y, width, height));
    }

    /// <summary>
    /// Whether the client really is the window drawn at its own rectangle.
    /// </summary>
    /// <remarks>
    /// The same two questions the commit point asks before an act, asked here
    /// before a measurement, and for the same reason: desktop duplication copies
    /// whatever is on screen at those coordinates, so a covered client yields a
    /// photograph of the thing covering it — a picture that looks perfectly
    /// valid and is of the wrong program. Foreground alone is not enough,
    /// because a small window can sit on top without taking it.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static bool ClientIsInFront(Perception.ClientWindow window, out string? failureReason)
    {
        var desktop = new LowLevel.Win32CommitEnvironment();

        if (desktop.ForegroundWindow() != window.Handle)
        {
            failureReason = "NosTale non e' la finestra in primo piano.";
            return false;
        }

        int centreX = window.ClientArea.X + (window.ClientArea.Width / 2);
        int centreY = window.ClientArea.Y + (window.ClientArea.Height / 2);
        if (desktop.RootWindowFromPoint(centreX, centreY) != window.Handle)
        {
            failureReason = "Qualcosa copre il centro della finestra di NosTale.";
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>Reads one pixel coordinate, and refuses one outside the picture.</summary>
    /// <remarks>
    /// The bound is the honest half of the conversion: a number larger than the
    /// image is a reading taken off the desktop instead of off the client, and it
    /// would produce a plausible fraction of the wrong rectangle.
    /// </remarks>
    private static bool TryReadPixel(string label, int limit, out int value)
    {
        Console.Write($"{label}: ");
        string? typed = Console.ReadLine()?.Trim();
        if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || value < 0
            || value > limit)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Serve un numero intero fra 0 e {limit}."));
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs a replay over one of the recordings in <c>data/</c>.
    /// </summary>
    /// <remarks>
    /// The paths are listed rather than typed because a mistyped path is a
    /// refusal the operator then has to read, and the recordings are the one
    /// thing on this bench that needs no client, no driver and no elevation.
    /// </remarks>
    private static int RunReplay(Func<string, int> replay)
    {
        string directory = Path.GetFullPath("data");
        string[] captures = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.noscap")
            : Array.Empty<string>();

        if (captures.Length == 0)
        {
            Console.WriteLine($"Nessuna registrazione .noscap in {directory}.");
            return 2;
        }

        Array.Sort(captures, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < captures.Length; i++)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {i + 1}  {Path.GetFileName(captures[i])}"));
        }

        Console.Write("Numero (INVIO per la prima): ");
        string? typed = Console.ReadLine()?.Trim();
        int index = 1;
        if (!string.IsNullOrEmpty(typed)
            && (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                || index < 1
                || index > captures.Length))
        {
            Console.WriteLine("Non e' una voce dell'elenco.");
            return 2;
        }

        Console.WriteLine();
        return replay(captures[index - 1]);
    }

    /// <summary>
    /// Phase 3. Needs the wire running, because the operator supplies the press and
    /// the wire supplies the restoration — the probe refuses rather than checking
    /// the falls against the same clock that produced them.
    /// </summary>
    private static int RunSkillCooldowns()
    {
        Console.WriteLine("Serve il runtime avviato con --observe-game in una console elevata:");
        Console.WriteLine("senza sr dal filo non c'e' una seconda sorgente e la sonda rifiuta.");
        Console.Write("Slot dell'abilita' (come lo numera il filo): ");
        string? typed = Console.ReadLine()?.Trim();

        if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot < 0)
        {
            Console.WriteLine("Non e' uno slot.");
            return 2;
        }

        return Navigation.SkillCooldownProbe.Run(slot);
    }

    private static int RunEntityNames()
    {
        string directory = Path.GetFullPath("data");
        string[] captures = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.noscap")
            : Array.Empty<string>();

        if (captures.Length == 0)
            return EntityNameProbe.Run();

        Array.Sort(captures, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("Registrazione da cui prendere i nomi del filo (INVIO = nessuna):");
        for (int i = 0; i < captures.Length; i++)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {i + 1}  {Path.GetFileName(captures[i])}"));
        }

        Console.Write("Numero: ");
        string? typed = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(typed))
            return EntityNameProbe.Run();

        if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 1
            || index > captures.Length)
        {
            Console.WriteLine("Non e' una voce dell'elenco.");
            return 2;
        }

        return EntityNameProbe.Run(captures[index - 1]);
    }

    private static int RunPlayerVitals()
    {
        string directory = Path.GetFullPath("data");
        string[] captures = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.noscap")
            : Array.Empty<string>();

        if (captures.Length == 0)
            return PlayerVitalsProbe.Run();

        Array.Sort(captures, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("Registrazione da cui prendere HP/MP del filo (INVIO = nessuna):");
        for (int i = 0; i < captures.Length; i++)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {i + 1}  {Path.GetFileName(captures[i])}"));
        }

        Console.Write("Numero: ");
        string? typed = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(typed))
            return PlayerVitalsProbe.Run();

        if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 1
            || index > captures.Length)
        {
            Console.WriteLine("Non e' una voce dell'elenco.");
            return 2;
        }

        return PlayerVitalsProbe.Run(captures[index - 1]);
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
