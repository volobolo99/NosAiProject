using System.Globalization;
using NosAi.Runtime.GameData;

namespace NosAi.LiveIntegration;

/// <summary>
/// Prints every entity the client has, with the memory name candidate and the
/// name the wire gave for the same id when a recording has one.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 operator command. Discordance is a column, not a log line: a
/// mismatch sits on the same row as the two names. A match is still UNKNOWN —
/// it is evidence for the operator, not a promotion this command is allowed to
/// make.
/// </para>
/// <para>
/// Read-only. The capture is optional; without it the wire column is empty and
/// the memory column still prints.
/// </para>
/// </remarks>
public static class EntityNameProbe
{
    public const string Flag = "--entity-names";
    public const string ClientNotReadable = "client_not_readable";

    /// <param name="capturePath">
    /// A <c>.noscap</c> to take <c>in</c>/<c>drop</c> names from, or null when
    /// only memory is being shown.
    /// </param>
    public static int Run(string? capturePath = null)
    {
        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? failure))
        {
            Console.WriteLine($"[REFUSED] {ClientNotReadable}:{failure}");
            return 1;
        }

        using (session)
        {
            WireEntityNameTable wire = LoadWire(capturePath, out string wireSource);
            Console.WriteLine("=== entity names (candidates; UNKNOWN until concordance) ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session!.ProcessId}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wire:   {wireSource}"));
            Console.WriteLine();
            Console.WriteLine(
                "kind        id         xy          memory                          wire                            vs");
            Console.WriteLine(
                "----------  ---------  ----------  ------------------------------  ------------------------------  --------");

            int rows = 0;
            int matches = 0;
            int mismatches = 0;
            foreach (MapEntityKind kind in Enum.GetValues<MapEntityKind>())
            {
                if (!session.TryReadEntities(kind, out IReadOnlyList<MapEntityReading> entities, out string? listFailure))
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{kind,-10}  [REFUSED] {listFailure}"));
                    continue;
                }

                foreach (MapEntityReading entity in entities)
                {
                    rows++;
                    WireEntityName? wireRow = wire.TryGet(entity.EntityId, out WireEntityName found)
                        ? found
                        : null;
                    string vs = WireEntityNameTable.Compare(entity.Name, wireRow);
                    if (vs == "match")
                        matches++;
                    if (vs == "MISMATCH")
                        mismatches++;

                    Console.WriteLine(FormatRow(kind, entity, wireRow, vs));
                }
            }

            Console.WriteLine();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{rows} entities, {matches} match, {mismatches} MISMATCH. A match is not LIVE. Nothing downstream may decide on these names."));
        }

        return 0;
    }

    internal static string FormatRow(
        MapEntityKind kind, in MapEntityReading entity, WireEntityName? wire, string vs)
    {
        string memory = entity.Name.HasValue
            ? entity.Name.Value!
            : $"— ({entity.Name.Reason})";
        string wireText = wire is { } row
            ? row.Name is { Length: > 0 } name
                ? name
                : row.Vnum is { } vnum
                    ? string.Create(CultureInfo.InvariantCulture, $"vnum={vnum}")
                    : "—"
            : "—";

        return string.Create(CultureInfo.InvariantCulture,
            $"{kind,-10}  {entity.EntityId,-9}  {entity.X},{entity.Y,-7}  {Pad(memory, 30)}  {Pad(wireText, 30)}  {vs}");
    }

    private static WireEntityNameTable LoadWire(string? capturePath, out string source)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            source = "no capture (pass a .noscap to fill this column)";
            return new WireEntityNameTable();
        }

        if (!File.Exists(capturePath))
        {
            source = $"recording_not_found:{capturePath}";
            return new WireEntityNameTable();
        }

        GameReferenceLocator.TryOpen(out GameReferenceDatabase? catalog, out string? catalogWhy);
        try
        {
            WireEntityNameTable table = WireEntityNameTable.FromCapture(capturePath, catalog);
            source = catalog is null
                ? $"{Path.GetFileName(capturePath)}  {table.Count} ids  catalog: {catalogWhy}"
                : $"{Path.GetFileName(capturePath)}  {table.Count} ids  catalog loaded";
            return table;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            source = $"recording_unreadable:{ex.GetType().Name}";
            return new WireEntityNameTable();
        }
        finally
        {
            catalog?.Dispose();
        }
    }

    private static string Pad(string text, int width)
    {
        if (text.Length <= width)
            return text.PadRight(width);
        return text[..(width - 1)] + "…";
    }
}
