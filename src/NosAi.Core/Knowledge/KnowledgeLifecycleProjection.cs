using NosAi.Core.Memory;

namespace NosAi.Core.Knowledge;

/// <summary>
/// The one, total, explicit projection from the ingestion pipeline's own
/// <see cref="KnowledgeLifecycle"/> (candidate -> tested -> validated ->
/// verified, plus revalidation/rejection) onto the general-purpose
/// <see cref="KnowledgeStatus"/> every <see cref="KnowledgeEntry"/> is
/// stored and queried under. Exists because the two enums are genuinely
/// different concepts (ingestion pipeline vs. general storage lifecycle),
/// not aliases of each other -- <see cref="KnowledgeLifecycle"/> has no
/// "Discovered"/"Promising" (states meaningful only before/around
/// ingestion) and <see cref="KnowledgeStatus"/> has no
/// "RevalidationRequired"/"Forbidden" (states meaningful only to the
/// ingestion pipeline's own validator) -- so unifying the two enums into
/// one would either invent states one side never produces or drop states
/// the other side needs (docs/agents/phases/AP-09/AP-09_A1_STATUS.md).
/// </summary>
/// <remarks>
/// Replaces the switch previously inlined in
/// <see cref="KnowledgeCandidateStrategyProjector.Project"/>, whose
/// unnamed <c>default</c> branch silently collapsed
/// <see cref="KnowledgeLifecycle.RevalidationRequired"/>,
/// <see cref="KnowledgeLifecycle.Deprecated"/> and
/// <see cref="KnowledgeLifecycle.Forbidden"/> onto
/// <see cref="KnowledgeStatus.Candidate"/> -- a rejected or
/// needs-recheck candidate read back as merely "not yet validated" is
/// exactly the kind of silent downgrade this project's "Unknown is not
/// zero" discipline forbids elsewhere. This projection names all seven
/// cases explicitly and throws rather than guessing on an out-of-range
/// value.
/// </remarks>
public static class KnowledgeLifecycleProjection
{
    /// <summary>
    /// <see cref="KnowledgeLifecycle.Candidate"/>/<see cref="KnowledgeLifecycle.Validated"/>/<see cref="KnowledgeLifecycle.Verified"/>
    /// map onto their own-named <see cref="KnowledgeStatus"/> counterpart.
    /// <see cref="KnowledgeLifecycle.Tested"/> maps to
    /// <see cref="KnowledgeStatus.Testing"/> (the only name mismatch, both
    /// meaning "currently being verified"). <see cref="KnowledgeLifecycle.RevalidationRequired"/>
    /// maps to <see cref="KnowledgeStatus.Testing"/> as well: a fact
    /// flagged for re-check is neither newly discovered nor still
    /// trustworthy, and <see cref="KnowledgeStatus.Testing"/> already
    /// excludes it from live use the same way a first-time candidate is
    /// excluded (<c>AdaptiveStrategyMemory.QueryAsync</c>'s
    /// <c>Validated</c>/<c>Verified</c>-only filter). <see cref="KnowledgeLifecycle.Deprecated"/>
    /// and <see cref="KnowledgeLifecycle.Forbidden"/> both map to
    /// <see cref="KnowledgeStatus.Deprecated"/> -- the most restrictive
    /// status <see cref="KnowledgeStatus"/> has, so neither is ever
    /// mistaken for eligible knowledge.
    /// </summary>
    public static KnowledgeStatus ToKnowledgeStatus(KnowledgeLifecycle lifecycle) => lifecycle switch
    {
        KnowledgeLifecycle.Candidate => KnowledgeStatus.Candidate,
        KnowledgeLifecycle.Tested => KnowledgeStatus.Testing,
        KnowledgeLifecycle.Validated => KnowledgeStatus.Validated,
        KnowledgeLifecycle.Verified => KnowledgeStatus.Verified,
        KnowledgeLifecycle.RevalidationRequired => KnowledgeStatus.Testing,
        KnowledgeLifecycle.Deprecated => KnowledgeStatus.Deprecated,
        KnowledgeLifecycle.Forbidden => KnowledgeStatus.Deprecated,
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, "Unknown KnowledgeLifecycle.")
    };
}
