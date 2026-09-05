using NosAi.Core.Cognitive;

namespace NosAi.ControlPanel;

public static class CognitiveObservabilityRegistry
{
    private static readonly InMemoryCognitiveObservability InstanceValue = new();

    public static ICognitiveObservabilitySink Sink => InstanceValue;
    public static ICognitiveObservabilityReader Reader => InstanceValue;
}
