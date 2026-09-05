using NosAi.Core.Cognitive;

namespace NosAi.Runtime.Observability;

/// <summary>Process-local observability endpoint shared by the hosted runtime and Control Panel.</summary>
public static class CognitiveObservabilityRegistry
{
    private static readonly InMemoryCognitiveObservability Shared = new();
    public static ICognitiveObservabilitySink Sink => Shared;
    public static ICognitiveObservabilityReader Reader => Shared;
}
