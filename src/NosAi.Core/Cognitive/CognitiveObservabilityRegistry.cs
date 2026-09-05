namespace NosAi.Core.Cognitive;

/// <summary>Process-local bridge between the hosted runtime and read-only operator views.</summary>
public static class CognitiveObservabilityRegistry
{
    private static readonly object Gate = new();
    private static ICognitiveObservabilityReader? _current;

    public static ICognitiveObservabilityReader? Current
    {
        get { lock (Gate) return _current; }
    }

    public static void Publish(ICognitiveObservabilityReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (Gate) _current = reader;
    }

    public static void Clear(ICognitiveObservabilityReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (Gate)
        {
            if (ReferenceEquals(_current, reader)) _current = null;
        }
    }
}
