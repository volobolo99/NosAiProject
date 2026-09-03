using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.LowLevel;

/// <summary>Where the operator's answers come from, so a run can be driven by a test.</summary>
public interface IKeybindConfirmPrompt
{
    /// <summary>Asks whether to press this intent's key now. False skips it.</summary>
    bool ShouldPress(string intent, string label);

    /// <summary>Says something to the operator.</summary>
    void Report(string line);
}

/// <summary>The console the operator actually sits at.</summary>
public sealed class ConsoleKeybindConfirmPrompt : IKeybindConfirmPrompt
{
    /// <inheritdoc />
    public bool ShouldPress(string intent, string label)
    {
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{intent}  (tasto: {label}) - fuori dal combattimento."));
        Console.Write("INVIO per premerlo ora, 'x' per saltarlo: ");
        string? typed = Console.ReadLine();
        return !string.Equals(typed?.Trim(), "x", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Report(string line) => Console.WriteLine(line);
}

/// <summary>
/// Presses a declared keybind and classifies what changed, per
/// <c>docs/TASTI_E_BERSAGLIO.md</c> § 3. This is the one path allowed to write
/// <c>data/keybinds.json</c>: everywhere else the file is the operator's and is
/// only ever read.
/// </summary>
/// <remarks>
/// <para>
/// <b>The runtime discovers its own keys instead of receiving them.</b> A
/// catalogue can say "1 is usually the HP potion"; only pressing it and watching
/// what the client does can say it is true <i>here</i>. Confirming does not
/// change what a key is bound to — <c>KeybindMap</c> still owns that — it only
/// changes whether <see cref="InputActionEffector"/> is willing to press it.
/// </para>
/// <para>
/// <b>Why the wire and not memory.</b> HP and MP are also readable from memory
/// (<c>PlayerVitalsProbe</c>), but that reading is itself an unconfirmed
/// candidate today (phase 2 of the memory-layout extension: <c>Integrated</c>,
/// not <c>Verified</c>). Using an unconfirmed candidate to confirm something
/// else would make the confirmation only as trustworthy as the candidate. The
/// wire's vitals are the one source this project already calls
/// <c>Verified</c>, so this probe reads only that, and refuses outright when it
/// cannot be reached rather than falling back to memory.
/// </para>
/// <para>
/// <b>What is never attempted.</b> Only <c>consumable.*</c> and <c>skill.*</c>
/// intents are pressed. Interface intents (<c>ui.*</c>) have no observable
/// effect this runtime can read yet — <c>C3-3</c> — and pressing one to look for
/// nothing would be exactly the false confidence this whole path exists to
/// avoid.
/// </para>
/// </remarks>
public static class KeybindConfirmProbe
{
    /// <summary>The command line this probe answers to.</summary>
    public const string Flag = "--keybinds-confirm";

    /// <summary>Where the runtime publishes what the wire has said.</summary>
    public const string DefaultRuntimeUrl = "http://127.0.0.1:8766/api/gate1";

    /// <summary>Reported when the runtime is not publishing the wire.</summary>
    public const string WireUnreachableReason = "wire_unreachable_no_second_source";

    /// <summary>Reported when the client cannot be read at all.</summary>
    public const string ClientNotReadable = "client_not_readable";

    /// <summary>Reported when the game window is not the focused one.</summary>
    public const string ClientNotFocusedReason = "client_window_not_focused";

    /// <summary>Reported when an intent's name does not match a pressable prefix.</summary>
    public const string UnsupportedIntentReason = "keybind_confirm_unsupported_intent";

    /// <summary>How long to keep sampling the wire for an effect after the press.</summary>
    private const int RefocusGraceMs = 3000;
    private const int ObservationWindowMs = 2500;
    private const int PollIntervalMs = 200;
    private const int KeyPressMs = 80;

    /// <summary>Console entry for <c>--keybinds-confirm [intent]</c>.</summary>
    public static int Run(string? onlyIntent = null, string? runtimeUrl = null, IKeybindConfirmPrompt? prompt = null)
    {
        prompt ??= new ConsoleKeybindConfirmPrompt();
        runtimeUrl ??= Environment.GetEnvironmentVariable("NOSAI_RUNTIME_URL") is { Length: > 0 } fromEnv
            ? fromEnv.TrimEnd('/') + "/api/gate1"
            : DefaultRuntimeUrl;

        string path = KeybindMap.RelativePath;
        if (!KeybindMap.TryLoad(path, out KeybindMap map, out string? loadFailure))
        {
            prompt.Report($"[REFUSED] {loadFailure}");
            return 1;
        }

        List<string> targets = onlyIntent is { Length: > 0 }
            ? [onlyIntent]
            : map.ConfiguredIntents.Where(i => !IsAlreadyConfirmed(map, i)).ToList();

        if (targets.Count == 0)
        {
            prompt.Report("Niente da confermare: nessun intento dichiarato e non confermato.");
            return 0;
        }

        // Checked once, before anything is read from the client: a run that
        // discovers halfway through that it has no wire has already asked the
        // operator to press keys for nothing.
        if (TryFetch(runtimeUrl, out _) is { } wireDown)
        {
            prompt.Report($"[REFUSED] {WireUnreachableReason}:{wireDown}");
            prompt.Report("Avvia il runtime con --observe-game <ip>:<porta> in una console elevata.");
            return 1;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            prompt.Report($"[REFUSED] {ClientNotReadable}:{attachFailure}");
            return 1;
        }

        using (session)
        {
            if (ClientWindowLocator.TryFind(session!.ProcessId, out string? windowFailure) is not { } window)
            {
                prompt.Report($"[REFUSED] {windowFailure ?? "window_not_located"}");
                return 1;
            }

            var policy = new RuntimeSafetyPolicy(
                LiveInputEnabled: true,
                PacketInjectionEnabled: false,
                RequireClientHealthy: true,
                RequireGuardApproval: false);
            var input = new LowLevel.GatedInputBackend(new LowLevel.Win32InputBackend(), policy);

            var confirmedNow = new List<string>();
            var notConfirmed = new List<(string Intent, string Reason)>();
            var skipped = new List<string>();

            foreach (string intent in targets)
            {
                if (!map.TryGet(intent, out Keybind bind))
                {
                    prompt.Report($"{intent}: {UnsupportedIntentReason}:not_in_file");
                    skipped.Add(intent);
                    continue;
                }

                bool isConsumable = intent.StartsWith(KeybindsCheck.ConsumablePrefix, StringComparison.Ordinal);
                bool isSkill = intent.StartsWith(KeybindsCheck.SkillPrefix, StringComparison.Ordinal);
                if (!isConsumable && !isSkill)
                {
                    prompt.Report($"{intent}: {UnsupportedIntentReason}:no_observable_effect_yet");
                    skipped.Add(intent);
                    continue;
                }

                int? slot = isConsumable ? ParseTrailingInt(intent, KeybindsCheck.ConsumablePrefix.Length) : null;
                if (isConsumable && slot is null)
                {
                    prompt.Report($"{intent}: {UnsupportedIntentReason}:slot_not_numeric");
                    skipped.Add(intent);
                    continue;
                }

                if (!prompt.ShouldPress(intent, bind.Label))
                {
                    skipped.Add(intent);
                    continue;
                }

                // The operator just answered inside THIS console, which took the
                // focus that a moment ago belonged to the game. A key sent right
                // now would go wherever the console sits, not to NosTale, so a
                // short window is given to click back before the check runs.
                prompt.Report("  Clicca su NosTale ora...");
                Thread.Sleep(RefocusGraceMs);

                if (!IsClientFocused(window.Handle))
                {
                    // Recoverable, and specific to this one intent: the operator
                    // can simply try the next one, or this one again on a second
                    // run, rather than losing progress already made this pass.
                    prompt.Report($"  non confermato: {intent} ({ClientNotFocusedReason})");
                    notConfirmed.Add((intent, ClientNotFocusedReason));
                    continue;
                }

                KeybindConfirmResult result = isConsumable
                    ? ConfirmConsumable(runtimeUrl, input, bind, slot!.Value, prompt)
                    : ConfirmSkill(runtimeUrl, input, bind, prompt);

                if (result.Confirmed)
                {
                    confirmedNow.Add(intent);
                    prompt.Report($"  CONFERMATO: {intent}");
                }
                else
                {
                    notConfirmed.Add((intent, result.Reason!));
                    prompt.Report($"  non confermato: {intent} ({result.Reason})");
                }
            }

            if (confirmedNow.Count > 0 && !TryWriteConfirmed(path, confirmedNow, out string? writeFailure))
            {
                prompt.Report($"[REFUSED] scrittura fallita: {writeFailure}");
                prompt.Report("Nessun file e' stato toccato solo in parte: o tutti i confermati sono");
                prompt.Report("scritti, o nessuno lo e'.");
                return 1;
            }

            prompt.Report("");
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"confermati ora: {confirmedNow.Count}  non confermati: {notConfirmed.Count}  saltati: {skipped.Count}"));

            return notConfirmed.Count > 0 && confirmedNow.Count == 0 && skipped.Count == 0 ? 1 : 0;
        }
    }

    private static bool IsAlreadyConfirmed(KeybindMap map, string intent)
        => map.TryGet(intent, out Keybind bind) && bind.Confirmed;

    private static int? ParseTrailingInt(string intent, int afterIndex)
        => int.TryParse(intent.AsSpan(afterIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static KeybindConfirmResult ConfirmConsumable(
        string runtimeUrl, LowLevel.GatedInputBackend input, Keybind bind, int slot, IKeybindConfirmPrompt prompt)
    {
        ReadVitalsSample(runtimeUrl, slot, out int? beforeHp, out _, out int? beforeSlot);

        input.KeyPress(bind.VirtualKey, KeyPressMs);
        DateTime pressedAtUtc = DateTime.UtcNow;

        (int? afterHp, int? afterSlot) = SampleUntilSettled(
            () =>
            {
                ReadVitalsSample(runtimeUrl, slot, out int? hp, out _, out int? slotAmount);
                return (hp, slotAmount);
            },
            pressedAtUtc);

        return KeybindConfirmation.ClassifyConsumable(beforeHp, afterHp, beforeSlot, afterSlot);
    }

    /// <summary>Fetches one body and parses it. A failed fetch reads as no source, same as before this call existed.</summary>
    private static void ReadVitalsSample(string runtimeUrl, int? slot, out int? hp, out int? mp, out int? slotAmount)
    {
        hp = null;
        mp = null;
        slotAmount = null;
        if (TryFetch(runtimeUrl, out string? json) is null && json is not null)
            TryReadVitalsAndSlot(json, slot, out hp, out mp, out slotAmount);
    }

    private static KeybindConfirmResult ConfirmSkill(
        string runtimeUrl, LowLevel.GatedInputBackend input, Keybind bind, IKeybindConfirmPrompt prompt)
    {
        ReadVitalsSample(runtimeUrl, slot: null, out _, out int? beforeMp, out _);

        input.KeyPress(bind.VirtualKey, KeyPressMs);
        DateTime pressedAtUtc = DateTime.UtcNow;

        bool sawReady = false;
        (int? afterMp, _) = SampleUntilSettled(
            () =>
            {
                int? mp = null;
                if (TryFetch(runtimeUrl, out string? json) is null && json is not null)
                {
                    TryReadVitalsAndSlot(json, slot: null, out _, out mp, out _);
                    if (!sawReady)
                        sawReady = AnySkillReadyAfter(json, pressedAtUtc);
                }

                return (mp, (int?)null);
            },
            pressedAtUtc);

        return KeybindConfirmation.ClassifySkill(beforeMp, afterMp, sawReady);
    }

    /// <summary>
    /// Polls at a fixed interval for a fixed window and returns the last reading.
    /// There is nothing to converge toward — an absent value stays absent, a
    /// present one may still change again after this window closes — so "settled"
    /// means only that the window this probe is willing to wait has elapsed.
    /// </summary>
    private static (int? First, int? Second) SampleUntilSettled(
        Func<(int? First, int? Second)> sample, DateTime sinceUtc)
    {
        (int? First, int? Second) last = (null, null);
        DateTime deadline = sinceUtc.AddMilliseconds(ObservationWindowMs);
        while (DateTime.UtcNow < deadline)
        {
            last = sample();
            Thread.Sleep(PollIntervalMs);
        }

        return last;
    }

    /// <summary>
    /// Whether any skillsReady entry's timestamp is strictly after
    /// <paramref name="afterUtc"/>. Internal rather than private so the JSON
    /// shape can be pinned by a test without a live client, the same way
    /// <see cref="Navigation.SkillCooldownProbe.TryReadSkillReady"/> is.
    /// </summary>
    internal static bool AnySkillReadyAfter(string json, DateTime afterUtc)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("gameplayBaseline", out JsonElement baseline)
                || !baseline.TryGetProperty("skillsReady", out JsonElement node)
                || !node.TryGetProperty("value", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (entry.TryGetProperty("observedAtUtc", out JsonElement atNode)
                    && atNode.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(
                        atNode.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)
                    && at > afterUtc)
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads HP and, when <paramref name="slot"/> is given, that inventory slot's amount.</summary>
    /// <summary>
    /// Reads HP, MP and (when <paramref name="slot"/> is given) that inventory
    /// slot's amount from one <c>/api/gate1</c> body already fetched by the
    /// caller. Internal for the same reason as <see cref="AnySkillReadyAfter"/>
    /// — pure parsing, pinned against a recorded shape rather than only
    /// exercised through a live client and a live HTTP call together.
    /// </summary>
    internal static void TryReadVitalsAndSlot(
        string json, int? slot, out int? hp, out int? mp, out int? slotAmount)
    {
        hp = null;
        mp = null;
        slotAmount = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("gameplayBaseline", out JsonElement baseline))
                return;

            hp = ReadClassifiedInt(baseline, "hp");
            mp = ReadClassifiedInt(baseline, "mp");

            if (slot is { } wanted
                && baseline.TryGetProperty("inventory", out JsonElement inv)
                && inv.TryGetProperty("value", out JsonElement list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in list.EnumerateArray())
                {
                    if (entry.TryGetProperty("slot", out JsonElement slotNode)
                        && slotNode.TryGetInt32(out int entrySlot)
                        && entrySlot == wanted
                        && entry.TryGetProperty("amount", out JsonElement amountNode)
                        && amountNode.TryGetInt32(out int amount))
                    {
                        slotAmount = amount;
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static int? ReadClassifiedInt(JsonElement baseline, string property)
    {
        if (!baseline.TryGetProperty(property, out JsonElement node)
            || !node.TryGetProperty("value", out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            return null;
        }

        return result;
    }

    /// <summary>
    /// Sets <c>confirmed: true</c> for exactly the given intents and rewrites the
    /// file. Every other key — other binds, <c>_readme</c>, <c>version</c> — is
    /// carried through a mutable DOM untouched, so confirming one intent cannot
    /// silently reformat or drop another the operator wrote by hand.
    /// </summary>
    /// <summary>
    /// Internal so a test can confirm the file is rewritten as an all-or-nothing
    /// operation without exercising the rest of <see cref="Run"/>.
    /// </summary>
    internal static bool TryWriteConfirmed(string path, IReadOnlyList<string> intents, out string? failureReason)
    {
        failureReason = null;
        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonObject rootObject
                || rootObject["binds"] is not JsonObject binds)
            {
                failureReason = "binds_missing";
                return false;
            }

            foreach (string intent in intents)
            {
                if (binds[intent] is not JsonObject bind)
                {
                    failureReason = $"intent_missing:{intent}";
                    return false;
                }

                bind["confirmed"] = true;
            }

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            failureReason = $"{ex.GetType().Name}";
            return false;
        }
    }

    private static bool IsClientFocused(IntPtr handle)
        => OperatingSystem.IsWindows() && NativeMethods.GetForegroundWindow() == handle;

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

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
    }
}
