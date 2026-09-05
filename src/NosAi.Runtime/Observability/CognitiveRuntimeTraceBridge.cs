using NosAi.Core.Cognitive;
using NosAi.Runtime.Gate3;

namespace NosAi.Runtime.Observability;

/// <summary>Compatibility facade used by the hosted Control Panel session.</summary>
public sealed class CognitiveRuntimeTraceBridge : IDisposable
{
    private readonly CognitiveObservabilityBridge _inner;

    public CognitiveRuntimeTraceBridge(Gate3DecisionLoop loop, ICognitiveObservabilitySink sink)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(sink);
        _inner = new CognitiveObservabilityBridge(loop.Orchestrator.StageBoard, sink, loop);
    }

    public void Dispose() => _inner.Dispose();
}
