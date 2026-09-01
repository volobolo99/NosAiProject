using System;
using System.Globalization;
using System.Text;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// Prints the cell the character is standing on, and its eight neighbours, as
/// the bytes the grid file actually holds.
/// </summary>
/// <remarks>
/// <para>
/// The standing-cell proof in <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3:
/// the character is still, perception names the map and the square, and that
/// square has to come back walkable. A 3×3 is printed with it because a single
/// open cell can be luck; eight neighbours that do not look like the ground on
/// screen is the signal that the bits do not mean what the layout claims.
/// </para>
/// <para>
/// Read-only. It does not rewrite a cell, invert a bit, or transpose a row. A
/// blocked standing cell is reported as blocked, and that is the whole answer.
/// </para>
/// </remarks>
public static class MapGridCheck
{
    /// <summary>
    /// Loads the named grid and prints the standing cell plus the 3×3 around it.
    /// </summary>
    /// <returns>0 when the bytes were printed; 1 when the file could not be read.</returns>
    public static int Inspect(string mapsDirectory, int mapId, int x, int y)
    {
        if (!MapGridExtractor.TryInfo(mapsDirectory, mapId, out MapGrid grid, out _, out string? reason))
        {
            Console.WriteLine($"[REFUSED] {reason}");
            return 1;
        }

        Console.Write(Describe(grid, x, y));
        return 0;
    }

    /// <summary>
    /// Reads map id and position from the running client, loads that map's grid,
    /// and prints the standing cell.
    /// </summary>
    public static int Run(string? mapsDirectory = null)
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

            if (!session.TryReadMapId(out int mapId, out string? mapFailure))
            {
                Console.WriteLine($"[REFUSED] {mapFailure}");
                return 1;
            }

            return Inspect(mapsDirectory, mapId, player.X, player.Y);
        }
    }

    /// <summary>
    /// The standing cell and the 3×3 of raw bytes around it. No interpretation
    /// beyond the walkability predicate the grid already exposes.
    /// </summary>
    public static string Describe(MapGrid grid, int x, int y)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"map={grid.MapId} {grid.Width}x{grid.Height} player={x},{y}"));

        bool inside = grid.Contains(x, y);
        bool walkable = grid.IsWalkable(x, y);
        if (!inside)
            text.AppendLine("standing: blocked outside");
        else if (!walkable)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"standing: blocked raw={FormatByte(grid.RawAt(x, y))}"));
        else
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"standing: walkable raw={FormatByte(grid.RawAt(x, y))}"));

        text.AppendLine("3x3:");
        for (int dy = -1; dy <= 1; dy++)
        {
            text.Append("  ");
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx > -1)
                    text.Append(' ');
                int cx = x + dx;
                int cy = y + dy;
                text.Append(grid.Contains(cx, cy) ? FormatByte(grid.RawAt(cx, cy)) : "--");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string FormatByte(byte value) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{value:X2}");
}
