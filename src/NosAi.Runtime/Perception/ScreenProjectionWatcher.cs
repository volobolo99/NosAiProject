using System.Diagnostics;
using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;


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
    /// Watches for clicks and records one sample per click, until the time runs
    /// out or enough have been collected.
    /// </summary>
    public static int Run(int seconds, int wanted, string? repoRoot = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        repoRoot ??= Directory.GetCurrentDirectory();

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            PixelRect? area = LocateClientArea(session!.ProcessId);
            if (area is not { } clientArea)
            {
                Console.WriteLine("[REFUSED] client_window_not_located");
                return 1;
            }

            var backend = new Win32InputBackend();
            var samples = new List<ScreenProjectionSample>();
            var recorded = new List<string>();

            Console.WriteLine($"Client area {clientArea.Width}x{clientArea.Height} at {clientArea.X},{clientArea.Y}");
            Console.WriteLine();
            Console.WriteLine($"CLICCA PER CAMMINARE, {wanted} volte, in direzioni diverse.");
            Console.WriteLine("  Clicca sul TERRENO, non su un mostro o un oggetto.");
            Console.WriteLine("  Ogni clic e' un campione: il client stesso traduce il pixel in una casella.");
            Console.WriteLine($"  Hai {seconds} secondi.");
            Console.WriteLine();

            short? lastTargetX = null;
            short? lastTargetY = null;
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed.TotalSeconds < seconds && samples.Count < wanted)
            {
                Thread.Sleep(PollIntervalMs);

                if (!session.TryReadPlayer(out PlayerObjectReading player, out _))
                    continue;
                if (player.WalkTargetX is not { } tx || player.WalkTargetY is not { } ty)
                    continue;

                bool changed = lastTargetX is null || tx != lastTargetX || ty != lastTargetY;
                lastTargetX = tx;
                lastTargetY = ty;
                if (!changed)
                    continue;

                // The very first read establishes what the target already was; it
                // is not a click and pairs with no cursor the operator placed.
                if (samples.Count == 0 && clock.Elapsed.TotalMilliseconds < PollIntervalMs * 3)
                    continue;

                // A click that resolves to where the character already stands is
                // not a direction, and it would contribute a zero-length delta.
                if (tx == player.X && ty == player.Y)
                    continue;

                if (!backend.TryGetCursorPosition(out int cursorX, out int cursorY))
                    continue;

                int relativeX = cursorX - clientArea.X;
                int relativeY = cursorY - clientArea.Y;
                if (relativeX < 0 || relativeX >= clientArea.Width
                    || relativeY < 0 || relativeY >= clientArea.Height)
                {
                    Console.WriteLine($"  ignorato: cursore fuori dalla finestra ({cursorX},{cursorY})");
                    continue;
                }

                var sample = new ScreenProjectionSample(
                    new Autonomy.MapPoint(tx - player.X, ty - player.Y), relativeX, relativeY);
                samples.Add(sample);
                recorded.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.MapDelta.X} {sample.MapDelta.Y} {relativeX} {relativeY} {clientArea.Width} {clientArea.Height}"));

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  campione {samples.Count}/{wanted}: sono a ({player.X},{player.Y}), "
                    + $"clic su casella ({tx},{ty}) = delta ({sample.MapDelta.X},{sample.MapDelta.Y}) "
                    + $"al pixel ({relativeX},{relativeY})"));
            }

            if (samples.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("[REFUSED] no_clicks_observed");
                Console.WriteLine("  Nessun clic di movimento visto. Il campione nasce dal cambio della");
                Console.WriteLine("  casella di destinazione: clicca sul terreno per camminare.");
                return 1;
            }

            string path = Path.Combine(repoRoot, ScreenProjectionProbe.SamplesRelativePath);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllLines(path, recorded);

            Console.WriteLine();
            Console.WriteLine($"{samples.Count} campioni scritti in {path}");
            Console.WriteLine("  Le coordinate sono DELTA rispetto al personaggio, non assolute:");
            Console.WriteLine("  con la telecamera che lo segue, e' la sola forma che significhi qualcosa.");
            return 0;
        }
    }

    private static PixelRect? LocateClientArea(int processId)
        => OperatingSystem.IsWindows()
            ? ClientWindowLocator.TryFind(processId, out _)?.ClientArea
            : null;
}
