using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Certification;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Certification;

public sealed class CertificationStageResultTests
{
    [Fact]
    public void Verified_WithBlockers_Throws()
    {
        EquatableArray<string> blockers = EquatableArray<string>.From(new[] { "something" });

        Assert.Throws<ArgumentException>(() => new CertificationStageResult(
            CertificationStage.Startup, VerificationLevel.Verified, "evidence", blockers));
    }

    [Fact]
    public void Verified_WithNoBlockers_Constructs()
    {
        var result = new CertificationStageResult(
            CertificationStage.Startup, VerificationLevel.Verified, "evidence", EquatableArray<string>.Empty);

        Assert.Equal(VerificationLevel.Verified, result.Level);
    }

    [Fact]
    public void NonVerified_WithBlockers_Constructs()
    {
        EquatableArray<string> blockers = EquatableArray<string>.From(new[] { "no_real_data" });

        var result = new CertificationStageResult(
            CertificationStage.Combat, VerificationLevel.Present, "evidence", blockers);

        Assert.Equal(blockers, result.Blockers);
    }

    [Fact]
    public void EmptyEvidence_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CertificationStageResult(
            CertificationStage.Startup, VerificationLevel.Present, "", EquatableArray<string>.Empty));
    }
}

public sealed class CertificationReportTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static CertificationStageResult BuildResult(CertificationStage stage, VerificationLevel level) =>
        new(stage, level, "evidence",
            level == VerificationLevel.Verified ? EquatableArray<string>.Empty : EquatableArray<string>.From(new[] { "blocker" }));

    [Fact]
    public void OverallLevel_NoStages_IsNull()
    {
        var report = new CertificationReport(EquatableArray<CertificationStageResult>.Empty, Now);

        Assert.Null(report.OverallLevel);
    }

    [Fact]
    public void OverallLevel_IsTheWeakestStage()
    {
        var stages = EquatableArray<CertificationStageResult>.From(new[]
        {
            BuildResult(CertificationStage.Startup, VerificationLevel.Verified),
            BuildResult(CertificationStage.Combat, VerificationLevel.Present),
            BuildResult(CertificationStage.Perception, VerificationLevel.Integrated),
        });

        var report = new CertificationReport(stages, Now);

        Assert.Equal(VerificationLevel.Present, report.OverallLevel);
    }

    [Fact]
    public void IsFullyCertified_FalseWhenFewerThanFourteenStages()
    {
        var stages = EquatableArray<CertificationStageResult>.From(new[]
        {
            BuildResult(CertificationStage.Startup, VerificationLevel.Verified),
        });

        var report = new CertificationReport(stages, Now);

        Assert.False(report.IsFullyCertified);
    }

    [Fact]
    public void IsFullyCertified_FalseWhenAnyStageBelowVerified()
    {
        var stages = new List<CertificationStageResult>();
        foreach (CertificationStage stage in Enum.GetValues<CertificationStage>())
        {
            stages.Add(BuildResult(stage, stage == CertificationStage.Evidence ? VerificationLevel.Present : VerificationLevel.Verified));
        }

        var report = new CertificationReport(EquatableArray<CertificationStageResult>.From(stages), Now);

        Assert.False(report.IsFullyCertified);
    }

    [Fact]
    public void IsFullyCertified_TrueWhenAllFourteenStagesVerified()
    {
        var stages = new List<CertificationStageResult>();
        foreach (CertificationStage stage in Enum.GetValues<CertificationStage>())
            stages.Add(BuildResult(stage, VerificationLevel.Verified));

        var report = new CertificationReport(EquatableArray<CertificationStageResult>.From(stages), Now);

        Assert.True(report.IsFullyCertified);
        Assert.Equal(14, Enum.GetValues<CertificationStage>().Length);
    }
}
