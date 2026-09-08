using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Verifies that TargetRoiAutoCalibrator (probe T-09) derives, verifies and persists its target ROI
/// when fed only hand-painted synthetic frames and injected target labels, with no client, screen,
/// GPU or network involved.
/// </summary>
public sealed class TargetRoiAutoCalibratorTests
{
    private const int FrameWidth = 400;
    private const int FrameHeight = 300;
    private const int TargetX = 100;
    private const int TargetY = 50;
    private const int TargetWidth = 120;
    private const int TargetHeight = 16;

    private static readonly PixelRect ClientArea = new(0, 0, FrameWidth, FrameHeight);
    private static readonly DateTime T0 = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DerivaIlRettangoloNotoEChiudeQuandoLaVerificaPassa()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            calibrator.Offer(WithTarget(T0.AddSeconds(0)), true, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(1)), true, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(2)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(3)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(4)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(5)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(6)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(7)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(8)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(9)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(10)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(11)), false, ClientArea);

            Assert.NotNull(calibrator.Proposed);
            PixelRect proposed = calibrator.Proposed!.Value;
            Assert.InRange(proposed.X, TargetX - 2, TargetX + 2);
            Assert.InRange(proposed.Y, TargetY - 2, TargetY + 2);
            Assert.InRange(proposed.Width, TargetWidth - 2, TargetWidth + 2);
            Assert.InRange(proposed.Height, TargetHeight - 2, TargetHeight + 2);
            Assert.Equal(0, calibrator.Contradictions);
            Assert.True(calibrator.VerifiedPresent >= 3);
            Assert.True(calibrator.VerifiedAbsent >= 3);
            Assert.Equal(TargetRoiCalibrationState.Confirmed, calibrator.State);

            string? writtenPath = calibrator.WrittenPath;
            Assert.NotNull(writtenPath);
            Assert.True(File.Exists(writtenPath!));
            Assert.Equal("nosai-target-roi 1", File.ReadLines(writtenPath!).First());

            Assert.True(calibrator.BuildEvidence(T0).Complete);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void SenzaVerificaSuperataNonScriveLaCalibrazione()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(0)), true, ClientArea);
            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(1)), true, ClientArea);
            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(2)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(3)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(4)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(5)), false, ClientArea);
            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(6)), true, ClientArea);
            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(7)), true, ClientArea);
            calibrator.Offer(WithNoisyTarget(T0.AddSeconds(8)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(9)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(10)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(11)), false, ClientArea);

            Assert.Null(calibrator.WrittenPath);
            Assert.False(File.Exists(calibrationPath));
            Assert.NotEqual(TargetRoiCalibrationState.Confirmed, calibrator.State);
            Assert.Equal(0, calibrator.VerifiedPresent);

            DeferredEvidenceRecord evidence = calibrator.BuildEvidence(T0);
            Assert.False(evidence.Complete);
            Assert.NotNull(evidence.IncompleteReason);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void BersaglioMaiSelezionatoRestaInAttesaEDiceIConteggi()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            for (int i = 0; i < 6; i++)
            {
                calibrator.Offer(WithoutTarget(T0.AddSeconds(i)), false, ClientArea);
            }

            Assert.Equal(TargetRoiCalibrationState.Waiting, calibrator.State);
            Assert.Equal(0, calibrator.FramesWithTarget);
            Assert.True(calibrator.FramesWithoutTarget >= 3);
            Assert.Null(calibrator.Proposed);
            Assert.False(File.Exists(calibrationPath));

            DeferredEvidenceRecord evidence = calibrator.BuildEvidence(T0);
            Assert.NotNull(evidence.IncompleteReason);
            Assert.Contains("0", evidence.IncompleteReason!);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void EtichettaSconosciutaNonEntraInNessunGruppo()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            for (int i = 0; i < 3; i++)
            {
                calibrator.Offer(WithTarget(T0.AddSeconds(i)), null, ClientArea);
            }

            Assert.Equal(0, calibrator.FramesWithTarget);
            Assert.Equal(0, calibrator.FramesWithoutTarget);
            Assert.Equal(3, calibrator.Rejections["target_label_unknown"]);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void FrameSenzaPrimoPianoNonEntraInNessunGruppo()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            calibrator.Offer(WithTarget(T0.AddSeconds(0)), true, ClientArea, clientInForeground: false);

            Assert.Equal(0, calibrator.FramesWithTarget);
            Assert.Equal(1, calibrator.Rejections["client_not_foreground"]);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void CambioDiGeometriaAzzeraGliAccumulatori()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            calibrator.Offer(WithTarget(T0.AddSeconds(0)), true, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(1)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(2)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(3)), false, ClientArea);
            calibrator.Offer(Frame640x480(T0.AddSeconds(4)), false, new PixelRect(0, 0, 640, 480));

            Assert.Equal(1, calibrator.Rejections["frame_geometry_changed"]);
            Assert.Equal(TargetRoiCalibrationState.Waiting, calibrator.State);
            Assert.True(calibrator.FramesWithTarget + calibrator.FramesWithoutTarget <= 1);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Fact]
    public void EvidenzaT09VieneScrittaConIlPuntatoreLatest()
    {
        (string directory, string calibrationPath) = CreateCalibrationPath();
        string evidenceRoot = Path.Combine(Path.GetTempPath(), "nosai-t09-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

            calibrator.Offer(WithTarget(T0.AddSeconds(0)), true, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(1)), true, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(2)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(3)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(4)), false, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(5)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(6)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(7)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(8)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(9)), false, ClientArea);
            calibrator.Offer(WithTarget(T0.AddSeconds(10)), true, ClientArea);
            calibrator.Offer(WithoutTarget(T0.AddSeconds(11)), false, ClientArea);

            string evidencePath = DeferredEvidence.Write(calibrator.BuildEvidence(T0), evidenceRoot);

            Assert.True(File.Exists(evidencePath));
            DeferredEvidencePointer? pointer = DeferredEvidence.ReadPointer("T-09", evidenceRoot);
            Assert.NotNull(pointer);
            DeferredEvidenceRecord? latest = DeferredEvidence.ReadLatest("T-09", evidenceRoot);
            Assert.NotNull(latest);
            Assert.Equal("T-09", latest!.TestId);
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
            DeleteDirectoryIfPresent(evidenceRoot);
        }
    }

    /// <summary>
    /// Paints a 400x300 frame whose known 120x16 target box is completely filled with the
    /// recognized fill colour.
    /// </summary>
    private static CaptureFrame WithTarget(DateTime at)
    {
        return TargetFrame(at, noisy: false);
    }

    /// <summary>
    /// Paints a 400x300 frame that contains only background, so no target box is visible.
    /// </summary>
    private static CaptureFrame WithoutTarget(DateTime at)
    {
        byte[] bgra = EmptyBackground(FrameWidth, FrameHeight);
        return new CaptureFrame(FrameWidth, FrameHeight, bgra, DataSourceKind.Live, at);
    }

    /// <summary>
    /// Paints a 400x300 frame whose target box is a checkerboard: the fill colour only appears
    /// where (x + y) is even, which the ROI reader rejects as a noisy profile (Unreadable).
    /// </summary>
    private static CaptureFrame WithNoisyTarget(DateTime at)
    {
        return TargetFrame(at, noisy: true);
    }

    /// <summary>
    /// Paints a 640x480 background-only frame used to prove that a geometry change resets the
    /// calibration accumulators.
    /// </summary>
    private static CaptureFrame Frame640x480(DateTime at)
    {
        const int width = 640;
        const int height = 480;
        byte[] bgra = EmptyBackground(width, height);
        return new CaptureFrame(width, height, bgra, DataSourceKind.Live, at);
    }

    private static CaptureFrame TargetFrame(DateTime at, bool noisy)
    {
        byte[] bgra = EmptyBackground(FrameWidth, FrameHeight);
        for (int x = TargetX; x < TargetX + TargetWidth; x++)
        {
            for (int y = TargetY; y < TargetY + TargetHeight; y++)
            {
                if (noisy && (x + y) % 2 != 0)
                {
                    continue;
                }

                int index = (y * FrameWidth + x) * 4;
                bgra[index] = 0;
                bgra[index + 1] = 200;
                bgra[index + 2] = 0;
                bgra[index + 3] = 255;
            }
        }

        return new CaptureFrame(FrameWidth, FrameHeight, bgra, DataSourceKind.Live, at);
    }

    private static byte[] EmptyBackground(int width, int height)
    {
        byte[] bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 3] = 255;
        }

        return bgra;
    }

    private static (string Directory, string CalibrationPath) CreateCalibrationPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nosai-t09-" + Guid.NewGuid().ToString("N"));
        string calibrationPath = Path.Combine(directory, "target-roi.calibration");
        return (directory, calibrationPath);
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
