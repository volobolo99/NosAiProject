using System.Text.Json;

namespace NosAi.GuardClient;

/// <summary>
/// One classified field, exactly as the runtime published it.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is null whenever <see cref="Source"/> is UNKNOWN. Nothing
/// substitutes a zero, a dash or an empty string for an unobserved reading: on an
/// operator's screen those are indistinguishable from a real measurement.
/// </remarks>
public sealed record ClassifiedField(string Name, string? Value, string Source)
{
    public const string Unknown = "UNKNOWN";

    public bool IsKnown => Source != Unknown && Value is not null;

    public string Display => IsKnown ? $"{Value} [{Source}]" : Unknown;
}

/// <summary>
/// The Gate 1 snapshot, parsed into something a screen can render.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in the phone application for two reasons. It is pure —
/// JSON in, records out — so it can be tested without a device, which is the only
/// way the phone's rendering gets tested at all. And it keeps the application a
/// shell around the client, which is what its own documentation claims it is.
/// </para>
/// <para>
/// Nothing in here decides anything. It reports what the runtime said, including
/// when the runtime said it does not know.
/// </para>
/// </remarks>
public sealed record GuardSnapshotView(
    string? RuntimeStatus,
    string? ClientStatus,
    IReadOnlyList<ClassifiedField> Client,
    IReadOnlyList<ClassifiedField> Safety,
    DateTimeOffset? CapturedAtUtc)
{
    /// <summary>
    /// Whether the runtime reports execution as disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read, never assumed. The screen used to carry a fixed line saying no input
    /// and no injection were possible — an assertion by the application about a
    /// property only the runtime is authoritative for (ADR-0003). Had execution
    /// ever been enabled, the phone would have kept saying it was not.
    /// </para>
    /// <para>
    /// Null when the snapshot does not say. Not <c>true</c>: an absent statement
    /// about a safety property is not a statement that the property holds.
    /// </para>
    /// </remarks>
    public bool? ExecutionDisabled
    {
        get
        {
            var mode = Safety.FirstOrDefault(f => f.Name == ExecutionModeField);
            if (mode is null || !mode.IsKnown)
                return null;
            return mode.Value!.Contains("disabled", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Name given to the execution-mode field, so callers need no string literal.</summary>
    public const string ExecutionModeField = "Esecuzione";

    /// <summary>
    /// Parses a telemetry snapshot.
    /// </summary>
    /// <exception cref="GuardProtocolException">The payload is not a snapshot.</exception>
    public static GuardSnapshotView Parse(string telemetryJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(telemetryJson);
        }
        catch (JsonException ex)
        {
            throw new GuardProtocolException("invalid_telemetry", ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new GuardProtocolException("invalid_telemetry", "snapshot is not an object");

            var client = Section(root, "client");
            var safety = Section(root, "safety");

            return new GuardSnapshotView(
                Text(root, "runtimeStatus"),
                Text(client, "status"),
                new[]
                {
                    Field(client, "processName", "Processo"),
                    Field(client, "processId", "PID"),
                    Field(client, "windowTitle", "Finestra"),
                    Field(client, "processResponding", "Risponde"),
                    Field(client, "windowVisible", "Visibile"),
                    Field(client, "gameplayBaseline", "Gameplay"),
                },
                new[]
                {
                    Field(safety, "executionMode", ExecutionModeField),
                    Field(safety, "liveInputEnabled", "Input diretto"),
                    Field(safety, "packetInjectionEnabled", "Injection pacchetti"),
                },
                Timestamp(root, "capturedAtUtc"));
        }
    }

    private static JsonElement Section(JsonElement root, string name) =>
        root.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object
            ? section
            : default;

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;

    private static DateTimeOffset? Timestamp(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Reads one classified field.
    /// </summary>
    /// <remarks>
    /// A missing field, a null value and a source of UNKNOWN all mean the same
    /// thing and are collapsed to the same result: not observed. A value carried
    /// without a source is treated as unobserved too, because an unlabelled
    /// reading is exactly what the classification exists to prevent.
    /// </remarks>
    private static ClassifiedField Field(JsonElement section, string property, string label)
    {
        if (section.ValueKind != JsonValueKind.Object || !section.TryGetProperty(property, out var field))
            return new ClassifiedField(label, null, ClassifiedField.Unknown);

        var source = field.TryGetProperty("source", out var s) ? s.GetString() : null;
        var value = field.TryGetProperty("value", out var v) && v.ValueKind is not JsonValueKind.Null
            ? v.ToString()
            : null;

        return value is null || string.IsNullOrEmpty(source)
            ? new ClassifiedField(label, null, ClassifiedField.Unknown)
            : new ClassifiedField(label, value, source);
    }
}
