using System.Diagnostics;
using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;
using System.Linq;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Collects calibration samples by watching the operator click to walk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why clicks and not the character.</b> Sampling the character's own position
/// against its own pixel cannot work here: the camera follows it, so it stays
/// drawn in the same place while the map scrolls underneath. Measured on the real
/// client, walking twelve tiles moved the character's pixel by seven — the
/// operator's hand, not the game. Three such samples fit six unknowns exactly, so
/// the fit looked perfect and described nothing.
/// </para>
/// <para>
/// <b>What a click gives instead.</b> Click-to-walk makes the client resolve a
/// pixel into a map square itself, and it writes that square where it can be
/// read. So every click is a pairing of a screen point with a map coordinate,
/// produced by the client rather than by a person reading numbers off a screen —
/// the same reason ADR-0017 took its glyph labels from the wire rather than from
/// the operator's typing.
/// </para>
/// <para>
/// <b>Watched, not timed.</b> The character starts walking immediately, so a
/// sample taken a second later pairs the new position with the old target. This
/// polls for the moment the target <i>changes</i>, which is the instant of the
/// click, and reads all three values there.
/// </para>
/// </remarks>
public static class ScreenProjectionWatcher
{
    /// <summary>How often the walk target is checked for a change.</summary>
    private const int PollIntervalMs = 40;

    /// <summary>
    /// Watches for clicks and records one sample per accepted click, until the
    /// time runs out or enough have been collected.
    /// </summary>
    /// <param name="wanted">
    /// How many samples to ask for, raised to <see cref="ScreenSampleCoach.DefaultWantedSamples"/>
    /// when the caller asks for fewer, and said out loud when it is raised. Five
    /// is the number this tool asked for until 2026-09-07, and five clicks are
    /// what <see cref="ScreenProjectionCalibration"/> then refused as
    /// <c>scale_not_determined:72x43pct</c>: a tool that keeps asking for a number
    /// known not to work is asking the operator to waste a session.
    /// </param>
    /// <param name="report">
    /// Where each line goes. Defaults to the console, which is what the CLI wants;
    /// the ControlPanel passes its own so the operator reads the same guidance in
    /// the window instead of a terminal they were never meant to open. The loop is
    /// not duplicated for the second caller: a second copy of it would be a second
    /// place for the at-rest rule to be forgotten.
    /// </param>
    public static int Run(
        int seconds, int wanted, string? repoRoot = null, Action<string>? report = null)
    {
        void Say(string line = "") => (report ?? Console.WriteLine)(line);

        if (!OperatingSystem.IsWindows())
        {
            Say("Reading process memory needs Windows.");
            return 2;
        }

        repoRoot ??= Directory.GetCurrentDirectory();

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Say($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            PixelRect? area = LocateClientArea(session!.ProcessId, out IntPtr windowHandle);
            if (area is not { } clientArea)
            {
                Say("[REFUSED] client_window_not_located");
                return 1;
            }

            // Il regime va sulla riga, e il DPI ne fa parte: due sessioni alla
            // stessa dimensione di finestra e DPI diverso sono geometrie diverse,
            // ed e' il caso che il formato precedente non sapeva vedere. Senza
            // DPI leggibile non si raccoglie: un campione di regime ignoto entra
            // nel fit e nessuno sa piu' di che sessione fosse.
            GeometryShape shape = GeometryEpoch.Read(windowHandle).Shape;
            if (!shape.IsKnown)
            {
                Say("[REFUSED] client_geometry_unknown");
                Say("  Il DPI della finestra non e' leggibile, e senza regime un campione");
                Say("  vale meno di nessun campione.");
                return 1;
            }

            var backend = new Win32InputBackend();
            int target = Math.Max(wanted, ScreenSampleCoach.DefaultWantedSamples);
            var coach = new ScreenSampleCoach(clientArea.Width, clientArea.Height, target);
            var recorded = new List<string>();

            // Ogni sessione parte da zero, e quella prima viene archiviata invece
            // che riscritta.
            //
            // Fino al 2026-09-07 i campioni dello stesso regime venivano ripresi,
            // per non buttare una sessione interrotta. La misura ha detto che non
            // si puo': tre campioni della prima sessione del 7 settembre uniti ai
            // nove della seconda danno un residuo di 9,62 caselle, i nove da soli
            // 4,23. Le due sessioni avevano la stessa finestra e lo stesso DPI --
            // stesso regime, quindi -- e proiezioni diverse. Il regime non
            // identifica la proiezione, e una sessione ereditata e' una miscela
            // che nessun filtro presente sa vedere.
            string samplesPath = Path.Combine(repoRoot, ScreenProjectionProbe.SamplesRelativePath);
            string? archived = ArchivePreviousSession(samplesPath);

            Say($"Client area {clientArea.Width}x{clientArea.Height} at {clientArea.X},{clientArea.Y}");
            Say();
            if (target != wanted)
                Say($"(richiesti {wanted} campioni, ne servono {target}: con cinque clic il fit non si determina)");
            Say($"CLICCA PER CAMMINARE, {target} volte, TUTTO ATTORNO al personaggio.");
            Say("  Clicca sul TERRENO, non su un mostro o un oggetto.");
            Say($"  ASPETTA CHE SI FERMI prima di ogni clic, e vai lontano: almeno {ScreenSampleCoach.MinimumDeltaTiles} caselle.");
            Say("  Ogni clic e' un campione: il client stesso traduce il pixel in una casella.");
            Say($"  Hai {seconds} secondi. Ti dico io dove cliccare dopo ognuno.");
            Say();
            if (archived is not null)
                Say($"  La sessione precedente e' stata archiviata in {Path.GetFileName(archived)}.");
            Say($"  -> {coach.Advice}");

            int acceptedThisSession = 0;
            short? lastTargetX = null;
            short? lastTargetY = null;
            bool wasAtRest = false;
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed.TotalSeconds < seconds && !coach.IsSatisfied)
            {
                Thread.Sleep(PollIntervalMs);

                if (!session.TryReadPlayer(out PlayerObjectReading player, out _))
                    continue;
                if (player.WalkTargetX is not { } tx || player.WalkTargetY is not { } ty)
                    continue;

                // Whether the character had finished the previous walk when this
                // poll began. It is read one poll behind on purpose: the sample is
                // the click's tile minus the character's tile, and a character that
                // is still walking has already left the tile the click was aimed
                // from. That lag is the systematic error the 2026-09-07 refit
                // found — six of the twelve samples on file sat above the 1.5-tile
                // residual threshold with no single bad click to blame.
                bool atRestNow = tx == player.X && ty == player.Y;
                bool hadArrived = wasAtRest;

                bool changed = lastTargetX is null || tx != lastTargetX || ty != lastTargetY;
                lastTargetX = tx;
                lastTargetY = ty;
                wasAtRest = atRestNow;
                if (!changed)
                    continue;

                // The very first read establishes what the target already was; it
                // is not a click and pairs with no cursor the operator placed.
                if (acceptedThisSession == 0 && clock.Elapsed.TotalMilliseconds < PollIntervalMs * 3)
                    continue;

                // A click that resolves to where the character already stands is
                // not a direction, and it would contribute a zero-length delta.
                if (atRestNow)
                    continue;

                if (!backend.TryGetCursorPosition(out int cursorX, out int cursorY))
                    continue;

                int relativeX = cursorX - clientArea.X;
                int relativeY = cursorY - clientArea.Y;
                if (relativeX < 0 || relativeX >= clientArea.Width
                    || relativeY < 0 || relativeY >= clientArea.Height)
                {
                    Say($"  ignorato: cursore fuori dalla finestra ({cursorX},{cursorY})");
                    continue;
                }

                var sample = new ScreenProjectionSample(
                    new Contracts.MapPoint(tx - player.X, ty - player.Y), relativeX, relativeY);

                ScreenSampleVerdict verdict = coach.Offer(sample, hadArrived);
                if (!verdict.Accepted)
                {
                    Say(string.Create(CultureInfo.InvariantCulture,
                        $"  scartato [{verdict.Reason}]: delta ({sample.MapDelta.X},{sample.MapDelta.Y}) al pixel ({relativeX},{relativeY})"));
                    Say($"  -> {verdict.Advice}");
                    continue;
                }

                recorded.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.MapDelta.X} {sample.MapDelta.Y} {relativeX} {relativeY} {clientArea.Width} {clientArea.Height} {shape.Dpi}"));

                acceptedThisSession++;
                ScreenSampleCoverage coverage = coach.Coverage;
                Say(string.Create(CultureInfo.InvariantCulture,
                    $"  campione {coverage.Accepted}/{target}: sono a ({player.X},{player.Y}), "
                    + $"clic su casella ({tx},{ty}) = delta ({sample.MapDelta.X},{sample.MapDelta.Y}) "
                    + $"al pixel ({relativeX},{relativeY})"));
                Say(string.Create(CultureInfo.InvariantCulture,
                    $"     direzioni {coverage.SectorsFilled}/{ScreenSampleCoach.SectorCount}, "
                    + $"il piu' lontano a {coverage.FarthestTiles} caselle, dispersione {coverage.Spread:F0}"));
                Say($"  -> {verdict.Advice}");
            }

            if (acceptedThisSession == 0)
            {
                Say();
                Say("[REFUSED] no_clicks_observed");
                Say("  Nessun clic di movimento visto. Il campione nasce dal cambio della");
                Say("  casella di destinazione: clicca sul terreno per camminare.");
                return 1;
            }

            string? directory = Path.GetDirectoryName(samplesPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllLines(samplesPath,
                new[] { ScreenProjectionProbe.SamplesHeader }.Concat(recorded));

            Say();
            Say($"{recorded.Count} campioni scritti in {samplesPath}");
            Say("  Le coordinate sono DELTA rispetto al personaggio, non assolute:");
            Say("  con la telecamera che lo segue, e' la sola forma che significhi qualcosa.");

            // The same question --screen-calibrate will be asked, asked now, while
            // the operator is still in front of the client and one more click is
            // cheap. Finding out afterwards costs the whole session.
            Say();
            if (coach.TrySolve(
                    clientArea.Width, clientArea.Height, DateTime.UtcNow,
                    out ScreenProjectionCalibration solved, out string? solveFailure,
                    clientDpi: shape.Dpi))
            {
                Say(string.Create(CultureInfo.InvariantCulture,
                    $"Questi campioni si risolvono: passo {Math.Sqrt((solved.A * solved.A) + (solved.D * solved.D)):F2} x "
                    + $"{Math.Sqrt((solved.B * solved.B) + (solved.E * solved.E)):F2} px per casella, "
                    + $"residuo peggiore {solved.WorstResidualPixels:F1} px."));
                Say("  Lancia --screen-calibrate per scriverla.");
            }
            else
            {
                Say($"NON si risolvono ancora: {solveFailure}");
                Say($"  {coach.Advice}");
                Say("  I campioni restano sul file: la prossima sessione riparte da qui.");
            }

            return 0;
        }
    }

    /// <summary>
    /// Sposta i campioni della sessione precedente in un file con la propria data,
    /// e restituisce dove sono finiti.
    /// </summary>
    /// <remarks>
    /// Archiviare e non cancellare: una sessione di campioni costa all'operatore
    /// una partita, e il 2026-09-07 e' servito rileggerne tre vecchie per capire
    /// perche' la quarta non si risolveva.
    /// </remarks>
    private static string? ArchivePreviousSession(string samplesPath)
    {
        if (!File.Exists(samplesPath))
            return null;

        string directory = Path.GetDirectoryName(samplesPath) ?? ".";
        string archived = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture,
            $"screen-samples-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
        File.Move(samplesPath, archived, overwrite: false);
        return archived;
    }

    private static PixelRect? LocateClientArea(int processId, out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;
        if (!OperatingSystem.IsWindows())
            return null;

        var window = ClientWindowLocator.TryFind(processId, out _);
        if (window is null)
            return null;

        windowHandle = window.Handle;
        return window.ClientArea;
    }
}
