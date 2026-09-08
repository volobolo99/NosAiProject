using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests.Testing;

public class StartupRoundTests
{
    private static string NewTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "nosai-startup-round-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_failing_suite_makes_the_round_red()
    {
        var suites = new[]
        {
            new CertificationSuite("k1", "--k1-test", "prima", () => Task.FromResult(true)),
            new CertificationSuite("k2", "--k2-test", "seconda", () => Task.FromResult(false)),
            new CertificationSuite("k3", "--k3-test", "terza", () => Task.FromResult(true)),
        };

        StartupRoundReport report = await StartupRound.RunAsync(
            StartupRoundMode.Full, suites, includeEnvironment: false);

        Assert.Equal(1, report.FailCount);
        Assert.True(report.HasFailures);
        Assert.Equal(1, StartupRound.ExitCode(report));

        StartupCheckRecord failed = Assert.Single(report.Checks, c => c.Outcome == StartupCheckOutcome.Fail);
        Assert.NotNull(failed.Reason);
    }

    [Fact]
    public void An_unknown_check_is_not_a_failure()
    {
        var checks = new[]
        {
            new StartupCheckRecord(
                "suite.k1", "Suite k1", StartupCheckRecord.SuiteCategory,
                StartupCheckOutcome.Pass, 0, "evidence-one", null),
            new StartupCheckRecord(
                "suite.k2", "Suite k2", StartupCheckRecord.SuiteCategory,
                StartupCheckOutcome.Unknown, 0, "evidence-two", null),
        };

        StartupRoundReport report = StartupRoundReport.Create(
            StartupRoundMode.Quick, DateTime.UtcNow, 10, checks);

        Assert.True(report.HasUnknown);
        Assert.False(report.HasFailures);
        Assert.Equal(1, report.UnknownCount);
        Assert.Equal(1, report.PassCount);
        Assert.Equal(0, StartupRound.ExitCode(report));
    }

    [Fact]
    public async Task A_throwing_suite_is_recorded_and_the_round_continues()
    {
        var suites = new[]
        {
            new CertificationSuite("k1", "--k1-test", "prima", () => Task.FromResult(true)),
            new CertificationSuite(
                "k2", "--k2-test", "seconda",
                () => throw new InvalidOperationException("boom")),
            new CertificationSuite("k3", "--k3-test", "terza", () => Task.FromResult(true)),
        };

        StartupRoundReport report = await StartupRound.RunAsync(
            StartupRoundMode.Full, suites, includeEnvironment: false);

        Assert.Equal(3, report.Checks.Count);
        StartupCheckRecord second = report.Checks[1];
        Assert.Equal(StartupCheckOutcome.Fail, second.Outcome);
        Assert.NotNull(second.Reason);
        Assert.Contains("boom", second.Reason!);
        Assert.Equal(StartupCheckOutcome.Pass, report.Checks[2].Outcome);
    }

    [Fact]
    public void The_report_round_trips_through_json()
    {
        var checks = new[]
        {
            new StartupCheckRecord(
                "suite.gate1", "Gate 1", StartupCheckRecord.SuiteCategory,
                StartupCheckOutcome.Pass, 12, "evidence-one", "passing with a note"),
            new StartupCheckRecord(
                "suite.gate2", "Gate 2", StartupCheckRecord.SuiteCategory,
                StartupCheckOutcome.Fail, 3, "evidence-two", null),
        };

        DateTime startedUtc = new DateTime(2026, 9, 8, 7, 6, 5, DateTimeKind.Utc);
        StartupRoundReport report = StartupRoundReport.Create(
            StartupRoundMode.Full, startedUtc, 1234, checks);

        string json = report.Serialize();
        StartupRoundReport? parsed = StartupRoundReport.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(report.SchemaVersion, parsed!.SchemaVersion);
        Assert.Equal(report.Mode, parsed.Mode);
        Assert.Equal(report.TotalMs, parsed.TotalMs);
        Assert.Equal(report.PassCount, parsed.PassCount);
        Assert.Equal(report.FailCount, parsed.FailCount);
        Assert.Equal(report.UnknownCount, parsed.UnknownCount);
        Assert.Equal(report.SkippedCount, parsed.SkippedCount);
        Assert.Equal(report.Checks.Count, parsed.Checks.Count);

        StartupCheckRecord first = checks[0];
        StartupCheckRecord roundTrippedFirst = parsed.Checks[0];
        Assert.Equal(first.Id, roundTrippedFirst.Id);
        Assert.Equal(first.Outcome, roundTrippedFirst.Outcome);
        Assert.Equal(first.Evidence, roundTrippedFirst.Evidence);
        Assert.Equal(first.Reason, roundTrippedFirst.Reason);
    }

    [Fact]
    public void Corrupt_json_is_read_as_absent()
    {
        Assert.Null(StartupRoundReport.TryParse("{ questo non e json"));
        Assert.Null(StartupRoundReport.TryParse(""));

        string dir = NewTempDirectory();
        try
        {
            Directory.CreateDirectory(dir);

            StartupRoundReport report = StartupRoundReport.Create(
                StartupRoundMode.Quick, DateTime.UtcNow, 5, Array.Empty<StartupCheckRecord>());
            string truncatedPath = Path.Combine(dir, "truncated.json");
            File.WriteAllText(truncatedPath, report.Serialize().Substring(0, 20));

            Assert.Null(StartupRoundReport.TryRead(truncatedPath));
            Assert.Null(StartupRoundReport.TryRead(Path.Combine(dir, "missing.json")));
        }
        finally
        {
            DeleteDirectoryQuietly(dir);
        }
    }

    [Fact]
    public void Write_keeps_latest_and_prunes_history_to_twenty()
    {
        string dir = NewTempDirectory();
        try
        {
            Directory.CreateDirectory(dir);

            DateTime start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var reports = new List<StartupRoundReport>(25);
            for (int i = 0; i < 25; i++)
            {
                StartupRoundReport report = StartupRoundReport.Create(
                    StartupRoundMode.Full, start.AddSeconds(i), 100 + i, Array.Empty<StartupCheckRecord>());
                reports.Add(report);
                report.Write(dir);
            }

            Assert.True(File.Exists(Path.Combine(dir, StartupRoundReport.LatestFileName)));

            string[] historyFiles = Directory.GetFiles(dir, "startup-*.json")
                .Where(f => !string.Equals(
                    Path.GetFileName(f), StartupRoundReport.LatestFileName, StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(20, historyFiles.Length);

            string HistoryFileName(StartupRoundReport report) => "startup-"
                + report.StartedUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff'Z'", CultureInfo.InvariantCulture)
                + ".json";

            Assert.True(File.Exists(Path.Combine(dir, HistoryFileName(reports[24]))));
            Assert.False(File.Exists(Path.Combine(dir, HistoryFileName(reports[0]))));
        }
        finally
        {
            DeleteDirectoryQuietly(dir);
        }
    }

    [Fact]
    public void Write_creates_the_directory_when_missing()
    {
        string dir = Path.Combine(NewTempDirectory(), "health");
        try
        {
            StartupRoundReport report = StartupRoundReport.Create(
                StartupRoundMode.Quick, DateTime.UtcNow, 1, Array.Empty<StartupCheckRecord>());
            string written = report.Write(dir);

            Assert.True(File.Exists(written));
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            DeleteDirectoryQuietly(Path.GetDirectoryName(dir)!);
        }
    }

    [Fact]
    public void Default_suites_match_the_certification_table()
    {
        IReadOnlyList<CertificationSuite> full = StartupRound.DefaultSuites(StartupRoundMode.Full);
        Assert.Equal(CertificationSuites.All.Count, full.Count);
        for (int i = 0; i < CertificationSuites.All.Count; i++)
            Assert.Equal(CertificationSuites.All[i].Key, full[i].Key);

        Assert.Empty(StartupRound.DefaultSuites(StartupRoundMode.Quick));
    }

    [Fact]
    public async Task Every_certification_suite_produces_one_record()
    {
        CertificationSuite[] suites = CertificationSuites.All
            .Select(s => new CertificationSuite(s.Key, s.Flag, s.Description, () => Task.FromResult(true)))
            .ToArray();

        StartupRoundReport report = await StartupRound.RunAsync(
            StartupRoundMode.Full, suites, includeEnvironment: false);

        int suiteRecords = report.Checks.Count(
            c => string.Equals(c.Category, StartupCheckRecord.SuiteCategory, StringComparison.Ordinal));
        Assert.Equal(CertificationSuites.All.Count, suiteRecords);

        foreach (CertificationSuite suite in CertificationSuites.All)
        {
            string expectedId = "suite." + suite.Key;
            int matches = report.Checks.Count(c => string.Equals(c.Id, expectedId, StringComparison.Ordinal));
            Assert.Equal(1, matches);
        }
    }

    [Fact]
    public async Task Quick_mode_runs_no_suites()
    {
        StartupRoundReport report = await StartupRound.RunAsync(
            StartupRoundMode.Quick, StartupRound.DefaultSuites(StartupRoundMode.Quick), includeEnvironment: false);

        Assert.Empty(report.Checks);
        Assert.Equal(StartupRoundMode.Quick, report.Mode);
    }
}
