using System.Text.Json;
using System.Text.Json.Serialization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Testing;

/// <summary>What a test run concluded.</summary>
/// <remarks>
/// <see cref="NotRun"/> is a first-class outcome, not a missing value. A test that
/// has never executed must never render as green: "we did not check" and "it
/// works" are different claims, and collapsing them is how a dashboard starts
/// lying about the thing it exists to report.
/// </remarks>
public enum TestOutcome
{
    NotRun = 0,
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    Errored = 4
}

/// <summary>Which suite a test belongs to and how it is executed.</summary>
public enum TestSuiteKind
{
    Unknown = 0,

    /// <summary>xUnit tests in <c>tests/NosAi.Runtime.Tests</c>, run out of process.</summary>
    DotNet = 1,

    /// <summary>pytest tests under <c>tests/</c>, run out of process.</summary>
    Python = 2,

    /// <summary>The in-process gate certification checks (<c>--gateN-test</c>).</summary>
    GateCertification = 3
}

/// <summary>
/// One value a test actually looked at, carried out of the test so it can be seen.
/// </summary>
/// <remarks>
/// This is the difference between a dashboard that says "18 passed" and one that
/// says what was true when they passed. The <see cref="Source"/> travels with the
/// value for the same reason it does everywhere else in this runtime: an
/// observation from a simulated world and one from a live client are not
/// interchangeable evidence.
/// </remarks>
public sealed record TestObservation(
    string Key,
    string Value,
    DataSourceKind Source,
    string? Note = null)
{
    public static TestObservation Live(string key, object? value, string? note = null) =>
        new(key, Render(value), DataSourceKind.Live, note);

    public static TestObservation Simulated(string key, object? value, string? note = null) =>
        new(key, Render(value), DataSourceKind.Simulated, note);

    public static TestObservation Unknown(string key, string reason) =>
        new(key, "UNKNOWN", DataSourceKind.Unknown, reason);

    /// <summary>
    /// Renders a value without inventing one: null becomes the word null rather
    /// than an empty cell that reads as zero or as absence of a problem.
    /// </summary>
    private static string Render(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("O"),
        _ => value.ToString() ?? "null"
    };
}

/// <summary>The full record of one test: what it is, and what happened last time.</summary>
public sealed record TestRecord(
    string Id,
    string Suite,
    TestSuiteKind Kind,
    string Name,
    TestOutcome Outcome,
    DateTime? RanAtUtc,
    double DurationMs,
    IReadOnlyList<TestObservation> Observations,
    string? Message)
{
    /// <summary>A test that exists but has never been executed here.</summary>
    public static TestRecord NeverRun(string id, string suite, TestSuiteKind kind, string name) =>
        new(id, suite, kind, name, TestOutcome.NotRun, null, 0, Array.Empty<TestObservation>(), null);

    /// <summary>
    /// How old this result is, or null when it has never run.
    /// </summary>
    /// <remarks>
    /// Surfaced deliberately: a pass from three days ago is evidence about three
    /// days ago. The page shows the age so a stale green cannot pass for a fresh one.
    /// </remarks>
    public TimeSpan? Age => RanAtUtc is null ? null : DateTime.UtcNow - RanAtUtc.Value;
}

/// <summary>
/// Collects observations from whichever check is currently running.
/// </summary>
/// <remarks>
/// An ambient sink rather than a parameter on every check: the gate runners hold
/// roughly a hundred <c>Func&lt;bool&gt;</c> checks, and threading an evidence
/// argument through all of them would be a large mechanical change for no gain.
/// A check that records nothing is reported as having recorded nothing — never as
/// having observed something it did not.
/// </remarks>
public static class TestEvidenceScope
{
    private static readonly AsyncLocal<List<TestObservation>?> Current = new();

    /// <summary>Starts collecting for one check; dispose to stop.</summary>
    public static IDisposable Begin(out List<TestObservation> sink)
    {
        sink = new List<TestObservation>();
        Current.Value = sink;
        return new Scope();
    }

    /// <summary>
    /// Records one observation, if a run is collecting. Safe to call anywhere.
    /// </summary>
    /// <remarks>
    /// Calling this outside a run is a no-op rather than an error: production code
    /// paths that record evidence must not fail because nobody is watching.
    /// </remarks>
    public static void Record(TestObservation observation) => Current.Value?.Add(observation);

    public static void Live(string key, object? value, string? note = null) =>
        Record(TestObservation.Live(key, value, note));

    public static void Simulated(string key, object? value, string? note = null) =>
        Record(TestObservation.Simulated(key, value, note));

    public static void Unknown(string key, string reason) =>
        Record(TestObservation.Unknown(key, reason));

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

/// <summary>
/// Every test known to this repository, with the last result seen for each.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is <b>discovered</b>, never hand-written: xUnit through
/// <c>dotnet test --list-tests</c>, pytest through its JUnit report, and the gate
/// checks by registering themselves as they run. A hand-maintained list would be
/// wrong the first time someone adds a test and forgets the list — and a test page
/// that silently omits a test is worse than no page at all.
/// </para>
/// <para>
/// Results persist to <c>data/test_evidence.json</c> so the page can show what was
/// last observed without re-running everything, together with how long ago that was.
/// </para>
/// </remarks>
public sealed class TestCatalog
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TestRecord> _records = new(StringComparer.Ordinal);
    private readonly string? _persistPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TestCatalog(string? persistPath = null)
    {
        _persistPath = persistPath;
        Load();
    }

    /// <summary>When the catalogue was last refreshed from discovery.</summary>
    public DateTime? LastDiscoveryUtc { get; private set; }

    public IReadOnlyList<TestRecord> All()
    {
        lock (_lock)
            return _records.Values
                .OrderBy(r => r.Suite, StringComparer.Ordinal)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToArray();
    }

    /// <summary>
    /// Adds tests found by discovery, leaving any result already known untouched.
    /// </summary>
    public void Register(IEnumerable<(string Id, string Suite, TestSuiteKind Kind, string Name)> discovered)
    {
        lock (_lock)
        {
            foreach (var (id, suite, kind, name) in discovered)
            {
                if (!_records.ContainsKey(id))
                    _records[id] = TestRecord.NeverRun(id, suite, kind, name);
            }
            LastDiscoveryUtc = DateTime.UtcNow;
        }
        Persist();
    }

    /// <summary>Records the outcome of one test run.</summary>
    public void Report(TestRecord record)
    {
        lock (_lock)
            _records[record.Id] = record;
        Persist();
    }

    /// <summary>Records several outcomes at once, persisting only after the last.</summary>
    public void ReportMany(IEnumerable<TestRecord> records)
    {
        lock (_lock)
        {
            foreach (var record in records)
                _records[record.Id] = record;
        }
        Persist();
    }

    /// <summary>
    /// Drops the stand-in entry for a suite once its real checks are known.
    /// </summary>
    /// <remarks>
    /// The gate suites are listed as a single "not yet run" row until they execute,
    /// because their checks are anonymous lambdas that only name themselves while
    /// running. Once the real names arrive the stand-in has to go, or the page shows
    /// a permanently-unrun row for a suite that just passed.
    /// </remarks>
    public void RemovePlaceholder(string placeholderId)
    {
        lock (_lock)
            _records.Remove(placeholderId);
        // Persisted like every other change: removing it only in memory brought the
        // stand-in back on the next start, so a suite that had run showed as unrun.
        Persist();
    }

    /// <summary>
    /// Marks every test in a suite as never run.
    /// </summary>
    /// <remarks>
    /// Used before a suite executes so that a test which vanished from the suite
    /// (renamed, deleted, or excluded by a filter) cannot keep displaying the pass
    /// it earned under its old name.
    /// </remarks>
    public void InvalidateSuite(TestSuiteKind kind)
    {
        lock (_lock)
        {
            foreach (var id in _records.Where(r => r.Value.Kind == kind).Select(r => r.Key).ToArray())
            {
                TestRecord old = _records[id];
                _records[id] = TestRecord.NeverRun(id, old.Suite, old.Kind, old.Name);
            }
        }
    }

    public TestCatalogSummary Summarize()
    {
        lock (_lock)
        {
            TestRecord[] all = _records.Values.ToArray();
            return new TestCatalogSummary(
                Total: all.Length,
                Passed: all.Count(r => r.Outcome == TestOutcome.Passed),
                Failed: all.Count(r => r.Outcome is TestOutcome.Failed or TestOutcome.Errored),
                Skipped: all.Count(r => r.Outcome == TestOutcome.Skipped),
                NotRun: all.Count(r => r.Outcome == TestOutcome.NotRun),
                WithObservations: all.Count(r => r.Observations.Count > 0),
                LastDiscoveryUtc: LastDiscoveryUtc);
        }
    }

    // ------------------------------------------------------------ persistence

    private void Persist()
    {
        if (_persistPath is null)
            return;
        try
        {
            string? dir = Path.GetDirectoryName(_persistPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            TestRecord[] snapshot;
            lock (_lock)
                snapshot = _records.Values.ToArray();

            // Written through a temporary file: a crash mid-write would otherwise
            // leave a truncated catalogue that reads as "these tests do not exist".
            string temp = _persistPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temp, _persistPath, overwrite: true);
        }
        catch (Exception)
        {
            // Persistence is a convenience; losing it must not take down a test run.
        }
    }

    private void Load()
    {
        if (_persistPath is null || !File.Exists(_persistPath))
            return;
        try
        {
            var loaded = JsonSerializer.Deserialize<TestRecord[]>(
                File.ReadAllText(_persistPath), JsonOptions);
            if (loaded is null)
                return;
            lock (_lock)
            {
                foreach (TestRecord record in loaded)
                    _records[record.Id] = record;
            }
        }
        catch (Exception)
        {
            // A corrupt file means we know nothing, which is the state we start in
            // anyway. It must not mean we claim everything passed.
        }
    }
}

public sealed record TestCatalogSummary(
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int NotRun,
    int WithObservations,
    DateTime? LastDiscoveryUtc);
