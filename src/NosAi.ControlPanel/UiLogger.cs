using NosAi.Runtime.Observability;

namespace NosAi.ControlPanel;

public sealed record LogEntry(DateTimeOffset At, string Level, string Message);

/// <summary>Forwards runtime logs into the operator diary.</summary>
public sealed class UiLogger : IRuntimeLogger
{
    public event Action<LogEntry>? Written;

    public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null)
        => Emit("INFO", Format(message, properties));

    public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null)
        => Emit("WARN", Format(message, properties));

    public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var extra = exception is null ? properties : Merge(properties, exception.GetType().Name, exception.Message);
        Emit("ERROR", Format(message, extra));
    }

    public void Operator(string message) => Emit("UI", message);

    private void Emit(string level, string message)
        => Written?.Invoke(new LogEntry(DateTimeOffset.Now, level, message));

    private static string Format(string message, IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
            return message;
        var suffix = string.Join(" ", properties.Select(p => $"{p.Key}={p.Value}"));
        return string.IsNullOrEmpty(suffix) ? message : $"{message}  {suffix}";
    }

    private static IReadOnlyDictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?>? source, string key, string? value)
    {
        var result = source is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(source);
        result[key] = value;
        return result;
    }
}
