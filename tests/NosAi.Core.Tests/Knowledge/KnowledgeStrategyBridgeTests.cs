using NosAi.Core.Knowledge;
using NosAi.Core.Memory;
using Xunit;

namespace NosAi.Core.Tests.Knowledge;

public sealed class KnowledgeCandidateStrategyProjectorTests
{
    private static readonly DateTime T0 = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);

    private static KnowledgeCandidate Candidate(KnowledgeLifecycle lifecycle, KnowledgeScope scope = KnowledgeScope.Context) => new(
        "candidate-1",
        "objective-1",
        scope,
        null,
        null,
        "2026",
        lifecycle,
        new KnowledgeSource("https://example.invalid", "CommunityResearch", T0, null, null),
        [],
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    [Theory]
    [InlineData(KnowledgeLifecycle.Candidate, KnowledgeStatus.Candidate)]
    [InlineData(KnowledgeLifecycle.Tested, KnowledgeStatus.Testing)]
    [InlineData(KnowledgeLifecycle.Validated, KnowledgeStatus.Validated)]
    [InlineData(KnowledgeLifecycle.Verified, KnowledgeStatus.Verified)]
    [InlineData(KnowledgeLifecycle.RevalidationRequired, KnowledgeStatus.Testing)]
    [InlineData(KnowledgeLifecycle.Deprecated, KnowledgeStatus.Deprecated)]
    [InlineData(KnowledgeLifecycle.Forbidden, KnowledgeStatus.Deprecated)]
    public void Project_StatusMatchesTheOfficialLifecycleProjection_ForEveryLifecycleValue(
        KnowledgeLifecycle lifecycle, KnowledgeStatus expectedStatus)
    {
        KnowledgeEntry entry = new KnowledgeCandidateStrategyProjector().Project(Candidate(lifecycle));

        Assert.Equal(expectedStatus, entry.Status);
    }

    [Fact]
    public void Project_ScopeIsPassedThroughUnchanged_NeverReDerived()
    {
        KnowledgeEntry entry = new KnowledgeCandidateStrategyProjector().Project(Candidate(KnowledgeLifecycle.Verified, KnowledgeScope.Class));

        Assert.Equal(KnowledgeScope.Class, entry.Scope);
    }
}
