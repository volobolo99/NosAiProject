using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Verifies that OpportunisticBarProbe (probe T-03) reaches its terminal states and emits coherent
/// deferred evidence when fed only hand-painted synthetic frames and injected wire readings, with
/// no client, screen, GPU or network involved.
/// </summary>
public sealed class OpportunisticBarProbeTests
{
    private const int FrameWidth = 1000;
    private const int FrameHeight = 1000;
    private const int BarRoiX = 112;
    private const int BarRoiY = 36;
    private const int BarRoiWidth = 121;
    private const int BarRoiHeight = 15;

    private static readonly PixelRect ClientArea = new(0, 0, FrameWidth, FrameHeight);
    private static readonly DateTime T0 = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly WirePlayerVitals WireAtSixtyPercent = new(600, 1000, null, null, null, null, "stat");
    private static readonly WirePlayerVitals WireAtFull = new(1000, 1000, null, null, null, null, "stat");

    [Fact]
    public void TreeCampioniEntroTolleranzaChiudonoConfermata()
    {
        var probe = new OpportunisticBarProbe();

        probe.Offer(Frame(73, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null);
        probe.Offer(Frame(73, T0.AddSeconds(1)), WireAtSixtyPercent, ClientArea, atlas: null);
        probe.Offer(Frame(73, T0.AddSeconds(2)), WireAtSixtyPercent, ClientArea, atlas: null);

        Assert.Equal(BarProbeState.Confirmed, probe.State);
        Assert.True(probe.IsClosed);
        Assert.Equal(3, probe.Samples.Count);
        foreach (BarProbeSample sample in probe.Samples)
        {
            Assert.True(sample.Difference < 0.08, $"Sample difference {sample.Difference} is not below tolerance 0.08.");
        }
    }

    [Fact]
    public void DifferenzaOltreLaTolleranzaChiudeInDisaccordoEConservaICampioni()
    {
        var probe = new OpportunisticBarProbe();

        probe.Offer(Frame(24, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null);

        Assert.Equal(BarProbeState.Disagreed, probe.State);
        Assert.Single(probe.Samples);
        Assert.NotNull(probe.FailureReason);
        Assert.StartsWith("pixel_wire_disagree", probe.FailureReason!);

        DeferredEvidenceRecord evidence = probe.BuildEvidence(T0);
        Assert.False(evidence.Complete);
        Assert.Equal("1", evidence.Measurements["valid_samples"]);
        Assert.True(evidence.Measurements.ContainsKey("sample_1"));
    }

    [Fact]
    public void BarraSemprePienaNonChiudeELoDiceNellEvidenza()
    {
        var probe = new OpportunisticBarProbe();

        for (int i = 0; i < 5; i++)
        {
            probe.Offer(Frame(121, T0.AddSeconds(i)), WireAtFull, ClientArea, atlas: null);
        }

        Assert.Equal(BarProbeState.Waiting, probe.State);
        Assert.Empty(probe.Samples);
        Assert.Equal(5, probe.Rejections["bar_not_drained"]);

        DeferredEvidenceRecord evidence = probe.BuildEvidence(T0);
        Assert.NotNull(evidence.IncompleteReason);
        Assert.Contains("piena", evidence.IncompleteReason!);
    }

    [Fact]
    public void MisuraConFailureReasonNonProduceCampioneENonVieneContataComeZero()
    {
        var probe = new OpportunisticBarProbe();

        probe.Offer(Frame(0, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null);

        Assert.Empty(probe.Samples);
        Assert.Equal(1, probe.Rejections["no_bar_signature"]);
        Assert.Equal(BarProbeState.Waiting, probe.State);
    }

    [Fact]
    public void SenzaFiloNessunCampioneENessunAddestramento()
    {
        var probe = new OpportunisticBarProbe();

        for (int i = 0; i < 3; i++)
        {
            probe.Offer(Frame(73, T0.AddSeconds(i)), null, ClientArea, atlas: new HudGlyphAtlas());
        }

        Assert.Empty(probe.Samples);
        Assert.Equal(3, probe.Rejections["wire_unavailable"]);
        Assert.Null(probe.LastTraining);
        Assert.Equal(0, probe.AtlasLearnedTotal);
        Assert.Contains("seconda fonte", probe.PendingReason);
    }

    [Fact]
    public void FrameSenzaPrimoPianoNonProduceProva()
    {
        var probe = new OpportunisticBarProbe();

        probe.Offer(Frame(73, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null, clientInForeground: false);

        Assert.Empty(probe.Samples);
        Assert.Equal(1, probe.Rejections["client_not_foreground"]);
    }

    [Fact]
    public void CampioniDiGeometriaDiversaNonSiMescolano()
    {
        var probe = new OpportunisticBarProbe();

        probe.Offer(Frame(73, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null);
        probe.Offer(Frame(73, T0.AddSeconds(1)), WireAtSixtyPercent, ClientArea, atlas: null);
        probe.Offer(DifferentGeometryFrame(T0.AddSeconds(2)), WireAtSixtyPercent, new PixelRect(0, 0, 800, 600), atlas: null);

        Assert.Equal(BarProbeState.Waiting, probe.State);
        Assert.True(probe.Samples.Count <= 3);
    }

    [Fact]
    public void EvidenzaT03VieneScrittaConIlPuntatoreLatest()
    {
        string root = Path.Combine(Path.GetTempPath(), "nosai-t03-" + Guid.NewGuid().ToString("N"));
        try
        {
            var probe = new OpportunisticBarProbe();
            probe.Offer(Frame(73, T0.AddSeconds(0)), WireAtSixtyPercent, ClientArea, atlas: null);
            probe.Offer(Frame(73, T0.AddSeconds(1)), WireAtSixtyPercent, ClientArea, atlas: null);
            probe.Offer(Frame(73, T0.AddSeconds(2)), WireAtSixtyPercent, ClientArea, atlas: null);

            string evidencePath = DeferredEvidence.Write(probe.BuildEvidence(T0), root);

            Assert.True(File.Exists(evidencePath));
            DeferredEvidencePointer? pointer = DeferredEvidence.ReadPointer("T-03", root);
            Assert.NotNull(pointer);
            DeferredEvidenceRecord? latest = DeferredEvidence.ReadLatest("T-03", root);
            Assert.NotNull(latest);
            Assert.True(latest!.Complete);
            Assert.Equal("T-03", latest.TestId);
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    /// <summary>
    /// Paints a 1000x1000 frame whose 121-pixel-wide HP bar ROI keeps only its first
    /// <paramref name="filledColumns"/> columns in the recognized fill colour.
    /// </summary>
    private static CaptureFrame Frame(int filledColumns, DateTime at)
    {
        byte[] bgra = new byte[FrameWidth * FrameHeight * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 3] = 255;
        }

        for (int x = BarRoiX; x < BarRoiX + filledColumns; x++)
        {
            for (int y = BarRoiY; y < BarRoiY + BarRoiHeight; y++)
            {
                int index = (y * FrameWidth + x) * 4;
                bgra[index] = 0;
                bgra[index + 1] = 200;
                bgra[index + 2] = 0;
                bgra[index + 3] = 255;
            }
        }

        return new CaptureFrame(FrameWidth, FrameHeight, bgra, DataSourceKind.Live, at);
    }

    /// <summary>
    /// Paints an 800x600 background-only frame used to prove that samples coming from a different
    /// frame geometry never merge with the 1000x1000 ones.
    /// </summary>
    private static CaptureFrame DifferentGeometryFrame(DateTime at)
    {
        const int width = 800;
        const int height = 600;
        byte[] bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 3] = 255;
        }

        return new CaptureFrame(width, height, bgra, DataSourceKind.Live, at);
    }

    private static void DeleteDirectoryIfPresent(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
