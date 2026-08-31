using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace NosAi.Runtime.Testing;

/// <summary>The result of asking a suite to discover or run its tests.</summary>
/// <remarks>
/// <see cref="Available"/> is separate from success on purpose. "The SDK is not
/// installed here" and "the tests failed" are different facts, and a page that
/// showed the first as the second would send someone hunting a bug that does not
/// exist.
/// </remarks>
public sealed record SuiteResult(
    TestSuiteKind Kind,
    bool Available,
    string? Unavailable,
    IReadOnlyList<TestRecord> Records,
    string? RawOutput)
{
    public static SuiteResult NotAvailable(TestSuiteKind kind, string reason) =>
        new(kind, false, reason, Array.Empty<TestRecord>(), null);
}

/// <summary>
/// Discovers and runs the repository's test suites, reporting what each observed.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is discovery-driven. Nothing holds a hand-written list of test
/// names, so a test added tomorrow appears on the operator page without anyone
/// remembering to register it — the failure mode of a manual list is that it is
/// silently incomplete, which is precisely the property a test inventory must not have.
/// </para>
/// <para>
/// The out-of-process suites are read from their machine-readable reports (TRX for
/// xUnit, JUnit XML for pytest) rather than by scraping console text, so a change
/// in console formatting cannot quietly turn failures into unparsed lines.
/// </para>
/// </remarks>
public sealed class TestSuiteRunner
{
    private readonly string _repoRoot;
    private readonly string _workDirectory;

    public TestSuiteRunner(string repoRoot, string? workDirectory = null)
    {
        _repoRoot = repoRoot;
        _workDirectory = workDirectory ?? Path.Combine(Path.GetTempPath(), "nosai-test-reports");
    }

    /// <summary>
    /// Finds the repository root by walking up for a known marker.
    /// </summary>
    /// <returns>Null when it cannot be found, rather than a guess that would make
    /// every subsequent command run in the wrong directory.</returns>
    public static string? FindRepositoryRoot(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    // ------------------------------------------------------------- discovery

    /// <summary>
    /// The compiled xUnit assembly, or null when the project has not been built.
    /// </summary>
    /// <remarks>
    /// Everything .NET-side goes through <c>dotnet vstest</c> on this assembly rather
    /// than <c>dotnet test</c>, which would rebuild. The runtime serving this page is
    /// itself running <c>NosAi.Runtime.dll</c>, and a rebuild cannot replace a file
    /// Windows has open: <c>dotnet test</c> failed on the locked file and that
    /// surfaced as "no tests listed", which reads as "this suite is empty". A page
    /// that cannot tell an empty suite from an unbuildable one is worse than useless.
    /// </remarks>
    private string? FindTestAssembly()
    {
        string bin = Path.Combine(_repoRoot, "tests", "NosAi.Runtime.Tests", "bin");
        if (!Directory.Exists(bin))
            return null;

        return Directory.EnumerateFiles(bin, "NosAi.Runtime.Tests.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private const string BuildFirst =
        "assembly di test non compilato - esegui: dotnet build tests/NosAi.Runtime.Tests -c Release";

    /// <summary>Lists the xUnit tests without running any of them.</summary>
    public async Task<SuiteResult> DiscoverDotNetAsync(CancellationToken token = default)
    {
        string? assembly = FindTestAssembly();
        if (assembly is null)
            return SuiteResult.NotAvailable(TestSuiteKind.DotNet, BuildFirst);

        ProcessResult run = await RunProcessAsync(
            "dotnet",
            $"vstest \"{assembly}\" --ListTests",
            TimeSpan.FromMinutes(5),
            token).ConfigureAwait(false);

        if (!run.Started)
            return SuiteResult.NotAvailable(TestSuiteKind.DotNet, run.Failure ?? "dotnet_not_available");

        var records = new List<TestRecord>();
        foreach (string line in run.StandardOutput.Split('\n'))
        {
            string name = line.TrimEnd('\r').Trim();
            // The listing indents each test; anything unindented is chrome. The name
            // may legitimately contain spaces: a [Theory] case carries its arguments,
            // as in Check(mode: Stopped, risk: 0). Excluding those silently dropped 51
            // of 329 tests from the page, which is the exact failure a discovered
            // inventory exists to avoid.
            if (name.Length == 0
                || !line.StartsWith("    ", StringComparison.Ordinal)
                || !name.Contains('.'))
                continue;
            records.Add(TestRecord.NeverRun($"dotnet::{name}", "NosAi.Runtime.Tests", TestSuiteKind.DotNet, name));
        }

        return records.Count == 0
            ? SuiteResult.NotAvailable(TestSuiteKind.DotNet,
                $"nessun test elencato dall'assembly ({Path.GetFileName(assembly)})")
            : new SuiteResult(TestSuiteKind.DotNet, true, null, records, null);
    }

    /// <summary>Lists the pytest tests without running any of them.</summary>
    public async Task<SuiteResult> DiscoverPythonAsync(CancellationToken token = default)
    {
        ProcessResult run = await RunProcessAsync(
            "python",
            // Deliberately not -q: the quiet form prints one "file: count" line per
            // module, which listed 35 entries for 189 tests. The default collect-only
            // output prints the node id of every test, one per line.
            "-m pytest --collect-only",
            TimeSpan.FromMinutes(3),
            token,
            workingDirectory: _repoRoot).ConfigureAwait(false);

        if (!run.Started)
            return SuiteResult.NotAvailable(TestSuiteKind.Python, run.Failure ?? "python_not_available");

        var records = new List<TestRecord>();
        foreach (string raw in run.StandardOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            // A node id looks like tests/dir/test_file.py::test_name. The angle-bracket
            // lines are pytest's own tree chrome (<Module ...>, <Function ...>).
            if (line.Contains("::", StringComparison.Ordinal)
                && line.Contains(".py", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("<", StringComparison.Ordinal))
            {
                records.Add(TestRecord.NeverRun(
                    PythonIdFromNodeId(line), PythonSuiteOf(line), TestSuiteKind.Python, line));
            }
        }

        return records.Count == 0
            ? SuiteResult.NotAvailable(TestSuiteKind.Python, "no_tests_collected")
            : new SuiteResult(TestSuiteKind.Python, true, null, records, null);
    }

    // --------------------------------------------------------------- running

    /// <summary>Runs the xUnit suite and reads the outcome of each test from the TRX report.</summary>
    public async Task<SuiteResult> RunDotNetAsync(string? filter = null, CancellationToken token = default)
    {
        string? assembly = FindTestAssembly();
        if (assembly is null)
            return SuiteResult.NotAvailable(TestSuiteKind.DotNet, BuildFirst);

        Directory.CreateDirectory(_workDirectory);
        string trxName = $"dotnet-{Guid.NewGuid():N}.trx";
        string args = $"vstest \"{assembly}\" --logger:\"trx;LogFileName={trxName}\" " +
                      $"--ResultsDirectory:\"{_workDirectory}\"";
        if (!string.IsNullOrWhiteSpace(filter))
            args += $" --TestCaseFilter:\"{filter}\"";

        ProcessResult run = await RunProcessAsync("dotnet", args, TimeSpan.FromMinutes(15), token).ConfigureAwait(false);
        if (!run.Started)
            return SuiteResult.NotAvailable(TestSuiteKind.DotNet, run.Failure ?? "dotnet_not_available");

        string trx = Path.Combine(_workDirectory, trxName);
        if (!File.Exists(trx))
            return SuiteResult.NotAvailable(TestSuiteKind.DotNet,
                $"referto TRX assente (uscita {run.ExitCode}); l'esecuzione non ha prodotto un risultato leggibile");

        IReadOnlyList<TestRecord> records = ParseTrx(trx);
        TryDelete(trx);
        return new SuiteResult(TestSuiteKind.DotNet, true, null, records, Tail(run.StandardOutput));
    }

    /// <summary>Runs pytest and reads each outcome from the JUnit report.</summary>
    public async Task<SuiteResult> RunPythonAsync(string? nodeId = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(_workDirectory);
        string xml = Path.Combine(_workDirectory, $"pytest-{Guid.NewGuid():N}.xml");
        string target = string.IsNullOrWhiteSpace(nodeId) ? "" : $" \"{nodeId}\"";
        // junit_logging=all puts the captured stdout into the report. Without it the
        // report records only pass/fail, and every observation a test emitted is
        // dropped before it can be read back.
        string args = $"-m pytest -q -o junit_logging=all --junit-xml=\"{xml}\"{target}";

        ProcessResult run = await RunProcessAsync("python", args, TimeSpan.FromMinutes(10), token,
            workingDirectory: _repoRoot).ConfigureAwait(false);
        if (!run.Started)
            return SuiteResult.NotAvailable(TestSuiteKind.Python, run.Failure ?? "python_not_available");

        if (!File.Exists(xml))
            return SuiteResult.NotAvailable(TestSuiteKind.Python,
                $"junit_report_missing (exit {run.ExitCode}); the run produced no machine-readable result");

        IReadOnlyList<TestRecord> records = ParseJUnit(xml);
        TryDelete(xml);
        return new SuiteResult(TestSuiteKind.Python, true, null, records, Tail(run.StandardOutput));
    }

    // ----------------------------------------------------------- report parsing

    private static IReadOnlyList<TestRecord> ParseTrx(string path)
    {
        var records = new List<TestRecord>();
        XDocument doc = XDocument.Load(path);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        foreach (XElement result in doc.Descendants(ns + "UnitTestResult"))
        {
            string name = result.Attribute("testName")?.Value ?? "(senza nome)";
            string outcome = result.Attribute("outcome")?.Value ?? "";
            string? message = result.Descendants(ns + "Message").FirstOrDefault()?.Value;
            string? stack = result.Descendants(ns + "StackTrace").FirstOrDefault()?.Value;
            string? stdout = result.Descendants(ns + "StdOut").FirstOrDefault()?.Value;

            double ms = 0;
            if (TimeSpan.TryParse(result.Attribute("duration")?.Value, out TimeSpan d))
                ms = d.TotalMilliseconds;

            DateTime? at = DateTime.TryParse(result.Attribute("endTime")?.Value, out DateTime e)
                ? e.ToUniversalTime()
                : DateTime.UtcNow;

            // Observations the test emitted explicitly come first; whatever it printed
            // for a human is kept after them rather than discarded.
            var (emitted, remaining) = TestEvidenceProtocol.Extract(stdout);
            var observations = new List<TestObservation>(emitted);
            if (!string.IsNullOrWhiteSpace(remaining))
                observations.Add(TestObservation.Live("stdout", Trim(remaining, 2000), "output prodotto dal test"));

            records.Add(new TestRecord(
                $"dotnet::{name}",
                "NosAi.Runtime.Tests",
                TestSuiteKind.DotNet,
                name,
                MapOutcome(outcome),
                at,
                ms,
                observations,
                Compose(message, stack)));
        }
        return records;
    }

    private static IReadOnlyList<TestRecord> ParseJUnit(string path)
    {
        var records = new List<TestRecord>();
        XDocument doc = XDocument.Load(path);

        foreach (XElement testCase in doc.Descendants("testcase"))
        {
            string cls = testCase.Attribute("classname")?.Value ?? "";
            string name = testCase.Attribute("name")?.Value ?? "(senza nome)";
            string full = string.IsNullOrEmpty(cls) ? name : $"{cls}::{name}";

            double ms = double.TryParse(testCase.Attribute("time")?.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                ? seconds * 1000
                : 0;

            TestOutcome outcome = TestOutcome.Passed;
            string? message = null;
            if (testCase.Element("failure") is XElement failure)
            {
                outcome = TestOutcome.Failed;
                message = Trim(failure.Attribute("message")?.Value + "\n" + failure.Value, 4000);
            }
            else if (testCase.Element("error") is XElement error)
            {
                outcome = TestOutcome.Errored;
                message = Trim(error.Attribute("message")?.Value + "\n" + error.Value, 4000);
            }
            else if (testCase.Element("skipped") is not null)
            {
                outcome = TestOutcome.Skipped;
                message = testCase.Element("skipped")?.Attribute("message")?.Value;
            }

            var (emitted, remaining) = TestEvidenceProtocol.Extract(testCase.Element("system-out")?.Value);
            var observations = new List<TestObservation>(emitted);
            if (!string.IsNullOrWhiteSpace(remaining))
                observations.Add(TestObservation.Live("stdout", Trim(remaining, 2000), "output prodotto dal test"));

            records.Add(new TestRecord(
                PythonId(cls, name),
                PythonSuiteOf(cls),
                TestSuiteKind.Python,
                full,
                outcome,
                DateTime.UtcNow,
                ms,
                observations,
                message));
        }
        return records;
    }

    // ------------------------------------------------------------- utilities

    /// <summary>
    /// The one identity a pytest test has, whichever report it came from.
    /// </summary>
    /// <remarks>
    /// Collection prints node ids (<c>tests/x/test_y.py::test_z</c>) while the JUnit
    /// report prints a dotted module (<c>tests.x.test_y</c>). Left alone the same test
    /// appeared twice on the page, once as never run and once as passed, so both forms
    /// are folded to the dotted one here.
    /// <para>
    /// This repository has no class-based pytest tests. If one is added its JUnit
    /// module carries the class as a further dotted segment and the two forms would
    /// differ again; the duplicate would then be visible on the page rather than
    /// silent, which is why this is written down instead of guessed at.
    /// </para>
    /// </remarks>
    public static string PythonId(string moduleOrPath, string testName)
    {
        string module = moduleOrPath
            .Replace(".py", "", StringComparison.OrdinalIgnoreCase)
            .Replace('/', '.')
            .Replace('\\', '.')
            .Trim('.');
        return $"python::{module}::{testName}";
    }

    /// <summary>Folds a collected node id into that same identity.</summary>
    public static string PythonIdFromNodeId(string nodeId)
    {
        int split = nodeId.LastIndexOf("::", StringComparison.Ordinal);
        return split < 0
            ? PythonId(nodeId, "")
            : PythonId(nodeId[..split], nodeId[(split + 2)..]);
    }

    private static string PythonSuiteOf(string classNameOrPath)
    {
        string s = classNameOrPath.Replace('/', '.').Replace('\\', '.');
        int last = s.LastIndexOf('.');
        return last <= 0 ? "pytest" : s[..last];
    }

    private static TestOutcome MapOutcome(string trxOutcome) => trxOutcome switch
    {
        "Passed" => TestOutcome.Passed,
        "Failed" => TestOutcome.Failed,
        "NotExecuted" => TestOutcome.Skipped,
        "Error" => TestOutcome.Errored,
        // An outcome we do not recognise is not a pass. Fail closed.
        _ => TestOutcome.Errored
    };

    private static string? Compose(string? message, string? stack)
    {
        string joined = string.Join("\n", new[] { message, stack }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(joined) ? null : Trim(joined, 4000);
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Length <= max ? value : value[..max] + $"\n… ({value.Length - max} caratteri omessi)";
    }

    private static string Tail(string value, int lines = 40)
    {
        string[] all = value.Split('\n');
        return all.Length <= lines ? value : string.Join("\n", all[^lines..]);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record ProcessResult(bool Started, int ExitCode, string StandardOutput, string? Failure);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken token,
        string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,

            // Read as UTF-8 rather than the console codepage. Two [Theory] names
            // contain a middle dot; decoded with the OEM codepage they came back as
            // mojibake, so their ids no longer matched the ones in the TRX report and
            // both tests appeared twice: once passed, once never run.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

            if (!process.Start())
                return new ProcessResult(false, -1, "", $"could_not_start:{fileName}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A hung suite must be reported as hung, not left to block the page.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new ProcessResult(false, -1, output.ToString(),
                    $"timeout_after_{timeout.TotalSeconds:F0}s");
            }

            return new ProcessResult(true, process.ExitCode, output.ToString(), null);
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, -1, output.ToString(), $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
