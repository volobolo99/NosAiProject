namespace NosAi.Runtime.Testing;

/// <summary>What the test console is doing right now.</summary>
public sealed record TestRunState(
    bool Running,
    string? Target,
    DateTime? StartedAtUtc,
    string? LastError,
    DateTime? LastFinishedAtUtc);

/// <summary>
/// The service behind the operator's test page: discovers, runs, and remembers.
/// </summary>
/// <remarks>
/// <para>
/// One run at a time. Two concurrent <c>dotnet test</c> invocations on the same
/// project fight over the build output, and the failures that produces would be
/// reported as test failures — the page would then be manufacturing the very bugs
/// it exists to reveal.
/// </para>
/// <para>
/// Runs happen in the background because a full suite takes far longer than a
/// browser will wait. The page polls for state instead of holding a request open.
/// </para>
/// </remarks>
public sealed class TestConsoleService
{
    private readonly TestCatalog _catalog;
    private readonly TestSuiteRunner _suites;
    private readonly GateCertificationRunner _gates;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private volatile TestRunState _state = new(false, null, null, null, null);

    public TestConsoleService(TestCatalog catalog, TestSuiteRunner suites, GateCertificationRunner gates)
    {
        _catalog = catalog;
        _suites = suites;
        _gates = gates;
    }

    public TestCatalog Catalog => _catalog;
    public TestRunState State => _state;

    /// <summary>The suites the page can offer, whether or not they have ever run.</summary>
    public static IReadOnlyList<(string Key, string Label)> Targets { get; } = new[]
    {
        ("all", "Tutto (xUnit + pytest + certificazioni)"),
        ("dotnet", "xUnit — tests/NosAi.Runtime.Tests"),
        ("python", "pytest — tests/"),
        ("gates", "Certificazioni Gate 1–6 + gateway")
    };

    /// <summary>
    /// Fills the catalogue with every test that exists, without running any.
    /// </summary>
    /// <remarks>
    /// Called at startup so the page can list the whole inventory immediately, each
    /// entry marked as not run until it actually is.
    /// </remarks>
    public async Task DiscoverAllAsync(CancellationToken token = default)
    {
        var notes = new List<string>();
        DiscoveryComplete = false;
        _catalog.Register(_gates.Discover());

        SuiteResult dotnet = await _suites.DiscoverDotNetAsync(token).ConfigureAwait(false);
        if (dotnet.Available)
            _catalog.Register(dotnet.Records.Select(r => (r.Id, r.Suite, r.Kind, r.Name)));
        else
            notes.Add($"xUnit non elencabile: {dotnet.Unavailable}");

        SuiteResult python = await _suites.DiscoverPythonAsync(token).ConfigureAwait(false);
        if (python.Available)
            _catalog.Register(python.Records.Select(r => (r.Id, r.Suite, r.Kind, r.Name)));
        else
            notes.Add($"pytest non elencabile: {python.Unavailable}");

        // A suite that could not be listed must say so. Skipping it quietly would
        // leave the page looking complete while an entire suite went uncounted --
        // the same silent incompleteness a discovered inventory exists to prevent.
        DiscoveryNotes = notes;
        DiscoveryComplete = true;
    }

    /// <summary>Why a suite could not be listed, empty when everything was.</summary>
    public IReadOnlyList<string> DiscoveryNotes { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// False while the inventory is still being built.
    /// </summary>
    /// <remarks>
    /// Without this the page shows a partial inventory that looks complete: the
    /// xUnit listing finishes seconds before pytest's, and a total read in between
    /// is simply wrong rather than merely early.
    /// </remarks>
    public bool DiscoveryComplete { get; private set; }

    /// <summary>
    /// Starts a run in the background.
    /// </summary>
    /// <returns>False when a run is already in flight; the caller is told rather
    /// than silently queued behind it.</returns>
    public bool TryStart(string target, out string reason)
    {
        if (!_oneAtATime.Wait(0))
        {
            reason = $"run_already_in_progress:{_state.Target}";
            return false;
        }

        if (!Targets.Any(t => t.Key == target))
        {
            _oneAtATime.Release();
            reason = $"unknown_target:{target}";
            return false;
        }

        reason = "started";
        _state = new TestRunState(true, target, DateTime.UtcNow, null, _state.LastFinishedAtUtc);

        _ = Task.Run(async () =>
        {
            string? error = null;
            try
            {
                await ExecuteAsync(target, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                _state = new TestRunState(false, target, _state.StartedAtUtc, error, DateTime.UtcNow);
                _oneAtATime.Release();
            }
        });

        return true;
    }

    private async Task ExecuteAsync(string target, CancellationToken token)
    {
        if (target is "all" or "dotnet")
        {
            // Cleared first so a test deleted or renamed since the last run cannot
            // keep showing the pass it earned under a name that no longer exists.
            _catalog.InvalidateSuite(TestSuiteKind.DotNet);
            SuiteResult result = await _suites.RunDotNetAsync(null, token).ConfigureAwait(false);
            if (result.Available)
                _catalog.ReportMany(result.Records);
            else
                MarkSuiteUnavailable(TestSuiteKind.DotNet, result.Unavailable);
        }

        if (target is "all" or "python")
        {
            _catalog.InvalidateSuite(TestSuiteKind.Python);
            SuiteResult result = await _suites.RunPythonAsync(null, token).ConfigureAwait(false);
            if (result.Available)
                _catalog.ReportMany(result.Records);
            else
                MarkSuiteUnavailable(TestSuiteKind.Python, result.Unavailable);
        }

        if (target is "all" or "gates")
        {
            _catalog.InvalidateSuite(TestSuiteKind.GateCertification);
            foreach (GateSuite suite in GateCertificationRunner.Suites)
            {
                SuiteResult result = await _gates.RunAsync(suite.Key, token).ConfigureAwait(false);
                if (!result.Available)
                    continue;

                _catalog.ReportMany(result.Records);

                // The stand-in row stays only while the suite has never named its
                // checks; once it has, keeping it would show the suite as unrun.
                string placeholder = $"gate::{suite.Key}::(suite)";
                if (result.Records.Any(r => r.Id != placeholder))
                    _catalog.RemovePlaceholder(placeholder);
            }
        }
    }

    /// <summary>
    /// Records that a suite could not run at all.
    /// </summary>
    /// <remarks>
    /// Its tests stay listed and stay <c>NotRun</c>. Removing them would make the
    /// page look complete while hiding an entire suite nobody is checking.
    /// </remarks>
    private void MarkSuiteUnavailable(TestSuiteKind kind, string? reason)
    {
        TestRecord[] affected = _catalog.All().Where(r => r.Kind == kind).ToArray();
        _catalog.ReportMany(affected.Select(r => r with
        {
            Outcome = TestOutcome.NotRun,
            Message = $"Suite non eseguibile: {reason ?? "motivo non riportato"}"
        }));
    }

    /// <summary>The catalogue shaped for the operator page.</summary>
    public object Snapshot()
    {
        TestCatalogSummary summary = _catalog.Summarize();
        TestRunState state = _state;

        return new
        {
            summary = new
            {
                total = summary.Total,
                passed = summary.Passed,
                failed = summary.Failed,
                skipped = summary.Skipped,
                notRun = summary.NotRun,
                withObservations = summary.WithObservations,
                lastDiscoveryUtc = summary.LastDiscoveryUtc
            },
            state = new
            {
                running = state.Running,
                target = state.Target,
                startedAtUtc = state.StartedAtUtc,
                lastFinishedAtUtc = state.LastFinishedAtUtc,
                lastError = state.LastError
            },
            targets = Targets.Select(t => new { key = t.Key, label = t.Label }).ToArray(),
            discoveryNotes = DiscoveryNotes,
            discoveryComplete = DiscoveryComplete,
            tests = _catalog.All().Select(r => new
            {
                id = r.Id,
                suite = r.Suite,
                kind = r.Kind.ToString(),
                name = r.Name,
                outcome = r.Outcome.ToString(),
                ranAtUtc = r.RanAtUtc,
                ageSeconds = r.Age?.TotalSeconds,
                durationMs = Math.Round(r.DurationMs, 2),
                message = r.Message,
                observations = r.Observations.Select(o => new
                {
                    key = o.Key,
                    value = o.Value,
                    source = o.Source.ToString().ToUpperInvariant(),
                    note = o.Note
                }).ToArray()
            }).ToArray()
        };
    }
}
