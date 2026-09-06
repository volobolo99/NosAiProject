using NosAi.Core.Knowledge;
using NosAi.Core.Memory;
using Xunit;

namespace NosAi.Core.Tests.Knowledge;

/// <summary>
/// Every <see cref="KnowledgeLifecycle"/> value maps to a named
/// <see cref="KnowledgeStatus"/> -- no case falls through to a silent
/// default. Regression coverage for the bug this projection replaces:
/// <c>KnowledgeCandidateStrategyProjector.Project</c>'s previous inline
/// switch collapsed <see cref="KnowledgeLifecycle.RevalidationRequired"/>,
/// <see cref="KnowledgeLifecycle.Deprecated"/> and
/// <see cref="KnowledgeLifecycle.Forbidden"/> onto
/// <see cref="KnowledgeStatus.Candidate"/>.
/// </summary>
public sealed class KnowledgeLifecycleProjectionTests
{
    [Theory]
    [InlineData(KnowledgeLifecycle.Candidate, KnowledgeStatus.Candidate)]
    [InlineData(KnowledgeLifecycle.Tested, KnowledgeStatus.Testing)]
    [InlineData(KnowledgeLifecycle.Validated, KnowledgeStatus.Validated)]
    [InlineData(KnowledgeLifecycle.Verified, KnowledgeStatus.Verified)]
    [InlineData(KnowledgeLifecycle.RevalidationRequired, KnowledgeStatus.Testing)]
    [InlineData(KnowledgeLifecycle.Deprecated, KnowledgeStatus.Deprecated)]
    [InlineData(KnowledgeLifecycle.Forbidden, KnowledgeStatus.Deprecated)]
    public void ToKnowledgeStatus_MapsEveryLifecycleValueExplicitly(KnowledgeLifecycle lifecycle, KnowledgeStatus expected)
    {
        Assert.Equal(expected, KnowledgeLifecycleProjection.ToKnowledgeStatus(lifecycle));
    }

    [Fact]
    public void ToKnowledgeStatus_RevalidationRequired_IsNeverReadAsEligibleForLiveUse()
    {
        KnowledgeStatus status = KnowledgeLifecycleProjection.ToKnowledgeStatus(KnowledgeLifecycle.RevalidationRequired);

        Assert.NotEqual(KnowledgeStatus.Validated, status);
        Assert.NotEqual(KnowledgeStatus.Verified, status);
    }

    [Fact]
    public void ToKnowledgeStatus_DeprecatedAndForbidden_AreNeverReadAsCandidate()
    {
        // The exact defect this projection fixes: a rejected/needs-recheck
        // candidate must never come back looking merely "not yet validated".
        Assert.NotEqual(KnowledgeStatus.Candidate, KnowledgeLifecycleProjection.ToKnowledgeStatus(KnowledgeLifecycle.Deprecated));
        Assert.NotEqual(KnowledgeStatus.Candidate, KnowledgeLifecycleProjection.ToKnowledgeStatus(KnowledgeLifecycle.Forbidden));
    }

    [Fact]
    public void ToKnowledgeStatus_UnrecognizedValue_ThrowsRatherThanGuessing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KnowledgeLifecycleProjection.ToKnowledgeStatus((KnowledgeLifecycle)99));
    }
}
