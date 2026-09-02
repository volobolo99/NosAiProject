using System.Globalization;
using System.IO;
using NosAi.ControlPanel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Target-hunt view: a missing file is not zero candidates, a missing
/// no-target pass is drawn as missing, Advice is TargetIdFinder.Advice,
/// and an uncalibrated ROI is the second source rather than a combat error.
/// </summary>
public sealed class TargetInspectTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-panel-target-" + Guid.NewGuid().ToString("N"));

    public TargetInspectTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AMissingCandidateFileIsHuntNotStartedAndNotZeroCandidates()
    {
        string candidates = Path.Combine(_dir, "absent.txt");
        string roi = Path.Combine(_dir, "absent.calibration");

        TargetHuntView view = TargetInspect.Inspect(candidates, roi);

        Assert.Equal(TargetHuntKind.NotStarted, view.HuntKind);
        Assert.Equal(TargetInspect.HuntNotStartedLabel, view.HuntStatusLine);
        Assert.DoesNotContain("0 candidati", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.False(view.ClearedPassMissing);
        Assert.DoesNotContain(TargetInspect.ClearedMissingLabel, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.HuntNotStartedLabel, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.HuntNotStartedLabel, view.AdviceLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Nessun candidato", view.AdviceLine, StringComparison.Ordinal);
        Assert.All(
            view.Fields.Where(f => f.Label != TargetInspect.RoiLabel),
            f =>
            {
                Assert.Equal("UNKNOWN", f.Source);
                Assert.Contains(TargetInspect.HuntNotStartedLabel, f.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("0 [", f.Value, StringComparison.Ordinal);
            });
        Assert.False(File.Exists(candidates));
    }

    [Fact]
    public void AFileWithCandidatesShowsCountsAndCallsAdvice()
    {
        string path = WriteCandidates(
            selections: 2,
            restarts: 1,
            cleared: true,
            processId: 7932,
            "manager 40 313906 -1",
            "heap DEAD 3205 -1");
        string roi = Path.Combine(_dir, "no-roi");

        TargetHuntView view = TargetInspect.Inspect(path, roi);

        Assert.Equal(TargetHuntKind.InProgress, view.HuntKind);
        Assert.Contains("2 candidati", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.Contains("1 ancorati", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.Contains($"2/{TargetIdFinder.RequiredSelections}", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.Contains("1/1", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.False(view.ClearedPassMissing);
        Assert.Contains(TargetInspect.ClearedDoneLabel, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetInspect.ClearedMissingLabel, view.ClearedPassLine, StringComparison.Ordinal);

        string expected = TargetIdFinder.Advice(count: 2, durable: 1, selections: 2, restarts: 1, sawCleared: true);
        Assert.Equal(expected, view.AdviceLine);
        Assert.Equal(expected, Field(view, TargetInspect.AdviceLabel).Value);
        Assert.Equal("DERIVED", Field(view, TargetInspect.AdviceLabel).Source);
        Assert.Equal("2 [CACHED]", Field(view, TargetInspect.CandidatesLabel).Value);
        Assert.Equal("1 [CACHED]", Field(view, TargetInspect.AnchoredLabel).Value);
        Assert.Equal($"2/{TargetIdFinder.RequiredSelections} [CACHED]", Field(view, TargetInspect.SelectionsLabel).Value);
        Assert.Equal("1/1 [CACHED]", Field(view, TargetInspect.RestartsLabel).Value);
        Assert.Equal(TargetInspect.ClearedDoneLabel, Field(view, TargetInspect.ClearedLabel).Value);
    }

    [Fact]
    public void AZeroCandidateFileIsNotHuntNotStartedAndAdviceIsTheEmptySetSentence()
    {
        string path = WriteCandidates(selections: 3, restarts: 1, cleared: true, processId: 1);
        string roi = Path.Combine(_dir, "no-roi");

        TargetHuntView view = TargetInspect.Inspect(path, roi);

        Assert.Equal(TargetHuntKind.ZeroCandidates, view.HuntKind);
        Assert.NotEqual(TargetInspect.HuntNotStartedLabel, view.HuntStatusLine);
        Assert.DoesNotContain(TargetInspect.HuntNotStartedLabel, view.HuntStatusLine, StringComparison.Ordinal);
        Assert.Contains("0 candidati", view.HuntStatusLine, StringComparison.Ordinal);
        Assert.Equal("0 [CACHED]", Field(view, TargetInspect.CandidatesLabel).Value);

        string expected = TargetIdFinder.Advice(count: 0, durable: 0, selections: 3, restarts: 1, sawCleared: true);
        Assert.Equal(expected, view.AdviceLine);
        Assert.Contains("Nessun candidato", view.AdviceLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingClearedPassIsDrawnAsMissingAndBlocksTheAdviceOrder()
    {
        string path = WriteCandidates(
            selections: 1,
            restarts: 0,
            cleared: false,
            processId: 100,
            "manager 40 313906 -1",
            "module 10 313906");
        string roi = Path.Combine(_dir, "no-roi");

        TargetHuntView view = TargetInspect.Inspect(path, roi);

        Assert.Equal(TargetHuntKind.InProgress, view.HuntKind);
        Assert.True(view.ClearedPassMissing);
        Assert.Contains(TargetInspect.ClearedMissingLabel, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.ClearedPassWhy, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetInspect.ClearedDoneLabel, view.ClearedPassLine, StringComparison.Ordinal);
        Assert.Equal(TargetInspect.ClearedMissingLabel, Field(view, TargetInspect.ClearedLabel).Value);

        string expected = TargetIdFinder.Advice(count: 2, durable: 2, selections: 1, restarts: 0, sawCleared: false);
        Assert.Equal(expected, view.AdviceLine);
        Assert.Contains("TORNARE", view.AdviceLine, StringComparison.Ordinal);
        Assert.DoesNotContain("selezioni diverse", view.AdviceLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentCalibrationIsNotCalibratedAndIsNotACombatError()
    {
        string candidates = Path.Combine(_dir, "absent.txt");
        string roi = Path.Combine(_dir, "absent.calibration");

        TargetHuntView view = TargetInspect.Inspect(candidates, roi);

        Assert.Equal(TargetRoiKind.NotCalibrated, view.RoiKind);
        Assert.False(view.RoiIsError);
        Assert.Contains(TargetInspect.RoiNotCalibratedLabel, view.RoiLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.IndependentSourceNote, view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain("ERRORE", view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain("HasTarget resta UNKNOWN", view.RoiLine, StringComparison.Ordinal);
        DisplayField field = Field(view, TargetInspect.RoiLabel);
        Assert.Equal("UNKNOWN", field.Source);
        Assert.Contains(TargetInspect.RoiNotCalibratedLabel, field.Value, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.IndependentSourceNote, field.Value, StringComparison.Ordinal);
        Assert.False(field.Value.StartsWith("UNKNOWN ·", StringComparison.Ordinal));
        Assert.False(File.Exists(roi));
    }

    [Fact]
    public void APresentCalibrationNamesWhenAndWhichResolutionAndStaysTheSecondSource()
    {
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        string roiPath = Path.Combine(_dir, "target-roi.calibration");
        TargetRoiCalibration.Confirmed(0.40, 0.06, 0.20, 0.02, 1920, 1080, at).Save(roiPath);
        string candidates = Path.Combine(_dir, "absent.txt");

        TargetHuntView view = TargetInspect.Inspect(candidates, roiPath);

        Assert.Equal(TargetRoiKind.Calibrated, view.RoiKind);
        Assert.False(view.RoiIsError);
        Assert.Contains("1920x1080", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains("2026-09-01 12:00:00 UTC", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.IndependentSourceNote, view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetInspect.RoiNotCalibratedLabel, view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain("ERRORE", view.RoiLine, StringComparison.Ordinal);
        DisplayField field = Field(view, TargetInspect.RoiLabel);
        Assert.Equal("CACHED", field.Source);
        Assert.Contains("1920x1080", field.Value, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.IndependentSourceNote, field.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedCalibrationIsUnreadableAndStillNotACombatError()
    {
        string roiPath = Path.Combine(_dir, "target-roi.calibration");
        File.WriteAllText(roiPath, "garbage");
        string candidates = Path.Combine(_dir, "absent.txt");

        TargetHuntView view = TargetInspect.Inspect(candidates, roiPath);

        Assert.Equal(TargetRoiKind.Unreadable, view.RoiKind);
        Assert.False(view.RoiIsError);
        Assert.Contains("UNKNOWN", view.RoiLine, StringComparison.Ordinal);
        Assert.Contains(TargetInspect.IndependentSourceNote, view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain("ERRORE", view.RoiLine, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetInspect.RoiNotCalibratedLabel, view.RoiLine, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectDoesNotCreateMissingFiles()
    {
        string candidates = Path.Combine(_dir, "uncreated.txt");
        string roi = Path.Combine(_dir, "perception", "target-roi.calibration");

        TargetInspect.Inspect(candidates, roi);

        Assert.False(File.Exists(candidates));
        Assert.False(File.Exists(roi));
        Assert.False(Directory.Exists(Path.Combine(_dir, "perception")));
    }

    [Fact]
    public void SignatureChangesWhenACandidateFileAppearsAndNotWhenNothingDoes()
    {
        string candidates = Path.Combine(_dir, "sig.txt");
        string roi = Path.Combine(_dir, "sig.calibration");

        string absent = TargetInspect.Signature(candidates, roi);
        Assert.Equal(absent, TargetInspect.Signature(candidates, roi));

        WriteCandidates(selections: 1, restarts: 0, cleared: false, processId: 1, "manager 40 1 -1");
        File.Move(Path.Combine(_dir, "candidates.txt"), candidates);
        string present = TargetInspect.Signature(candidates, roi);
        Assert.NotEqual(absent, present);
        Assert.Equal(present, TargetInspect.Signature(candidates, roi));
    }

    [Fact]
    public void AProvenSetIsNamedProvenAndAdviceIsTheFoundSentence()
    {
        string path = WriteCandidates(
            selections: TargetIdFinder.RequiredSelections,
            restarts: 1,
            cleared: true,
            processId: 7,
            "manager 40 313906 -1");
        string roi = Path.Combine(_dir, "no-roi");

        TargetHuntView view = TargetInspect.Inspect(path, roi);

        Assert.Equal(TargetHuntKind.Proven, view.HuntKind);
        Assert.Equal(
            TargetIdFinder.Advice(count: 1, durable: 1, selections: TargetIdFinder.RequiredSelections, restarts: 1, sawCleared: true),
            view.AdviceLine);
        Assert.Contains("TROVATO", view.AdviceLine, StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewHasNoWritePathIntoTheRuntime()
    {
        string root = SurroundingsInspectTests.RepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "TargetInspect.cs"));
        SurroundingsInspectTests.AssertNoWrite(source);
        Assert.DoesNotContain("File.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.CreateDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetIdFinder.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetIdFinder.Save", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetRoiCalibration.Confirmed", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Save(", source, StringComparison.Ordinal);

        string xaml = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"NavTarget\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ViewTarget\"", xaml, StringComparison.Ordinal);
        int start = xaml.IndexOf("x:Name=\"ViewTarget\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("x:Name=\"ViewPhone\"", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        string view = xaml[start..end];
        Assert.DoesNotContain("<Button", view, StringComparison.Ordinal);

        string window = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml.cs"));
        Assert.Contains("ApplyTarget", window, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetIdFinder.Run", window, StringComparison.Ordinal);
        int applyAt = window.IndexOf("private void ApplyTarget()", StringComparison.Ordinal);
        int nextMethod = window.IndexOf("private static string ModeLabel", StringComparison.Ordinal);
        Assert.True(applyAt > 0 && nextMethod > applyAt);
        string applyTarget = window[applyAt..nextMethod];
        SurroundingsInspectTests.AssertNoWrite(applyTarget);
        Assert.DoesNotContain("Directory.CreateDirectory", applyTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", applyTarget, StringComparison.Ordinal);
    }

    private string WriteCandidates(int selections, int restarts, bool cleared, int processId, params string[] hits)
    {
        // Layout of TargetIdFinder.Format: the panel reads this file, it does not
        // call the internal writer, so the fixture is that writer’s text.
        var text = new System.Text.StringBuilder();
        text.AppendLine("# nosai target-id candidates (ADR-0021)");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"selections={selections}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"restarts={restarts}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"process={processId}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cleared={(cleared ? 1 : 0)}"));
        foreach (string hit in hits)
            text.AppendLine(hit);

        string path = Path.Combine(_dir, "candidates.txt");
        File.WriteAllText(path, text.ToString());
        return path;
    }

    private static DisplayField Field(TargetHuntView view, string label)
        => Assert.Single(view.Fields, f => f.Label == label);
}
