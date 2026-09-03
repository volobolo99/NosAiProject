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
/// One round of that is a coincidence filter, not a proof: on a big heap some
/// unrelated pair will hold those two numbers. The second round is the proof.
/// After the wire reports a <b>different</b> current, an address that is really
/// health follows it, and an address that merely happened to hold the old pair
/// does not. Nothing here asks a human to judge which candidate looks right.
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
    /// An address that held the old pair by coincidence has no reason to hold
    /// the new one, and that is the whole argument.
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
            if (!TryListen(address, port, player.CharacterId, seconds, 1, out WirePlayerVitals first, out string? firstWhy))
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
            if (!TryListen(address, port, player.CharacterId, seconds, 2, out WirePlayerVitals second, out string? secondWhy))
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

            var established = 0;
            established += Settle(read, hp, (uint)hp1, (uint)hp2, (uint)maxHp2, "hp");
            established += Settle(read, mp, (uint)mp1, (uint)mp2, (uint)maxMp2, "mp");

            Console.WriteLine();
            Console.WriteLine(
                "An address that agreed twice is evidence, not a promotion. It is a heap");
            Console.WriteLine(
                "address and dies with the client: turning it into a reading needs a chain");
            Console.WriteLine(
                "from a resolved base, which is the next question, not this one.");

            return established == 2 ? 0 : 1;
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

    private static int Settle(
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
            return 0;
        }

        List<VitalsPairHit> survivors = Confirm(candidates, read, max, after);
        if (Verdict(survivors, field) is { } why)
        {
            Console.WriteLine($"{field}: [REFUSED] {why}");
            foreach (VitalsPairHit hit in survivors)
                Console.WriteLine($"    {hit.Describe()}");
            return 0;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{field}: AGREED TWICE at {survivors[0].Describe()}"));
        return 1;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryListen(
        IPAddress address,
        int port,
        long characterId,
        int seconds,
        int round,
        out WirePlayerVitals vitals,
        out string? failureReason)
    {
        vitals = default;

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
        failureReason = null;
        return true;
    }
}
