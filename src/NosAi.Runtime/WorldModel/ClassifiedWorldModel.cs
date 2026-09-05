using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception.Fusion;

namespace NosAi.Runtime.WorldModel;

/// <summary>
/// The AP-01 runtime World Model surface: classified planning state plus the
/// fused observation it came from. The coarse <see cref="IWorldModel"/> cannot
/// carry UNKNOWN, so this holder is what consumers must read when a fact may
/// be missing.
/// </summary>
public interface IClassifiedWorldModel
{
    /// <summary>The latest planning state. UNKNOWN fields stay UNKNOWN.</summary>
    Gate3WorldState Current { get; }

    /// <summary>The fusion snapshot that produced <see cref="Current"/>, or null when none ran.</summary>
    FusedWorldObservation? LastFusion { get; }

    /// <summary>When this holder was last published.</summary>
    DateTime PublishedAtUtc { get; }

    /// <summary>
    /// Whether the last publish also wrote the coarse <see cref="WorldState"/>.
    /// False means the coarse model was left untouched — not rewritten as zero.
    /// </summary>
    bool CoarsePublished { get; }

    /// <summary>Why the coarse projection was refused, or null when it was written.</summary>
    string? CoarseRefusalReason { get; }

    /// <summary>Replaces the classified state. Never invents a value.</summary>
    void Publish(Gate3WorldState planning, FusedWorldObservation? fusion, WorldModelProjection? coarse);
}

/// <summary>Thread-safe classified World Model used by the Gate 1 host.</summary>
public sealed class ClassifiedWorldModel : IClassifiedWorldModel
{
    private Gate3WorldState _current = Gate3WorldState.Unobserved("world_model_not_published");
    private FusedWorldObservation? _fusion;
    private DateTime _publishedAtUtc;
    private bool _coarsePublished;
    private string? _coarseRefusalReason = "world_model_not_published";

    public Gate3WorldState Current => Volatile.Read(ref _current);

    public FusedWorldObservation? LastFusion => Volatile.Read(ref _fusion);

    public DateTime PublishedAtUtc => _publishedAtUtc;

    public bool CoarsePublished => _coarsePublished;

    public string? CoarseRefusalReason => _coarseRefusalReason;

    public void Publish(Gate3WorldState planning, FusedWorldObservation? fusion, WorldModelProjection? coarse)
    {
        ArgumentNullException.ThrowIfNull(planning);

        Volatile.Write(ref _current, planning);
        Volatile.Write(ref _fusion, fusion);
        _publishedAtUtc = fusion?.FusedAtUtc ?? DateTime.UtcNow;
        _coarsePublished = coarse is { Succeeded: true, State: not null };
        _coarseRefusalReason = _coarsePublished ? null : coarse?.FailureReason ?? "coarse_world_state_not_projected";
    }
}
