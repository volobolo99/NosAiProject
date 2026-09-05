namespace NosAi.Core.Knowledge;

public enum KnowledgeLifecycle
{
    Candidate,
    Tested,
    Validated,
    Verified,
    RevalidationRequired,
    Deprecated,
    Forbidden
}

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

public readonly record struct KnowledgeSource(
    string Uri,
    string SourceType,
    DateTime ObservedAtUtc,
    string? Author,
    string? Title);

public readonly record struct KnowledgeEvidence(
    string EvidenceId,
    string Description,
    DateTime ObservedAtUtc,
    double Confidence,
    bool IndependentlyObserved);

public sealed record KnowledgeCandidate(
    string Id,
    string Topic,
    KnowledgeScope Scope,
    string? ClassId,
    string? SpecialistId,
    string? RulesetVersion,
    KnowledgeLifecycle Lifecycle,
    KnowledgeSource Source,
    IReadOnlyList<KnowledgeEvidence> Evidence,
    IReadOnlyDictionary<string, string> Conditions,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsUsableForLiveRanking =>
        Lifecycle is KnowledgeLifecycle.Validated or KnowledgeLifecycle.Verified;
}

public interface IKnowledgeIngestionEngine
{
    ValueTask<KnowledgeCandidate> IngestAsync(
        KnowledgeSource source,
        string topic,
        KnowledgeScope scope,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}

public interface IKnowledgeValidator
{
    KnowledgeCandidate Evaluate(KnowledgeCandidate candidate);
}
