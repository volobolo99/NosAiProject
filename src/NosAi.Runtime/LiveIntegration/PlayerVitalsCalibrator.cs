using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using NosAi.LiveIntegration.Capture;

namespace NosAi.LiveIntegration;

/// <summary>One address whose word pair matched what the wire said.</summary>
/// <remarks>
/// The pair is <c>[address] == max</c> and <c>[address + 4] == current</c>. That
/// adjacency is not taken from the third-party source, which puts the maxima
/// 0xF0 apart in one block: it was measured on a live client, where MaxHP sat in
/// the four bytes immediately before HP and the source's MP half did not hold at
/// all.
/// </remarks>
public readonly record struct VitalsPairHit(IntPtr Address, uint Max, uint Current)
{
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"0x{Address.ToInt64():X}  {Current}/{Max}");
}

/// <summary>
/// Finds HP and MP by asking the wire what they are and looking for that,
/// instead of guessing where they might be.
/// </summary>
/// <remarks>
/// <para>
/// The scan for a shape failed on a real client: a structural filter admits any
/// four words where a current sits inside a maximum, and zero satisfies that
/// always, so it produced noise and none of it was health. This works the other
/// way round. The wire's <c>stat</c> packet carries hp, maxHp, mp and maxMp as
/// exact integers from a source that cannot see the client's memory, so the
/// question stops being "what looks like health" and becomes "where do these two
/// numbers sit side by side".
/// </para>
/// <para>
/// The adjacency does most of the narrowing, and it does it in the first round:
/// on a live client seven addresses held the maximum and one had the current
/// beside it, thirty-eight and one for MP. The second round removes what is left
/// of coincidence — an unrelated pair that happened to hold the old numbers has
/// no reason to hold the new ones.
/// </para>
/// <para>
/// It does <b>not</b> remove a mirror, and that limit is the honest part. A HUD
/// binding, a stats cache, or the value a health bar animates toward is written
/// from the same packet that writes the authoritative field, so it follows the
/// wire exactly as well — twenty rounds would confirm it too, because following
/// the wire is what a mirror does. What separates the two is not more rounds: it
/// is reachability at a fixed distance from a base the runtime resolves, which
/// is why this ends by asking what points at the survivor rather than by
/// declaring it found.
/// </para>
/// <para>
/// Agreement is still not <see cref="NosAi.Runtime.Contracts.DataSourceKind.Live"/>.
/// It is the concordance the spec asks for before that question may even be put,
/// and the classification stays where the phase left it until a session records it.
/// </para>
/// </remarks>
public static class PlayerVitalsCalibrator
{
    public const string Flag = "--calibrate-vitals";

    public const string NoStatReason = "calibrate_wire_had_no_stat";
    public const string UnchangedPrefix = "calibrate_value_did_not_move";
    public const string NoCandidatePrefix = "calibrate_no_address_held_the_pair";
    public const string AmbiguousPrefix = "calibrate_ambiguous";
    public const string SameAddressReason = "calibrate_hp_and_mp_are_one_address";
    public const string MovedDuringScanPrefix = "calibrate_value_moved_during_scan";

    /// <summary>
    /// Whether the search was run against a truth that had already expired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole-process scan is not atomic with the wire reading, and it takes
    /// real time. If a value moved while the scan ran, the authoritative address
    /// stopped holding the number being searched for and was dropped — while a
    /// copy that refreshes only on a UI event still held it and survived. The race
    /// does not merely lose the answer, it prefers the wrong one, and MP is the
    /// exposed field because regeneration ticks with no fighting at all.
    /// </para>
    /// <para>
    /// The earliest reading of the next round is the first thing the wire said
    /// after the scan, so the pair of readings brackets it. A value that already
    /// differs there was searched for against something that had stopped being
    /// true, and the result is refused rather than reported.
    /// </para>
    /// </remarks>
    public static string? MovedDuringScanReason(
        int? hpAfterScan, int? mpAfterScan, uint hpSearched, uint mpSearched)
    {
        if (hpAfterScan is { } hp && (uint)hp != hpSearched)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{MovedDuringScanPrefix}:hp:{hpSearched}->{hp}");
        }

        if (mpAfterScan is { } mp && (uint)mp != mpSearched)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{MovedDuringScanPrefix}:mp:{mpSearched}->{mp}");
        }

        return null;
    }

    /// <summary>
    /// Keeps the candidates that hold <paramref name="max"/> with
    /// <paramref name="current"/> in the next word.
    /// </summary>
    /// <param name="readWord">
    /// Reads one 32-bit word, or null when that address cannot be read. A word
    /// that will not read is dropped rather than treated as zero: unreadable and
    /// zero are different answers, and only one of them is a number.
    /// </param>
    public static List<VitalsPairHit> KeepAdjacent(
        IReadOnlyList<IntPtr> candidates,
        Func<IntPtr, uint?> readWord,
        uint max,
        uint current)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(readWord);

        var kept = new List<VitalsPairHit>();
        foreach (IntPtr address in candidates)
        {
            if (readWord(address) is not { } atMax || atMax != max)
                continue;
            if (readWord(address + sizeof(uint)) is not { } atCurrent || atCurrent != current)
                continue;

            kept.Add(new VitalsPairHit(address, max, current));
        }

        return kept;
    }

    /// <summary>
    /// Keeps the survivors that still hold the pair after the wire moved.
    /// </summary>
    /// <remarks>
    /// The same predicate as <see cref="KeepAdjacent"/> against a later truth.
    /// An address that held the old pair by coincidence has no reason to hold the
    /// new one. A copy written from the same packet does hold it, so surviving
    /// here rules out luck and not mirrors.
    /// </remarks>
    public static List<VitalsPairHit> Confirm(
        IReadOnlyList<VitalsPairHit> previous,
        Func<IntPtr, uint?> readWord,
        uint max,
        uint current)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var addresses = new List<IntPtr>(previous.Count);
        foreach (VitalsPairHit hit in previous)
            addresses.Add(hit.Address);

        return KeepAdjacent(addresses, readWord, max, current);
    }

    /// <summary>
    /// Whether a second round can prove anything.
    /// </summary>
    /// <remarks>
    /// If the current did not move, re-checking the same pair re-asks the first
    /// question and every coincidence survives. Saying so is the honest answer;
    /// reporting the unchanged survivors as confirmed would not be.
    /// </remarks>
    public static bool CanConfirm(uint before, uint after) => before != after;

    /// <summary>The named verdict for one field's survivors, or null when it is the single one.</summary>
    public static string? Verdict(IReadOnlyList<VitalsPairHit> survivors, string field)
    {
        ArgumentNullException.ThrowIfNull(survivors);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return survivors.Count switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"{NoCandidatePrefix}:{field}"),
            1 => null,
            _ => string.Create(CultureInfo.InvariantCulture,
                $"{AmbiguousPrefix}:{field}:{survivors.Count}"),
        };
    }

    /// <summary>The reason a round could not confirm, or null when it could.</summary>
    public static string? UnchangedReason(uint before, uint after, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return CanConfirm(before, after)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{UnchangedPrefix}:{field}:{before}");
    }

    /// <summary>Two rounds of wire truth against process memory, unattended.</summary>
    /// <param name="endpoint">The game server as <c>ip:port</c>, for the capture.</param>
    /// <param name="seconds">How long each round listens to the wire.</param>
    [SupportedOSPlatform("windows")]
    public static int Run(string? endpoint, int seconds = 20)
    {
        if (!WireRecorder.TryParseEndpoint(endpoint, out IPAddress address, out int port, out string? endpointFailure))
        {
            Console.WriteLine($"[REFUSED] {endpointFailure}");
            Console.WriteLine($"Usage: {Flag} <ip>:<port> [--watch N]");
            return 2;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] client_not_readable:{attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? playerFailure))
            {
                Console.WriteLine($"[REFUSED] {playerFailure}");
                return 1;
            }

            Console.WriteLine("=== calibrating player vitals from the wire ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session.ProcessId}, character {player.CharacterId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"server: {address}:{port}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rounds: 2 x {seconds}s"));
            Console.WriteLine();
            Console.WriteLine("Round 1 listens, then memory is searched for the two numbers the wire");
            Console.WriteLine("reported. Round 2 needs them to have MOVED: fight, so HP and MP change.");
            Console.WriteLine();

            Console.WriteLine("--- round 1 ---");
            if (!TryListen(address, port, player.CharacterId, seconds, 1, out WirePlayerVitals first, out _, out string? firstWhy))
            {
                Console.WriteLine($"[REFUSED] {firstWhy}");
                return 1;
            }

            if (first.Hp is not { } hp1 || first.MaxHp is not { } maxHp1
                || first.Mp is not { } mp1 || first.MaxMp is not { } maxMp1)
            {
                Console.WriteLine($"[REFUSED] {NoStatReason}");
                return 1;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"wire says: hp {hp1}/{maxHp1}, mp {mp1}/{maxMp1}"));

            Func<IntPtr, uint?> read = Word(session);
            List<VitalsPairHit> hp = Search(session, read, (uint)maxHp1, (uint)hp1, "hp");
            List<VitalsPairHit> mp = Search(session, read, (uint)maxMp1, (uint)mp1, "mp");

            Console.WriteLine();
            Console.WriteLine("--- round 2 (make the numbers move) ---");
            if (!TryListen(address, port, player.CharacterId, seconds, 2, out WirePlayerVitals second, out WirePlayerVitals bracket, out string? secondWhy))
            {
                Console.WriteLine($"[REFUSED] {secondWhy}");
                return 1;
            }

            if (second.Hp is not { } hp2 || second.MaxHp is not { } maxHp2
                || second.Mp is not { } mp2 || second.MaxMp is not { } maxMp2)
            {
                Console.WriteLine($"[REFUSED] {NoStatReason}");
                return 1;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"wire says: hp {hp2}/{maxHp2}, mp {mp2}/{maxMp2}"));
            Console.WriteLine();

            if (MovedDuringScanReason(bracket.Hp, bracket.Mp, (uint)hp1, (uint)mp1) is { } raced)
            {
                Console.WriteLine($"[REFUSED] {raced}");
                Console.WriteLine("The search ran against a number that had already changed, so the");
                Console.WriteLine("address holding it may have been dropped. Hold still until the two");
                Console.WriteLine("search lines print, then fight for round 2.");
                return 1;
            }

            VitalsPairHit? hpHit = Settle(read, hp, (uint)hp1, (uint)hp2, (uint)maxHp2, "hp");
            VitalsPairHit? mpHit = Settle(read, mp, (uint)mp1, (uint)mp2, (uint)maxMp2, "mp");

            if (hpHit is null && mpHit is null)
                return 1;

            // Two fields cannot be the same word. Equal pools would normally make
            // both searches ambiguous and refuse on their own, but relying on that
            // is reasoning where a comparison will do.
            if (hpHit is { } h && mpHit is { } m && h.Address == m.Address)
            {
                Console.WriteLine($"[REFUSED] {SameAddressReason}");
                return 1;
            }

            // An agreed address is heap and dies with the client, so the run does
            // not stop at proving which one it is. Asking what points at it is the
            // difference between a calibration and a reading.
            Console.WriteLine();
            Console.WriteLine("--- what could anchor these ---");
            var anchored = 0;
            if (hpHit is { } confirmedHp)
                anchored += PointerAnchorHunter.Report(session, confirmedHp.Address, PointerAnchorHunter.DefaultSpan, "hp") == 0 ? 1 : 0;
            if (mpHit is { } confirmedMp)
                anchored += PointerAnchorHunter.Report(session, confirmedMp.Address, PointerAnchorHunter.DefaultSpan, "mp") == 0 ? 1 : 0;

            return hpHit is not null && mpHit is not null && anchored == 2 ? 0 : 1;
        }
    }

    /// <summary>Reads one 32-bit word, or null when that address will not read.</summary>
    private static Func<IntPtr, uint?> Word(ClientMemorySession session) => address =>
    {
        MemoryReadResult result = session.Reader.Read(address, sizeof(uint));
        return result.Ok ? BitConverter.ToUInt32(result.Bytes) : null;
    };

    [SupportedOSPlatform("windows")]
    private static List<VitalsPairHit> Search(
        ClientMemorySession session, Func<IntPtr, uint?> read, uint max, uint current, string field)
    {
        // The maximum is the stable half, so it is what the whole-process scan
        // looks for; the current is what turns a common number into a pair.
        MemoryScanner.ScanResult scan = MemoryScanner.Scan(session.Reader, (int)max);
        List<VitalsPairHit> kept = KeepAdjacent(scan.Addresses, read, max, current);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{field}: {scan.Addresses.Count} address(es) held {max}, {kept.Count} had {current} beside it"));
        return kept;
    }

    /// <summary>The one address that agreed twice, or null with the reason printed.</summary>
    private static VitalsPairHit? Settle(
        Func<IntPtr, uint?> read,
        IReadOnlyList<VitalsPairHit> candidates,
        uint before,
        uint after,
        uint max,
        string field)
    {
        if (UnchangedReason(before, after, field) is { } stuck)
        {
            Console.WriteLine($"{field}: [REFUSED] {stuck}");
            return null;
        }

        List<VitalsPairHit> survivors = Confirm(candidates, read, max, after);
        if (Verdict(survivors, field) is { } why)
        {
            Console.WriteLine($"{field}: [REFUSED] {why}");
            foreach (VitalsPairHit hit in survivors)
                Console.WriteLine($"    {hit.Describe()}");
            return null;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{field}: AGREED TWICE at {survivors[0].Describe()}"));
        return survivors[0];
    }

    [SupportedOSPlatform("windows")]
    private static bool TryListen(
        IPAddress address,
        int port,
        long characterId,
        int seconds,
        int round,
        out WirePlayerVitals vitals,
        out WirePlayerVitals earliest,
        out string? failureReason)
    {
        vitals = default;
        earliest = default;

        string path = Path.Combine(
            WireRecorder.DefaultDirectory,
            string.Create(CultureInfo.InvariantCulture,
                $"calibrate_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z_round{round}.noscap"));

        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(address, port, out string? driverFailure);
        if (source is null)
        {
            failureReason = $"{WireRecorder.DriverUnavailablePrefix}:{driverFailure}";
            return false;
        }

        RecordingOutcome outcome;
        using (source)
        using (var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"listening {seconds}s -> {path}"));
            outcome = WireRecorder.RecordFrom(source, path, stopping.Token);
        }

        if (!outcome.Ok)
        {
            failureReason = outcome.FailureReason;
            return false;
        }

        WirePlayerVitals? parsed = WirePlayerVitalsParser.FromCapture(path, characterId);
        if (parsed is null)
        {
            failureReason = NoStatReason;
            return false;
        }

        vitals = parsed.Value;
        earliest = WirePlayerVitalsParser.FromCapture(path, characterId, firstInsteadOfLast: true) ?? parsed.Value;
        failureReason = null;
        return true;
    }
}
