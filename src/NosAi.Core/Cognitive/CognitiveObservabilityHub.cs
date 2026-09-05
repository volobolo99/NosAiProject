namespace NosAi.Core.Cognitive;

/// <summary>
/// Process-local read/write endpoint shared by the hosted runtime and the WPF
/// Control Panel. It is observability only: no execution or safety authority is
/// exposed through this hub.
/// </summary>
public static class CognitiveObservabilityHub
{
    private static readonly InMemoryCognitiveObservability Shared = new();

    public static ICognitiveObservabilitySink Sink => Shared;
    public static ICognitiveObservabilityReader Reader => Shared;
}
