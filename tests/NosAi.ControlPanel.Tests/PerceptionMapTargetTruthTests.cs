using System.Globalization;
using System.IO;
using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// The five straight facts the 2026-09-08 audit found wrong in the perception,
/// map and target views: the HUD-crop row is LIVE even when nothing was written,
/// one constant reason is false in its branch, the grid identity can never be
/// shown intact, the observation instant is rebuilt with the panel clock, and
/// two read facts (ROI fractions and process id) are never shown.
/// </summary>
public sealed class PerceptionMapTargetTruthTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-panel-truth-" + Guid.NewGuid().ToString("N"));

    public PerceptionMapTargetTruthTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Hud_crop_row_is_unknown_when_nothing_was_written_and_live_with_its_instant_when_it_was()
    {
        DisplayField empty = PerceptionProbe.HudCropField(Array.Empty<HudCropWrite>());
        Assert.Equal("UNKNOWN", empty.Source);
        Assert.Contains("crop_not_saved", empty.Value, StringComparison.Ordinal);

        var at = new DateTime(2026, 9, 8, 2, 0, 0, DateTimeKind.Utc);
        DisplayField written = PerceptionProbe.HudCropField(
        [
            new HudCropWrite("hp_latest.bmp", at),
            new HudCropWrite("mp_latest.bmp", at.AddSeconds(1))
        ]);
        Assert.Equal("LIVE", written.Source);
        Assert.Contains("hp_latest.bmp 2026-09-08 02:00:00 UTC", written.Value, StringComparison.Ordinal);
        Assert.Contains("mp_latest.bmp 2026-09-08 02:00:01 UTC", written.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveCrops_reports_only_the_crops_that_have_pixels()
    {
        var frame = new CaptureFrame(200, 200, new byte[200 * 200 * 4], DataSourceKind.Simulated, DateTime.UtcNow);
        ScreenBarFill bar = ScreenDerivedBarGate.Unknown(0, "test");
        ScreenVitalPair pair = ScreenDerivedVitalGate.Unknown(0, "test");

        var inside = new ScreenVitalObservation(
            new PixelRect(10, 10, 20, 10),
            new PixelRect(10, 20, 20, 10),
            bar, bar, pair, pair, 0, 0, 0);
        IReadOnlyList<HudCropWrite> written = HudCropWriter.SaveCrops(_dir, frame, inside);
        Assert.Equal(2, written.Count);
        Assert.Contains(written, w => w.FileName == "hp_latest.bmp");
        Assert.Contains(written, w => w.FileName == "mp_latest.bmp");
        Assert.All(written, w => Assert.True(w.WrittenAtUtc != default));

        var outside = new ScreenVitalObservation(
            new PixelRect(500, 500, 20, 10),
            new PixelRect(500, 520, 20, 10),
            bar, bar, pair, pair, 0, 0, 0);
        Assert.Empty(HudCropWriter.SaveCrops(_dir, frame, outside));
    }

    [Fact]
    public void No_frame_branch_names_the_frame_not_the_glyphs()
    {
        DisplayField[] fields = PerceptionProbe.VitalUnknownFields();
        DisplayField hp = Assert.Single(fields, f => f.Label == "HP attuale");
        DisplayField max = Assert.Single(fields, f => f.Label == "HP massimo");

        Assert.Contains("no_frame_within_budget", hp.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ocr_glyphs_not_trained", hp.Value, StringComparison.Ordinal);
        Assert.Contains("no_frame_within_budget", max.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ocr_glyphs_not_trained", max.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void True_constant_reasons_are_still_produced_where_they_belong()
    {
        ClientWindowLookup empty = PerceptionProbe.LocateClientWindow("");
        Assert.Null(empty.Window);
        Assert.Equal("client_process_name_empty", empty.FailureReason);

        ClientWindowLookup missing = PerceptionProbe.LocateClientWindow("nosai_no_such_process_7f3a1c9e");
        Assert.Null(missing.Window);
        Assert.Equal("client_process_not_running", missing.FailureReason);

        var atlas = new HudGlyphAtlas();
        DisplayField unknownPath = PerceptionProbe.AtlasField(atlas, "atlas_path_unknown");
        Assert.Equal("UNKNOWN", unknownPath.Source);
        Assert.Contains("atlas_path_unknown", unknownPath.Value, StringComparison.Ordinal);

        DisplayField notTrained = PerceptionProbe.AtlasField(atlas, "atlas_not_trained_yet");
        Assert.Contains("atlas_not_trained_yet", notTrained.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Intact_grids_with_unknown_client_differ_from_tampered_grids()
    {
        using TempDir dir = TempDir.Create();
        byte[] payload = "grid-payload"u8.ToArray();
        string sha = MapGridSetIdentity.HashFile(payload);
        File.WriteAllText(
            Path.Combine(dir.Maps, MapGridExtractor.ManifestFileName),
            $"{MapGridExtractor.ManifestMagic} {MapGridExtractor.ManifestVersion}\n" +
            "fingerprint fp-client\n" +
            $"9 {sha} 2 2\n");
        File.WriteAllBytes(Path.Combine(dir.Maps, "9.grid"), payload);

        Assert.True(MapGridManifest.TryRead(dir.Maps, out MapGridSetIdentity? recorded, out _));
        MapGridSetDiskCheck intact = MapGridManifest.CheckIntact(dir.Maps, recorded);
        Assert.Equal(MapGridSetDiskState.Intact, intact.State);

        MapView intactView = MapInspect.Build(
            ClassifiedValue<int>.Live(9), ClassifiedValue<int>.Live(0), ClassifiedValue<int>.Live(0),
            Grid(9, 1, 1, 0x00), null, sha, recorded, currentIdentity: null, intact);
        DisplayField intactField = Assert.Single(intactView.Fields, f => f.Label == "Griglie intatte");
        Assert.Equal("CACHED", intactField.Source);
        Assert.Contains("sì", intactField.Value, StringComparison.Ordinal);

        File.WriteAllBytes(Path.Combine(dir.Maps, "9.grid"), "tampered-payload"u8.ToArray());
        MapGridSetDiskCheck tampered = MapGridManifest.CheckIntact(dir.Maps, recorded);
        Assert.Equal(MapGridSetDiskState.Changed, tampered.State);

        MapView tamperedView = MapInspect.Build(
            ClassifiedValue<int>.Live(9), ClassifiedValue<int>.Live(0), ClassifiedValue<int>.Live(0),
            Grid(9, 1, 1, 0x00), null, sha, recorded, currentIdentity: null, tampered);
        DisplayField tamperedField = Assert.Single(tamperedView.Fields, f => f.Label == "Griglie intatte");
        Assert.Equal("DERIVED", tamperedField.Source);
        Assert.Contains("no · map_grid_set_changed:", tamperedField.Value, StringComparison.Ordinal);

        Assert.NotEqual(intactField.Value, tamperedField.Value);

        DisplayField verified = Assert.Single(intactView.Fields, f => f.Label == "Identità verificata");
        Assert.Equal("UNKNOWN", verified.Source);
        Assert.Contains("map_grids_current_identity_unknown", verified.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Cached_value_carries_the_explicit_observed_instant_not_now()
    {
        var observed = new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var json = """
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "processId": { "value": 2484, "source": "CACHED", "observedAtUtc": "2000-01-02T03:04:05.0000000Z" }
              },
              "gameObservation": {
                "lastHp": { "value": 100, "source": "CACHED", "observedAtUtc": "2000-01-02T03:04:05.0000000Z" },
                "lastMaxHp": { "value": 120, "source": "CACHED", "observedAtUtc": "2000-01-02T03:04:05.0000000Z" }
              }
            }
            """;

        SnapshotView view = AttachedSnapshot.Parse(json);
        Assert.Equal(observed, view.ClientProcessId.ObservedAtUtc);
        Assert.Equal(observed, view.ObservationLastHp.ObservedAtUtc);
        Assert.Equal(observed, view.ObservationLastMaxHp.ObservedAtUtc);
        Assert.Equal(100, view.ObservationLastHp.Value);
    }

    [Fact]
    public void Cached_value_without_an_observed_instant_is_not_marked_fresh()
    {
        var json = """
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "processId": { "value": 2484, "source": "CACHED" }
              }
            }
            """;

        SnapshotView view = AttachedSnapshot.Parse(json);
        Assert.False(view.ClientProcessId.HasValue);
        Assert.Equal("cached_observed_at_missing", view.ClientProcessId.FailureReason);
    }

    [Fact]
    public void Roi_fractions_are_shown()
    {
        var at = new DateTime(2026, 9, 7, 15, 0, 0, DateTimeKind.Utc);
        string roiPath = Path.Combine(_dir, "target-roi.calibration");
        TargetRoiCalibration.Confirmed(0.3789, 0.5052, 0.2246, 0.2995, 1024, 768, at).Save(roiPath);
        string candidatePath = Path.Combine(_dir, "absent.txt");

        TargetHuntView view = TargetInspect.Inspect(candidatePath, roiPath);

        Assert.Contains("x=0.3789", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains("y=0.5052", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains("w=0.2246", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains("h=0.2995", view.RoiLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_id_is_shown()
    {
        string candidatePath = WriteCandidates(2484, "manager 40 313906 -1");
        string roiPath = Path.Combine(_dir, "no-roi");

        TargetHuntView view = TargetInspect.Inspect(candidatePath, roiPath);

        DisplayField process = Assert.Single(view.Fields, f => f.Label == TargetInspect.ProcessLabel);
        Assert.Equal("CACHED", process.Source);
        Assert.Contains("2484", process.Value, StringComparison.Ordinal);
    }

    private string WriteCandidates(int processId, params string[] hits)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("# nosai target-id candidates (ADR-0021)");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"selections={1}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"restarts={0}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"process={processId}"));
        text.AppendLine("cleared=0");
        foreach (string hit in hits)
            text.AppendLine(hit);

        string path = Path.Combine(_dir, "candidates.txt");
        File.WriteAllText(path, text.ToString());
        return path;
    }

    private static MapGrid Grid(int mapId, int width, int height, params byte[] cells)
        => new(mapId, width, height, cells);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }
        public string Maps => Path.Combine(Root, "maps");

        private TempDir(string root) => Root = root;

        public static TempDir Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "nosai-panel-truth-dir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "maps"));
            return new TempDir(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
