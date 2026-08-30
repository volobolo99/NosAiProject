namespace NosAi.Runtime.Observability;

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
        var correlationId = Activity.Current?.Id ?? "none";
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
