using CoreKnowledgeEntry = NosAi.Core.Memory.KnowledgeEntry;
using CoreKnowledgeStatus = NosAi.Core.Memory.KnowledgeStatus;
using CoreKnowledgeProvenance = NosAi.Core.Memory.KnowledgeProvenance;
using IAdaptiveKnowledgeStore = NosAi.Core.Memory.IAdaptiveKnowledgeStore;

namespace NosAi.Core.Knowledge;

/// <summary>
/// Converts externally discovered community knowledge into a durable adaptive-memory candidate.
/// Candidates remain non-live until independently evaluated and promoted by the validator.
/// </summary>
public sealed class AdaptiveKnowledgeIngestionEngine : IKnowledgeIngestionEngine
{
    private static readonly string[] ForbiddenMarkers =
    [
        "gm", "moderator", "admin", "server database", "server db",
        "packet injection", "exploit", "cheat", "hack", "dupe", "bot"
    ];

    private readonly IAdaptiveKnowledgeStore _store;

    public AdaptiveKnowledgeIngestionEngine(IAdaptiveKnowledgeStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<KnowledgeCandidate> IngestAsync(
        KnowledgeSource source,
        string topic,
        KnowledgeScope scope,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var normalizedTopic = topic.Trim();
        var mergedMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceUri"] = source.Uri.Trim(),
            ["sourceType"] = source.SourceType.Trim(),
            ["title"] = source.Title?.Trim() ?? string.Empty,
            ["ingestedAtUtc"] = DateTime.UtcNow.ToString("O")
        };

        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                mergedMetadata[pair.Key.Trim()] = pair.Value ?? string.Empty;
            }
        }

        var candidate = new KnowledgeCandidate(
            CreateStableId(source.Uri, normalizedTopic),
            normalizedTopic,
            scope,
            TryGet(mergedMetadata, "classId"),
            TryGet(mergedMetadata, "specialistId"),
            TryGet(mergedMetadata, "rulesetVersion"),
            KnowledgeLifecycle.Candidate,
            source,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            mergedMetadata);

        var entry = new CoreKnowledgeEntry(
            candidate.Id,
            candidate.Topic,
            MapScope(candidate.Scope),
            CoreKnowledgeStatus.Candidate,
            MapProvenance(candidate.Source.SourceType),
            candidate.RulesetVersion ?? "unknown",
            BuildContent(candidate),
            0.50,
            0,
            candidate.Source.ObservedAtUtc,
            candidate.Source.ObservedAtUtc,
            candidate.Metadata,
            []);

        await _store.SaveAsync(entry, cancellationToken);
        return candidate;
    }

    private static void ValidateSource(KnowledgeSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceType);

        var descriptor = $"{source.Uri} {source.SourceType} {source.Title}".ToLowerInvariant();
        if (ForbiddenMarkers.Any(descriptor.Contains))
            throw new InvalidOperationException("The knowledge source crosses the unprivileged gameplay boundary.");
    }

    private static string CreateStableId(string uri, string topic)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{uri.Trim()}\n{topic}"));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static string? TryGet(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string BuildContent(KnowledgeCandidate candidate)
        => $"Community candidate: {candidate.Topic}. Source: {candidate.Source.Title ?? candidate.Source.Uri}. " +
           "Not independently validated; never use as live gameplay truth.";

    private static NosAi.Core.Memory.KnowledgeScope MapScope(KnowledgeScope scope)
        => Enum.Parse<NosAi.Core.Memory.KnowledgeScope>(scope.ToString(), ignoreCase: false);

    private static CoreKnowledgeProvenance MapProvenance(string sourceType)
        => sourceType.Contains("official", StringComparison.OrdinalIgnoreCase)
            ? CoreKnowledgeProvenance.OfficialDocumentation
            : CoreKnowledgeProvenance.CommunityResearch;
}

public sealed class EvidenceKnowledgeValidator : IKnowledgeValidator
{
    public KnowledgeCandidate Evaluate(KnowledgeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Lifecycle == KnowledgeLifecycle.Forbidden)
            return candidate;

        if (candidate.Evidence.Count == 0)
            return candidate with { Lifecycle = KnowledgeLifecycle.Candidate };

        var confidence = candidate.Evidence.Average(e => Math.Clamp(e.Confidence, 0d, 1d));
        var independentCount = candidate.Evidence.Count(e => e.IndependentlyObserved);
        var lifecycle = independentCount >= 2 && confidence >= 0.85
            ? KnowledgeLifecycle.Verified
            : independentCount >= 1 && confidence >= 0.70
                ? KnowledgeLifecycle.Validated
                : KnowledgeLifecycle.Tested;

        return candidate with { Lifecycle = lifecycle };
    }
}
