// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — Finding where the client keeps the selected target (C1-6)
// ============================================================================
//
// ADR-0021. The first version of this oracle constrained a word by its CONTENT:
// "it holds the id of an entity the client currently has in its scene". On the
// live client that constraint could not be evaluated at all —
// NosTaleClientLayout.SceneManagerSignature is a data pattern of mostly FF, 00
// and wildcards, it matched padding, and the pointer it produced read as
// 0xFFFFFFFF. Measured on process 27192: "entita' nella scena: 0".
//
// The constraint is now about BEHAVIOUR, and it asks nothing of anybody:
//
//     a word is a candidate only if it CHANGES exactly when the selection
//     changes, and RETURNS TO THE SAME "nobody" value every time the target
//     is cleared.
//
// That is very nearly the only field in the process that does this. A timer
// always changes; a counter only grows; a position drifts while the character
// walks; a cached id stays put. Only the selection comes back to the same value
// on every deselection and takes a new, different one on every target.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>One place that behaves the way the selected-target field behaves.</summary>
/// <param name="Anchor">The base the offset is measured from.</param>
/// <param name="Offset">Distance from that base; the address itself when heap.</param>
/// <param name="EntityId">The value read there while a target was selected.</param>
/// <param name="NobodyValue">
/// The value this word takes when the target is cleared, as observed the first time it
/// was cleared. It is per-candidate and not a global constant: the client's sentinel is
/// not known in advance and could be zero, minus one, or anything else, so it is
/// <i>measured</i> and then required to repeat.
/// </param>
public readonly record struct TargetIdHit(
    MapIdAnchorKind Anchor,
    long Offset,
    long EntityId,
    long NobodyValue)
{
    /// <summary>Whether this can be written down: an offset outlives the process, an address does not.</summary>
    public bool IsDurable => Anchor is not MapIdAnchorKind.Heap;

    public string Describe() => Anchor is MapIdAnchorKind.Heap
        ? string.Create(CultureInfo.InvariantCulture,
            $"heap 0x{Offset:X} = {EntityId} (nessuno = {NobodyValue})")
        : string.Create(CultureInfo.InvariantCulture,
            $"{MapIdAnchors.NameOf(Anchor)}+0x{Offset:X} = {EntityId} (nessuno = {NobodyValue})");
}

/// <summary>What a target-candidate file says, read back.</summary>
/// <param name="Selections">How many different selected entities the surviving set has tracked.</param>
/// <param name="Restarts">How many client restarts it has survived.</param>
/// <param name="SawCleared">Whether the target has been cleared at least once against this set.</param>
/// <param name="ProcessId">The client that produced it.</param>
public sealed record TargetIdCandidates(
    int Selections,
    int Restarts,
    bool SawCleared,
    int ProcessId,
    IReadOnlyList<TargetIdHit> Hits);

/// <summary>Where the operator's answers come from, so a hunt can be driven by a test.</summary>
public interface ITargetHuntPrompt
{
    /// <summary>
    /// Shows one instruction and waits for the operator to say they have done it.
    /// </summary>
    /// <returns>False when the operator wants to stop; the hunt then saves and exits.</returns>
    bool Confirm(string instruction);

    /// <summary>Reports progress. Separate from the instruction so a test can stay silent.</summary>
    void Report(string line);
}

/// <summary>The console the operator actually sits at.</summary>
public sealed class ConsoleTargetHuntPrompt : ITargetHuntPrompt
{
    public bool Confirm(string instruction)
    {
        Console.WriteLine();
        Console.WriteLine(instruction);
        Console.Write("  INVIO per continuare, 'x' per fermarti qui: ");
        string? typed = Console.ReadLine();
        return !string.Equals(typed?.Trim(), "x", StringComparison.OrdinalIgnoreCase);
    }

    public void Report(string line) => Console.WriteLine(line);
}

/// <summary>
/// Finds where the client keeps the id of the entity the character has selected, by
/// how that place behaves rather than by what it contains.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the content constraint was dropped.</b> It depended on reading the client's
/// scene list, and on the live build that list cannot be reached: the signature that
/// finds the scene manager is a data pattern of mostly <c>FF</c>, <c>00</c> and
/// wildcards, which matches padding as readily as it matches a structure. It found one
/// candidate and the pointer behind it read <c>0xFFFFFFFF</c> — the wildcard bytes were
/// all <c>FF</c>. The oracle was resting on a reading that was never confirmed.
/// </para>
/// <para>
/// <b>Why the behavioural constraint is stronger, not merely different.</b> The old rule
/// said "this word holds an id that exists". Millions of words hold plausible integers,
/// and every entry of the client's own entity list holds a real id — which is why a pass
/// with the target cleared was needed just to separate the selection from the list. The
/// new rule says "this word tracks the selection and returns to one particular value
/// when there is none", and the list does not do that, a timer does not do that, and a
/// remembered id does not do that.
/// </para>
/// <para>
/// <b>Why one execution instead of five.</b> The rounds now happen inside a single run,
/// with the operator alternating at the keyboard while it waits. That is not only
/// kinder: without a content filter the first round has nothing to narrow with, so it
/// keeps a snapshot of the client's private memory, and a snapshot only exists while the
/// process that took it is alive. The survivors are what reaches the file.
/// </para>
/// <para>
/// <b>The one proof that still needs two executions</b> is the restart, and necessarily
/// so: an offset that survives the client being closed and reopened is exactly what a
/// bare address is not, and there is no way to observe that without a second process.
/// </para>
/// <para>
/// <b>Where this file lives.</b> Beside <see cref="MapIdFinder"/>, reusing
/// <see cref="MapIdAnchors"/> whole — the arithmetic that turns an address into a
/// distance from a base is the valuable, tested part and there should be one of it.
/// </para>
/// </remarks>
public static class TargetIdFinder
{
    /// <summary>Where the surviving candidates are kept between executions.</summary>
    public const string CandidatePath = "data/target_candidates.txt";

    /// <summary>The file format this oracle writes and reads.</summary>
    /// <remarks>
    /// Version 1 was written by the scene-list oracle. Its rows carry no
    /// <see cref="TargetIdHit.NobodyValue"/> and were selected by a rule this code no
    /// longer applies, so they are refused rather than migrated: mixing survivors of two
    /// different proofs would produce a set nobody could describe.
    /// </remarks>
    public const int FormatVersion = 2;

    /// <summary>Different selected entities the set must track before it is proven.</summary>
    public const int RequiredSelections = 3;

    /// <summary>Times the target must be cleared and the same value return.</summary>
    /// <remarks>
    /// Two, and the second is the one that proves anything. The first clearing only
    /// <i>records</i> what the word becomes; any word that happened to change would pass
    /// it. The second requires that same value back, which is what a counter, a timer and
    /// a drifting coordinate cannot do.
    /// </remarks>
    public const int RequiredClearings = 2;

    private const int PrintLimit = 12;
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// How much private memory the first round may hold on to.
    /// </summary>
    /// <remarks>
    /// The snapshot is the raw bytes of the regions scanned — four bytes per word, not a
    /// list of pairs — so it costs what the client's private memory costs. Refusing above
    /// this is deliberate and is the opposite of what the previous version did: it capped
    /// the candidate list at twenty thousand and kept whichever words came first in
    /// address order, which is not a sample of anything.
    /// </remarks>
    public const long MaxSnapshotBytes = 768L * 1024 * 1024;

    /// <summary>
    /// How far above the measured player id a value may be and still be an entity id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This bound never decides anything, and that is why it is allowed to exist.</b>
    /// Its only job is to keep the survivor list small enough to work with. If it were
    /// wrong — if the client allocated the selected id far outside the range of the id it
    /// gave this character — the hunt would end with <i>zero</i> survivors and say so,
    /// which is a loud failure and not a wrong answer.
    /// </para>
    /// <para>
    /// It is anchored on a measurement rather than a guess: the character's own entity id,
    /// read from the player object on this very run. Ids come from one allocation scheme
    /// on one server, so a value more than two hundred and fifty-six times the one we can
    /// see is not another id from that scheme — it is a pointer, a tick count or a bit
    /// pattern. Two orders of magnitude of headroom, deliberately generous, because the
    /// cost of being too tight is losing the answer and the cost of being too loose is a
    /// longer list.
    /// </para>
    /// </remarks>
    public const long PlausibleIdCeilingFactor = 256;

    /// <summary>
    /// Whether a value could be an entity id on this build, given one that certainly is.
    /// </summary>
    /// <remarks>
    /// Applied only to values seen while a target is <i>selected</i>. The value a word
    /// takes when the target is cleared is a sentinel the client chose — zero, minus one,
    /// anything — and filtering it would throw away the candidate for holding exactly the
    /// value the proof is about to require it to repeat.
    /// </remarks>
    public static bool IsPlausibleEntityId(long value, long playerEntityId)
    {
        if (value <= 0 || playerEntityId <= 0)
            return false;

        return value <= playerEntityId * PlausibleIdCeilingFactor;
    }

    /// <summary>The ids the client currently has in its scene, from its own lists.</summary>
    /// <remarks>
    /// <b>No longer part of the oracle</b>, and kept because the Control Panel's
    /// "Attorno" view reads the same lists: knowing why it comes back empty is worth more
    /// than deleting it. On the build measured for `C1-6` it returns nothing and names
    /// <c>scene_manager_not_confirmed</c> — see ADR-0021 § 2.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static HashSet<long> SceneIds(ClientMemorySession session, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(session);

        var ids = new HashSet<long>();
        failureReason = null;

        foreach (MapEntityKind kind in new[]
                 { MapEntityKind.Player, MapEntityKind.Monster, MapEntityKind.Npc })
        {
            if (!session.TryReadEntities(kind, out IReadOnlyList<MapEntityReading> entities, out string? why))
            {
                failureReason ??= why;
                continue;
            }

            foreach (MapEntityReading entity in entities)
                ids.Add(entity.EntityId);
        }

        if (ids.Count > 0)
            failureReason = null;

        return ids;
    }

    // ------------------------------------------------------------- the narrowing rules

    /// <summary>
    /// Keeps the candidates that moved to a new, plausible id for this selection.
    /// </summary>
    /// <param name="read">
    /// The value at a candidate now, or null when it could not be read. Injected so the
    /// rules can be exercised without a client.
    /// </param>
    /// <remarks>
    /// Three requirements, and each removes a different impostor. The value must have
    /// <i>changed</i> — a word frozen on one id while the selection moved is a memory of
    /// an entity, not the selection. It must differ from the recorded sentinel — a word
    /// that goes back to "nobody" while a target is selected is not tracking it. And it
    /// must be a plausible id, which is the only place the measured ceiling is used.
    /// </remarks>
    public static List<TargetIdHit> NarrowOnSelection(
        IReadOnlyList<TargetIdHit> previous,
        Func<TargetIdHit, long?> read,
        long playerEntityId,
        bool sentinelKnown)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(read);

        var kept = new List<TargetIdHit>(previous.Count);
        foreach (TargetIdHit hit in previous)
        {
            if (read(hit) is not { } value)
                continue;

            if (value == hit.EntityId)
                continue;

            if (sentinelKnown && value == hit.NobodyValue)
                continue;

            if (!IsPlausibleEntityId(value, playerEntityId))
                continue;

            kept.Add(hit with { EntityId = value });
        }

        return kept;
    }

    /// <summary>
    /// Keeps the candidates that left the selected value when the target was cleared —
    /// and, once a sentinel is known, only those that came back to exactly it.
    /// </summary>
    /// <param name="sentinelKnown">
    /// False on the first clearing, which <i>records</i> what each word becomes; true
    /// afterwards, when the recorded value has to repeat. The distinction is the whole
    /// strength of the rule: recording proves nothing, repeating proves a great deal.
    /// </param>
    public static List<TargetIdHit> NarrowOnCleared(
        IReadOnlyList<TargetIdHit> previous,
        Func<TargetIdHit, long?> read,
        bool sentinelKnown)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(read);

        var kept = new List<TargetIdHit>(previous.Count);
        foreach (TargetIdHit hit in previous)
        {
            if (read(hit) is not { } value)
                continue;

            // Whatever it becomes, it must stop holding the entity that was selected.
            if (value == hit.EntityId)
                continue;

            if (sentinelKnown)
            {
                if (value != hit.NobodyValue)
                    continue;

                kept.Add(hit);
                continue;
            }

            kept.Add(hit with { NobodyValue = value });
        }

        return kept;
    }

    /// <summary>Whether the set may be written down as the answer.</summary>
    /// <remarks>
    /// One candidate, anchored to something that survives a restart, seen to follow
    /// several different selections and to return to the same sentinel more than once.
    /// The restart is what separates an offset from an address that worked, exactly as it
    /// did for the map id.
    /// </remarks>
    public static bool Proven(IReadOnlyList<TargetIdHit> hits, int selections, int restarts, bool sawCleared)
    {
        ArgumentNullException.ThrowIfNull(hits);
        return hits.Count == 1
            && hits[0].IsDurable
            && selections >= RequiredSelections
            && restarts >= 1
            && sawCleared;
    }

    /// <summary>The one thing to do next, given where the hunt has got to.</summary>
    public static string Advice(int count, int durable, int selections, int restarts, bool sawCleared)
    {
        if (count == 0)
        {
            return "Nessun candidato e' sopravvissuto. Non e' un guasto: vuol dire che nessuna "
                 + "parola si e' comportata come la selezione. Rilancia il giro; se si ripete, "
                 + "il campo non e' un intero a 32 bit, oppure non torna sempre allo stesso "
                 + "valore quando togli il bersaglio.";
        }

        if (!sawCleared)
        {
            return "Il giro non ha mai visto il bersaglio tolto. Rilancia e arriva almeno fino "
                 + "alla seconda deselezione: e' quella che chiede al valore di TORNARE, ed e' "
                 + "l'unica che un contatore non sa fare.";
        }

        if (selections < RequiredSelections)
        {
            string left = (RequiredSelections - selections).ToString(CultureInfo.InvariantCulture);
            return $"Mancano {left} selezioni diverse. Rilancia e alterna su mostri diversi: un "
                 + "valore fermo mentre la selezione cambia e' il ricordo di un'entita', non la "
                 + "selezione.";
        }

        if (durable == 0)
        {
            return "Il superstite e' un indirizzo nudo, non una distanza da una base che il "
                 + "runtime ritrova: al riavvio del client non vuol dire piu' niente. Riavvia "
                 + "il client, rientra con lo stesso personaggio e rilancia.";
        }

        if (restarts < 1)
        {
            return "Chiudi NosTale, riaprilo, rientra con lo stesso personaggio e rilancia questo "
                 + "comando. Il giro riparte dai superstiti, non da una scansione nuova, e il "
                 + "riavvio e' l'unica prova che vuole due esecuzioni: un offset lo supera, un "
                 + "indirizzo no.";
        }

        if (count > 1)
        {
            string n = count.ToString(CultureInfo.InvariantCulture);
            return $"Restano {n} candidati che hanno superato tutto. Rilancia: ogni giro di "
                 + "selezioni e deselezioni ne toglie ancora.";
        }

        return "TROVATO. Un candidato ancorato, che ha seguito piu' selezioni, e' tornato allo "
             + "stesso valore ogni volta che il bersaglio e' stato tolto, ed e' sopravvissuto a "
             + "un riavvio. Scrivilo in NosTaleClientLayout come il codice mappa.";
    }

    // ------------------------------------------------------------------ the hunt itself

    /// <summary>Runs one interactive hunt and reports where it stands.</summary>
    /// <remarks>
    /// The operator alternates while this runs. Nothing here reads a pixel, measures a
    /// rectangle or asks anybody to aim: the only thing a person supplies is that they
    /// have selected something, or cleared it, and that is one keypress.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static int Run(string? candidatePath = null, ITargetHuntPrompt? prompt = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        prompt ??= new ConsoleTargetHuntPrompt();

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            prompt.Report($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryResolveBases(out IntPtr manager, out IntPtr playerObject, out string? baseFailure))
            {
                prompt.Report($"[REFUSED] {baseFailure}");
                return 1;
            }

            // The measurement the ceiling is anchored on. Without it there is no defensible
            // bound at all, so the hunt refuses rather than inventing one.
            if (!session.TryReadPlayer(out PlayerObjectReading player, out string? playerFailure)
                || player.EntityId <= 0)
            {
                prompt.Report($"[REFUSED] player_entity_id_unreadable:{playerFailure ?? "non_positive"}");
                prompt.Report("  L'id del personaggio e' l'unico id di entita' che questa build ci");
                prompt.Report("  mostra con certezza. Senza, il limite di plausibilita' sarebbe");
                prompt.Report("  inventato, e un limite inventato puo' escludere la risposta.");
                return 1;
            }

            var anchors = new MapIdAnchors(
                session.ModuleBase.ToInt64(), session.ModuleSize, manager.ToInt64(), playerObject.ToInt64());

            long ceiling = player.EntityId * PlausibleIdCeilingFactor;
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"process={session.ProcessId} module=0x{anchors.ModuleBase:X} manager=0x{anchors.PlayerManager:X}"));
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"id del personaggio: {player.EntityId} [LIVE] - limite di plausibilita': {ceiling}"));

            // Context, not a gate. The scene list is no longer the oracle; when it is
            // unreadable that is worth printing and worth nothing else.
            HashSet<long> scene = SceneIds(session, out string? sceneFailure);
            prompt.Report(scene.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"lista entita' del client: {scene.Count} (informativa)")
                : $"lista entita' del client: non leggibile ({sceneFailure ?? "vuota"}) - non serve piu' a questo comando");

            candidatePath ??= CandidatePath;
            TargetIdCandidates? previous = TryLoad(candidatePath, out string? loadNote);
            if (loadNote is not null)
                prompt.Report($"[NOTA] {loadNote}");

            bool sameProcess = previous is not null && previous.ProcessId == session.ProcessId;
            long Read(TargetIdHit hit) =>
                anchors.TryResolve(new MapIdHit(hit.Anchor, hit.Offset, 0), out long address)
                    ? ReadInt32(session.Reader, address) ?? long.MinValue
                    : long.MinValue;

            long? ReadOrNull(TargetIdHit hit)
            {
                long value = Read(hit);
                return value == long.MinValue ? null : value;
            }

            List<TargetIdHit> hits;
            int selections;
            int restarts;
            bool sawCleared;
            bool sentinelKnown;

            if (previous is not null && previous.Hits.Count > 0)
            {
                IReadOnlyList<TargetIdHit> carried = sameProcess ? previous.Hits : Durable(previous.Hits);
                string note = sameProcess ? string.Empty : " (indirizzi nudi caduti col processo)";
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"riporto {carried.Count} candidati dall'esecuzione precedente{note}"));

                hits = new List<TargetIdHit>(carried);
                selections = previous.Selections;
                restarts = previous.Restarts + (sameProcess ? 0 : 1);
                sawCleared = previous.SawCleared;
                sentinelKnown = sawCleared;
            }
            else
            {
                if (!prompt.Confirm(
                    "1) Seleziona un mostro nel client, poi torna qui.\n"
                  + "   Questa prima fotografia tiene la memoria privata del client: serve\n"
                  + "   un bersaglio selezionato perche' il valore da cui si parte sia il suo."))
                {
                    prompt.Report("Fermato prima della prima fotografia. Niente da salvare.");
                    return 1;
                }

                if (!TrySnapshot(session.Reader, anchors, out MemorySnapshot snapshot, out string? snapshotFailure))
                {
                    prompt.Report($"[REFUSED] {snapshotFailure}");
                    return 1;
                }

                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"fotografia: {snapshot.Regions} regioni, {snapshot.Bytes / (1024 * 1024)} MiB"));

                if (!prompt.Confirm(
                    "2) Ora TOGLI il bersaglio (ESC, oppure clicca a vuoto), poi torna qui.\n"
                  + "   Questa passata tiene solo le parole che sono CAMBIATE, e registra il\n"
                  + "   valore che ognuna prende quando non c'e' nessun bersaglio."))
                {
                    prompt.Report("Fermato prima della prima deselezione. Niente da salvare.");
                    return 1;
                }

                hits = FirstNarrowing(session.Reader, anchors, snapshot, player.EntityId, out int changed);
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"parole cambiate e plausibili: {changed} - candidati: {hits.Count}"));

                selections = 1;
                restarts = 0;
                sawCleared = true;
                sentinelKnown = true;
            }

            // From here every round is cheap: the survivor list is small, and the rounds
            // alternate for as long as the operator keeps going.
            int clearings = sawCleared ? 1 : 0;
            while (hits.Count > 0)
            {
                if (!prompt.Confirm(string.Create(CultureInfo.InvariantCulture,
                        $"3) Seleziona un mostro DIVERSO dai precedenti, poi torna qui. "
                      + $"({hits.Count} candidati, {selections}/{RequiredSelections} selezioni)")))
                {
                    break;
                }

                hits = NarrowOnSelection(hits, ReadOrNull, player.EntityId, sentinelKnown);
                selections++;
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"   dopo la selezione: {hits.Count} candidati"));

                if (hits.Count == 0)
                    break;

                if (!prompt.Confirm(string.Create(CultureInfo.InvariantCulture,
                        $"4) TOGLI di nuovo il bersaglio, poi torna qui. "
                      + $"({hits.Count} candidati) - e' la passata che chiede al valore di TORNARE")))
                {
                    break;
                }

                hits = NarrowOnCleared(hits, ReadOrNull, sentinelKnown);
                clearings++;
                sawCleared = true;
                sentinelKnown = true;
                prompt.Report(string.Create(CultureInfo.InvariantCulture,
                    $"   dopo la deselezione: {hits.Count} candidati"));

                if (selections >= RequiredSelections && clearings >= RequiredClearings && hits.Count <= 1)
                    break;
            }

            int durable = 0;
            foreach (TargetIdHit hit in hits)
            {
                if (hit.IsDurable)
                    durable++;
            }

            prompt.Report(string.Empty);
            prompt.Report(string.Create(CultureInfo.InvariantCulture,
                $"candidati: {hits.Count} ({durable} ancorati) - selezioni {selections}/{RequiredSelections}, "
              + $"deselezioni {clearings}/{RequiredClearings}, riavvii {restarts}/1"));

            int shown = 0;
            foreach (TargetIdHit hit in hits)
            {
                if (shown++ >= PrintLimit)
                {
                    prompt.Report("  ...");
                    break;
                }

                prompt.Report($"  {hit.Describe()}");
            }

            Save(candidatePath, new TargetIdCandidates(selections, restarts, sawCleared, session.ProcessId, hits), prompt);

            prompt.Report(string.Empty);
            prompt.Report(Advice(hits.Count, durable, selections, restarts, sawCleared));

            return Proven(hits, selections, restarts, sawCleared) ? 0 : 1;
        }
    }

    // ------------------------------------------------------------------ the snapshot

    /// <summary>The raw bytes of the regions worth watching, and where each came from.</summary>
    /// <remarks>
    /// Raw bytes rather than a list of (address, value) pairs: four bytes per word instead
    /// of sixteen, which is the difference between holding the client's private memory and
    /// holding four times it.
    /// </remarks>
    private readonly struct MemorySnapshot
    {
        public MemorySnapshot(List<(long Base, byte[] Bytes)> chunks, int regions, long bytes)
        {
            Chunks = chunks;
            Regions = regions;
            Bytes = bytes;
        }

        public List<(long Base, byte[] Bytes)> Chunks { get; }
        public int Regions { get; }
        public long Bytes { get; }
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySnapshot(
        ProcessMemoryReader reader,
        in MapIdAnchors anchors,
        out MemorySnapshot snapshot,
        out string? failureReason)
    {
        var chunks = new List<(long Base, byte[] Bytes)>();
        var regions = 0;
        long total = 0;
        failureReason = null;

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            if (!region.IsPrivate && !(region.IsWritable && MapIdFinder.InMainModule(region, anchors)))
                continue;

            regions++;
            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                var address = new IntPtr(region.BaseAddress.ToInt64() + offset);
                MemoryReadResult read = reader.Read(address, length);
                if (read.Ok && read.Bytes.Length >= sizeof(int))
                {
                    total += read.Bytes.Length;
                    if (total > MaxSnapshotBytes)
                    {
                        snapshot = default;
                        failureReason = string.Create(CultureInfo.InvariantCulture,
                            $"snapshot_too_large:{total / (1024 * 1024)}MiB_over_{MaxSnapshotBytes / (1024 * 1024)}MiB");
                        return false;
                    }

                    chunks.Add((address.ToInt64(), read.Bytes));
                }

                offset += length;
            }
        }

        snapshot = new MemorySnapshot(chunks, regions, total);
        return chunks.Count > 0;
    }

    /// <summary>
    /// Compares the snapshot against memory now and keeps the words that changed.
    /// </summary>
    /// <remarks>
    /// This is the only round that touches every word, and it is where the two filters
    /// meet: a word is kept if its <i>snapshot</i> value was a plausible id — it was taken
    /// with a target selected — and its value now is different. What it is now becomes the
    /// candidate's sentinel, to be required again at the next clearing.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static List<TargetIdHit> FirstNarrowing(
        ProcessMemoryReader reader,
        in MapIdAnchors anchors,
        in MemorySnapshot snapshot,
        long playerEntityId,
        out int changed)
    {
        var found = new List<TargetIdHit>();
        changed = 0;

        foreach ((long baseAddress, byte[] before) in snapshot.Chunks)
        {
            MemoryReadResult read = reader.Read(new IntPtr(baseAddress), before.Length);
            if (!read.Ok || read.Bytes.Length != before.Length)
                continue;

            ReadOnlySpan<byte> now = read.Bytes;
            for (int i = 0; i + 4 <= before.Length; i += 4)
            {
                long selected = BitConverter.ToInt32(before, i);
                if (!IsPlausibleEntityId(selected, playerEntityId))
                    continue;

                long cleared = BitConverter.ToInt32(now.Slice(i, 4));
                if (cleared == selected)
                    continue;

                changed++;
                MapIdHit anchored = anchors.Anchor(baseAddress + i, 0);
                found.Add(new TargetIdHit(anchored.Anchor, anchored.Offset, selected, cleared));
            }
        }

        return found;
    }

    private static List<TargetIdHit> Durable(IReadOnlyList<TargetIdHit> hits)
    {
        var kept = new List<TargetIdHit>(hits.Count);
        foreach (TargetIdHit hit in hits)
        {
            if (hit.IsDurable)
                kept.Add(hit);
        }

        return kept;
    }

    [SupportedOSPlatform("windows")]
    private static long? ReadInt32(ProcessMemoryReader reader, long address)
    {
        MemoryReadResult read = reader.Read(new IntPtr(address), sizeof(int));
        return read.Ok && read.Bytes.Length == sizeof(int)
            ? BitConverter.ToInt32(read.Bytes, 0)
            : null;
    }

    // ------------------------------------------------------------------ persistence

    internal static TargetIdCandidates? TryLoad(string path, out string? note)
    {
        note = null;
        if (!File.Exists(path))
            return null;

        try
        {
            string[] lines = File.ReadAllLines(path);
            int selections = 0, restarts = 0, processId = 0, version = 1;
            bool sawCleared = false;
            var hits = new List<TargetIdHit>();

            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#'))
                    continue;

                if (text.StartsWith("version=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                else if (text.StartsWith("selections=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out selections);
                else if (text.StartsWith("restarts=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out restarts);
                else if (text.StartsWith("process=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out processId);
                else if (text.StartsWith("cleared=", StringComparison.Ordinal))
                    sawCleared = text.AsSpan(8).SequenceEqual("1");
                else if (TryParseHit(text, out TargetIdHit hit))
                    hits.Add(hit);
            }

            if (version < FormatVersion)
            {
                string found = version.ToString(CultureInfo.InvariantCulture);
                note = $"{path} e' della versione {found}: l'ha scritto l'oracolo della lista scena, "
                     + "che non esiste piu'. Riparto da una fotografia nuova invece di mescolare due prove.";
                return null;
            }

            return new TargetIdCandidates(selections, restarts, sawCleared, processId, hits);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = $"{path} non leggibile ({ex.GetType().Name}); riparto da una fotografia nuova.";
            return null;
        }
    }

    internal static bool TryParseHit(string line, out TargetIdHit hit)
    {
        hit = default;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        MapIdAnchorKind? anchor = MapIdAnchors.Parse(parts[0]);
        if (anchor is not { } kind)
            return false;

        if (!long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long entityId)
            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long nobody))
        {
            return false;
        }

        hit = new TargetIdHit(kind, offset, entityId, nobody);
        return true;
    }

    internal static string Format(TargetIdCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var text = new System.Text.StringBuilder();
        text.AppendLine("# nosai target-id candidates (ADR-0021, behavioural oracle)");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"version={FormatVersion}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"selections={candidates.Selections}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"restarts={candidates.Restarts}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"process={candidates.ProcessId}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cleared={(candidates.SawCleared ? 1 : 0)}"));
        foreach (TargetIdHit hit in candidates.Hits)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{MapIdAnchors.NameOf(hit.Anchor)} {hit.Offset:X} {hit.EntityId} {hit.NobodyValue}"));
        }

        return text.ToString();
    }

    private static void Save(string path, TargetIdCandidates candidates, ITargetHuntPrompt prompt)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, Format(candidates));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            prompt.Report($"[WARN] non ho potuto scrivere {path}: {ex.GetType().Name}");
        }
    }
}
