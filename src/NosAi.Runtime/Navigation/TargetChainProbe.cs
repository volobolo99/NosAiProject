// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — The target chain, and the proof it still needs (C1-6)
// ============================================================================
//
// The behavioural oracle established manager+0x44 and nothing else. What that
// word holds is a POINTER to the selected entity's object, so HasTarget is
// settled — non-zero is a target, zero is none — and the entity's identity is
// not: [pointer]+0x08 being the id is an analogy with the player object, and an
// analogy is not a measurement.
//
// This command is where the analogy is put to the test the project asks for
// everywhere: two independent sources agreeing on one number. The client's
// memory says one thing; `ct` on the wire says another; they either coincide or
// the identity stays UNKNOWN.

using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>Whether the target's identity may be treated as established, and why not.</summary>
/// <param name="Established">True only when both sources produced the same id.</param>
/// <param name="Reason">Named. Null exactly when established.</param>
/// <param name="MemoryId">What the memory chain read, or null.</param>
/// <param name="WireId">What <c>ct</c> named, or null.</param>
public readonly record struct TargetChainVerdict(
    bool Established,
    string? Reason,
    long? MemoryId,
    long? WireId);

/// <summary>
/// Prints the chain from the player manager to the selected entity, and compares the id
/// it produces against the one the wire named.
/// </summary>
public static class TargetChainProbe
{
    /// <summary>The runtime's operator API, where the wire's answer is published.</summary>
    public const string DefaultRuntimeUrl = "http://127.0.0.1:8766/api/gate1";

    /// <summary>Reported when nothing is selected, so there is no identity to establish.</summary>
    public const string NoTargetReason = "no_target_selected";

    /// <summary>Reported when the word behind the pointer could not be read.</summary>
    public const string MemoryIdUnreadableReason = "target_entity_id_unreadable";

    /// <summary>Reported when the wire has not named a selected entity.</summary>
    public const string WireSilentReason = "wire_target_not_observed";

    /// <summary>Reported when the two sources produced different numbers.</summary>
    public const string DisagreeReason = "target_sources_disagree";

    /// <summary>Reported when the memory id is nowhere near the ids this build allocates.</summary>
    public const string ImplausibleReason = "target_entity_id_implausible";

    /// <summary>
    /// How far above the character's own id a value may be and still be believed as one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tighter than the hunt's bound, on purpose, because the question is the
    /// opposite one.</b> <see cref="TargetIdFinder.PlausibleIdCeilingFactor"/> is 256 and
    /// deliberately generous: there the bound only has to keep a list of millions
    /// workable, and being too tight would <i>lose the answer</i>. Here there is one
    /// candidate and the decision is whether to believe it, so being too loose would
    /// <i>publish a pointer as an id</i> — which is precisely what nearly happened.
    /// </para>
    /// <para>
    /// The number that made this necessary: <c>0x22C8A4F0</c> is 583 574 768, and the
    /// hunt's 256× ceiling admits everything below 881 463 552. It would have passed. A
    /// check that lets through the exact value it exists to catch is decoration.
    /// </para>
    /// <para>
    /// Sixteen, from the two ids this build has actually shown: the character's own
    /// (3 443 217) and one off the wire (313 906) — a spread of about eleven. A factor of
    /// sixteen covers that spread with room and still sits an order of magnitude below
    /// any pointer in this process. It is a bound from two samples and it is written down
    /// as such; if a real id ever exceeds it the command says <c>implausible</c> and
    /// prints the number, which is a visible refusal rather than a wrong answer.
    /// </para>
    /// </remarks>
    public const long IdentityCeilingFactor = 16;

    /// <summary>Whether one candidate number belongs to the family of ids this build allocates.</summary>
    public static bool IsPlausibleIdentity(long value, long playerEntityId) =>
        value > 0 && playerEntityId > 0 && value <= playerEntityId * IdentityCeilingFactor;

    /// <summary>
    /// The comparison, with no I/O in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions, and they check different things. <b>Plausibility</b> asks whether
    /// the number belongs to the family of ids this build allocates, measured against the
    /// character's own — it is what would have caught the pointer being mistaken for an
    /// id, since the pointer sits three orders of magnitude away. <b>Agreement</b> asks
    /// whether a second, independent source says the same thing, which is what the map id
    /// required and what nothing else can substitute for.
    /// </para>
    /// <para>
    /// A number that is plausible and unconfirmed is exactly what this project refuses to
    /// publish: it looks right, and looking right is not evidence.
    /// </para>
    /// </remarks>
    public static TargetChainVerdict Compare(
        int? memoryId,
        string? memoryFailure,
        long? wireId,
        string? wireFailure,
        long playerEntityId)
    {
        if (memoryId is not { } fromMemory)
            return new TargetChainVerdict(false, memoryFailure ?? MemoryIdUnreadableReason, null, wireId);

        if (!IsPlausibleIdentity(fromMemory, playerEntityId))
        {
            return new TargetChainVerdict(
                false,
                string.Create(CultureInfo.InvariantCulture, $"{ImplausibleReason}:{fromMemory}"),
                fromMemory,
                wireId);
        }

        if (wireId is not { } fromWire)
            return new TargetChainVerdict(false, wireFailure ?? WireSilentReason, fromMemory, null);

        if (fromMemory != fromWire)
        {
            return new TargetChainVerdict(
                false,
                string.Create(CultureInfo.InvariantCulture, $"{DisagreeReason}:{fromMemory}_vs_{fromWire}"),
                fromMemory,
                fromWire);
        }

        return new TargetChainVerdict(true, null, fromMemory, fromWire);
    }

    /// <summary>Pulls the entity id <c>ct</c> named, from the runtime's operator API.</summary>
    /// <remarks>
    /// Machine-read on purpose. The operator could be asked to type what they saw, and
    /// that would make a human the second source for a number whose whole point is that
    /// no human measured it.
    /// </remarks>
    public static bool TryReadWireTarget(string json, out long entityId, out string? failureReason)
    {
        entityId = 0;
        failureReason = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!NosAi.Runtime.Observability.OperatorApiSnapshot.TryGameplayBaseline(
                    document.RootElement, out JsonElement baseline, out failureReason))
            {
                return false;
            }

            if (!NosAi.Runtime.Observability.OperatorApiSnapshot.TryField(
                    baseline, "selectedTarget", out JsonElement value, out string? fieldReason))
            {
                // The field's own reason says why the wire had no target, which is
                // more use than a generic silence.
                failureReason = fieldReason ?? WireSilentReason;
                return false;
            }

            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("entityId", out JsonElement id)
                || !id.TryGetInt64(out entityId))
            {
                failureReason = WireSilentReason;
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (JsonException ex)
        {
            failureReason = $"operator_api_unparsable:{ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>Reads the chain, compares it with the wire, and prints both.</summary>
    [SupportedOSPlatform("windows")]
    public static int Run(string? runtimeUrl = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? playerFailure))
            {
                Console.WriteLine($"[REFUSED] player_unreadable:{playerFailure}");
                return 1;
            }

            if (!session.TryReadTarget(out TargetPointerReading target, out string? targetFailure))
            {
                Console.WriteLine($"[REFUSED] target_chain_unreadable:{targetFailure}");
                return 1;
            }

            Console.WriteLine("=== Catena del bersaglio ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"process                 = {session.ProcessId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"manager                 = 0x{target.PlayerManager.ToInt64():X}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"id personaggio          = {player.EntityId} [LIVE]"));

            string pointer = target.HasTarget
                ? string.Create(CultureInfo.InvariantCulture, $"0x{target.TargetObject.ToInt64():X}")
                : "0 (nessun bersaglio)";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"manager+0x{NosTaleClientLayout.TargetPointerOffset:X}            = {pointer}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"HasTarget               = {(target.HasTarget ? "true" : "false")} [DERIVED]"));

            if (!target.HasTarget)
            {
                Console.WriteLine();
                Console.WriteLine("Nessun bersaglio selezionato: HasTarget e' gia' stabilito, l'identita' non");
                Console.WriteLine("ha nulla da stabilire. Seleziona un mostro e rilancia.");
                return 1;
            }

            string candidate = target.CandidateEntityId is { } id
                ? id.ToString(CultureInfo.InvariantCulture)
                : $"non leggibile ({target.CandidateFailureReason})";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[bersaglio]+0x{NosTaleClientLayout.EntityIdOffset:X}         = {candidate}  <- IPOTESI"));

            runtimeUrl ??= Environment.GetEnvironmentVariable("NOSAI_RUNTIME_URL") is { Length: > 0 } fromEnv
                ? fromEnv.TrimEnd('/') + "/api/gate1"
                : DefaultRuntimeUrl;

            long? wireId = null;
            string? wireFailure = TryFetch(runtimeUrl, out string? json);
            if (wireFailure is null && json is not null)
            {
                if (TryReadWireTarget(json, out long fromWire, out wireFailure))
                    wireId = fromWire;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ct sul filo             = {(wireId is { } w ? w.ToString(CultureInfo.InvariantCulture) : $"non disponibile ({wireFailure})")}"));

            TargetChainVerdict verdict = Compare(
                target.CandidateEntityId, target.CandidateFailureReason, wireId, wireFailure, player.EntityId);

            Console.WriteLine();
            if (verdict.Established)
            {
                Console.WriteLine("STABILITO. Le due sorgenti indipendenti dicono lo stesso numero, e il numero");
                Console.WriteLine("sta nella famiglia degli id di questa build. L'identita' del bersaglio puo'");
                Console.WriteLine("smettere di essere un'ipotesi: scrivi l'offset dove sta il codice mappa.");
                return 0;
            }

            Console.WriteLine($"NON stabilito: {verdict.Reason}");
            Console.WriteLine(Advice(verdict));
            return 1;
        }
    }

    /// <summary>The one thing to do next, given how the comparison failed.</summary>
    public static string Advice(TargetChainVerdict verdict)
    {
        if (verdict.Reason is null)
            return "Nulla da fare: e' stabilito.";

        if (verdict.Reason.StartsWith(DisagreeReason, StringComparison.Ordinal))
        {
            return "Le due sorgenti non concordano. NON scrivere l'offset: e' esattamente il caso\n"
                 + "che la doppia sorgente esiste per prendere. Rilancia con un bersaglio fermo e\n"
                 + "senza cambiarlo fra le due letture; se persiste, [bersaglio]+0x08 non e' l'id.";
        }

        if (verdict.Reason.StartsWith(ImplausibleReason, StringComparison.Ordinal))
        {
            return "Il numero letto non e' nella famiglia degli id di questa build — e' cosi' che il\n"
                 + "puntatore era stato scambiato per un id. L'analogia col giocatore non regge:\n"
                 + "l'id del bersaglio non e' a +0x08 sul suo oggetto.";
        }

        if (verdict.Reason.StartsWith(WireSilentReason, StringComparison.Ordinal)
            || verdict.Reason.Contains("gameplay_provider", StringComparison.Ordinal))
        {
            return "Manca la seconda sorgente. Avvia il runtime con --observe-game e la console\n"
                 + "elevata, seleziona il bersaglio col mouse (e' il gesto che manda ct), e rilancia:\n"
                 + "senza il filo l'identita' resta un'ipotesi, e un'ipotesi non si scrive nel layout.";
        }

        return "La catena in memoria non ha prodotto un numero. HasTarget resta comunque\n"
             + "stabilito: sapere CHE c'e' un bersaglio non aspetta di sapere QUALE.";
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
            return $"operator_api_unreachable:{ex.GetType().Name}";
        }
    }
}
