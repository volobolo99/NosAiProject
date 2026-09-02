using System.Globalization;
using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;

namespace NosAi.ControlPanel;

/// <summary>
/// How one cell of the standing-cell crop is drawn. Three geometry states plus
/// an explicit unknown; unknown is never rendered as open ground.
/// </summary>
internal enum MapCellDraw : byte
{
    /// <summary>Static geometry permits standing here.</summary>
    Walkable = 0,

    /// <summary>Static geometry forbids standing here.</summary>
    Blocked = 1,

    /// <summary>The coordinate is outside the loaded rectangle.</summary>
    OutOfGrid = 2,

    /// <summary>
    /// Geometry itself is unknown: no grid, or no position to centre on.
    /// Distinct from <see cref="OutOfGrid"/> and from an empty cell.
    /// </summary>
    Unknown = 3
}

/// <summary>The standing-cell proof, one named outcome per case the tests pin.</summary>
internal enum StandingCellKind : byte
{
    Walkable = 0,
    NotWalkable = 1,
    OutOfGrid = 2,
    PositionUnknown = 3,
    GridUnknown = 4
}

/// <summary>Operator-facing map view: fields, standing-cell line, and the 31×31 crop.</summary>
internal sealed class MapView
{
    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
    public string StandingLine { get; init; } = "";
    public StandingCellKind StandingKind { get; init; }
    public bool StandingIsError { get; init; }
    public IReadOnlyList<MapCellDraw> Crop { get; init; } = Array.Empty<MapCellDraw>();
    public string CropGlyphs { get; init; } = "";
}

/// <summary>Map id and standing cell as classified readings, or the reason they are not.</summary>
internal readonly record struct MapWorldReading(
    ClassifiedValue<int> MapId,
    ClassifiedValue<int> CellX,
    ClassifiedValue<int> CellY)
{
    public static MapWorldReading Unknown(string reason) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason));
}

/// <summary>
/// Read-only map view for the control panel: map id, loaded grid, set identity,
/// and the standing-cell proof. It never writes to the runtime, never arms input,
/// and never invents walkability when the grid is absent.
/// </summary>
internal static class MapInspect
{
    /// <summary>Cells across and down the crop centred on the character.</summary>
    public const int CropSize = 31;

    /// <summary>Chebyshev radius that makes a <see cref="CropSize"/> window.</summary>
    public const int CropRadius = 15;

    /// <summary>Open ground. Distinct in black and white from the other three glyphs.</summary>
    public const char WalkableGlyph = '·';

    /// <summary>Blocked by static geometry.</summary>
    public const char BlockedGlyph = '#';

    /// <summary>Outside the loaded rectangle. Not the same drawing as unknown.</summary>
    public const char OutOfGridGlyph = '×';

    /// <summary>Geometry not known. Not an empty cell and not out of grid.</summary>
    public const char UnknownGlyph = '?';

    public const string StandingWalkableLabel = "calpestabile";
    public const string StandingNotWalkableLabel = "NON calpestabile";
    public const string StandingOutOfGridLabel = "fuori griglia";
    public const string StandingErrorPrefix = "ERRORE — ";
    public const string MapIdProvenanceLive = "memoria del processo client";

    /// <summary>Fallback when a grid is absent and no more specific extractor token was supplied.</summary>
    public const string GridNotLoaded = "grid_not_loaded";

    /// <summary>Fallback when neither coordinate carries a failure reason of its own.</summary>
    public const string PositionUnknown = "position_unknown";

    /// <summary>
    /// Builds the view from already-classified readings. Occupancy is not a
    /// parameter: when it is not available it is not drawn, so open ground here
    /// is geometry alone and never a claim that the cell is clear of movers.
    /// </summary>
    public static MapView Build(
        ClassifiedValue<int> mapId,
        ClassifiedValue<int> cellX,
        ClassifiedValue<int> cellY,
        MapGrid grid,
        string? gridFailureReason,
        string? gridFileHash,
        MapGridSetIdentity? recordedIdentity,
        MapGridSetIdentity? currentIdentity)
    {
        StandingCellKind standing = ClassifyStanding(cellX, cellY, grid, gridFailureReason, out bool error, out string standingLine);
        MapCellDraw[] crop = Crop(grid, cellX, cellY);
        return new MapView
        {
            Fields = Fields(mapId, grid, gridFailureReason, gridFileHash, recordedIdentity, currentIdentity),
            StandingLine = standingLine,
            StandingKind = standing,
            StandingIsError = error,
            Crop = crop,
            CropGlyphs = RenderGlyphs(crop)
        };
    }

    /// <summary>
    /// Composes the view for a live session. Idle is entirely UNKNOWN. Grid files
    /// and the client process are read; nothing is written, armed, or commanded.
    /// </summary>
    public static MapView Observe(
        SessionKind kind,
        ClassifiedValue<int?> processId,
        string? mapsDirectory = null,
        MapWorldReading? world = null)
    {
        if (kind == SessionKind.Idle)
            return UnknownAll("runtime_not_connected");

        MapWorldReading reading = world ?? MapWorldReader.Read(ProcessIdOrZero(processId));
        return Compose(reading, mapsDirectory);
    }

    /// <summary>Every field UNKNOWN with the same reason. The crop is unknown, not open.</summary>
    public static MapView UnknownAll(string reason)
    {
        MapWorldReading unknown = MapWorldReading.Unknown(reason);
        return Build(
            unknown.MapId,
            unknown.CellX,
            unknown.CellY,
            default,
            reason,
            null,
            null,
            null);
    }

    /// <summary>
    /// 31×31 window centred on the standing cell. Coordinates that fall off the
    /// map are <see cref="MapCellDraw.OutOfGrid"/>; a missing grid or unknown
    /// position fills the window with <see cref="MapCellDraw.Unknown"/> rather
    /// than asking <see cref="MapGrid.IsWalkable"/> of a default instance, which
    /// would paint a blocked map and hide that geometry was never loaded.
    /// </summary>
    public static MapCellDraw[] Crop(MapGrid grid, ClassifiedValue<int> cellX, ClassifiedValue<int> cellY)
    {
        var cells = new MapCellDraw[CropSize * CropSize];
        if (!cellX.HasValue || !cellY.HasValue || !grid.IsLoaded)
        {
            Array.Fill(cells, MapCellDraw.Unknown);
            return cells;
        }

        int originX = cellX.Value - CropRadius;
        int originY = cellY.Value - CropRadius;
        for (int row = 0; row < CropSize; row++)
        {
            int cy = originY + row;
            int offset = row * CropSize;
            for (int col = 0; col < CropSize; col++)
            {
                int cx = originX + col;
                if (!grid.Contains(cx, cy))
                    cells[offset + col] = MapCellDraw.OutOfGrid;
                else if (grid.IsWalkable(cx, cy))
                    cells[offset + col] = MapCellDraw.Walkable;
                else
                    cells[offset + col] = MapCellDraw.Blocked;
            }
        }

        return cells;
    }

    /// <summary>The black-and-white glyph for a crop cell. Four drawings, no fifth.</summary>
    public static char Glyph(MapCellDraw kind) => kind switch
    {
        MapCellDraw.Walkable => WalkableGlyph,
        MapCellDraw.Blocked => BlockedGlyph,
        MapCellDraw.OutOfGrid => OutOfGridGlyph,
        _ => UnknownGlyph
    };

    private static MapView Compose(MapWorldReading world, string? mapsDirectory)
    {
        string? maps = mapsDirectory;
        string? mapsReason = null;
        if (string.IsNullOrWhiteSpace(maps))
        {
            if (!MapGridExtractor.TryResolveDedicatedMapsDirectory(out maps, out mapsReason))
                maps = null;
        }

        MapGrid grid = default;
        string? gridReason = mapsReason;
        string? fileHash = null;
        MapGridSetIdentity? recorded = null;

        if (maps is not null)
        {
            MapGridManifest.TryRead(maps, out recorded, out _);
            if (world.MapId.HasValue)
            {
                if (!MapGridExtractor.TryInfo(maps, world.MapId.Value, out grid, out fileHash, out gridReason))
                    grid = default;
            }
            else
            {
                gridReason = world.MapId.FailureReason;
            }
        }
        else if (!world.MapId.HasValue)
        {
            gridReason = world.MapId.FailureReason ?? mapsReason;
        }

        return Build(world.MapId, world.CellX, world.CellY, grid, gridReason, fileHash, recorded, currentIdentity: null);
    }

    private static StandingCellKind ClassifyStanding(
        ClassifiedValue<int> cellX,
        ClassifiedValue<int> cellY,
        MapGrid grid,
        string? gridFailureReason,
        out bool error,
        out string line)
    {
        if (!cellX.HasValue || !cellY.HasValue)
        {
            error = false;
            string reason = cellX.FailureReason ?? cellY.FailureReason ?? PositionUnknown;
            line = $"Cella di appoggio: UNKNOWN · {reason}";
            return StandingCellKind.PositionUnknown;
        }

        int x = cellX.Value;
        int y = cellY.Value;
        string at = string.Create(CultureInfo.InvariantCulture, $"({x},{y})");

        if (!grid.IsLoaded)
        {
            error = false;
            string reason = string.IsNullOrWhiteSpace(gridFailureReason) ? GridNotLoaded : gridFailureReason;
            line = $"Cella di appoggio: UNKNOWN · {reason}";
            return StandingCellKind.GridUnknown;
        }

        if (!grid.Contains(x, y))
        {
            error = true;
            line = $"{StandingErrorPrefix}Cella di appoggio: {at} {StandingOutOfGridLabel}";
            return StandingCellKind.OutOfGrid;
        }

        if (!grid.IsWalkable(x, y))
        {
            error = true;
            line = $"{StandingErrorPrefix}Cella di appoggio: {at} {StandingNotWalkableLabel}";
            return StandingCellKind.NotWalkable;
        }

        error = false;
        line = $"Cella di appoggio: {at} {StandingWalkableLabel}";
        return StandingCellKind.Walkable;
    }

    private static IReadOnlyList<DisplayField> Fields(
        ClassifiedValue<int> mapId,
        MapGrid grid,
        string? gridFailureReason,
        string? gridFileHash,
        MapGridSetIdentity? recordedIdentity,
        MapGridSetIdentity? currentIdentity)
    {
        bool mayLoad = MapGridSetIdentity.MayLoad(recordedIdentity, currentIdentity, out string? identityReason);
        string identitySource = recordedIdentity is null ? "UNKNOWN" : DataSourceKind.Cached.ToWire();

        return
        [
            Field("Id mappa", mapId),
            Provenance(mapId),
            GridField(grid, mapId, gridFailureReason),
            Dimensions(grid, gridFailureReason),
            new DisplayField(
                "Identità insieme",
                recordedIdentity is null
                    ? $"UNKNOWN · {identityReason ?? MapGridManifest.ManifestMissing}"
                    : $"{recordedIdentity.SetHash} [{identitySource}]",
                identitySource),
            new DisplayField(
                "Hash build",
                recordedIdentity is null
                    ? $"UNKNOWN · {identityReason ?? MapGridManifest.ManifestMissing}"
                    : $"{recordedIdentity.ClientFingerprint} [{identitySource}]",
                identitySource),
            new DisplayField(
                "Identità verificata",
                mayLoad ? "sì [DERIVED]" : $"UNKNOWN · {identityReason ?? MapGridManifest.ManifestMissing}",
                mayLoad ? "DERIVED" : "UNKNOWN"),
            new DisplayField(
                "Hash file griglia",
                string.IsNullOrWhiteSpace(gridFileHash) || !grid.IsLoaded
                    ? $"UNKNOWN · {gridFailureReason ?? GridNotLoaded}"
                    : $"{gridFileHash} [{DataSourceKind.Cached.ToWire()}]",
                grid.IsLoaded && !string.IsNullOrWhiteSpace(gridFileHash) ? "CACHED" : "UNKNOWN")
        ];
    }

    private static DisplayField Provenance(ClassifiedValue<int> mapId)
    {
        if (!mapId.HasValue)
        {
            string reason = string.IsNullOrWhiteSpace(mapId.FailureReason) ? "UNKNOWN" : mapId.FailureReason!;
            return new DisplayField("Provenienza id mappa", $"UNKNOWN · {reason}", "UNKNOWN");
        }

        return mapId.Source switch
        {
            DataSourceKind.Live => new DisplayField("Provenienza id mappa", $"{MapIdProvenanceLive} [LIVE]", "LIVE"),
            DataSourceKind.Cached => new DisplayField(
                "Provenienza id mappa", $"{MapGridSetIdentity.Provenance} [CACHED]", "CACHED"),
            DataSourceKind.Derived => new DisplayField("Provenienza id mappa", "DERIVED", "DERIVED"),
            _ => new DisplayField("Provenienza id mappa", "UNKNOWN", "UNKNOWN")
        };
    }

    private static DisplayField GridField(MapGrid grid, ClassifiedValue<int> mapId, string? gridFailureReason)
    {
        if (!grid.IsLoaded)
        {
            string reason = string.IsNullOrWhiteSpace(gridFailureReason) ? GridNotLoaded : gridFailureReason;
            return new DisplayField("Griglia", $"UNKNOWN · {reason}", "UNKNOWN");
        }

        string forMap = mapId.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"caricata per mappa {mapId.Value}")
            : "caricata";
        return new DisplayField("Griglia", $"{forMap} [CACHED]", "CACHED");
    }

    private static DisplayField Dimensions(MapGrid grid, string? gridFailureReason)
    {
        if (!grid.IsLoaded)
        {
            string reason = string.IsNullOrWhiteSpace(gridFailureReason) ? GridNotLoaded : gridFailureReason;
            return new DisplayField("Dimensioni", $"UNKNOWN · {reason}", "UNKNOWN");
        }

        return new DisplayField(
            "Dimensioni",
            string.Create(CultureInfo.InvariantCulture, $"{grid.Width}x{grid.Height} [CACHED]"),
            "CACHED");
    }

    private static DisplayField Field<T>(string label, ClassifiedValue<T> classified)
    {
        string source = classified.Source.ToWire();
        if (!classified.HasValue)
        {
            string reason = classified.FailureReason ?? "";
            return new DisplayField(
                label,
                string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : $"UNKNOWN · {reason}",
                "UNKNOWN");
        }

        return new DisplayField(label, $"{classified.Value} [{source}]", source);
    }

    private static string RenderGlyphs(IReadOnlyList<MapCellDraw> crop)
    {
        var text = new StringBuilder(CropSize * (CropSize + 1));
        for (int row = 0; row < CropSize; row++)
        {
            if (row > 0)
                text.Append('\n');
            int offset = row * CropSize;
            for (int col = 0; col < CropSize; col++)
                text.Append(Glyph(crop[offset + col]));
        }

        return text.ToString();
    }

    private static int ProcessIdOrZero(ClassifiedValue<int?> processId)
        => processId.HasValue && processId.Value is int pid && pid > 0 ? pid : 0;
}
