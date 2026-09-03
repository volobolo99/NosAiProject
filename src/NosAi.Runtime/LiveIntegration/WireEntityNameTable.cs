using System.Globalization;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration;

/// <summary>What one <c>in</c> or <c>drop</c> said about an id, when it named it.</summary>
/// <param name="EntityId">The id the packet carried.</param>
/// <param name="Name">
/// A display name when the packet or the catalogue supplied one; null when the
/// wire only had a vnum and nobody resolved it.
/// </param>
/// <param name="Vnum">The catalogue number, when the packet carried one.</param>
/// <param name="Opcode">Which packet produced this row.</param>
public readonly record struct WireEntityName(long EntityId, string? Name, int? Vnum, string Opcode);

/// <summary>
/// The second source for entity names: <c>in</c> (and <c>drop</c> for ground items).
/// </summary>
/// <remarks>
/// <para>
/// A monster <c>in</c> carries a vnum, not a display name. The name the operator
/// sees is the catalogue's, when it is loaded — the same resolution
/// <see cref="WorldReplayCommand"/> already uses. A player <c>in</c> carries the
/// name in the field where a monster carries the vnum. Neither side is LIVE.
/// </para>
/// <para>
/// Last packet for an id wins. The table is a session view, not a cache of
/// memory: it is rebuilt from the capture each time the command runs.
/// </para>
/// </remarks>
public sealed class WireEntityNameTable
{
    private readonly Dictionary<long, WireEntityName> _byId = new();

    public int Count => _byId.Count;

    public bool TryGet(long entityId, out WireEntityName entry) => _byId.TryGetValue(entityId, out entry);

    /// <summary>
    /// Compares a memory candidate to the wire row for the same id.
    /// </summary>
    /// <remarks>
    /// A match is a visible fact for the operator, not a promotion. The memory
    /// side stays UNKNOWN.
    /// </remarks>
    public static string Compare(in EntityNameCandidate memory, WireEntityName? wire)
    {
        bool hasMem = memory.HasValue;
        bool hasWire = wire is { Name: { Length: > 0 } };

        if (!hasMem && !hasWire)
            return "—";
        if (!hasMem)
            return "wire-only";
        if (!hasWire)
            return "mem-only";

        return string.Equals(memory.Value, wire!.Value.Name, StringComparison.Ordinal)
            ? "match"
            : "MISMATCH";
    }

    /// <summary>
    /// Reads every framed world line from a recording and keeps the last name
    /// each id received.
    /// </summary>
    public static WireEntityNameTable FromCapture(string path, GameReferenceDatabase? catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var table = new WireEntityNameTable();
        using IPacketSource packets = CaptureFile.Open(path);
        var engine = new GameTrafficCaptureEngine(
            packets, NosTaleWorldFramer.Factory(DataSourceKind.Cached));

        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;

            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
            {
                if (TryParsePacket(line, catalog, out WireEntityName entry))
                    table._byId[entry.EntityId] = entry;
            }
        };

        engine.Run();
        return table;
    }

    /// <summary>
    /// One world-channel line. Type 3 <c>in</c> yields a vnum; type 1 yields a name;
    /// <c>drop</c> yields the ground item's vnum under the drop id.
    /// </summary>
    public static bool TryParsePacket(string text, GameReferenceDatabase? catalog, out WireEntityName entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
            return false;

        if (fields[0] == "in")
            return TryParseEnter(fields, catalog, out entry);
        if (fields[0] == "drop")
            return TryParseDrop(fields, catalog, out entry);

        return false;
    }

    private static bool TryParseEnter(
        string[] fields, GameReferenceDatabase? catalog, out WireEntityName entry)
    {
        entry = default;
        if (fields.Length < 4)
            return false;

        // Type 1: in 1 Name id x y … — the name sits where type 3 puts the vnum.
        // The protocol decoder refuses this shape for sightings; here it is only
        // a name-to-id pair, and only while the id parses.
        if (fields[1] == "1")
        {
            if (!long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long playerId)
                || playerId <= 0
                || !EntityNameText.TryParseAnsi(
                    System.Text.Encoding.ASCII.GetBytes(fields[2] + "\0"), out string? name, out _))
                return false;

            entry = new WireEntityName(playerId, name, Vnum: null, "in");
            return true;
        }

        // Type 3: in 3 vnum id x y … — confirmed in PROTOCOLLO_NOSTALE.md.
        if (fields[1] != "3")
            return false;
        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vnum)
            || vnum <= 0
            || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long entityId)
            || entityId <= 0)
            return false;

        string? resolved = ResolveVnum("monster", vnum, catalog);
        entry = new WireEntityName(entityId, resolved, vnum, "in");
        return true;
    }

    private static bool TryParseDrop(
        string[] fields, GameReferenceDatabase? catalog, out WireEntityName entry)
    {
        entry = default;
        if (fields.Length < 3)
            return false;
        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vnum)
            || vnum <= 0
            || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long dropId)
            || dropId <= 0)
            return false;

        string? resolved = ResolveVnum("item", vnum, catalog);
        entry = new WireEntityName(dropId, resolved, vnum, "drop");
        return true;
    }

    private static string? ResolveVnum(string kind, int vnum, GameReferenceDatabase? catalog)
    {
        if (catalog is null)
            return null;

        string resolved = WorldReplayCommand.ResolveEntityName(kind, vnum, catalog);
        if (resolved.StartsWith("UNKNOWN", StringComparison.Ordinal)
            || resolved.StartsWith("vnum ", StringComparison.Ordinal))
            return null;

        return resolved;
    }
}
