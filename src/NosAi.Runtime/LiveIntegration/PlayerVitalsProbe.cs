using System.Globalization;
using NosAi.Runtime.Navigation;

namespace NosAi.LiveIntegration;

/// <summary>
/// Prints every structural HP/MP candidate in the player manager and player
/// object windows, beside the percentage the wire last gave for this character.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 operator command. Discordance is a column, not a log line. A match
/// is still UNKNOWN — it is evidence for the operator, not a promotion this
/// command is allowed to make.
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

    public static int Run(string? capturePath = null, int watchSeconds = 0)
    {
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

            if (!session.TryScanPlayerVitals(out IReadOnlyList<PlayerVitalsHit> hits, out string? scanFailure))
            {
                Console.WriteLine($"[REFUSED] {scanFailure}");
                return 1;
            }

            WirePlayerVitals? wire = LoadWire(capturePath, player.CharacterId, out string wireSource);

            Console.WriteLine("=== player vitals (candidates; UNKNOWN until concordance) ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session.ProcessId}, character {player.CharacterId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wire:   {wireSource}"));
            Console.WriteLine();

            PrintTable(hits, wire);

            if (watchSeconds > 0)
            {
                Console.WriteLine();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"Waiting {watchSeconds}s: take a hit. Survivors are offsets whose HP fell and whose maxima held."));
                Thread.Sleep(TimeSpan.FromSeconds(watchSeconds));

                if (!session.TryScanPlayerVitals(out IReadOnlyList<PlayerVitalsHit> after, out string? afterFailure))
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

            Console.WriteLine();
            Console.WriteLine("A match is not LIVE. Nothing downstream may decide on these vitals.");
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
