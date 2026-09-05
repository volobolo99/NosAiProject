using System.Text.Json;

namespace NosAi.Core.Memory;

public enum KnowledgeScope
{
    Universal,
    Progression,
    Class,
    Specialist,
    Context,
    Character,
    Environment
}

public enum KnowledgeStatus
{
    Discovered,
    Candidate,
    Testing,
    Promising,
    Validated,
    Verified,
    Deprecated
}

public enum KnowledgeProvenance
{
    OfficialDocumentation,
    CommunityResearch,
    ClientNetwork,
    ClientMemory,
    Screen,
    LocalTelemetry,
    Experiment,
    Derived,
    Unknown
}

public sealed record KnowledgeEntry(
    string Id,
    string Topic,
    KnowledgeScope Scope,
    KnowledgeStatus Status,
    KnowledgeProvenance Provenance,
    string RulesetVersion,
    string Content,
    double Confidence,
    int EvidenceCount,
    DateTime FirstObservedUtc,
    DateTime LastValidatedUtc,
    IReadOnlyDictionary<string, string>? Tags = null,
    IReadOnlyList<string>? RelatedEntryIds = null);

public sealed record KnowledgePath(
    string Root,
    string RelativeDirectory,
    string FileName)
{
    public string RelativeFilePath => Path.Combine(RelativeDirectory, FileName);
}

public sealed record KnowledgeDiscovery(
    string Topic,
    string ProposedDirectory,
    string ProposedFileName,
    string Reason,
    KnowledgeProvenance Provenance,
    DateTime ObservedAtUtc);

public interface IAdaptiveKnowledgeStore
{
    ValueTask<KnowledgeEntry?> GetAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<KnowledgeEntry>> QueryAsync(KnowledgeScope scope, string topic, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default);
    ValueTask<KnowledgePath> EnsurePathAsync(KnowledgeDiscovery discovery, CancellationToken cancellationToken = default);
}

public static class KnowledgePathPolicy
{
    public static KnowledgePath Build(string root, KnowledgeEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(entry);

        var scope = Sanitize(entry.Scope.ToString());
        var topic = Sanitize(entry.Topic);
        var id = Sanitize(entry.Id);
        return new KnowledgePath(root, Path.Combine(scope, topic), $"{id}.json");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
}

public static class KnowledgeSerialization
{
    public static string Serialize(KnowledgeEntry entry)
        => JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
}
