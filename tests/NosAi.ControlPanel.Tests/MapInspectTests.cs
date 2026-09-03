using System.Buffers.Binary;
using System.IO;
using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Standing-cell line, 31×31 crop, and the guarantee that the map view never
/// writes to the runtime.
/// </summary>
public sealed class MapInspectTests
{
    [Fact]
    public void AWalkableStandingCellIsNamedWalkableAndIsNotAnError()
    {
        MapView view = Standing(Grid(7, 3, 3,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00), 1, 1);

        Assert.Equal(StandingCellKind.Walkable, view.StandingKind);
        Assert.False(view.StandingIsError);
        Assert.Contains("(1,1)", view.StandingLine, StringComparison.Ordinal);
        Assert.Contains(MapInspect.StandingWalkableLabel, view.StandingLine, StringComparison.Ordinal);
        Assert.DoesNotContain(MapInspect.StandingErrorPrefix, view.StandingLine, StringComparison.Ordinal);
        Assert.Equal(MapCellDraw.Walkable, Center(view));
    }

    [Fact]
    public void ABlockedStandingCellIsAnErrorAndNotADetail()
    {
        MapView view = Standing(Grid(1, 3, 3,
            0x00, 0x00, 0x00,
            0x00, 0x01, 0x00,
            0x00, 0x00, 0x00), 1, 1);

        Assert.Equal(StandingCellKind.NotWalkable, view.StandingKind);
        Assert.True(view.StandingIsError);
        Assert.StartsWith(MapInspect.StandingErrorPrefix, view.StandingLine, StringComparison.Ordinal);
        Assert.Contains(MapInspect.StandingNotWalkableLabel, view.StandingLine, StringComparison.Ordinal);
        Assert.Equal(MapCellDraw.Blocked, Center(view));
    }

    [Fact]
    public void AStandingCellOutsideTheGridIsAnErrorNamedOutOfGrid()
    {
        MapView view = Standing(Grid(4, 1, 1, 0x00), 5, 5);

        Assert.Equal(StandingCellKind.OutOfGrid, view.StandingKind);
        Assert.True(view.StandingIsError);
        Assert.StartsWith(MapInspect.StandingErrorPrefix, view.StandingLine, StringComparison.Ordinal);
        Assert.Contains(MapInspect.StandingOutOfGridLabel, view.StandingLine, StringComparison.Ordinal);
        Assert.DoesNotContain(MapInspect.StandingNotWalkableLabel, view.StandingLine, StringComparison.Ordinal);
        Assert.Equal(MapCellDraw.OutOfGrid, Center(view));
    }

    [Fact]
    public void AnUnknownPositionIsUnknownWithTheReasonAndIsNotAnError()
    {
        MapView view = MapInspect.Build(
            ClassifiedValue<int>.Live(9),
            ClassifiedValue<int>.Unknown("player_object_unreadable"),
            ClassifiedValue<int>.Unknown("player_object_unreadable"),
            Grid(9, 2, 2, 0x00, 0x00, 0x00, 0x00),
            null,
            null,
            null,
            null);

        Assert.Equal(StandingCellKind.PositionUnknown, view.StandingKind);
        Assert.False(view.StandingIsError);
        Assert.Contains("UNKNOWN", view.StandingLine, StringComparison.Ordinal);
        Assert.Contains("player_object_unreadable", view.StandingLine, StringComparison.Ordinal);
        Assert.DoesNotContain(MapInspect.StandingErrorPrefix, view.StandingLine, StringComparison.Ordinal);
        Assert.All(view.Crop, cell => Assert.Equal(MapCellDraw.Unknown, cell));
        Assert.DoesNotContain(MapInspect.WalkableGlyph, view.CropGlyphs);
        Assert.DoesNotContain(MapInspect.OutOfGridGlyph, view.CropGlyphs);
    }

    [Fact]
    public void ACropAtTheMapEdgeDoesNotThrowAndShowsOutOfGrid()
    {
        MapGrid grid = Grid(5, 2, 2,
            0x00, 0x00,
            0x00, 0x01);

        MapView view = Standing(grid, 0, 0);

        Assert.Equal(MapInspect.CropSize * MapInspect.CropSize, view.Crop.Count);
        Assert.Equal(MapCellDraw.Walkable, At(view, 0, 0));
        Assert.Equal(MapCellDraw.Walkable, At(view, 1, 0));
        Assert.Equal(MapCellDraw.Walkable, At(view, 0, 1));
        Assert.Equal(MapCellDraw.Blocked, At(view, 1, 1));
        Assert.Equal(MapCellDraw.OutOfGrid, At(view, -1, 0));
        Assert.Equal(MapCellDraw.OutOfGrid, At(view, 0, -1));
        Assert.Equal(MapCellDraw.OutOfGrid, At(view, 2, 0));
        Assert.Equal(MapCellDraw.OutOfGrid, At(view, -MapInspect.CropRadius, -MapInspect.CropRadius));
        Assert.Contains(MapInspect.OutOfGridGlyph, view.CropGlyphs);
        Assert.Contains(MapInspect.WalkableGlyph, view.CropGlyphs);
        Assert.Contains(MapInspect.BlockedGlyph, view.CropGlyphs);
        Assert.DoesNotContain(MapInspect.UnknownGlyph, view.CropGlyphs);
    }

    [Fact]
    public void WithoutAGridNothingIsDrawnAsWalkable()
    {
        MapView view = MapInspect.Build(
            ClassifiedValue<int>.Live(3),
            ClassifiedValue<int>.Live(1),
            ClassifiedValue<int>.Live(1),
            default,
            "grid_file_not_found:3",
            null,
            null,
            null);

        Assert.Equal(StandingCellKind.GridUnknown, view.StandingKind);
        Assert.Contains("UNKNOWN", view.StandingLine, StringComparison.Ordinal);
        Assert.Contains("grid_file_not_found:3", view.StandingLine, StringComparison.Ordinal);
        Assert.All(view.Crop, cell => Assert.Equal(MapCellDraw.Unknown, cell));
        Assert.DoesNotContain(view.Crop, cell => cell == MapCellDraw.Walkable);
        Assert.DoesNotContain(view.Crop, cell => cell == MapCellDraw.Blocked);
        Assert.DoesNotContain(view.Crop, cell => cell == MapCellDraw.OutOfGrid);
        Assert.Equal(new string(MapInspect.UnknownGlyph, MapInspect.CropSize), view.CropGlyphs.Split('\n')[0]);
        DisplayField gridField = Assert.Single(view.Fields, f => f.Label == "Griglia");
        Assert.Equal("UNKNOWN", gridField.Source);
        Assert.Contains("grid_file_not_found:3", gridField.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleSessionIsUnknownAndDoesNotInventAFreeCell()
    {
        MapView view = MapInspect.Observe(
            SessionKind.Idle,
            MapWorldReading.Unknown("process_not_attached"));

        Assert.Equal(StandingCellKind.PositionUnknown, view.StandingKind);
        Assert.Contains("runtime_not_connected", view.StandingLine, StringComparison.Ordinal);
        Assert.All(view.Fields, f => Assert.Equal("UNKNOWN", f.Source));
        Assert.All(view.Crop, cell => Assert.Equal(MapCellDraw.Unknown, cell));
        Assert.DoesNotContain(MapInspect.WalkableGlyph, view.CropGlyphs);
    }

    [Fact]
    public void FourGlyphsStayDistinctInBlackAndWhite()
    {
        char[] glyphs =
        [
            MapInspect.Glyph(MapCellDraw.Walkable),
            MapInspect.Glyph(MapCellDraw.Blocked),
            MapInspect.Glyph(MapCellDraw.OutOfGrid),
            MapInspect.Glyph(MapCellDraw.Unknown)
        ];

        Assert.Equal(4, glyphs.Distinct().Count());
        Assert.Equal(MapInspect.WalkableGlyph, glyphs[0]);
        Assert.Equal(MapInspect.BlockedGlyph, glyphs[1]);
        Assert.Equal(MapInspect.OutOfGridGlyph, glyphs[2]);
        Assert.Equal(MapInspect.UnknownGlyph, glyphs[3]);
    }

    [Fact]
    public void RecordedIdentityIsCachedAndUnverifiedWithoutACurrentFingerprint()
    {
        MapGridSetIdentity recorded = MapGridSetIdentity.Compute(
            [new MapGridFile(7, MapGridSetIdentity.HashFile("grid-bytes"u8))],
            "client-abc");

        MapView view = MapInspect.Build(
            ClassifiedValue<int>.Live(7),
            ClassifiedValue<int>.Live(0),
            ClassifiedValue<int>.Live(0),
            Grid(7, 1, 1, 0x00),
            null,
            recorded.Files[0].Sha256,
            recorded,
            currentIdentity: null);

        DisplayField set = Assert.Single(view.Fields, f => f.Label == "Identità insieme");
        Assert.Equal("CACHED", set.Source);
        Assert.Contains(recorded.SetHash, set.Value, StringComparison.Ordinal);

        DisplayField build = Assert.Single(view.Fields, f => f.Label == "Hash build");
        Assert.Equal("CACHED", build.Source);
        Assert.Contains("client-abc", build.Value, StringComparison.Ordinal);

        DisplayField verified = Assert.Single(view.Fields, f => f.Label == "Identità verificata");
        Assert.Equal("UNKNOWN", verified.Source);
        Assert.Contains("map_grids_current_identity_unknown", verified.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingCurrentIdentityIsVerifiedDerived()
    {
        MapGridSetIdentity identity = MapGridSetIdentity.Compute(
            [new MapGridFile(1, MapGridSetIdentity.HashFile("a"u8))],
            "client-abc");

        MapView view = MapInspect.Build(
            ClassifiedValue<int>.Live(1),
            ClassifiedValue<int>.Live(0),
            ClassifiedValue<int>.Live(0),
            Grid(1, 1, 1, 0x00),
            null,
            identity.Files[0].Sha256,
            identity,
            identity);

        DisplayField verified = Assert.Single(view.Fields, f => f.Label == "Identità verificata");
        Assert.Equal("DERIVED", verified.Source);
        Assert.StartsWith("sì", verified.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void MapIdUnknownKeepsTheNamedReason()
    {
        MapView view = MapInspect.Build(
            ClassifiedValue<int>.Unknown("map_id_implausible:0"),
            ClassifiedValue<int>.Live(0),
            ClassifiedValue<int>.Live(0),
            default,
            "map_id_implausible:0",
            null,
            null,
            null);

        DisplayField id = Assert.Single(view.Fields, f => f.Label == "Id mappa");
        Assert.Equal("UNKNOWN", id.Source);
        Assert.Contains("map_id_implausible:0", id.Value, StringComparison.Ordinal);
        DisplayField provenance = Assert.Single(view.Fields, f => f.Label == "Provenienza id mappa");
        Assert.Equal("UNKNOWN", provenance.Source);
        Assert.Contains("map_id_implausible:0", provenance.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestRoundTripIsTheRecordedIdentity()
    {
        using TempDir dir = TempDir.Create();
        MapGridFile file = new(9, MapGridSetIdentity.HashFile("payload"u8));
        MapGridSetIdentity expected = MapGridSetIdentity.Compute([file], "fp-client");
        File.WriteAllText(
            Path.Combine(dir.Maps, MapGridExtractor.ManifestFileName),
            $"{MapGridExtractor.ManifestMagic} {MapGridExtractor.ManifestVersion}\n" +
            $"fingerprint fp-client\n" +
            $"9 {file.Sha256} 2 2\n");

        Assert.True(MapGridManifest.TryRead(dir.Maps, out MapGridSetIdentity? identity, out string? reason));
        Assert.Null(reason);
        Assert.Equal(expected.SetHash, identity!.SetHash);
        Assert.Equal("fp-client", identity.ClientFingerprint);
    }

    [Fact]
    public void AMissingManifestIsTheRecordedIdentityRefusal()
    {
        using TempDir dir = TempDir.Create();
        Assert.False(MapGridManifest.TryRead(dir.Maps, out MapGridSetIdentity? identity, out string? reason));
        Assert.Null(identity);
        Assert.Equal(MapGridManifest.ManifestMissing, reason);
    }

    [Fact]
    public void ObserveWithAWorldReadingLoadsTheGridAndDoesNotWriteIt()
    {
        using TempDir dir = TempDir.Create();
        byte[] file = BuildGridFile(2, 1, 0x00, 0x01);
        string path = Path.Combine(dir.Maps, "9.grid");
        File.WriteAllBytes(path, file);
        byte[] before = File.ReadAllBytes(path);

        var world = new MapWorldReading(
            ClassifiedValue<int>.Live(9),
            ClassifiedValue<int>.Live(0),
            ClassifiedValue<int>.Live(0));

        MapView view = MapInspect.Observe(
            SessionKind.Hosted,
            world,
            dir.Maps);

        Assert.Equal(StandingCellKind.Walkable, view.StandingKind);
        DisplayField dimensions = Assert.Single(view.Fields, f => f.Label == "Dimensioni");
        Assert.Contains("2x1", dimensions.Value, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void TheMapViewHasNoWritePathIntoTheRuntime()
    {
        string root = RepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "src", "NosAi.ControlPanel", "MapInspect.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "MapGridManifest.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "GameplayWireReader.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "SnapshotView.cs")
        ];

        string[] forbidden =
        [
            "TryBeginActuation",
            "GatedInputBackend",
            "Win32InputBackend",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "PostMessage",
            "WriteProcessMemory",
            "RequestHalt",
            "ImmediateHalt",
            "/api/command",
            "File.Write",
            "WriteAllBytes",
            "WriteAllText",
            "ArmInput",
            "--step",
            "--halt"
        ];

        foreach (string path in files)
        {
            string source = File.ReadAllText(path);
            foreach (string token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }

        string xaml = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"NavMap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ViewMap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandingCellText\"", xaml, StringComparison.Ordinal);
        int mapStart = xaml.IndexOf("x:Name=\"ViewMap\"", StringComparison.Ordinal);
        int mapEnd = xaml.IndexOf("x:Name=\"ViewPhone\"", StringComparison.Ordinal);
        Assert.True(mapStart > 0 && mapEnd > mapStart);
        string mapView = xaml[mapStart..mapEnd];
        Assert.DoesNotContain("<Button", mapView, StringComparison.Ordinal);

        string inspect = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MapInspect.cs"));
        Assert.DoesNotContain("TryAttach", inspect, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientMemorySession", inspect, StringComparison.Ordinal);
        Assert.DoesNotContain("MapWorldReader", inspect, StringComparison.Ordinal);

        string window = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml.cs"));
        Assert.Contains("snapshot.MapWorld", window, StringComparison.Ordinal);
        Assert.DoesNotContain("MapWorldReader", window, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientMemorySession.TryAttach", window, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostedSnapshotCopiesMapIdAndStandingCell()
    {
        DateTime at = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var observation = new GameplayObservation(
            ClassifiedValue<int>.Derived(100, at),
            ClassifiedValue<int>.Derived(100, at),
            ClassifiedValue<int>.Derived(50, at),
            ClassifiedValue<int>.Derived(50, at),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("not_counted"),
            at)
        {
            MapId = ClassifiedValue<int>.Live(7, at),
            StandingCell = ClassifiedValue<MapPoint>.Live(new MapPoint(12, 8), at),
        };

        SnapshotView snapshot = SnapshotView.From(Hosted(observation));
        MapView view = MapInspect.Observe(SessionKind.Hosted, snapshot.MapWorld);

        Assert.Equal(7, snapshot.MapWorld.MapId.Value);
        Assert.Equal(DataSourceKind.Live, snapshot.MapWorld.MapId.Source);
        Assert.Equal(12, snapshot.MapWorld.CellX.Value);
        Assert.Equal(8, snapshot.MapWorld.CellY.Value);
        Assert.Equal(StandingCellKind.GridUnknown, view.StandingKind);
        Assert.Contains("UNKNOWN", view.StandingLine, StringComparison.Ordinal);
        DisplayField id = Assert.Single(view.Fields, f => f.Label == "Id mappa");
        Assert.Contains("7", id.Value, StringComparison.Ordinal);
        Assert.Equal("LIVE", id.Source);
        DisplayField provenance = Assert.Single(view.Fields, f => f.Label == "Provenienza id mappa");
        Assert.Contains(MapInspect.MapIdProvenanceLive, provenance.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttachedSnapshotParsesMapIdAndStandingCell()
    {
        SnapshotView snapshot = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "gameplayBaseline": {
                  "source": "DERIVED",
                  "hasObservedValue": true,
                  "value": {
                    "mapId": {
                      "source": "LIVE",
                      "hasObservedValue": true,
                      "value": 9
                    },
                    "standingCell": {
                      "source": "LIVE",
                      "hasObservedValue": true,
                      "value": { "x": 1, "y": 2 }
                    }
                  }
                }
              }
            }
            """);

        Assert.Equal(9, snapshot.MapWorld.MapId.Value);
        Assert.Equal(1, snapshot.MapWorld.CellX.Value);
        Assert.Equal(2, snapshot.MapWorld.CellY.Value);
        Assert.Equal(DataSourceKind.Live, snapshot.MapWorld.MapId.Source);
        Assert.Equal(DataSourceKind.Live, snapshot.MapWorld.CellX.Source);
    }

    [Fact]
    public void AnAttachedSnapshotWithoutMapKeysKeepsTheNamedUnknown()
    {
        SnapshotView snapshot = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "gameplayBaseline": {
                  "source": "DERIVED",
                  "hasObservedValue": true,
                  "value": {
                    "entities": { "source": "UNKNOWN", "failureReason": "not_published_by_provider" }
                  }
                }
              }
            }
            """);

        Assert.False(snapshot.MapWorld.MapId.HasValue);
        Assert.Equal(GameplayObservation.MapIdNotReadReason, snapshot.MapWorld.MapId.FailureReason);
        Assert.False(snapshot.MapWorld.CellX.HasValue);
        Assert.Equal(GameplayObservation.StandingCellNotReadReason, snapshot.MapWorld.CellX.FailureReason);
        Assert.Equal(GameplayObservation.StandingCellNotReadReason, snapshot.MapWorld.CellY.FailureReason);
        Assert.All(MapInspect.Observe(SessionKind.Attached, snapshot.MapWorld).Crop,
            cell => Assert.Equal(MapCellDraw.Unknown, cell));
    }

    [Fact]
    public void AnEmptySnapshotMapWorldIsUnknownAndNotAFreeCell()
    {
        SnapshotView snapshot = SnapshotView.Empty("offline");
        MapView view = MapInspect.Observe(SessionKind.Idle, snapshot.MapWorld);

        Assert.False(snapshot.MapWorld.MapId.HasValue);
        Assert.Equal("runtime_not_connected", snapshot.MapWorld.MapId.FailureReason);
        Assert.All(view.Crop, cell => Assert.Equal(MapCellDraw.Unknown, cell));
        Assert.DoesNotContain(MapInspect.WalkableGlyph, view.CropGlyphs);
    }

    private static Gate1CanonicalSnapshot Hosted(GameplayObservation observation) =>
        Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            new ClientBaselineSnapshot(
                ProcessDetected: false,
                WindowDetected: false,
                ClientAttached: false,
                ProcessId: null,
                WindowHandle: IntPtr.Zero,
                Source: "test",
                ObservedAtUtc: observation.ObservedAtUtc,
                Availability: ClientBaselineAvailability.Unavailable,
                Status: "client_unavailable",
                Warning: null,
                FailureReason: "connector_not_bound"),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: observation);

    private static MapView Standing(MapGrid grid, int x, int y) => MapInspect.Build(
        ClassifiedValue<int>.Live(grid.MapId),
        ClassifiedValue<int>.Live(x),
        ClassifiedValue<int>.Live(y),
        grid,
        null,
        null,
        null,
        null);

    private static MapCellDraw Center(MapView view)
        => view.Crop[(MapInspect.CropRadius * MapInspect.CropSize) + MapInspect.CropRadius];

    private static MapCellDraw At(MapView view, int dx, int dy)
        => view.Crop[((MapInspect.CropRadius + dy) * MapInspect.CropSize) + (MapInspect.CropRadius + dx)];

    private static MapGrid Grid(int mapId, int width, int height, params byte[] cells)
        => new(mapId, width, height, cells);

    private static byte[] BuildGridFile(int width, int height, params byte[] cells)
    {
        var file = new byte[MapGridFormat.HeaderBytes + cells.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)height);
        cells.CopyTo(file.AsSpan(MapGridFormat.HeaderBytes));
        return file;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }
        public string Maps => Path.Combine(Root, "maps");

        private TempDir(string root) => Root = root;

        public static TempDir Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "nosai-panel-map-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "maps"));
            return new TempDir(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
