using NosAi.Core.WorldModel.Certification;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The per-stage scorecard, built from what the suites actually said.
/// </summary>
/// <remarks>
/// The certification machinery answered one <see langword="bool"/> for the whole
/// product. <c>NosAi.Core.WorldModel.Certification</c> held the vocabulary for
/// the answer AP-10 asks for -- a level per stage, the evidence behind it, the
/// blockers in the way -- and had no caller. These tests pin the two properties
/// that make the wiring worth having rather than a prettier <c>bool</c>: that a
/// green run cannot certify the product, and that a stage nothing covers says so
/// instead of borrowing a neighbour's green.
/// </remarks>
public sealed class CertificationReportBuilderTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, bool> AllSuitesPassing() =>
        CertificationSuites.All.ToDictionary(s => s.Key, _ => true, StringComparer.Ordinal);

    [Fact]
    public void EveryStageIsReported()
    {
        CertificationReport report = CertificationReportBuilder.Build(AllSuitesPassing(), Now);

        Assert.Equal(Enum.GetValues<CertificationStage>().Length, report.Stages.Count);
        Assert.Equal(14, report.Stages.Count);
    }

    /// <summary>
    /// The property that matters most: no run of this process can certify the
    /// product, however green.
    /// </summary>
    /// <remarks>
    /// <c>Verified</c> means validated against the real client per CLAUDE.md's
    /// real-environment rule. A builder that could reach it from its own test
    /// suites would produce the most convincing wrong answer this repository is
    /// able to produce, so the ceiling is structural rather than a rule someone
    /// has to remember.
    /// </remarks>
    [Fact]
    public void AllSuitesGreenStillDoesNotCertify()
    {
        CertificationReport report = CertificationReportBuilder.Build(AllSuitesPassing(), Now);

        Assert.False(report.IsFullyCertified);
        Assert.All(report.Stages, s => Assert.NotEqual(VerificationLevel.Verified, s.Level));
        Assert.All(report.Stages, s =>
            Assert.Contains(CertificationReportBuilder.RealTargetBlocker, s.Blockers));
    }

    /// <summary>
    /// A stage no suite covers reports that, rather than inheriting a green from
    /// a suite that is about something else.
    /// </summary>
    [Fact]
    public void AStageNoSuiteCovers_SaysSoInsteadOfBorrowingAGreen()
    {
        CertificationReport report = CertificationReportBuilder.Build(AllSuitesPassing(), Now);

        CertificationStageResult uncovered = report.Stages.Single(s => s.Stage == CertificationStage.MultiStepQuest);

        Assert.False(CertificationReportBuilder.SuitesByStage.ContainsKey(CertificationStage.MultiStepQuest));
        Assert.Equal(VerificationLevel.Present, uncovered.Level);
        Assert.Contains(CertificationReportBuilder.NoSuiteBlocker, uncovered.Blockers);
    }

    /// <summary>A failing suite pulls its stages down and names itself.</summary>
    [Fact]
    public void AFailingSuiteNamesItselfInTheStagesItEvidences()
    {
        Dictionary<string, bool> suites = AllSuitesPassing();
        suites["gate3"] = false;

        CertificationReport report = CertificationReportBuilder.Build(suites, Now);

        foreach (CertificationStage stage in new[] { CertificationStage.Combat, CertificationStage.Recovery })
        {
            CertificationStageResult result = report.Stages.Single(s => s.Stage == stage);
            Assert.Equal(VerificationLevel.Present, result.Level);
            Assert.Contains("suite_failed:gate3", result.Blockers);
            Assert.Contains("gate3=FAIL", result.Evidence, StringComparison.Ordinal);
        }

        // A stage that does not rest on gate3 is untouched by its failure.
        Assert.Equal(
            VerificationLevel.Integrated,
            report.Stages.Single(s => s.Stage == CertificationStage.Navigation).Level);
    }

    /// <summary>
    /// A suite that was never run is not a suite that failed, and the blocker
    /// says which.
    /// </summary>
    [Fact]
    public void ASuiteThatWasNotRunIsDistinctFromOneThatFailed()
    {
        Dictionary<string, bool> suites = AllSuitesPassing();
        suites.Remove("navigation");

        CertificationStageResult navigation = CertificationReportBuilder
            .Build(suites, Now).Stages.Single(s => s.Stage == CertificationStage.Navigation);

        Assert.Contains("suite_not_run:navigation", navigation.Blockers);
        Assert.DoesNotContain("suite_failed:navigation", navigation.Blockers);
    }

    /// <summary>
    /// The overall level is the weakest stage's, so one uncovered stage keeps the
    /// whole report honest.
    /// </summary>
    [Fact]
    public void TheOverallLevelIsTheWeakestStage()
    {
        CertificationReport report = CertificationReportBuilder.Build(AllSuitesPassing(), Now);

        Assert.Equal(VerificationLevel.Present, report.OverallLevel);
    }

    /// <summary>
    /// Every suite named in the mapping exists in the registry, so a renamed
    /// suite cannot leave a stage silently evidenced by nothing.
    /// </summary>
    [Fact]
    public void EverySuiteNamedInTheMappingExists()
    {
        var known = CertificationSuites.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        string[] missing = CertificationReportBuilder.SuitesByStage
            .SelectMany(pair => pair.Value)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !known.Contains(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            "suite nominate nella mappa e assenti dal registro: " + string.Join(", ", missing));
    }
}
