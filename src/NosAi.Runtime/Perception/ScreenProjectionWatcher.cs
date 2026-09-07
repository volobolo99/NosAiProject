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
            var coach = new ScreenSampleCoach(target);
            var recorded = new List<string>();

            // A session that collects eight good clicks and then runs out of time
            // used to throw all eight away, because the file was overwritten. The
            // rows already on file for THIS regime are taken back in, so the
            // operator resumes instead of restarting -- and so the advice below
            // knows which directions are already covered.
            string samplesPath = Path.Combine(repoRoot, ScreenProjectionProbe.SamplesRelativePath);
            int carriedOver = 0, carriedDropped = 0;
            foreach ((ScreenProjectionSample existing, string line) in ReadExistingRows(samplesPath, clientArea, shape))
            {
                if (coach.Offer(existing, characterWasAtRest: true).Accepted)
                {
                    recorded.Add(line);
                    carriedOver++;
                }
                else
                {
                    carriedDropped++;
                }
            }

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
            if (carriedOver > 0 || carriedDropped > 0)
                Say($"  Ripresi {carriedOver} campioni gia' sul file per questo regime"
                    + (carriedDropped > 0 ? $", scartati {carriedDropped} (doppioni o troppo vicini)." : "."));
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

            if (acceptedThisSession == 0 && carriedOver == 0)
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
            Say($"{recorded.Count} campioni scritti in {samplesPath} ({acceptedThisSession} nuovi in questa sessione)");
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
    /// The rows already on file that were measured under the geometry in front of
    /// us now, with the line they came from so it can be written back byte for byte.
    /// </summary>
    /// <remarks>
    /// Rows of another regime are left alone rather than migrated: a sample taken
    /// at a different window size or DPI describes a different projection, and
    /// carrying it forward would put two geometries in one fit. A file that is not
    /// version 1 is not resumed at all — <c>--screen-samples-clear</c> is the way
    /// to start over, and guessing a regime for rows that never recorded one is
    /// how the mixture this format exists to prevent gets back in.
    /// </remarks>
    private static IEnumerable<(ScreenProjectionSample Sample, string Line)> ReadExistingRows(
        string path, PixelRect area, GeometryShape shape)
    {
        if (!File.Exists(path))
            yield break;

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0
            || !string.Equals(lines[0].Trim(), ScreenProjectionProbe.SamplesHeader, StringComparison.Ordinal))
            yield break;

        foreach (string line in lines.Skip(1))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 7) continue;
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mx)) continue;
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int my)) continue;
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sx)) continue;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sy)) continue;
            if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) continue;
            if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) continue;
            if (!uint.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dpi)) continue;
            if (w != area.Width || h != area.Height || dpi != shape.Dpi) continue;

            yield return (new ScreenProjectionSample(new Contracts.MapPoint(mx, my), sx, sy), line);
        }
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
