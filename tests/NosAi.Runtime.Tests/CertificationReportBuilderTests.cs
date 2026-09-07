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
    /// Every stage is mapped to at least one suite -- and the report still says
    /// so when one is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five stages had no suite when the report was first built, and it named
    /// them: MapDiscovery, Exploration, TargetRecognition and MultiStepQuest had
    /// nothing at all, and Attach was evidenced by gate1 without anyone having
    /// declared it. The four were covered by writing
    /// <c>ScenarioStageTestRunner</c>, so this test can no longer point at a real
    /// uncovered stage -- which is the outcome it existed to drive.
    /// </para>
    /// <para>
    /// It now asserts both halves: that nothing is uncovered today, and that a
    /// stage removed from the mapping would still be reported rather than
    /// silently inheriting a green. The second half is checked against a report
    /// built from a mapping with a hole in it, so the behaviour stays pinned
    /// without needing the product to have a gap.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryStageIsMapped_AndAnUnmappedOneWouldStillSaySo()
    {
        CertificationStage[] unmapped = Enum.GetValues<CertificationStage>()
            .Where(stage => !CertificationReportBuilder.SuitesByStage.ContainsKey(stage))
            .ToArray();

        Assert.True(unmapped.Length == 0,
            "stadi senza alcuna suite dichiarata: " + string.Join(", ", unmapped));

        // And the behaviour itself, on a report whose mapping has a hole: the
        // builder reads its own table, so removing every suite for a stage is
        // exactly the shape a future unmapped stage would take.
        CertificationReport report = CertificationReportBuilder.Build(
            AllSuitesPassing().Where(p => p.Key != "navigation").ToDictionary(p => p.Key, p => p.Value),
            Now);

        CertificationStageResult navigation = report.Stages.Single(s => s.Stage == CertificationStage.Navigation);
        Assert.Equal(VerificationLevel.Present, navigation.Level);
        Assert.Contains("suite_not_run:navigation", navigation.Blockers);
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
    /// The overall level is the weakest stage's, so one failing suite pulls the
    /// whole report down however green the rest is.
    /// </summary>
    [Fact]
    public void TheOverallLevelIsTheWeakestStage()
    {
        Assert.Equal(VerificationLevel.Integrated, CertificationReportBuilder.Build(AllSuitesPassing(), Now).OverallLevel);

        Dictionary<string, bool> oneFailing = AllSuitesPassing();
        oneFailing["gate3"] = false;

        Assert.Equal(VerificationLevel.Present, CertificationReportBuilder.Build(oneFailing, Now).OverallLevel);
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
