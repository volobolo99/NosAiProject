using System.Text.Json;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Testing;

/// <summary>
/// How a test tells the operator page what it actually observed.
/// </summary>
/// <remarks>
/// <para>
/// xUnit and pytest run out of process, so an in-memory sink cannot reach them.
/// What does cross the boundary is standard output: both runners capture it and
/// both put it in their machine-readable report. A test therefore emits one
/// marker line per observation and the console runner reads them back.
/// </para>
/// <para>
/// The marker is deliberately ugly and unlikely to occur by accident. A line that
/// does not parse is left in the visible output rather than dropped, so a
/// malformed emission shows up as noise the author can see instead of vanishing.
/// </para>
/// </remarks>
public static class TestEvidenceProtocol
{
    /// <summary>The line prefix that marks an observation.</summary>
    public const string Marker = "##nosai-evidence##";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Writes one observation to standard output, for a test running out of process.
    /// </summary>
    /// <remarks>
    /// Also records into <see cref="TestEvidenceScope"/> so an in-process gate check
    /// using the same call is covered without a second API to remember.
    /// </remarks>
    public static void Emit(string key, object? value, DataSourceKind source, string? note = null)
    {
        var observation = new TestObservation(
            key,
            value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                _ => value.ToString() ?? "null"
            },
            source,
            note);

        TestEvidenceScope.Record(observation);
        Console.WriteLine(Format(observation));
    }

    /// <summary>
    /// Renders one observation as the line a test writes.
    /// </summary>
    /// <remarks>
    /// Exposed because xUnit does not capture <c>Console</c>: a test there writes
    /// this line through its <c>ITestOutputHelper</c> instead, which is what the
    /// VSTest adapter puts in the TRX report.
    /// </remarks>
    public static string Format(TestObservation observation) =>
        $"{Marker} {JsonSerializer.Serialize(new
        {
            key = observation.Key,
            value = observation.Value,
            source = observation.Source.ToString(),
            note = observation.Note
        })}";

    /// <summary>Renders an observation from its parts.</summary>
    public static string Format(string key, object? value, DataSourceKind source, string? note = null) =>
        Format(new TestObservation(key, Render(value), source, note));

    private static string Render(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };

    /// <summary>Shorthand for a value the test genuinely observed.</summary>
    public static void Live(string key, object? value, string? note = null) =>
        Emit(key, value, DataSourceKind.Live, note);

    /// <summary>Shorthand for a value produced by a simulation or fixture.</summary>
    public static void Simulated(string key, object? value, string? note = null) =>
        Emit(key, value, DataSourceKind.Simulated, note);

    /// <summary>Shorthand for something the test could not determine.</summary>
    public static void Unknown(string key, string reason) =>
        Emit(key, "UNKNOWN", DataSourceKind.Unknown, reason);

    /// <summary>
    /// Pulls the observations out of a captured stdout, returning what is left.
    /// </summary>
    /// <remarks>
    /// The remaining text is kept and shown: output a test printed for a human is
    /// still evidence, just unstructured.
    /// </remarks>
    public static (IReadOnlyList<TestObservation> Observations, string Remaining) Extract(string? capturedOutput)
    {
        if (string.IsNullOrWhiteSpace(capturedOutput))
            return (Array.Empty<TestObservation>(), "");

        var observations = new List<TestObservation>();
        var remaining = new List<string>();

        foreach (string raw in capturedOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at < 0)
            {
                remaining.Add(line);
                continue;
            }

            string payload = line[(at + Marker.Length)..].Trim();
            TestObservation? parsed = TryParse(payload);
            if (parsed is null)
            {
                // Unparseable: keep it visible rather than swallow it.
                remaining.Add(line);
                continue;
            }
            observations.Add(parsed);
        }

        return (observations, Tidy(remaining));
    }

    /// <summary>
    /// Drops the section banners a runner adds around captured output.
    /// </summary>
    /// <remarks>
    /// pytest wraps captured output in "----- Captured Out -----" rules even when
    /// nothing was captured. Left in, every test without real output gained an
    /// observation containing only a row of dashes, which is noise dressed as
    /// evidence. Only the runner's own decoration is removed; anything the test
    /// actually printed is kept.
    /// </remarks>
    private static string Tidy(IEnumerable<string> lines)
    {
        var kept = lines.Where(l => !IsBanner(l)).ToList();

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0]))
            kept.RemoveAt(0);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        return string.Join("\n", kept);
    }

    private static bool IsBanner(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length > 8
               && trimmed.StartsWith("---", StringComparison.Ordinal)
               && trimmed.EndsWith("---", StringComparison.Ordinal);
    }

    private static TestObservation? TryParse(string payload)
    {
        try
        {
            EmittedObservation? emitted = JsonSerializer.Deserialize<EmittedObservation>(payload, Options);
            if (emitted?.Key is null)
                return null;

            // An unrecognised source becomes Unknown rather than Live: a test that
            // mislabels its evidence must not have that evidence read as certain.
            DataSourceKind source = Enum.TryParse(emitted.Source, ignoreCase: true, out DataSourceKind k)
                ? k
                : DataSourceKind.Unknown;

            return new TestObservation(emitted.Key, emitted.Value ?? "null", source, emitted.Note);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record EmittedObservation(string? Key, string? Value, string? Source, string? Note);
}
