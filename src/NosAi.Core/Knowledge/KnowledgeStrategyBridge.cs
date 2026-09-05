using NosAi.Core.Memory;

namespace NosAi.Core.Knowledge;

public sealed record StrategyMemoryItem(
    string KnowledgeId,
    string Topic,
    string Objective,
    string Strategy,
    double Priority,
    double Confidence,
    KnowledgeLifecycle Lifecycle,
    string? RulesetVersion,
    IReadOnlyDictionary<string, string> Conditions);

public interface IStrategyMemory
{
    ValueTask<IReadOnlyList<StrategyMemoryItem>> QueryAsync(
        string objective,
        string? rulesetVersion = null,
        CancellationToken cancellationToken = default);
}

public sealed class AdaptiveStrategyMemory : IStrategyMemory
{
    private readonly IAdaptiveKnowledgeStore _store;

    public AdaptiveStrategyMemory(IAdaptiveKnowledgeStore store) => _store = store;

    public async ValueTask<IReadOnlyList<StrategyMemoryItem>> QueryAsync(
        string objective,
        string? rulesetVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        var matches = new List<StrategyMemoryItem>();
        foreach (var scope in Enum.GetValues<Memory.KnowledgeScope>())
        {
            var entries = await _store.QueryAsync(scope, objective, cancellationToken);
            foreach (var entry in entries)
            {
                if (entry.Status is not (KnowledgeStatus.Validated or KnowledgeStatus.Verified))
                    continue;

                if (!string.IsNullOrWhiteSpace(rulesetVersion) &&
                    !string.Equals(entry.RulesetVersion, rulesetVersion, StringComparison.OrdinalIgnoreCase))
                    continue;

                var tags = entry.Tags ?? new Dictionary<string, string>();
                var strategy = tags.TryGetValue("strategy", out var value) ? value : entry.Content;
                var priority = tags.TryGetValue("priority", out var priorityText) &&
                               double.TryParse(priorityText, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : entry.Confidence;

                matches.Add(new StrategyMemoryItem(
                    entry.Id,
                    entry.Topic,
                    objective,
                    strategy,
                    priority,
                    entry.Confidence,
                    entry.Status == KnowledgeStatus.Verified ? KnowledgeLifecycle.Verified : KnowledgeLifecycle.Validated,
                    entry.RulesetVersion,
                    tags));
            }
        }

        return matches
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.KnowledgeId, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class KnowledgeCandidateStrategyProjector
{
    public KnowledgeEntry Project(KnowledgeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var tags = new Dictionary<string, string>(candidate.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["source_type"] = candidate.Source.SourceType,
            ["source_uri"] = candidate.Source.Uri,
            ["objective"] = candidate.Topic
        };

        if (candidate.Conditions.Count > 0)
            tags["conditions"] = string.Join(";", candidate.Conditions.Select(x => $"{x.Key}={x.Value}"));

        var confidence = candidate.Evidence.Count == 0
            ? 0d
            : candidate.Evidence.Average(x => Math.Clamp(x.Confidence, 0d, 1d));

        var status = candidate.Lifecycle switch
        {
            KnowledgeLifecycle.Verified => KnowledgeStatus.Verified,
            KnowledgeLifecycle.Validated => KnowledgeStatus.Validated,
            KnowledgeLifecycle.Tested => KnowledgeStatus.Testing,
            _ => KnowledgeStatus.Candidate
        };

        return new KnowledgeEntry(
            candidate.Id,
            candidate.Topic,
            MapScope(candidate.Scope),
            status,
            candidate.Source.SourceType.Contains("community", StringComparison.OrdinalIgnoreCase)
                ? KnowledgeProvenance.CommunityResearch
                : KnowledgeProvenance.Unknown,
            candidate.RulesetVersion ?? "unknown",
            candidate.Metadata.TryGetValue("strategy", out var strategy) ? strategy : candidate.Topic,
            confidence,
            candidate.Evidence.Count,
            candidate.Source.ObservedAtUtc,
            candidate.Evidence.Count == 0
                ? candidate.Source.ObservedAtUtc
                : candidate.Evidence.Max(x => x.ObservedAtUtc),
            tags);
    }

    private static Memory.KnowledgeScope MapScope(KnowledgeScope scope) => scope switch
    {
        KnowledgeScope.Universal => Memory.KnowledgeScope.Universal,
        KnowledgeScope.Progression => Memory.KnowledgeScope.Progression,
        KnowledgeScope.Class => Memory.KnowledgeScope.Class,
        KnowledgeScope.Specialist => Memory.KnowledgeScope.Specialist,
        KnowledgeScope.Context => Memory.KnowledgeScope.Context,
        KnowledgeScope.Character => Memory.KnowledgeScope.Character,
        KnowledgeScope.Environment => Memory.KnowledgeScope.Environment,
        _ => Memory.KnowledgeScope.Context
    };
}
