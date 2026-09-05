using NosAi.Core.WorldModel;
using RuntimeContracts = NosAi.Runtime.Contracts;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Converts a <see cref="RuntimeContracts.ClassifiedValue{T}"/> (the provenance
/// primitive used throughout <c>NosAi.Runtime.Contracts</c>/<c>NosAi.LiveIntegration</c>/
/// <c>NosAi.Runtime.Perception</c>) into a <see cref="WorldFact{T}"/> (AP-01's
/// canonical World Model primitive). Extracted from
/// <see cref="GameplayObservationProjector"/>, which needed exactly this
/// mapping first; <see cref="VisualObservationFusion"/> needs the same one
/// for the vision channel, so it lives here once rather than twice.
/// </summary>
internal static class ClassifiedValueBridge
{
    /// <summary>Converts a classified value, projecting its underlying value through <paramref name="project"/> when present.</summary>
    public static WorldFact<TResult> ToWorldFact<TSource, TResult>(RuntimeContracts.ClassifiedValue<TSource> value, Func<TSource, TResult> project)
    {
        if (!value.HasValue)
            return WorldFact<TResult>.Unknown(value.FailureReason ?? "not_observed", value.ObservedAtUtc);

        return WithSource(value.Source, project(value.Value), value.ObservedAtUtc);
    }

    /// <summary>Converts a classified value of the same type, with no projection.</summary>
    public static WorldFact<T> ToWorldFact<T>(RuntimeContracts.ClassifiedValue<T> value) => ToWorldFact(value, static v => v);

    /// <summary>Wraps an already-unwrapped value under the <see cref="WorldFact{T}"/> factory matching <paramref name="source"/>.</summary>
    public static WorldFact<T> WithSource<T>(RuntimeContracts.DataSourceKind source, T value, DateTime observedAtUtc) => source switch
    {
        RuntimeContracts.DataSourceKind.Live => WorldFact<T>.Live(value, 1.0, observedAtUtc),
        RuntimeContracts.DataSourceKind.Derived => WorldFact<T>.Derived(value, 1.0, observedAtUtc),
        RuntimeContracts.DataSourceKind.Cached => WorldFact<T>.Cached(value, 1.0, observedAtUtc),
        RuntimeContracts.DataSourceKind.Simulated => WorldFact<T>.Simulated(value, 1.0, observedAtUtc),
        _ => WorldFact<T>.Unknown("source_unknown", observedAtUtc)
    };
}
