using NosAi.Core.Memory;

namespace NosAi.Core.Tests.Memory;

public sealed class AdaptiveKnowledgeStoreTests
{
    [Fact]
    public async Task SaveAndGet_PersistsEntryInScopedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "NosAiMemory", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemAdaptiveKnowledgeStore(root);
            var entry = new KnowledgeEntry(
                "archer-001", "Combat Tactics", KnowledgeScope.Class, KnowledgeStatus.Verified,
                KnowledgeProvenance.Experiment, "private-test-2026.09", "Keep distance.", 0.95, 12,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            await store.SaveAsync(entry);
            var loaded = await store.GetAsync(entry.Id);

            Assert.NotNull(loaded);
            Assert.Equal(entry.Content, loaded!.Content);
            Assert.True(File.Exists(Path.Combine(root, "Class", "Combat Tactics", "archer-001.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EnsurePath_CreatesNewContextDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "NosAiMemory", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemAdaptiveKnowledgeStore(root);
            var path = await store.EnsurePathAsync(new KnowledgeDiscovery(
                "Unclassified Drop", "", "", "Observed a previously unknown drop.",
                KnowledgeProvenance.ClientNetwork, DateTime.UtcNow));

            Assert.True(Directory.Exists(Path.Combine(root, path.RelativeDirectory)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
