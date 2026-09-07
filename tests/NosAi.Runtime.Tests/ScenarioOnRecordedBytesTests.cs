using NosAi.Core.WorldModel.Certification;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The evidence-nature half of AP-10: a stage certified on a fixture and a
/// stage certified also on recorded bytes are not at the same point, and the
/// report must say so -- and, whatever the evidence, no stage may reach
/// <see cref="VerificationLevel.Verified"/> from this process.
/// </summary>
/// <remarks>
/// <para>
/// The recorded checks read the operator's own captures under <c>data/</c>,
/// which is gitignored. The ones below that need the bytes are gated on
/// <see cref="RecordedCaptureFactAttribute"/>, so a clone without <c>data/</c>
/// skips them with the reason named; the builder-level tests need no recording
/// and always run.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class ScenarioOnRecordedBytesTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private const string Combat = "nostale_combat.noscap";
    private const string Idle = "nostale_01.noscap";

    private static Dictionary<string, bool> AllSuitesPassing() =>
        CertificationSuites.All.ToDictionary(s => s.Key, _ => true, StringComparer.Ordinal);

    private static IReadOnlySet<CertificationStage> NoRecordedEvidence { get; } =
        new HashSet<CertificationStage>();

    private static IReadOnlySet<CertificationStage> RecordedScenarioEvidence { get; } =
        new HashSet<CertificationStage>
        {
            CertificationStage.Exploration,
            CertificationStage.TargetRecognition,
        };

    /// <summary>
    /// Written first: whatever the evidence combination, this process never
    /// certifies the product, because <c>Verified</c> is real-target validation
    /// and nothing here has touched a live client.
    /// </summary>
    [Fact]
    public void NoStageReachesVerified_InAnyEvidenceCombination()
    {
        // Every combination of recorded evidence this process can produce:
        // none, the two scenario stages the recordings cover, every stage, and a
        // scenario suite that failed while the rest is green.
        CertificationReport synthetic = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: NoRecordedEvidence);
        CertificationReport both = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: RecordedScenarioEvidence);
        CertificationReport allStages = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now,
            recordedEvidence: Enum.GetValues<CertificationStage>().ToHashSet());

        Dictionary<string, bool> failing = AllSuitesPassing();
        failing["scenario"] = false;
        CertificationReport failedScenario = CertificationReportBuilder.Build(
            failing, Now, recordedEvidence: RecordedScenarioEvidence);

        foreach (CertificationReport report in new[] { synthetic, both, allStages, failedScenario })
        {
            Assert.False(report.IsFullyCertified);
            Assert.All(report.Stages, s => Assert.NotEqual(VerificationLevel.Verified, s.Level));
        }
    }

    /// <summary>A stage covered only by fixtures is reported as synthetic evidence.</summary>
    [Fact]
    public void AStageCoveredOnlyByFixtures_ReportsSyntheticEvidence()
    {
        CertificationReport report = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: NoRecordedEvidence);

        CertificationStageResult exploration = report.Stages.Single(s => s.Stage == CertificationStage.Exploration);

        Assert.Equal(EvidenceNature.Synthetic,
            CertificationReportBuilder.NatureFor(CertificationStage.Exploration, NoRecordedEvidence));
        Assert.Equal(CertificationReportBuilder.SyntheticLabel, EvidenceLabel(exploration));
    }

    /// <summary>The same stage, once the recorded checks ran, is reported as both.</summary>
    [Fact]
    public void TheSameStage_WithRecordedChecksRun_ReportsBothEvidence()
    {
        CertificationReport report = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: RecordedScenarioEvidence);

        CertificationStageResult exploration = report.Stages.Single(s => s.Stage == CertificationStage.Exploration);

        Assert.Equal(EvidenceNature.Both,
            CertificationReportBuilder.NatureFor(CertificationStage.Exploration, RecordedScenarioEvidence));
        Assert.Equal(CertificationReportBuilder.BothLabel, EvidenceLabel(exploration));
    }

    /// <summary>
    /// Without <c>data/</c> the report stays valid and the stages stay
    /// <see cref="VerificationLevel.Integrated"/> on synthetic evidence.
    /// </summary>
    [Fact]
    public void WithoutData_TheReportStaysValid_AndStagesStayIntegratedOnSyntheticEvidence()
    {
        CertificationReport report = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: NoRecordedEvidence);

        Assert.Equal(VerificationLevel.Integrated, report.OverallLevel);
        CertificationStageResult exploration = report.Stages.Single(s => s.Stage == CertificationStage.Exploration);
        Assert.Equal(VerificationLevel.Integrated, exploration.Level);
        Assert.Equal(CertificationReportBuilder.SyntheticLabel, EvidenceLabel(exploration));
    }

    /// <summary>
    /// Without <c>data/</c> the recorded checks skip: the suite stays green, no
    /// stage gains recorded evidence, and the skip is said out loud rather than
    /// read as a silent pass.
    /// </summary>
    [Fact]
    public void WithoutData_TheRecordedChecksSkip_NotFail_NotSilentlyPass()
    {
        string nowhere = Path.Combine(Path.GetTempPath(), $"nosai-no-capture-{Guid.NewGuid():N}");

        string output = CaptureConsole(() =>
        {
            bool result = ScenarioStageTestRunner.RunAllTestsAsync(nowhere).GetAwaiter().GetResult();
            Assert.True(result, "a skip must not fail the suite");
        });

        // Not silently passed: no stage was recorded as evidenced ...
        Assert.Empty(ScenarioStageTestRunner.LastRecordedEvidence);
        // ... and the skip was printed with its reason.
        Assert.Contains("[SKIP]", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Attach stays uncovered by recorded bytes whatever else gains evidence:
    /// attaching to a live client is not simulable from a recording, and the
    /// stage carries its own blocker saying what would lift it.
    /// </summary>
    [Fact]
    public void Attach_StaysUncoveredByRecordedBytes_AndCarriesItsOwnBlocker()
    {
        CertificationReport report = CertificationReportBuilder.Build(
            AllSuitesPassing(), Now, recordedEvidence: RecordedScenarioEvidence);

        CertificationStageResult attach = report.Stages.Single(s => s.Stage == CertificationStage.Attach);

        Assert.Equal(EvidenceNature.Synthetic,
            CertificationReportBuilder.NatureFor(CertificationStage.Attach, RecordedScenarioEvidence));
        Assert.Equal(CertificationReportBuilder.SyntheticLabel, EvidenceLabel(attach));
        Assert.Contains(CertificationReportBuilder.RealTargetBlocker, attach.Blockers);
    }

    /// <summary>The recorded target-recognition check holds against the real combat capture.</summary>
    [RecordedCaptureFact(Combat)]
    public void The_combat_recording_carries_entities_whose_vnum_the_wire_stated()
    {
        string path = RecordedCaptureFactAttribute.Resolve(Combat)!;

        Assert.True(ScenarioStageTestRunner.CaptureCarriesVnum(path));
    }

    /// <summary>The recorded exploration check holds against the real idle capture.</summary>
    [RecordedCaptureFact(Idle)]
    public void The_idle_recording_carries_distinct_entities_at_observed_positions()
    {
        string path = RecordedCaptureFactAttribute.Resolve(Idle)!;

        Assert.True(ScenarioStageTestRunner.CaptureCarriesDistinctPositions(path));
    }

    private static string EvidenceLabel(CertificationStageResult stage) =>
        stage.Evidence.Split(':', 2)[0];

    private static string CaptureConsole(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.Out.Flush();
            Console.SetOut(original);
        }
    }
}
