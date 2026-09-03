using System.Globalization;
using System.Text.Json;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>Where the operator's answers come from, so a hunt can be driven by a test.</summary>
public interface ISkillHuntPrompt
{
    /// <summary>Asks the operator to do something and waits until they say they did.</summary>
    bool Confirm(string instruction);

    /// <summary>Says something to the operator.</summary>
    void Report(string line);
}

/// <summary>The console the operator actually sits at.</summary>
public sealed class ConsoleSkillHuntPrompt : ISkillHuntPrompt
{
    /// <inheritdoc />
    public bool Confirm(string instruction)
    {
        Console.WriteLine(instruction);
        Console.Write("INVIO quando fatto, 'x' per fermarsi: ");
        string? typed = Console.ReadLine();
        return !string.Equals(typed?.Trim(), "x", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Report(string line) => Console.WriteLine(line);
}

/// <summary>
/// Drives <see cref="SkillCooldownFinder"/> against the running client, with the
/// operator using the skill and the wire saying when it came back (phase 3 of
/// <c>docs/SPEC_ESTENSIONE_LAYOUT_MEMORIA.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the operator presses the key and the wire is still the evidence.</b> The
/// wire's <c>su</c> reports a hit and its target; it does not name the slot, so
/// nothing on the wire says <i>which</i> skill was used. The operator does that part,
/// exactly as they select and clear a target for <see cref="TargetIdFinder"/>. What
/// the operator never supplies is the moment the skill came <i>back</i>: that is
/// <c>sr</c>, off the wire, and it is the half the whole oracle is anchored to. An
/// operator who mistimes their keypress produces no candidate; they cannot produce a
/// wrong one, because the falls are checked against a clock they do not control.
/// </para>
/// <para>
/// <b>Where it looks.</b> A window from each base the client's own layout already
/// resolves — player manager and player object — so a survivor is a distance from a
/// base rather than an address, which is the difference between an offset and
/// something that worked once. The candidate map's RVA is not read here, and neither
/// is its 0x48 stride: the stride is reported afterwards from what the offsets show.
/// </para>
/// <para>
/// <b>What it refuses.</b> No wire, no hunt. Without <c>sr</c> there is no
/// independent clock and the falls would be checked against the operator's own
/// timing, which is the same source as the presses — one source wearing two hats.
/// It reports <see cref="WireUnreachableReason"/> and stops rather than degrading
/// into a single-source hunt whose output would look identical.
/// </para>
/// </remarks>
public static class SkillCooldownProbe
{
    /// <summary>The command line this probe answers to.</summary>
    public const string Flag = "--skill-cooldowns";

    /// <summary>Where the runtime publishes what the wire has said.</summary>
    public const string DefaultRuntimeUrl = "http://127.0.0.1:8766/api/gate1";

    /// <summary>How many bytes above each resolved base are sampled.</summary>
    /// <remarks>
    /// A window, not a scan of the process: the spec says to search from the bases
    /// that are already resolved, and a cooldown table for the character's own skills
    /// is part of the character's own structures. Widening this is cheap and is the
    /// first thing to try if a hunt ends with no candidate.
    /// </remarks>
    public const int WindowBytes = 8192;

    /// <summary>Reported when the client cannot be read at all.</summary>
    public const string ClientNotReadable = "client_not_readable";

    /// <summary>Reported when the runtime is not publishing the wire.</summary>
    public const string WireUnreachableReason = "wire_unreachable_no_second_source";

    /// <summary>Reported when the wire is up but has never named this slot.</summary>
    public const string SlotNeverAnnouncedReason = "slot_never_announced_on_wire";

    /// <summary>How long to wait for the wire to announce the restoration.</summary>
    public const int ReadyTimeoutSeconds = 90;

    private const int PollMs = 250;

    /// <summary>
    /// Reads the moment the wire last said this slot came off cooldown.
    /// </summary>
    /// <remarks>
    /// Pure, so the whole wire side can be driven by a test. The shape is the one
    /// <c>GameplayProvider</c> publishes: <c>gameplayBaseline.skillsReady.value[]</c>
    /// with <c>slot</c> and <c>observedAtUtc</c>.
    /// </remarks>
    public static bool TryReadSkillReady(
        string json, int slot, out DateTime observedAtUtc, out string? failureReason)
    {
        observedAtUtc = default;
        failureReason = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("gameplayBaseline", out JsonElement baseline)
                || baseline.ValueKind != JsonValueKind.Object)
            {
                failureReason = "gameplay_provider_not_available";
                return false;
            }

            if (!baseline.TryGetProperty("skillsReady", out JsonElement node)
                || node.ValueKind != JsonValueKind.Object
                || !node.TryGetProperty("value", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                failureReason = SlotNeverAnnouncedReason;
                return false;
            }

            var found = false;
            DateTime latest = default;
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("slot", out JsonElement slotNode)
                    || !slotNode.TryGetInt32(out int entrySlot)
                    || entrySlot != slot
                    || !entry.TryGetProperty("observedAtUtc", out JsonElement atNode)
                    || atNode.ValueKind != JsonValueKind.String
                    || !DateTime.TryParse(
                        atNode.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out DateTime at))
                {
                    continue;
                }

                if (!found || at > latest)
                {
                    latest = at;
                    found = true;
                }
            }

            if (!found)
            {
                failureReason = SlotNeverAnnouncedReason;
                return false;
            }

            observedAtUtc = latest;
            return true;
        }
        catch (JsonException)
        {
            failureReason = "wire_json_malformed";
            return false;
        }
    }

    /// <summary>
    /// Turns a block of bytes read at a base into the word map the finder consumes.
    /// </summary>
    /// <remarks>
    /// Offsets are distances from <paramref name="anchorOffset"/>, so two windows read
    /// from two different bases can be fed to one finder without their offsets
    /// colliding — the caller separates them by giving each window its own span.
    /// </remarks>
    public static Dictionary<int, uint> WordsFrom(ReadOnlySpan<byte> bytes, int anchorOffset = 0)
    {
        var words = new Dictionary<int, uint>(bytes.Length / sizeof(uint));
        for (var i = 0; i + sizeof(uint) <= bytes.Length; i += sizeof(uint))
            words[anchorOffset + i] = BitConverter.ToUInt32(bytes[i..]);

        return words;
    }

    /// <summary>Console entry for <c>--skill-cooldowns</c>.</summary>
    public static int Run(int slot, string? runtimeUrl = null, ISkillHuntPrompt? prompt = null)
    {
        prompt ??= new ConsoleSkillHuntPrompt();
        runtimeUrl ??= Environment.GetEnvironmentVariable("NOSAI_RUNTIME_URL") is { Length: > 0 } fromEnv
            ? fromEnv.TrimEnd('/') + "/api/gate1"
            : DefaultRuntimeUrl;

        // The second source is checked before anything is read from the client. A
        // hunt that discovers halfway through that it has no clock has already asked
        // the operator to play for nothing.
        if (TryFetch(runtimeUrl, out string? probeJson) is { } wireDown)
        {
            prompt.Report($"[REFUSED] {WireUnreachableReason}:{wireDown}");
            prompt.Report("Avvia il runtime con --observe-game <ip>:<porta> in una console elevata.");
            prompt.Report("Senza sr dal filo le discese verrebbero controllate sul tuo stesso");
            prompt.Report("cronometro, che e' la sorgente che preme i tasti: una sola, con due cappelli.");
            return 1;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? failure))
        {
            prompt.Report($"[REFUSED] {ClientNotReadable}:{failure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryResolveBases(out IntPtr manager, out IntPtr playerObject, out string? baseFailure))
            {
                prompt.Report($"[REFUSED] {baseFailure}");
                return 1;
            }

            var finder = new SkillCooldownFinder();

            if (!TrySample(session, manager, playerObject, out Dictionary<int, uint> first, out string? sampleFailure))
            {
                prompt.Report($"[REFUSED] {sampleFailure}");
                return 1;
            }

            finder.Observe(first);
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"=== cooldown, slot {slot} (candidati; UNKNOWN finche' non converge) ==="));
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session.ProcessId}, finestre di {WindowBytes} byte da manager e player object"));
            prompt.Report(string.Create(CultureInfo.InvariantCulture, $"filo:   {runtimeUrl}"));
            prompt.Report("");

            _ = TryReadSkillReady(probeJson!, slot, out DateTime lastReady, out _);

            for (var round = 1; round <= SkillCooldownFinder.RequiredReadies; round++)
            {
                if (!prompt.Confirm(string.Create(CultureInfo.InvariantCulture,
                    $"[{round}/{SkillCooldownFinder.RequiredReadies}] Usa l'abilita' dello slot {slot}.")))
                {
                    prompt.Report("Interrotto dall'operatore.");
                    break;
                }

                if (!TrySample(session, manager, playerObject, out Dictionary<int, uint> used, out sampleFailure))
                {
                    prompt.Report($"[REFUSED] {sampleFailure}");
                    return 1;
                }

                finder.NoteUsed(slot, used);

                prompt.Report("Aspetto che il filo annunci il ripristino...");
                if (!WaitForReady(runtimeUrl, slot, lastReady, out DateTime seenAt, out string? waitFailure))
                {
                    prompt.Report($"[REFUSED] {waitFailure}");
                    prompt.Report("Nessun sr per questo slot: o l'abilita' non e' stata usata, o il");
                    prompt.Report("filo non la annuncia. Nessuna delle due si risolve indovinando.");
                    return 1;
                }

                if (!TrySample(session, manager, playerObject, out Dictionary<int, uint> ready, out sampleFailure))
                {
                    prompt.Report($"[REFUSED] {sampleFailure}");
                    return 1;
                }

                finder.NoteReady(slot, ready);
                lastReady = seenAt;

                SkillCooldownVerdict interim = finder.Verdict(slot);
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"  ripristino visto: {seenAt:HH:mm:ss.fff}Z — sopravvissuti: {interim.Survivors.Count}"));
            }

            return Report(finder, slot, prompt);
        }
    }

    /// <summary>Prints the verdict, and says what it does not know.</summary>
    private static int Report(SkillCooldownFinder finder, int slot, ISkillHuntPrompt prompt)
    {
        SkillCooldownVerdict verdict = finder.Verdict(slot);
        prompt.Report("");

        switch (verdict.Outcome)
        {
            case SkillCooldownOutcome.Established:
                prompt.Report($"STABILITO: {verdict.Single!.Value.Describe()}");
                prompt.Report("Una parola che scende quando il filo dice « pronta » e risale quando");
                prompt.Report("l'abilita' viene usata, due volte. Resta da ripetere dopo un riavvio");
                prompt.Report("del client: un offset che non sopravvive al riavvio e' un indirizzo.");
                if (finder.ObservedStride() is { } stride)
                {
                    prompt.Report(string.Create(CultureInfo.InvariantCulture,
                        $"Passo misurato fra slot stabiliti: 0x{stride:X} (misurato, non assunto)."));
                }

                return 0;

            case SkillCooldownOutcome.Ambiguous:
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"NON stabilito: {verdict.Reason}"));
                foreach (SkillCooldownHit hit in verdict.Survivors)
                    prompt.Report("  " + hit.Describe());

                prompt.Report("Piu' parole si comportano allo stesso modo. Sceglierne una sarebbe");
                prompt.Report("preferenza, non misura: rilancia con altri giri per separarle.");
                return 1;

            case SkillCooldownOutcome.NoCandidate:
                prompt.Report($"NON stabilito: {verdict.Reason}");
                prompt.Report("Niente in queste finestre si comporta come un cooldown. Il campo resta");
                prompt.Report("UNKNOWN e la fase si chiude comunque: un cooldown ignoto e' onesto,");
                prompt.Report("uno sbagliato fa proporre azioni che il Verify scopre fallite.");
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"Se lo cerchi ancora, la prima cosa da allargare e' WindowBytes ({WindowBytes})."));
                return 1;

            default:
                prompt.Report($"NON stabilito: {verdict.Reason}");
                return 1;
        }
    }

    /// <summary>Reads both windows and merges them into one word map.</summary>
    private static bool TrySample(
        ClientMemorySession session,
        IntPtr manager,
        IntPtr playerObject,
        out Dictionary<int, uint> words,
        out string? failureReason)
    {
        words = [];

        MemoryReadResult fromManager = session.Reader.Read(manager, WindowBytes);
        if (fromManager.Bytes is not { } managerBytes)
        {
            failureReason = $"window_unreadable:manager:{fromManager.FailureReason}";
            return false;
        }

        MemoryReadResult fromPlayer = session.Reader.Read(playerObject, WindowBytes);
        if (fromPlayer.Bytes is not { } playerBytes)
        {
            failureReason = $"window_unreadable:player_object:{fromPlayer.FailureReason}";
            return false;
        }

        // The player-object window is offset past the manager window so the two
        // cannot collide in one map. The survivor's offset is decoded back against
        // whichever base it fell in, which is what keeps it a distance and not an
        // address.
        words = WordsFrom(managerBytes);
        foreach (KeyValuePair<int, uint> pair in WordsFrom(playerBytes, WindowBytes))
            words[pair.Key] = pair.Value;

        failureReason = null;
        return true;
    }

    /// <summary>Polls the wire until this slot is announced later than it last was.</summary>
    private static bool WaitForReady(
        string runtimeUrl, int slot, DateTime after, out DateTime observedAtUtc, out string? failureReason)
    {
        observedAtUtc = default;
        failureReason = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(ReadyTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (TryFetch(runtimeUrl, out string? json) is null
                && json is not null
                && TryReadSkillReady(json, slot, out DateTime at, out _)
                && at > after)
            {
                observedAtUtc = at;
                return true;
            }

            Thread.Sleep(PollMs);
        }

        failureReason = string.Create(CultureInfo.InvariantCulture,
            $"{SlotNeverAnnouncedReason}:waited={ReadyTimeoutSeconds}s");
        return false;
    }

    private static string? TryFetch(string url, out string? json)
    {
        json = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            json = client.GetStringAsync(url).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ex.GetType().Name;
        }
    }
}
