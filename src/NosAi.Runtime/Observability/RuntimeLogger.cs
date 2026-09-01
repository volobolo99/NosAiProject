using System.Diagnostics;

namespace NosAi.Runtime.Observability;

/// <summary>
/// The correlation id in scope for whichever logical run is currently active, so
/// <see cref="ConsoleRuntimeLogger"/> can stamp every line with it without every
/// call site repeating it in a properties dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, <see cref="ConsoleRuntimeLogger"/> read
/// <see cref="Activity.Current"/>, which nothing in this runtime ever started: an
/// <see cref="ActivitySource"/> with no registered <see cref="ActivityListener"/>
/// returns a null activity from every <c>StartActivity</c> call, so every line
/// printed <c>correlationId=none</c> regardless of how many bootstrap runs were
/// interleaved in the same process. The one place that tried to correlate by hand
/// (<c>Gate1BootstrapHost</c>'s "Gate 1 bootstrap starting." line) only carried
/// its id as an ordinary property, invisible on every other line from the same run.
/// </para>
/// <para>
/// Backed by <see cref="AsyncLocal{T}"/>: a value set here flows into whatever the
/// caller forks off afterwards (background loops, accept loops) the same way
/// <see cref="Activity.Current"/> would, without depending on anything having
/// registered a listener for that propagation to happen.
/// </para>
/// </remarks>
public static class CorrelationScope
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <summary>The correlation id in scope right now, or null outside any scope.</summary>
    public static string? Current => _current.Value;

    /// <summary>
    /// Establishes <paramref name="correlationId"/> as current until the returned
    /// handle is disposed, restoring whatever was current before it.
    /// </summary>
    public static IDisposable Begin(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("A correlation id is required.", nameof(correlationId));
        string? previous = _current.Value;
        _current.Value = correlationId;
        return new Handle(previous);
    }

    private sealed class Handle : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Handle(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}

public interface IRuntimeLogger
{
    void Info(string message, IReadOnlyDictionary<string, object?>? properties = null);
    void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null);
    void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null);
}

public sealed class ConsoleRuntimeLogger : IRuntimeLogger
{
    public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) => Write("INFO", message, properties);
    public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) => Write("WARN", message, properties);
    public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var details = exception is null ? properties : Merge(properties, new Dictionary<string, object?> { ["exception"] = exception.ToString() });
        Write("ERROR", message, details);
    }

    private static void Write(string level, string message, IReadOnlyDictionary<string, object?>? properties)
    {
        // CorrelationScope is the one this runtime actually populates; Activity.Current
        // is kept as a fallback in case something later wires OpenTelemetry in-process.
        var correlationId = CorrelationScope.Current ?? Activity.Current?.Id ?? "none";
        var suffix = properties is null || properties.Count == 0
            ? string.Empty
            : " " + string.Join(" ", properties.Select(p => $"{p.Key}={p.Value}"));
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] correlationId={correlationId} {message}{suffix}");
    }

    private static IReadOnlyDictionary<string, object?> Merge(IReadOnlyDictionary<string, object?>? source, Dictionary<string, object?> additions)
    {
        var result = source is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(source);
        foreach (var pair in additions) result[pair.Key] = pair.Value;
        return result;
    }
}
