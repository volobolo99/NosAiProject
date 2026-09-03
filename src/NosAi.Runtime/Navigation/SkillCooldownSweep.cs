using System.Globalization;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>One word that behaved like a cooldown, and what it holds when ready.</summary>
public readonly record struct SweepWord(IntPtr Address, uint ReadyValue)
{
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"0x{Address.ToInt64():X}  ready={ReadyValue}");
}

/// <summary>
/// The cooldown hunt over the whole process rather than two 8 KB windows.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SkillCooldownFinder"/> searches 8192 bytes from the player manager
/// and the player object. On a real client that returned zero survivors across
/// two valid rounds — the wire announced both restorations, so the oracle had
/// what it needed and found nothing where it looked. Phase 2 had the same shape
/// and the same cause: on this build these fields hang off a static pointer, and
/// proximity to a resolved base is the wrong axis. Widening the window did not
/// help there and is not expected to help here.
/// </para>
/// <para>
/// <b>The predicate makes no assumption about the encoding.</b> The existing
/// oracle keeps a word that falls to zero when the skill becomes available; that
/// is only true if the cooldown is a countdown, and it might be a ready-at
/// timestamp, a tick count, or a flag beside one. This keeps a word that
/// <i>changed</i> when the skill was used and returned to <i>exactly</i> what it
/// held when ready. A countdown satisfies that; so does a timestamp; a word that
/// merely drifts does not.
/// </para>
/// <para>
/// One round of that is not evidence — a great many words in 195 MB move and
/// come back. Two rounds intersected is the argument, in the same shape the
/// target pointer and the vitals were established with.
/// </para>
/// </remarks>
public static class SkillCooldownSweep
{
    public const string Flag = "--sweep-cooldown";

    /// <summary>The most changed words carried out of one comparison.</summary>
    /// <remarks>
    /// A bound before an allocation, as everywhere else here. Reaching it means
    /// the comparison was too broad to mean anything, and that is reported rather
    /// than silently truncated into a smaller-looking answer.
    /// </remarks>
    public const int MaxChanged = 4_000_000;

    public const string NoCandidateReason = "sweep_no_word_behaved_like_a_cooldown";
    public const string TruncatedReason = "sweep_too_many_words_changed";
    public const string AmbiguousPrefix = "sweep_ambiguous";
    public const string WireUnreachableReason = "sweep_wire_unreachable_no_second_source";

    /// <summary>
    /// Words whose value differs between the ready snapshot and now.
    /// </summary>
    /// <remarks>
    /// The ready value is carried, not the busy one: it is what the word has to
    /// come back to, and comparing against it later is the whole test.
    /// </remarks>
    public static void CollectChanged(
        IntPtr regionBase,
        ReadOnlySpan<byte> ready,
        ReadOnlySpan<byte> busy,
        List<SweepWord> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        int length = Math.Min(ready.Length, busy.Length);
        for (int offset = 0; offset + sizeof(uint) <= length; offset += sizeof(uint))
        {
            uint before = BitConverter.ToUInt32(ready[offset..]);
            uint after = BitConverter.ToUInt32(busy[offset..]);
            if (before == after)
                continue;

            into.Add(new SweepWord(new IntPtr(regionBase.ToInt64() + offset), before));
            if (into.Count >= MaxChanged)
                return;
        }
    }

    /// <summary>
    /// Of the changed words, those that have returned to their ready value.
    /// </summary>
    /// <param name="read">
    /// One 32-bit word, or null when unreadable. Unreadable drops the candidate:
    /// a word nobody can read is not the one the runtime will read later.
    /// </param>
    public static List<SweepWord> KeepRestored(
        IReadOnlyList<SweepWord> candidates, Func<IntPtr, uint?> read)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(read);

        var kept = new List<SweepWord>();
        foreach (SweepWord word in candidates)
        {
            if (read(word.Address) is { } now && now == word.ReadyValue)
                kept.Add(word);
        }

        return kept;
    }

    /// <summary>
    /// Words that survived both rounds at the same address with the same ready value.
    /// </summary>
    /// <remarks>
    /// The ready value has to match too. An address that came back to a different
    /// number in the second round is not returning to a resting state, it is
    /// passing through.
    /// </remarks>
    public static List<SweepWord> Intersect(
        IReadOnlyList<SweepWord> first, IReadOnlyList<SweepWord> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var later = new Dictionary<long, uint>(second.Count);
        foreach (SweepWord word in second)
            later[word.Address.ToInt64()] = word.ReadyValue;

        var kept = new List<SweepWord>();
        foreach (SweepWord word in first)
        {
            if (later.TryGetValue(word.Address.ToInt64(), out uint ready) && ready == word.ReadyValue)
                kept.Add(word);
        }

        return kept;
    }

    /// <summary>
    /// Of the candidates, those that never left their resting value while nothing
    /// was happening.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The missing half of the oracle. "Moved when the skill was used and came
    /// back" admits every word that churns: on a live client two rounds of it left
    /// 8265 survivors, and their values gave them away — 490441108 and 842281263
    /// are ASCII read as integers, so the sweep was walking string buffers.
    /// </para>
    /// <para>
    /// A cooldown at rest does not move while nobody uses the skill. Anything that
    /// deviates even once during a quiet stretch is doing something else, and this
    /// is what the target pointer's oracle meant by <i>and not otherwise</i>.
    /// Sampling repeatedly matters: a word checked once at the end would have had
    /// time to churn and return, which is exactly the population being removed.
    /// </para>
    /// </remarks>
    public static List<SweepWord> KeepStill(
        IReadOnlyList<SweepWord> candidates, Func<IntPtr, uint?> read)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(read);

        var kept = new List<SweepWord>();
        foreach (SweepWord word in candidates)
        {
            if (read(word.Address) is { } now && now == word.ReadyValue)
                kept.Add(word);
        }

        return kept;
    }

    /// <summary>The distance between one skill's cooldown word and the next.</summary>
    /// <remarks>
    /// From the third-party bot's own source, where the test for "skill n is
    /// ready" is <c>*(DWORD*)(table + (n - 1) * 0x48) == 0</c>. Its starting
    /// addresses are not used and are not trusted: the same source puts the
    /// vitals at 0x004F4BA8 while this client's were derived at 0x51FEA4, so its
    /// RVAs are for another build. A stride is a property of the structure rather
    /// than of an address, and that is the part worth borrowing.
    /// </remarks>
    public const int SkillStride = 0x48;

    /// <summary>How many neighbours at the stride make a table rather than a coincidence.</summary>
    /// <remarks>
    /// A character has several skills, so a real cooldown table has entries either
    /// side at the stride. Two is deliberately low: the bot describes separate
    /// tables for slots 1-4 and 5+, so a word can sit near the end of its own.
    /// </remarks>
    public const int MinNeighbours = 2;

    public const string NoTableReason = "sweep_no_word_sits_in_a_skill_table";

    /// <summary>
    /// Of the candidates, those with neighbours at the skill stride.
    /// </summary>
    /// <remarks>
    /// The second independent filter, and it discards a different population than
    /// the quiet control does. Churn is scattered; a cooldown belongs to a table,
    /// and its neighbours are other skills' cooldowns behaving the same way. A run
    /// of survivors spaced exactly 0x48 apart is a structure, and string buffers
    /// do not produce one.
    /// </remarks>
    public static List<SweepWord> KeepInSkillTable(
        IReadOnlyList<SweepWord> candidates, int stride = SkillStride, int minNeighbours = MinNeighbours)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));

        var addresses = new HashSet<long>(candidates.Count);
        foreach (SweepWord word in candidates)
            addresses.Add(word.Address.ToInt64());

        var kept = new List<SweepWord>();
        foreach (SweepWord word in candidates)
        {
            long at = word.Address.ToInt64();
            var neighbours = 0;

            // Walk both ways: a word can be the first or the last of its table.
            for (var step = 1; step <= minNeighbours; step++)
            {
                if (addresses.Contains(at - (long)step * stride))
                    neighbours++;
                if (addresses.Contains(at + (long)step * stride))
                    neighbours++;
            }

            if (neighbours >= minNeighbours)
                kept.Add(word);
        }

        return kept;
    }

    /// <summary>The named verdict for a set of survivors, or null for the single one.</summary>
    public static string? Verdict(IReadOnlyList<SweepWord> survivors)
    {
        ArgumentNullException.ThrowIfNull(survivors);

        return survivors.Count switch
        {
            0 => NoCandidateReason,
            1 => null,
            _ => string.Create(CultureInfo.InvariantCulture, $"{AmbiguousPrefix}:{survivors.Count}"),
        };
    }

    /// <summary>Every private region's bytes, as they are at this instant.</summary>
    /// <remarks>
    /// Private only: the cooldown is per-character state, not something mapped
    /// from the image. On the measured client that is 195 MB, which is what one
    /// comparison costs and the reason the whole process can be swept at all.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static Dictionary<IntPtr, byte[]> Snapshot(ProcessMemoryReader reader, out long bytes)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var taken = new Dictionary<IntPtr, byte[]>();
        long total = 0;

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            if (!region.IsPrivate)
                continue;

            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                var at = new IntPtr(region.BaseAddress.ToInt64() + offset);

                MemoryReadResult read = reader.Read(at, length);
                if (read.Ok)
                {
                    taken[at] = read.Bytes;
                    total += length;
                }

                offset += length;
            }
        }

        bytes = total;
        return taken;
    }

    /// <summary>Compares a snapshot against the process as it is now.</summary>
    [SupportedOSPlatform("windows")]
    public static List<SweepWord> Changed(
        ProcessMemoryReader reader, IReadOnlyDictionary<IntPtr, byte[]> ready, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ready);

        var changed = new List<SweepWord>();
        foreach ((IntPtr at, byte[] before) in ready)
        {
            MemoryReadResult now = reader.Read(at, before.Length);
            if (!now.Ok)
                continue;

            CollectChanged(at, before, now.Bytes, changed);
            if (changed.Count >= MaxChanged)
            {
                truncated = true;
                return changed;
            }
        }

        truncated = false;
        return changed;
    }

    private const int ChunkSize = 64 * 1024;

    private const int ReadyTimeoutSeconds = 90;
    private const int PollMs = 250;

    /// <summary>How long the quiet control watches for a word that will not sit still.</summary>
    public const int QuietSeconds = 20;

    private const int QuietSampleMs = 400;

    /// <summary>Samples the candidates repeatedly while nothing is happening.</summary>
    [SupportedOSPlatform("windows")]
    private static List<SweepWord> Quiet(ClientMemorySession session, List<SweepWord> candidates)
    {
        Func<IntPtr, uint?> read = address =>
        {
            MemoryReadResult result = session.Reader.Read(address, sizeof(uint));
            return result.Ok ? BitConverter.ToUInt32(result.Bytes) : null;
        };

        List<SweepWord> alive = candidates;
        DateTime deadline = DateTime.UtcNow.AddSeconds(QuietSeconds);
        var samples = 0;

        while (DateTime.UtcNow < deadline && alive.Count > 1)
        {
            alive = KeepStill(alive, read);
            samples++;
            Thread.Sleep(QuietSampleMs);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  campionate {samples} volte"));
        return alive;
    }

    /// <summary>Two rounds of use-and-restore over the whole process.</summary>
    [SupportedOSPlatform("windows")]
    public static int Run(int slot, string? runtimeUrl = null)
    {
        if (slot < 0)
        {
            Console.WriteLine($"[REFUSED] {Flag} <slot> vuole lo slot con cui il filo numera l'abilita'.");
            return 2;
        }

        runtimeUrl ??= TargetChainProbe.DefaultRuntimeUrl;

        // The wire is checked before the client is touched, so a missing second
        // source costs nothing but a message.
        if (Fetch(runtimeUrl) is null)
        {
            Console.WriteLine($"[REFUSED] {WireUnreachableReason}");
            Console.WriteLine("Avvia il runtime con --observe-game <ip>:<porta> in una console elevata.");
            return 1;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] client_not_readable:{attachFailure}");
            return 1;
        }

        using (session)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"=== cooldown slot {slot}, tutta la memoria privata ==="));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session!.ProcessId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"filo:   {runtimeUrl}"));
            Console.WriteLine();
            Console.WriteLine("Due giri. In ognuno: stato di riposo, poi usi l'abilita', poi il filo");
            Console.WriteLine("annuncia il ripristino. Tengo le parole che si muovono e tornano.");
            Console.WriteLine();

            List<SweepWord>? previous = null;
            for (var round = 1; round <= 2; round++)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"--- giro {round}/2 ---"));

                if (!TryRound(session, slot, runtimeUrl, out List<SweepWord> survivors, out string? why))
                {
                    Console.WriteLine($"[REFUSED] {why}");
                    return 1;
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  tornate al valore di riposo: {survivors.Count}"));

                previous = previous is null ? survivors : Intersect(previous, survivors);
                if (round == 2)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  sopravvissute a entrambi i giri: {previous.Count}"));
                }
            }

            List<SweepWord> found = previous ?? new List<SweepWord>();

            // The negative control, and the half the first version was missing.
            // Two active rounds left 8265 survivors on a live client because
            // "moved and came back" admits everything that churns.
            if (found.Count > 1)
            {
                Console.WriteLine();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"--- controllo negativo: NON usare lo slot {slot} per {QuietSeconds}s ---"));
                Console.WriteLine("  INVIO per cominciare, poi tieni le mani ferme:");
                if (Console.ReadLine() is null)
                {
                    Console.WriteLine("[REFUSED] sweep_aborted");
                    return 1;
                }

                found = Quiet(session, found);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  mai mosse mentre non succedeva nulla: {found.Count}"));
            }

            // The second filter, discarding a different population than the quiet
            // control: churn is scattered, a cooldown belongs to a table.
            if (found.Count > 1)
            {
                List<SweepWord> inTable = KeepInSkillTable(found);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  con vicini a 0x{SkillStride:X} (tabella abilita'): {inTable.Count}"));

                if (inTable.Count == 0)
                {
                    // Saying which filter emptied the set is the useful half: it
                    // means the table hypothesis is wrong here, not that nothing
                    // behaved like a cooldown.
                    Console.WriteLine();
                    Console.WriteLine($"NON stabilito: {NoTableReason}");
                    Console.WriteLine("Parole che si comportano da cooldown ce ne sono, ma nessuna sta in una");
                    Console.WriteLine("tabella a passo 0x48. Su questa build le abilita' non sono disposte");
                    Console.WriteLine("come la fonte terza descrive, e il passo va ricavato, non assunto.");
                    return 1;
                }

                found = inTable;
            }

            Console.WriteLine();

            if (Verdict(found) is { } verdict)
            {
                Console.WriteLine($"NON stabilito: {verdict}");
                foreach (SweepWord word in found.Take(20))
                    Console.WriteLine($"    {word.Describe()}");
                if (found.Count > 20)
                    Console.WriteLine($"    … e altre {found.Count - 20}");
                return 1;
            }

            SweepWord single = found[0];
            Console.WriteLine($"STABILITO: {single.Describe()}");
            Console.WriteLine("Una parola che si e' mossa quando hai usato l'abilita' ed e' tornata");
            Console.WriteLine("esattamente al valore di riposo quando il filo ha annunciato il");
            Console.WriteLine("ripristino, due volte. Resta da ripetere dopo un riavvio del client.");
            Console.WriteLine();

            return PointerAnchorHunter.Report(
                session, single.Address, PointerAnchorHunter.DefaultSpan, "cooldown");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryRound(
        ClientMemorySession session,
        int slot,
        string runtimeUrl,
        out List<SweepWord> survivors,
        out string? failureReason)
    {
        survivors = new List<SweepWord>();

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Assicurati che lo slot {slot} sia PRONTO, poi INVIO per fotografare il riposo:"));
        if (Console.ReadLine() is null)
        {
            failureReason = "sweep_aborted";
            return false;
        }

        Dictionary<IntPtr, byte[]> resting = Snapshot(session.Reader, out long bytes);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  fotografate {bytes / (1024 * 1024)} MB in {resting.Count} blocchi"));

        DateTime usedAt = DateTime.UtcNow;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  Usa ora l'abilita' dello slot {slot}, poi INVIO:"));
        if (Console.ReadLine() is null)
        {
            failureReason = "sweep_aborted";
            return false;
        }

        List<SweepWord> changed = Changed(session.Reader, resting, out bool truncated);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  cambiate dopo l'uso: {changed.Count}"));
        if (truncated)
        {
            failureReason = TruncatedReason;
            return false;
        }

        Console.WriteLine("  Aspetto che il filo annunci il ripristino...");
        if (!TryWaitForReady(runtimeUrl, slot, usedAt, out DateTime readyAt, out failureReason))
            return false;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  ripristino visto: {readyAt:HH:mm:ss.fff}Z"));

        survivors = KeepRestored(changed, address =>
        {
            MemoryReadResult read = session.Reader.Read(address, sizeof(uint));
            return read.Ok ? BitConverter.ToUInt32(read.Bytes) : null;
        });

        failureReason = null;
        return true;
    }

    private static bool TryWaitForReady(
        string runtimeUrl, int slot, DateTime after, out DateTime observedAtUtc, out string? failureReason)
    {
        observedAtUtc = default;
        DateTime deadline = DateTime.UtcNow.AddSeconds(ReadyTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (Fetch(runtimeUrl) is { } json
                && SkillCooldownProbe.TryReadSkillReady(json, slot, out DateTime at, out _)
                && at > after)
            {
                observedAtUtc = at;
                failureReason = null;
                return true;
            }

            Thread.Sleep(PollMs);
        }

        failureReason = string.Create(CultureInfo.InvariantCulture,
            $"sweep_slot_never_announced:waited={ReadyTimeoutSeconds}s");
        return false;
    }

    private static string? Fetch(string url)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            return client.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
                                       or TaskCanceledException
                                       or InvalidOperationException)
        {
            return null;
        }
    }
}
