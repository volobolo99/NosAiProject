using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Runtime.Navigation;

namespace NosAi.LiveIntegration;

/// <summary>
/// Prints the established HP/MP reading and then every structural candidate,
/// each beside what the wire last said for this character.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 operator command, and it now shows two different things that must not
/// be confused. The <b>reading</b> follows the chain established on 3 September
/// 2026 and carries its own classification: LIVE while the permanent predicate
/// holds, UNKNOWN with the reason when it does not. The <b>candidates</b> below
/// it are the old window scan, and a structural match among them is still
/// UNKNOWN — evidence for the operator, not a promotion this command may make.
/// </para>
/// <para>
/// Keeping the scan is deliberate. It is the contrast: against a live client it
/// produced thirty-seven candidates and no match, while the chain read the right
/// numbers on the first try. A reader who sees only the working answer cannot
/// tell how little the shape filter was worth.
/// </para>
/// <para>
/// Read-only. The capture is optional; without it the wire column is empty and
/// the memory column still prints. <c>--watch</c> takes a second scan after the
/// operator has taken a hit, and keeps only the offsets whose HP fell while
/// the maxima held.
/// </para>
/// </remarks>
public static class PlayerVitalsProbe
{
    public const string Flag = "--player-vitals";
    public const string ClientNotReadable = "client_not_readable";

    public static int Run(
        string? capturePath = null,
        int watchSeconds = 0,
        int windowBytes = PlayerVitalsScan.DefaultWindowBytes)
    {
        // One window for both passes: the oracle compares offsets between them,
        // and a second pass over a different span would drop survivors for the
        // reason that they were never looked for.
        int window = PlayerVitalsScan.ClampWindow(windowBytes);

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? failure))
        {
            Console.WriteLine($"[REFUSED] {ClientNotReadable}:{failure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? playerFailure))
            {
                Console.WriteLine($"[REFUSED] {playerFailure}");
                return 1;
            }

            if (!session.TryScanPlayerVitals(out IReadOnlyList<PlayerVitalsHit> hits, out string? scanFailure, window))
            {
                Console.WriteLine($"[REFUSED] {scanFailure}");
                return 1;
            }

            WirePlayerVitals? wire = LoadWire(capturePath, player.CharacterId, out string wireSource);

            Console.WriteLine("=== player vitals: the established reading, then the candidates ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session.ProcessId}, character {player.CharacterId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wire:   {wireSource}"));
            // The searched span is printed because an empty result means nothing
            // without it: "not found" and "not looked for" read the same.
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"window: 0x{window:X} bytes from each of player manager and player object"));
            Console.WriteLine();

            PrintEstablished(session, wire);
            Console.WriteLine();

            PrintTable(hits, wire);

            if (watchSeconds > 0)
            {
                Console.WriteLine();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"Waiting {watchSeconds}s: take a hit. Survivors are offsets whose HP fell and whose maxima held."));
                Thread.Sleep(TimeSpan.FromSeconds(watchSeconds));

                if (!session.TryScanPlayerVitals(out IReadOnlyList<PlayerVitalsHit> after, out string? afterFailure, window))
                {
                    Console.WriteLine($"[REFUSED] {afterFailure}");
                    return 1;
                }

                List<PlayerVitalsHit> survivors = PlayerVitalsOracle.Survivors(hits, after);
                Console.WriteLine();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"oracle: {survivors.Count} survivor(s) of {hits.Count} → {after.Count} (HP fell, maxima held)."));
                PrintTable(survivors, wire);
            }

            // Scoped to the table above it. It used to be the last word of the
            // whole command, which stopped being true the moment an established
            // reading appeared higher up: the output would say LIVE and then deny
            // it four lines later, and a reader has no way to tell which sentence
            // is about what.
            Console.WriteLine();
            Console.WriteLine("The candidates above are not the reading. A structural match is not LIVE,");
            Console.WriteLine("and nothing downstream may decide on them. The chain at the top is the");
            Console.WriteLine("reading, and it carries its own classification.");
        }

        return 0;
    }

    internal static string FormatRow(in PlayerVitalsHit hit, WirePlayerVitals? wire, string vs)
    {
        PlayerVitalsCandidate memory = PlayerVitalsCandidate.From(hit);
        string wireHp = wire is { } w
            ? w.Hp is { } abs && w.MaxHp is { } absMax
                ? string.Create(CultureInfo.InvariantCulture, $"{abs}/{absMax}")
                : w.HasPercent
                    ? string.Create(CultureInfo.InvariantCulture, $"{w.HpPercent}%")
                    : "—"
            : "—";
        string wireMp = wire is { } w2
            ? w2.Mp is { } mp && w2.MaxMp is { } maxMp
                ? string.Create(CultureInfo.InvariantCulture, $"{mp}/{maxMp}")
                : w2.MpPercent is { } mpPct
                    ? string.Create(CultureInfo.InvariantCulture, $"{mpPct}%")
                    : "—"
            : "—";

        return string.Create(CultureInfo.InvariantCulture,
            $"{hit.Key,-14}  {hit.Block.Hp,5}/{hit.Block.MaxHp,-5}  {hit.Block.Mp,5}/{hit.Block.MaxMp,-5}  {memory.HpPercent,3}%/{memory.MpPercent,3}%  {Pad(wireHp, 11)}  {Pad(wireMp, 11)}  {vs}");
    }

    /// <summary>
    /// The established reading beside the wire, which is what the phase's fourth
    /// acceptance criterion asks an operator command to show.
    /// </summary>
    /// <remarks>
    /// This is the only line here that may say LIVE. It earned that on 3 September
    /// 2026: two rounds of concordance in two sessions, and an anchor that came
    /// back identical after a client restart while the heap address it reaches
    /// moved. The scan table below it is still candidates, and still UNKNOWN.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void PrintEstablished(ClientMemorySession session, WirePlayerVitals? wire)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"chain:  Module+0x{NosTaleClientLayout.PlayerVitalsModuleOffset:X} -> +0x{NosTaleClientLayout.MaxHpChainOffset:X} (hp) / +0x{NosTaleClientLayout.MaxMpChainOffset:X} (mp)"));

        if (!session.TryReadPlayerVitals(out PlayerVitalsReading reading, out string? why))
        {
            // The predicate withdrew it. Never the last good numbers.
            Console.WriteLine($"reading: UNKNOWN ({why})");
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"reading: {reading.Describe()}  [LIVE]"));

        if (wire is not { Hp: { } wireHp, MaxHp: { } wireMaxHp, Mp: { } wireMp, MaxMp: { } wireMaxMp })
        {
            Console.WriteLine("wire:    no absolute stat in the recording, so nothing corroborates this read");
            return;
        }

        bool agrees = reading.Hp == (uint)wireHp && reading.MaxHp == (uint)wireMaxHp
                      && reading.Mp == (uint)wireMp && reading.MaxMp == (uint)wireMaxMp;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"wire:    hp {wireHp}/{wireMaxHp}, mp {wireMp}/{wireMaxMp}  {(agrees ? "MATCH" : "MISMATCH")}"));

        if (!agrees)
        {
            Console.WriteLine(
                "         A recording taken at another moment disagrees by construction.");
            Console.WriteLine(
                "         Only a capture of this session says anything: --record-wire.");
        }
    }

    private static void PrintTable(IReadOnlyList<PlayerVitalsHit> hits, WirePlayerVitals? wire)
    {
        Console.WriteLine(
            "offset          hp/max           mp/max           mem%       wire hp       wire mp       vs");
        Console.WriteLine(
            "--------------  ---------------  ---------------  ---------  ------------  ------------  --------");

        if (hits.Count == 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"—               [REFUSED] {PlayerVitalsCandidate.NotFoundReason}"));
            return;
        }

        int matches = 0;
        int mismatches = 0;
        foreach (PlayerVitalsHit hit in hits)
        {
            string vs = WirePlayerVitalsParser.Compare(PlayerVitalsCandidate.From(hit), wire);
            if (vs == "match")
                matches++;
            if (vs == "MISMATCH")
                mismatches++;
            Console.WriteLine(FormatRow(hit, wire, vs));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{hits.Count} candidate(s), {matches} match, {mismatches} MISMATCH."));
    }

    private static WirePlayerVitals? LoadWire(string? capturePath, long playerId, out string source)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            source = "no capture (pass a .noscap to fill this column)";
            return null;
        }

        if (!File.Exists(capturePath))
        {
            source = $"recording_not_found:{capturePath}";
            return null;
        }

        try
        {
            WirePlayerVitals? row = WirePlayerVitalsParser.FromCapture(capturePath, playerId);
            source = row is { } found
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(capturePath)}  last {found.Opcode}")
                : $"{Path.GetFileName(capturePath)}  no player vitals packet";
            return row;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            source = $"recording_unreadable:{ex.GetType().Name}";
            return null;
        }
    }

    private static string Pad(string text, int width)
    {
        if (text.Length <= width)
            return text.PadRight(width);
        return text[..(width - 1)] + "…";
    }
}
