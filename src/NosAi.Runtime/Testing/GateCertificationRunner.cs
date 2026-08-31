using System.Text;
using System.Text.RegularExpressions;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Runs the in-process gate certification suites and records each check.
/// </summary>
/// <remarks>
/// <para>
/// The gate runners print one <c>[PASS]</c> or <c>[FAIL]</c> line per check and
/// return a single boolean for the whole suite. That boolean is enough to gate a
/// release and far too little to show on an operator page, so the console output
/// is captured and read back into one record per check.
/// </para>
/// <para>
/// Reading our own output is acceptable here in a way that scraping a third-party
/// tool would not be: this repository owns the format, every gate uses it, and a
/// run that yields no parsed checks is reported as an error rather than as a suite
/// with nothing wrong. Silence is never taken for success.
/// </para>
/// </remarks>
public sealed class GateCertificationRunner
{
    /// <summary>The certification suites, read from the one shared table.</summary>
    public static IReadOnlyList<GateSuite> Suites { get; } =
        CertificationSuites.All.Select(s => new GateSuite(s.Key, s.Description)).ToArray();

    private static readonly Regex CheckLine = new(
        @"^\[(?<state>PASS|FAIL)\]\s+(?<name>.+?)\s*(?:\[OK\]|\[KO\])?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly Func<string, Func<Task<bool>>?> _resolve;

    /// <param name="resolve">
    /// Maps a suite key to its runner. Injected rather than referenced directly so
    /// this type does not have to know every gate namespace, and so a suite can be
    /// exercised in isolation by a test.
    /// </param>
    public GateCertificationRunner(Func<string, Func<Task<bool>>?> resolve) => _resolve = resolve;

    /// <summary>Lists the checks of every suite without running anything.</summary>
    /// <remarks>
    /// Names are unknown until a suite has run at least once — the checks are
    /// anonymous lambdas until then. The catalogue therefore holds one placeholder
    /// per suite, which reads as "never run" rather than as "no tests here".
    /// </remarks>
    public IEnumerable<(string Id, string Suite, TestSuiteKind Kind, string Name)> Discover() =>
        Suites.Select(s => ($"gate::{s.Key}::(suite)", SuiteName(s), TestSuiteKind.GateCertification,
            $"{s.Key}: suite non ancora eseguita"));

    /// <summary>Runs one certification suite and returns a record per check.</summary>
    public async Task<SuiteResult> RunAsync(string suiteKey, CancellationToken token = default)
    {
        GateSuite? suite = Suites.FirstOrDefault(s => s.Key == suiteKey);
        if (suite is null)
            return SuiteResult.NotAvailable(TestSuiteKind.GateCertification, $"unknown_suite:{suiteKey}");

        Func<Task<bool>>? runner = _resolve(suiteKey);
        if (runner is null)
            return SuiteResult.NotAvailable(TestSuiteKind.GateCertification, $"runner_not_wired:{suiteKey}");

        var captured = new StringWriter();
        TextWriter previous = Console.Out;
        bool suitePassed;
        DateTime started = DateTime.UtcNow;

        try
        {
            Console.SetOut(captured);
            suitePassed = await runner().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.SetOut(previous);
            return new SuiteResult(TestSuiteKind.GateCertification, true, null, new[]
            {
                new TestRecord($"gate::{suiteKey}::(suite)", SuiteName(suite), TestSuiteKind.GateCertification,
                    $"{suiteKey}: la suite ha sollevato un'eccezione", TestOutcome.Errored, DateTime.UtcNow,
                    (DateTime.UtcNow - started).TotalMilliseconds, Array.Empty<TestObservation>(),
                    $"{ex.GetType().Name}: {ex.Message}")
            }, captured.ToString());
        }
        finally
        {
            Console.SetOut(previous);
        }

        string output = captured.ToString();
        double totalMs = (DateTime.UtcNow - started).TotalMilliseconds;
        var records = new List<TestRecord>();

        foreach (Match match in CheckLine.Matches(output))
        {
            string name = match.Groups["name"].Value.Trim();
            bool passed = match.Groups["state"].Value == "PASS";
            records.Add(new TestRecord(
                $"gate::{suiteKey}::{name}",
                SuiteName(suite),
                TestSuiteKind.GateCertification,
                name,
                passed ? TestOutcome.Passed : TestOutcome.Failed,
                DateTime.UtcNow,
                0,
                new[]
                {
                    TestObservation.Live("suite", suiteKey),
                    TestObservation.Live("ambiente", "locale — non è verifica in ambiente reale")
                },
                passed ? null : "Il controllo ha riportato FAIL; vedi l'output della suite."));
        }

        if (records.Count == 0)
        {
            // Parsing produced nothing. Whatever the suite returned, we cannot say
            // which checks ran, so this is reported as an error rather than a pass.
            records.Add(new TestRecord(
                $"gate::{suiteKey}::(suite)", SuiteName(suite), TestSuiteKind.GateCertification,
                $"{suiteKey}: nessun controllo riconosciuto nell'output",
                TestOutcome.Errored, DateTime.UtcNow, totalMs,
                new[] { TestObservation.Live("suiteReturned", suitePassed) },
                "L'output non conteneva righe [PASS]/[FAIL]: impossibile dire quali controlli siano stati eseguiti."));
        }

        return new SuiteResult(TestSuiteKind.GateCertification, true, null, records, output);
    }

    private static string SuiteName(GateSuite suite) => $"Certificazione — {suite.Description}";
}

/// <summary>One certification suite reachable from the command line.</summary>
public sealed record GateSuite(string Key, string Description);
