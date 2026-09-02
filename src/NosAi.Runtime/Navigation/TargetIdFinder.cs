using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>One place that currently holds a plausible target id, and what it is measured from.</summary>
/// <param name="Anchor">The base the offset is measured from.</param>
/// <param name="Offset">Distance from that base; the address itself when heap.</param>
/// <param name="EntityId">The value read there on the pass that recorded it.</param>
public readonly record struct TargetIdHit(MapIdAnchorKind Anchor, long Offset, long EntityId)
{
    /// <summary>Whether this can be written down: an offset outlives the process, an address does not.</summary>
    public bool IsDurable => Anchor is not MapIdAnchorKind.Heap;

    public string Describe() => Anchor is MapIdAnchorKind.Heap
        ? string.Create(CultureInfo.InvariantCulture, $"heap 0x{Offset:X} = {EntityId}")
        : string.Create(CultureInfo.InvariantCulture,
            $"{MapIdAnchors.NameOf(Anchor)}+0x{Offset:X} = {EntityId}");
}

/// <summary>What a target-candidate file says, read back.</summary>
/// <param name="Passes">How many different selected entities the surviving set has tracked.</param>
/// <param name="Restarts">How many client restarts it has survived.</param>
/// <param name="SawCleared">Whether a pass with no target has ever run against this set.</param>
/// <param name="ProcessId">The client that produced it.</param>
public sealed record TargetIdCandidates(
    int Passes,
    int Restarts,
    bool SawCleared,
    int ProcessId,
    IReadOnlyList<TargetIdHit> Hits);

/// <summary>
/// Finds where the client keeps the id of the entity the character has selected,
/// using the client's own scene list as the oracle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an oracle and not a calibration.</b> `ADR-0021`. `HasTarget` used to come
/// from the screen, which meant a human measured the target frame's rectangle once
/// per resolution, and a rectangle measured wrong does not fail — it reports a
/// confident <i>no target</i> every frame. An entity id has no pixels, so reading
/// it from memory is resolution-independent by construction rather than by effort.
/// </para>
/// <para>
/// <b>The constraint that does the work.</b> A word is a candidate only while it
/// holds the id of an entity <i>the client currently has in its scene</i>, and the
/// scene list is itself read from memory: the oracle needs no traffic capture, no
/// endpoint and no driver. This is `MapIdFinder`'s shape — there a word was a
/// candidate only while it named a grid that contained the character.
/// </para>
/// <para>
/// <b>The pass that removes the scene list itself.</b> Every entry of the client's
/// own entity list holds a scene id, so a selected-target pass alone cannot tell
/// the selection from the list. A pass taken with <i>no</i> target selected can:
/// the selection leaves the set, the list does not. That is why
/// <see cref="TargetIdCandidates.SawCleared"/> is part of the proof and not a
/// convenience.
/// </para>
/// <para>
/// <b>Where this file lives.</b> Beside <see cref="MapIdFinder"/>, in the same
/// namespace, because it reuses <see cref="MapIdAnchors"/> whole — the arithmetic
/// that turns an address into a distance from a base is the valuable, tested part
/// and there should be one of it. The anchor types still say « MapId » in their
/// names; renaming them is a mechanical change across two files and belongs to its
/// own commit, not to this one.
/// </para>
/// </remarks>
public static class TargetIdFinder
{
    /// <summary>Where the surviving candidates are kept between passes.</summary>
    public const string CandidatePath = "data/target_candidates.txt";

    /// <summary>Different selected entities the set must track before it is proven.</summary>
    public const int RequiredPasses = 3;

    private const int PrintLimit = 12;
    private const int ChunkSize = 64 * 1024;
    private const int MaxCandidates = 20_000;

    /// <summary>The ids the client currently has in its scene, from its own lists.</summary>
    /// <remarks>
    /// Every kind, because a target may be a monster, an NPC or a player, and a set
    /// that omitted one would discard the right word on the pass that selected it.
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
                // One unreadable list is not a failure of the pass: the others still
                // constrain. It becomes one only when nothing at all could be read,
                // because then the set is empty and would discard every candidate.
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

    /// <summary>
    /// Keeps the candidates that still hold what this pass requires of them.
    /// </summary>
    /// <param name="targetSelected">
    /// Whether the operator had a target selected while this pass ran. With one,
    /// the value must name an entity in the scene; without one, it must not —
    /// which is what tells the selection apart from the scene list.
    /// </param>
    [SupportedOSPlatform("windows")]
    public static List<TargetIdHit> Narrow(
        IReadOnlyList<TargetIdHit> previous,
        ProcessMemoryReader reader,
        in MapIdAnchors anchors,
        IReadOnlySet<long> scene,
        bool targetSelected)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(scene);

        var kept = new List<TargetIdHit>(previous.Count);
        foreach (TargetIdHit hit in previous)
        {
            if (!anchors.TryResolve(new MapIdHit(hit.Anchor, hit.Offset, 0), out long address))
                continue;

            if (ReadInt32(reader, address) is not { } value)
                continue;

            bool namesAnEntity = scene.Contains(value);
            if (namesAnEntity != targetSelected)
                continue;

            // A pass with a target must also see the value MOVE to whatever is
            // selected now. A word frozen on one id across two different
            // selections is a memory of an entity, not the selection.
            if (targetSelected && value == hit.EntityId && hit.EntityId != 0)
                continue;

            kept.Add(hit with { EntityId = targetSelected ? value : 0 });
        }

        return kept;
    }

    /// <summary>Whether the set may be written down as the answer.</summary>
    /// <remarks>
    /// One candidate, anchored to something that survives a restart, tracked across
    /// several different selections, and seen to leave the scene set once. The
    /// restart is what separates an offset from an address that worked, exactly as
    /// it did for the map id.
    /// </remarks>
    public static bool Proven(IReadOnlyList<TargetIdHit> hits, int passes, int restarts, bool sawCleared)
    {
        ArgumentNullException.ThrowIfNull(hits);
        return hits.Count == 1
            && hits[0].IsDurable
            && passes >= RequiredPasses
            && restarts >= 1
            && sawCleared;
    }

    /// <summary>The one thing to do next, given where the hunt has got to.</summary>
    public static string Advice(int count, int durable, int passes, int restarts, bool sawCleared)
    {
        if (count == 0)
        {
            return "Nessun candidato e' sopravvissuto. Rilancia con un bersaglio selezionato: "
                 + "si riparte da una scansione pulita. Se si ripete, il campo non e' un intero "
                 + "a 32 bit dove lo stiamo cercando.";
        }

        if (!sawCleared)
        {
            return "Ora TOGLI il bersaglio e rilancia dichiarando che non ce n'e' uno. "
                 + "E' la passata che distingue la selezione dalla lista delle entita': la "
                 + "selezione esce dall'insieme, la lista no.";
        }

        if (passes < RequiredPasses)
        {
            string left = (RequiredPasses - passes).ToString(CultureInfo.InvariantCulture);
            return $"Seleziona un mostro DIVERSO e rilancia. Mancano {left} selezioni diverse: "
                 + "un valore fermo su un id mentre la selezione cambia e' il ricordo di "
                 + "un'entita', non la selezione.";
        }

        if (durable == 0)
        {
            return "Il superstite e' un indirizzo nudo, non una distanza da una base che il "
                 + "runtime ritrova: al riavvio del client non vuol dire piu' niente. Riavvia "
                 + "il client, rientra con lo stesso personaggio e rilancia.";
        }

        if (restarts < 1)
        {
            return "Chiudi NosTale, riaprilo, rientra con lo stesso personaggio, seleziona un "
                 + "mostro e rilancia. Un offset sopravvive al riavvio, un indirizzo no.";
        }

        if (count > 1)
        {
            string n = count.ToString(CultureInfo.InvariantCulture);
            return $"Restano {n} candidati che hanno superato tutto. Continua ad alternare "
                 + "bersagli diversi e passate senza bersaglio: ogni giro ne toglie.";
        }

        return "TROVATO. Un candidato ancorato, che ha seguito piu' selezioni, e' uscito "
             + "dall'insieme quando il bersaglio e' stato tolto, ed e' sopravvissuto a un "
             + "riavvio. Scrivilo in NosTaleClientLayout come il codice mappa.";
    }

    /// <summary>Runs one pass and reports where the hunt stands.</summary>
    /// <param name="targetSelected">
    /// What the operator declares about this instant. It is the only thing a person
    /// supplies, it is one keypress, and it is not a measurement: nothing here reads
    /// a pixel or asks anybody to aim.
    /// </param>
    [SupportedOSPlatform("windows")]
    public static int Run(bool targetSelected, string? candidatePath = null)
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
            if (!session!.TryResolveBases(out IntPtr manager, out IntPtr playerObject, out string? baseFailure))
            {
                Console.WriteLine($"[REFUSED] {baseFailure}");
                return 1;
            }

            var anchors = new MapIdAnchors(
                session.ModuleBase.ToInt64(), session.ModuleSize, manager.ToInt64(), playerObject.ToInt64());

            HashSet<long> scene = SceneIds(session, out string? sceneFailure);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"process={session.ProcessId} module=0x{anchors.ModuleBase:X} manager=0x{anchors.PlayerManager:X}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"entita' nella scena: {scene.Count}"));
            Console.WriteLine($"bersaglio dichiarato: {(targetSelected ? "selezionato" : "nessuno")}");

            if (scene.Count == 0)
            {
                Console.WriteLine($"[REFUSED] scene_unreadable:{sceneFailure ?? "empty"}");
                Console.WriteLine("  Senza la lista delle entita' non c'e' oracolo: ogni parola");
                Console.WriteLine("  sarebbe plausibile, e la passata non toglierebbe nulla.");
                return 1;
            }

            candidatePath ??= CandidatePath;
            TargetIdCandidates? previous = TryLoad(candidatePath);
            bool sameProcess = previous is not null && previous.ProcessId == session.ProcessId;

            List<TargetIdHit> hits;
            int passes;
            int restarts;
            bool sawCleared;

            if (previous is not null && previous.Hits.Count > 0)
            {
                // A different process means the bare addresses are gone with it, and
                // that loss is the restart proof rather than an accident.
                IReadOnlyList<TargetIdHit> carried = sameProcess
                    ? previous.Hits
                    : Durable(previous.Hits);

                string carriedNote = sameProcess
                    ? string.Empty
                    : " (indirizzi nudi caduti col processo)";
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"riporto {carried.Count} candidati dalla passata precedente{carriedNote}"));

                hits = Narrow(carried, session.Reader, anchors, scene, targetSelected);
                passes = previous.Passes + (targetSelected ? 1 : 0);
                restarts = previous.Restarts + (sameProcess ? 0 : 1);
                sawCleared = previous.SawCleared || !targetSelected;
            }
            else
            {
                if (!targetSelected)
                {
                    Console.WriteLine("[REFUSED] first_pass_needs_a_target");
                    Console.WriteLine("  La prima passata deve avere un bersaglio selezionato: parte da");
                    Console.WriteLine("  cio' che nomina un'entita', e senza bersaglio non nomina nulla.");
                    return 1;
                }

                hits = Scan(session.Reader, scene, anchors, out int regions, out long bytes);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"scansione: {regions} regioni, {bytes / 1024} KiB"));
                passes = 1;
                restarts = 0;
                sawCleared = false;
            }

            int durable = 0;
            foreach (TargetIdHit hit in hits)
            {
                if (hit.IsDurable)
                    durable++;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"candidati: {hits.Count} ({durable} ancorati) - selezioni seguite {passes}/{RequiredPasses}, " +
                $"riavvii {restarts}/1, passata senza bersaglio: {(sawCleared ? "fatta" : "MANCA")}"));

            int shown = 0;
            foreach (TargetIdHit hit in hits)
            {
                if (shown++ >= PrintLimit)
                {
                    Console.WriteLine("  ...");
                    break;
                }

                Console.WriteLine($"  {hit.Describe()}");
            }

            Save(candidatePath, new TargetIdCandidates(passes, restarts, sawCleared, session.ProcessId, hits));

            Console.WriteLine();
            Console.WriteLine(Advice(hits.Count, durable, passes, restarts, sawCleared));

            return Proven(hits, passes, restarts, sawCleared) ? 0 : 1;
        }
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
    private static List<TargetIdHit> Scan(
        ProcessMemoryReader reader,
        IReadOnlySet<long> scene,
        MapIdAnchors anchors,
        out int regions,
        out long bytes)
    {
        var found = new List<TargetIdHit>();
        regions = 0;
        bytes = 0;

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
                if (!read.Ok)
                {
                    offset += length;
                    continue;
                }

                bytes += length;
                ReadOnlySpan<byte> window = read.Bytes;
                for (int i = 0; i + 4 <= window.Length; i += 4)
                {
                    long value = BitConverter.ToInt32(window.Slice(i, 4));
                    if (!scene.Contains(value))
                        continue;

                    MapIdHit anchored = anchors.Anchor(address.ToInt64() + i, 0);
                    found.Add(new TargetIdHit(anchored.Anchor, anchored.Offset, value));
                    if (found.Count >= MaxCandidates)
                        return found;
                }

                offset += length;
            }
        }

        return found;
    }

    [SupportedOSPlatform("windows")]
    private static long? ReadInt32(ProcessMemoryReader reader, long address)
    {
        MemoryReadResult read = reader.Read(new IntPtr(address), sizeof(int));
        return read.Ok && read.Bytes.Length == sizeof(int)
            ? BitConverter.ToInt32(read.Bytes, 0)
            : null;
    }

    internal static TargetIdCandidates? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            string[] lines = File.ReadAllLines(path);
            int passes = 0, restarts = 0, processId = 0;
            bool sawCleared = false;
            var hits = new List<TargetIdHit>();

            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#'))
                    continue;

                if (text.StartsWith("passes=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out passes);
                else if (text.StartsWith("restarts=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out restarts);
                else if (text.StartsWith("process=", StringComparison.Ordinal))
                    int.TryParse(text.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out processId);
                else if (text.StartsWith("cleared=", StringComparison.Ordinal))
                    sawCleared = text.AsSpan(8).SequenceEqual("1");
                else if (TryParseHit(text, out TargetIdHit hit))
                    hits.Add(hit);
            }

            return new TargetIdCandidates(passes, restarts, sawCleared, processId, hits);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool TryParseHit(string line, out TargetIdHit hit)
    {
        hit = default;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        MapIdAnchorKind? anchor = MapIdAnchors.Parse(parts[0]);
        if (anchor is not { } kind)
            return false;

        if (!long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long entityId))
        {
            return false;
        }

        hit = new TargetIdHit(kind, offset, entityId);
        return true;
    }

    internal static string Format(TargetIdCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var text = new System.Text.StringBuilder();
        text.AppendLine("# nosai target-id candidates (ADR-0021)");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"passes={candidates.Passes}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"restarts={candidates.Restarts}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"process={candidates.ProcessId}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cleared={(candidates.SawCleared ? 1 : 0)}"));
        foreach (TargetIdHit hit in candidates.Hits)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{MapIdAnchors.NameOf(hit.Anchor)} {hit.Offset:X} {hit.EntityId}"));
        }

        return text.ToString();
    }

    private static void Save(string path, TargetIdCandidates candidates)
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
            Console.WriteLine($"[WARN] non ho potuto scrivere {path}: {ex.GetType().Name}");
        }
    }
}
