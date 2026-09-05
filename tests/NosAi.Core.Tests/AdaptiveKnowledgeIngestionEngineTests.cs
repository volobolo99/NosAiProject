using NosAi.Core.Knowledge;
using NosAi.Core.Memory;

namespace NosAi.Core.Tests;

public sealed class AdaptiveKnowledgeIngestionEngineTests
{
    [Fact]
    public async Task Ingest_persists_candidate_without_granting_live_use()
    {
        var root = Path.Combine(Path.GetTempPath(), "nosai-knowledge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemAdaptiveKnowledgeStore(root);
            var engine = new AdaptiveKnowledgeIngestionEngine(store);
            var source = new KnowledgeSource(
                "https://forum.nostale.gameforge.com/example",
                "CommunityResearch",
                DateTime.UtcNow,
                "community",
                "Spider raid guide");

            var candidate = await engine.IngestAsync(
                source,
                "Spider Raid button sequence",
                KnowledgeScope.Context,
                new Dictionary<string, string> { ["rulesetVersion"] = "2026" });

            Assert.Equal(KnowledgeLifecycle.Candidate, candidate.Lifecycle);
            Assert.False(candidate.IsUsableForLiveRanking);

            var persisted = await store.GetAsync(candidate.Id);
            Assert.NotNull(persisted);
            Assert.Equal(KnowledgeStatus.Candidate, persisted!.Status);
            Assert.Equal(0, persisted.EvidenceCount);
            Assert.Equal("2026", persisted.RulesetVersion);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validator_requires_independent_evidence_for_verified_state()
    {
        var candidate = new KnowledgeCandidate(
            "candidate-1",
            "Lure grouping",
            KnowledgeScope.Context,
            null,
            null,
            "2026",
            KnowledgeLifecycle.Candidate,
            new KnowledgeSource("https://example.invalid", "CommunityResearch", DateTime.UtcNow, null, null),
            [
                new KnowledgeEvidence("e1", "Observed in controlled test A", DateTime.UtcNow, 0.90, true),
                new KnowledgeEvidence("e2", "Observed in controlled test B", DateTime.UtcNow, 0.90, true)
            ],
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var result = new EvidenceKnowledgeValidator().Evaluate(candidate);

        Assert.Equal(KnowledgeLifecycle.Verified, result.Lifecycle);
        Assert.True(result.IsUsableForLiveRanking);
    }

    [Fact]
    public async Task Ingest_rejects_privileged_or_exploit_sources()
    {
        var root = Path.Combine(Path.GetTempPath(), "nosai-knowledge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new AdaptiveKnowledgeIngestionEngine(new FileSystemAdaptiveKnowledgeStore(root));
            var source = new KnowledgeSource(
                "https://example.invalid/server-db",
                "AdminServerDatabase",
                DateTime.UtcNow,
                "admin",
                "Hidden server state");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await engine.IngestAsync(source, "hidden state", KnowledgeScope.Environment));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
