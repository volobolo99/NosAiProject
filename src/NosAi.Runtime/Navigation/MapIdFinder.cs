using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>A base the runtime resolves again on every attach.</summary>
/// <remarks>
/// Everything except <see cref="Heap"/> is re-derived from the client each time
/// the runtime attaches — the module from the loader, the two others by following
/// the signature and the chain — so a distance from one of them still names the
/// same field after the client has been restarted. <see cref="Heap"/> is the
/// absence of a base, kept because a bare address still narrows across a portal
/// inside one run of the process, and dropped the moment that process is gone.
/// </remarks>
public enum MapIdAnchorKind
{
    /// <summary>No base: the offset is an absolute address, and it dies with the process.</summary>
    Heap = 0,

    /// <summary>The client's own image, so the distance survives relocation.</summary>
    Module = 1,

    /// <summary>The player manager the signature reaches.</summary>
    PlayerManager = 2,

    /// <summary>The character's map object, which the manager points at.</summary>
    PlayerObject = 3,
}

/// <summary>One place that currently holds a plausible map id, and what it is measured from.</summary>
/// <param name="Anchor">The base the offset is measured from.</param>
/// <param name="Offset">Distance from that base; the address itself when <see cref="MapIdAnchorKind.Heap"/>.</param>
/// <param name="MapId">The value read there on the pass that recorded it.</param>
public readonly record struct MapIdHit(MapIdAnchorKind Anchor, long Offset, int MapId)
{
    /// <summary>Whether this can be written down: an offset outlives the process, an address does not.</summary>
    public bool IsDurable => Anchor is not MapIdAnchorKind.Heap;

    /// <summary>How the hit reads in a report and in the candidate file.</summary>
    public string Describe() => Anchor is MapIdAnchorKind.Heap
        ? string.Create(CultureInfo.InvariantCulture, $"heap 0x{Offset:X}")
        : string.Create(CultureInfo.InvariantCulture, $"{MapIdAnchors.NameOf(Anchor)}+0x{Offset:X}");
}

/// <summary>
/// The bases one attach resolved, and the arithmetic that turns an address found
/// by scanning into a distance from one of them.
/// </summary>
/// <remarks>
/// This is the whole difference between "where the field was in that run" and
/// "where the field is". The oracle finds an address; only a base makes it an
/// offset, and only an offset can be checked again after a restart.
/// </remarks>
public readonly record struct MapIdAnchors(
    long ModuleBase,
    long ModuleSize,
    long PlayerManager,
    long PlayerObject)
{
    /// <summary>How far past a struct's base an address is still that struct's field.</summary>
    /// <remarks>
    /// 4 KiB. The offsets already mapped on the manager are all under <c>0x40</c>;
    /// the window is far wider so a field further down the same object is still
    /// recognised, and still narrow enough that an unrelated allocation on the
    /// next page is not claimed as a field of it.
    /// </remarks>
    public const long StructWindow = 0x1000;

    /// <summary>The short name used in reports and in the candidate file.</summary>
    public static string NameOf(MapIdAnchorKind anchor) => anchor switch
    {
        MapIdAnchorKind.Module => "module",
        MapIdAnchorKind.PlayerManager => "manager",
        MapIdAnchorKind.PlayerObject => "object",
        _ => "heap",
    };

    /// <summary>The anchor named by <paramref name="text"/>, or null when it names none.</summary>
    public static MapIdAnchorKind? Parse(string text) => text switch
    {
        "module" => MapIdAnchorKind.Module,
        "manager" => MapIdAnchorKind.PlayerManager,
        "object" => MapIdAnchorKind.PlayerObject,
        "heap" => MapIdAnchorKind.Heap,
        _ => null,
    };

    /// <summary>
    /// Restates <paramref name="address"/> as a distance from the tightest base
    /// that contains it, and leaves it an address when none does.
    /// </summary>
    public MapIdHit Anchor(long address, int mapId)
    {
        if (ModuleBase > 0 && ModuleSize > 0 && address >= ModuleBase && address - ModuleBase < ModuleSize)
            return new MapIdHit(MapIdAnchorKind.Module, address - ModuleBase, mapId);

        long fromManager = Distance(PlayerManager, address);
        long fromObject = Distance(PlayerObject, address);

        // The nearer base wins: an address inside both windows belongs to
        // whichever object begins closest below it.
        if (fromManager >= 0 && (fromObject < 0 || fromManager <= fromObject))
            return new MapIdHit(MapIdAnchorKind.PlayerManager, fromManager, mapId);
        if (fromObject >= 0)
            return new MapIdHit(MapIdAnchorKind.PlayerObject, fromObject, mapId);

        return new MapIdHit(MapIdAnchorKind.Heap, address, mapId);
    }

    /// <summary>Where <paramref name="hit"/> reads in this attach, or false when nothing resolves it.</summary>
    public bool TryResolve(MapIdHit hit, out long address)
    {
        address = 0;
        switch (hit.Anchor)
        {
            case MapIdAnchorKind.Heap:
                address = hit.Offset;
                return hit.Offset > 0;
            case MapIdAnchorKind.Module:
                if (ModuleBase <= 0 || hit.Offset < 0 || hit.Offset >= ModuleSize)
                    return false;
                address = ModuleBase + hit.Offset;
                return true;
            case MapIdAnchorKind.PlayerManager:
                if (PlayerManager <= 0 || hit.Offset < 0 || hit.Offset >= StructWindow)
                    return false;
                address = PlayerManager + hit.Offset;
                return true;
            case MapIdAnchorKind.PlayerObject:
                if (PlayerObject <= 0 || hit.Offset < 0 || hit.Offset >= StructWindow)
                    return false;
                address = PlayerObject + hit.Offset;
                return true;
            default:
                return false;
        }
    }

    private static long Distance(long baseAddress, long address)
    {
        if (baseAddress <= 0)
            return -1;

        long delta = address - baseAddress;
        return delta >= 0 && delta < StructWindow ? delta : -1;
    }
}

/// <summary>What a candidate file says, read back.</summary>
/// <param name="Passes">How many different maps the surviving set has tracked.</param>
/// <param name="Restarts">How many client restarts it has survived.</param>
/// <param name="ProcessId">The client that produced it, or zero when the file predates the field.</param>
/// <param name="PlayerX">Where the character stood on that pass.</param>
/// <param name="PlayerY">Where the character stood on that pass.</param>
/// <param name="Hits">The surviving candidates.</param>
public sealed record MapIdCandidates(
    int Passes,
    int Restarts,
    int ProcessId,
    int PlayerX,
    int PlayerY,
    IReadOnlyList<MapIdHit> Hits)
{
    /// <summary>Whether the file names the process its addresses were taken in.</summary>
    public bool NamesTheProcess => ProcessId > 0;
}

/// <summary>
/// Finds the map id in the running client by using the extracted grids as an
/// oracle, not by guessing an offset.
/// </summary>
/// <remarks>
/// <para>
/// <c>+0x30</c> on the player manager was a heap pointer
/// (<c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3). Following it would be
/// guessing twice. The filter that is not a guess: a 32-bit word is a candidate
/// only while it equals the id of a <c>.grid</c> whose rectangle contains the
/// character. After a portal that word has to become a <i>different</i> such id.
/// One pass is not an answer.
/// </para>
/// <para>
/// <b>Two properties, two proofs.</b> That the word is the map id is shown by
/// crossing a portal: it has to change, and change to the id of a grid that still
/// contains the character. That what gets written down is an <i>offset</i> and
/// not an address is shown by restarting the client: a distance from a base the
/// runtime resolves again survives that, a heap address does not. A candidate
/// that has passed only the first proof is a fact about one run of one process.
/// </para>
/// <para>
/// Read-only. The candidate set lives between invocations because the method
/// needs two maps and a restart.
/// </para>
/// </remarks>
public static class MapIdFinder
{
    public const string CandidatePath = "data/mapid_candidates.txt";
    private const int PrintLimit = 12;
    private const int ChunkSize = 64 * 1024;
    private const int FormatVersion = 2;

    /// <summary>Map ids whose extracted rectangle contains <paramref name="x"/>,<paramref name="y"/>.</summary>
    public static HashSet<int> PlausibleIds(IReadOnlyList<MapGridSize> maps, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var ids = new HashSet<int>();
        foreach (MapGridSize map in maps)
        {
            if (map.Contains(x, y))
                ids.Add(map.MapId);
        }

        return ids;
    }

    /// <summary>
    /// Keeps candidates whose value, read through their own anchor, is still a
    /// plausible id — and, when a different map is being observed, is not the id
    /// they held on the previous one.
    /// </summary>
    /// <remarks>
    /// The address is recomputed from the anchor on every pass rather than
    /// remembered, because the bases move: the client replaces the manager's
    /// contents on a map change, and a field tracked by its old address would be
    /// read out of whatever occupies that memory afterwards.
    /// </remarks>
    public static List<MapIdHit> Narrow(
        IReadOnlyList<MapIdHit> previous,
        IReadOnlySet<int> plausible,
        MapIdAnchors anchors,
        Func<long, int?> read,
        bool requireChanged)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(plausible);
        ArgumentNullException.ThrowIfNull(read);

        var survivors = new List<MapIdHit>();
        foreach (MapIdHit hit in previous)
        {
            if (!anchors.TryResolve(hit, out long address))
                continue;
            if (read(address) is not { } now)
                continue;
            if (!plausible.Contains(now))
                continue;
            if (requireChanged && now == hit.MapId)
                continue;

            survivors.Add(hit with { MapId = now });
        }

        return survivors;
    }

    /// <summary>Whether both properties have been shown: it tracks maps, and it is an offset.</summary>
    public static bool Proven(IReadOnlyList<MapIdHit> hits, int passes, int restarts)
    {
        ArgumentNullException.ThrowIfNull(hits);
        return hits.Count == 1 && hits[0].IsDurable && passes >= 2 && restarts >= 1;
    }

    /// <summary>
    /// Which of a loaded file's candidates this attach may still read, and why the
    /// others were dropped.
    /// </summary>
    /// <remarks>
    /// A heap address means something only inside the process that produced it.
    /// When the file names a different process — or names none, which is the same
    /// ignorance — the addresses go and the anchored candidates stay. That drop is
    /// the restart proof doing its work, not a loss.
    /// </remarks>
    public static List<MapIdHit> Carry(MapIdCandidates previous, int processId, out string note)
    {
        ArgumentNullException.ThrowIfNull(previous);

        bool sameProcess = previous.NamesTheProcess && previous.ProcessId == processId;
        var carried = new List<MapIdHit>(previous.Hits.Count);
        int dropped = 0;
        foreach (MapIdHit hit in previous.Hits)
        {
            if (!sameProcess && !hit.IsDurable)
            {
                dropped++;
                continue;
            }

            carried.Add(hit);
        }

        if (sameProcess)
        {
            note = string.Create(CultureInfo.InvariantCulture,
                $"Same client process ({processId}): the addresses still mean what they meant.");
        }
        else if (!previous.NamesTheProcess)
        {
            note = string.Create(CultureInfo.InvariantCulture,
                $"The file does not name the process it was written in, so its {dropped} bare address(es) cannot be trusted; {carried.Count} anchored candidate(s) carried over.");
        }
        else
        {
            note = string.Create(CultureInfo.InvariantCulture,
                $"Client restarted ({previous.ProcessId} to {processId}): {dropped} bare address(es) dropped, {carried.Count} anchored candidate(s) re-resolved.");
        }

        return carried;
    }

    /// <summary>Console entry for <c>--find-mapid</c>.</summary>
    public static int Run(string? mapsDirectory = null, string? candidatePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        if (mapsDirectory is null
            && !MapGridExtractor.TryResolveDedicatedMapsDirectory(out mapsDirectory, out string? volumeReason))
        {
            Console.WriteLine($"[REFUSED] {volumeReason}");
            return 1;
        }

        if (!MapGridExtractor.TryLoadCatalog(mapsDirectory, out IReadOnlyList<MapGridSize> catalog, out string? catalogReason))
        {
            Console.WriteLine($"[REFUSED] {catalogReason}");
            return 1;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return 1;
            }

            if (!session.TryResolveBases(out IntPtr manager, out IntPtr playerObject, out string? baseFailure))
            {
                Console.WriteLine($"[REFUSED] {baseFailure}");
                return 1;
            }

            var anchors = new MapIdAnchors(
                session.ModuleBase.ToInt64(), session.ModuleSize, manager.ToInt64(), playerObject.ToInt64());

            HashSet<int> plausible = PlausibleIds(catalog, player.X, player.Y);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"process={session.ProcessId} module=0x{anchors.ModuleBase:X} manager=0x{anchors.PlayerManager:X} object=0x{anchors.PlayerObject:X}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"player={player.X},{player.Y}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"maps containing this cell: {plausible.Count} of {catalog.Count}"));

            if (plausible.Count == 0)
            {
                Console.WriteLine("[REFUSED] no_grid_contains_player");
                Console.WriteLine("  The extracted rectangles do not cover this position. The grids");
                Console.WriteLine("  and the character are not on the same map, or the coordinates");
                Console.WriteLine("  are not cell indices.");
                return 1;
            }

            candidatePath ??= CandidatePath;
            var byId = new Dictionary<int, MapGridSize>(catalog.Count);
            foreach (MapGridSize map in catalog)
                byId[map.MapId] = map;

            List<MapIdHit> hits;
            int passes;
            int restarts;
            int regions = 0;
            long bytes = 0;
            bool truncated = false;

            MapIdCandidates? previous = TryLoadCandidates(candidatePath, out MapIdCandidates? loaded)
                ? loaded
                : null;
            List<MapIdHit> carried = new();
            if (previous is not null)
            {
                carried = Carry(previous, session.ProcessId, out string note);
                Console.WriteLine(note);
            }

            if (previous is not null && carried.Count > 0)
            {
                bool sameProcess = previous.NamesTheProcess && previous.ProcessId == session.ProcessId;
                bool sameCell = previous.PlayerX == player.X && previous.PlayerY == player.Y;

                // The same cell is not a second map. Requiring a change there would
                // delete the set for standing still, and counting it as a pass
                // would claim evidence nobody gathered.
                bool anotherMap = sameProcess && !sameCell;
                bool restarted = previous.NamesTheProcess && !sameProcess;

                Console.WriteLine(anotherMap
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"Narrowing {carried.Count} candidate(s); the value must change.")
                    : string.Create(CultureInfo.InvariantCulture,
                        $"Narrowing {carried.Count} candidate(s); the value must still name a map that contains the character."));

                hits = Narrow(carried, plausible, anchors, address => ReadInt32(session.Reader, address), anotherMap);
                passes = previous.Passes + (anotherMap ? 1 : 0);
                restarts = previous.Restarts + (restarted ? 1 : 0);
            }
            else
            {
                Console.WriteLine("Scanning for those ids: private data, and the client's own writable image...");
                hits = Scan(session.Reader, plausible, anchors, out regions, out bytes, out truncated);
                passes = 1;
                restarts = 0;
            }

            SaveCandidates(candidatePath, hits, passes, restarts, session.ProcessId, player.X, player.Y);

            int durable = 0;
            foreach (MapIdHit hit in hits)
            {
                if (hit.IsDurable)
                    durable++;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"candidates={hits.Count} anchored={durable} maps={passes} restarts={restarts}"));
            if (truncated)
                Console.WriteLine("  (capped)");
            if (regions > 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"regions={regions} bytesRead={bytes:N0}"));
            }

            int shown = Math.Min(PrintLimit, hits.Count);
            for (int i = 0; i < shown; i++)
            {
                MapIdHit hit = hits[i];
                MapGridSize size = byId[hit.MapId];
                anchors.TryResolve(hit, out long address);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {hit.Describe(),-22} at 0x{address:X}  map={hit.MapId} {size.Width}x{size.Height}"));
            }

            if (hits.Count > PrintLimit)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  ... and {hits.Count - PrintLimit} more, all in {candidatePath}"));
            }

            Console.WriteLine();
            Console.WriteLine(Advice(hits.Count, durable, passes, restarts, truncated));
            return Proven(hits, passes, restarts) ? 0 : 1;
        }
    }

    internal static string Advice(int count, int durable, int passes, int restarts, bool truncated) => count switch
    {
        0 => "No candidate holds a map id whose grid contains the character. Stay on this map and check the extraction.",
        1 when durable == 0 =>
            "The survivor is a bare address, measured from no base the runtime resolves, so it cannot be written "
            + "down. Restarting the client drops it; if it really is the field, a fresh scan finds it again.",
        1 when passes < 2 =>
            "One candidate, but only one map. Cross a portal and run --find-mapid again before trusting it.",
        1 when restarts < 1 =>
            "One anchored candidate, and it tracked two maps. Now restart the client, log the same character in, "
            + "and run --find-mapid again: an offset survives that, an address does not.",
        1 => "Proven both ways — two maps and a restart. Write the offset into NosTaleClientLayout.",
        _ when truncated => "The set was capped; that many small ids are too common. Cross a portal and narrow.",
        _ => "Cross a portal onto a map of different size and run --find-mapid again. A candidate that did not change is not the map id."
    };

    [SupportedOSPlatform("windows")]
    private static List<MapIdHit> Scan(
        ProcessMemoryReader reader,
        IReadOnlySet<int> plausible,
        MapIdAnchors anchors,
        out int regions,
        out long bytes,
        out bool truncated)
    {
        var found = new List<MapIdHit>();
        regions = 0;
        bytes = 0;
        truncated = false;

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            // Private data holds the character's state; the client's own writable
            // image holds whatever it keeps in a global, which is the one place a
            // durable offset could live that the chain does not reach. Read-only
            // pages of that image carry constants compiled in, and are not state.
            if (!region.IsPrivate && !(region.IsWritable && InMainModule(region, anchors)))
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
                    int value = BitConverter.ToInt32(window.Slice(i, 4));
                    if (!plausible.Contains(value))
                        continue;

                    found.Add(anchors.Anchor(address.ToInt64() + i, value));
                    if (found.Count >= MemoryScanner.MaxCandidates)
                    {
                        truncated = true;
                        return found;
                    }
                }

                offset += length;
            }
        }

        return found;
    }

    internal static bool InMainModule(MemoryRegion region, in MapIdAnchors anchors)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (anchors.ModuleBase <= 0 || anchors.ModuleSize <= 0)
            return false;

        long start = region.BaseAddress.ToInt64();
        return start >= anchors.ModuleBase && start - anchors.ModuleBase < anchors.ModuleSize;
    }

    [SupportedOSPlatform("windows")]
    private static int? ReadInt32(ProcessMemoryReader reader, long address)
    {
        MemoryReadResult read = reader.Read(new IntPtr(address), sizeof(int));
        return read.Ok ? BitConverter.ToInt32(read.Bytes) : null;
    }

    /// <summary>Reads a candidate file, including one written before anchors existed.</summary>
    internal static bool TryLoadCandidates(string path, out MapIdCandidates? candidates)
    {
        candidates = null;
        if (!File.Exists(path))
            return false;

        int passes = 1;
        int restarts = 0;
        int processId = 0;
        int playerX = -1;
        int playerY = -1;
        var hits = new List<MapIdHit>();

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('#'))
            {
                ReadHeader(line, ref passes, ref restarts, ref processId, ref playerX, ref playerY);
                continue;
            }

            if (TryParseHit(line, out MapIdHit hit))
                hits.Add(hit);
        }

        if (hits.Count == 0)
            return false;

        candidates = new MapIdCandidates(passes, restarts, processId, playerX, playerY, hits);
        return true;
    }

    private static void ReadHeader(
        string line, ref int passes, ref int restarts, ref int processId, ref int playerX, ref int playerY)
    {
        string body = line.TrimStart('#').Trim();
        int equals = body.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
            return;

        string key = body[..equals];
        string value = body[(equals + 1)..];
        switch (key)
        {
            case "passes":
                TryInt(value, ref passes);
                break;
            case "restarts":
                TryInt(value, ref restarts);
                break;
            case "process":
                TryInt(value, ref processId);
                break;
            case "player":
                int comma = value.IndexOf(',', StringComparison.Ordinal);
                if (comma > 0)
                {
                    TryInt(value[..comma], ref playerX);
                    TryInt(value[(comma + 1)..], ref playerY);
                }

                break;
        }
    }

    private static void TryInt(string text, ref int target)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            target = parsed;
    }

    private static bool TryParseHit(string line, out MapIdHit hit)
    {
        hit = default;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Two fields is the format from before anchors existed: an address and the
        // id it held. It is read as what it was — an address — and dropped as soon
        // as the process that produced it is gone.
        if (parts.Length == 2)
        {
            if (!long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
                return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int legacyId))
                return false;

            hit = new MapIdHit(MapIdAnchorKind.Heap, address, legacyId);
            return true;
        }

        if (parts.Length < 3)
            return false;
        if (MapIdAnchors.Parse(parts[0]) is not { } anchor)
            return false;
        if (!long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
            return false;

        hit = new MapIdHit(anchor, offset, mapId);
        return true;
    }

    internal static string FormatCandidates(
        IReadOnlyList<MapIdHit> hits, int passes, int restarts, int processId, int playerX, int playerY)
    {
        ArgumentNullException.ThrowIfNull(hits);
        var text = new StringBuilder();
        text.Append("# format=").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# passes=").Append(passes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# restarts=").Append(restarts.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# process=").Append(processId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# player=").Append(playerX.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(playerY.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# candidates=").Append(hits.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (MapIdHit hit in hits)
        {
            text.Append(MapIdAnchors.NameOf(hit.Anchor)).Append(' ')
                .Append(hit.Offset.ToString("X", CultureInfo.InvariantCulture)).Append(' ')
                .Append(hit.MapId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return text.ToString();
    }

    private static void SaveCandidates(
        string path, IReadOnlyList<MapIdHit> hits, int passes, int restarts, int processId, int playerX, int playerY)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, FormatCandidates(hits, passes, restarts, processId, playerX, playerY));
    }
}
