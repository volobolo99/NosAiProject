using NosAi.Runtime.Navigation;
using NosAi.Runtime.Operator;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The sentence the operator acts on. It is tested and not merely read because
/// the wrong next step makes them perform the wrong experiment, and an
/// experiment performed for the wrong reason still produces a number.
/// </summary>
public sealed class OperatorMenuTests
{
    private static MapIdProgress Progress(
        bool mapsReady = true,
        bool hasFile = true,
        int candidates = 1,
        int anchored = 1,
        int passes = 1,
        int restarts = 0,
        string? winner = null)
        => new(mapsReady, Grids: 777, hasFile, candidates, anchored, passes, restarts, winner);

    [Fact]
    public void WithoutTheGridsNothingElseIsAsked()
    {
        Assert.Contains("NOSAI-SSD", OperatorMenu.NextStep(Progress(mapsReady: false)));
    }

    [Fact]
    public void WithTheGridsAndNoFileTheFirstPassIsAsked()
    {
        Assert.Contains("prima passata", OperatorMenu.NextStep(Progress(hasFile: false)));
    }

    [Fact]
    public void AnEmptySetSendsThemBackToAScanOnThisMap()
    {
        Assert.Contains("Resta su questa mappa", OperatorMenu.NextStep(Progress(candidates: 0, anchored: 0)));
    }

    [Fact]
    public void ManyCandidatesAskForAPortal()
    {
        string step = OperatorMenu.NextStep(Progress(candidates: 12, anchored: 3));

        Assert.Contains("12 candidati", step);
        Assert.Contains("portale", step);
    }

    [Fact]
    public void OneCandidateOnOneMapAsksForAPortalNotForARestart()
    {
        string step = OperatorMenu.NextStep(Progress(passes: 1));

        Assert.Contains("portale", step);
        Assert.DoesNotContain("riavvia", step, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneAnchoredCandidateOnTwoMapsAsksForTheRestart()
    {
        string step = OperatorMenu.NextStep(Progress(passes: 2));

        Assert.Contains("riaprilo", step);
        Assert.Contains("offset sopravvive", step);
    }

    [Fact]
    public void ABareAddressIsNamedAsUnwritable()
    {
        string step = OperatorMenu.NextStep(Progress(anchored: 0, passes: 2));

        Assert.Contains("indirizzo nudo", step);
    }

    [Fact]
    public void OnlyBothProofsProduceTheAnswer()
    {
        string step = OperatorMenu.NextStep(Progress(passes: 2, restarts: 1, winner: "manager+0x2A8"));

        Assert.Contains("TROVATO", step);
        Assert.Contains("manager+0x2A8", step);
    }

    [Fact]
    public void ProgressIsReadBackFromTheCandidateFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-menu-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, MapIdFinder.FormatCandidates(
                new[]
                {
                    new MapIdHit(MapIdAnchorKind.PlayerManager, 0x2A8, 5),
                    new MapIdHit(MapIdAnchorKind.Heap, 0x1DB2FF7C, 5),
                },
                passes: 2, restarts: 0, processId: 4242, playerX: 79, playerY: 110));

            MapIdProgress progress = OperatorMenu.ReadProgress(path);

            Assert.True(progress.HasFile);
            Assert.Equal(2, progress.Candidates);
            Assert.Equal(1, progress.Anchored);
            Assert.Equal(2, progress.Passes);
            Assert.Equal(0, progress.Restarts);
            Assert.Null(progress.Winner);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void AMissingFileIsNotAnEmptySet()
    {
        MapIdProgress progress = OperatorMenu.ReadProgress(
            Path.Combine(Path.GetTempPath(), "nosai-menu-absent-" + Guid.NewGuid().ToString("N") + ".txt"));

        Assert.False(progress.HasFile);
        Assert.Equal(0, progress.Candidates);
    }

    [Fact]
    public void TheMenuOffersNothingThatActuates()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "Operator", "OperatorMenu.cs"));

        // Arming input and the auto-calibrator move the character. A menu entry is
        // exactly the shape a bypass has, so they stay behind their own flags.
        Assert.DoesNotContain("--arm-input", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenProjectionAutoCalibrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActuationScope", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLauncherBuildsBeforeItRunsAndRefusesToRunAStaleExe()
    {
        string launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "NosAi.cmd"));

        Assert.Contains("dotnet build", launcher, StringComparison.Ordinal);
        Assert.Contains("--menu", launcher, StringComparison.Ordinal);
        Assert.Contains("if errorlevel 1 goto failed", launcher, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("NosAi.sln not found above the test output.");
    }
}
