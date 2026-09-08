using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Testing;

public enum StartupRoundMode
{
    Full,
    Quick
}

public enum StartupCheckOutcome
{
    Pass,
    Fail,
    Unknown,
    Skipped
}

public sealed record StartupCheckRecord(
    string Id,
    string Title,
    string Category,
    StartupCheckOutcome Outcome,
    long DurationMs,
    string Evidence,
    string? Reason)
{
    public const string SuiteCategory = "Suite";
    public const string EnvironmentCategory = "Environment";
}

public sealed record StartupRoundReport(
    int SchemaVersion,
    StartupRoundMode Mode,
    DateTime StartedUtc,
    long TotalMs,
    int PassCount,
    int FailCount,
    int UnknownCount,
    int SkippedCount,
    IReadOnlyList<StartupCheckRecord> Checks)
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultDirectory = "data/health";
    public const string LatestFileName = "startup-latest.json";
    public const string HistoryPrefix = "startup-";
    public const int HistoryLimit = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [JsonIgnore]
    public bool HasFailures => FailCount > 0;

    [JsonIgnore]
    public bool HasUnknown => UnknownCount > 0;

    public static StartupRoundReport Create(
        StartupRoundMode mode,
        DateTime startedUtc,
        long totalMs,
        IReadOnlyList<StartupCheckRecord> checks)
    {
        StartupCheckRecord[] materialized = checks.ToArray();
        int passCount = 0;
        int failCount = 0;
        int unknownCount = 0;
        int skippedCount = 0;

        foreach (StartupCheckRecord check in materialized)
        {
            switch (check.Outcome)
            {
                case StartupCheckOutcome.Pass:
                    passCount++;
                    break;
                case StartupCheckOutcome.Fail:
                    failCount++;
                    break;
                case StartupCheckOutcome.Unknown:
                    unknownCount++;
                    break;
                case StartupCheckOutcome.Skipped:
                    skippedCount++;
                    break;
                default:
                    break;
            }
        }

        return new StartupRoundReport(
            CurrentSchemaVersion,
            mode,
            startedUtc,
            totalMs,
            passCount,
            failCount,
            unknownCount,
            skippedCount,
            materialized);
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static StartupRoundReport? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            StartupRoundReport? report = JsonSerializer.Deserialize<StartupRoundReport>(json, JsonOptions);
            if (report is null || report.Checks is null)
            {
                return null;
            }

            return report;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    public static StartupRoundReport? TryRead(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return TryParse(json);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string Write(string directory)
    {
        Directory.CreateDirectory(directory);

        string json = Serialize();
        string latestPath = Path.Combine(directory, LatestFileName);
        File.WriteAllText(latestPath, json);

        string historyName =
            HistoryPrefix +
            StartedUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff'Z'", CultureInfo.InvariantCulture) +
            ".json";
        File.WriteAllText(Path.Combine(directory, historyName), json);

        PruneHistory(directory);

        return latestPath;
    }

    private static void PruneHistory(string directory)
    {
        try
        {
            string[] files = Directory.GetFiles(directory, HistoryPrefix + "*.json");
            List<string> historyFiles = files
                .Where(file => !string.Equals(Path.GetFileName(file), LatestFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
                .ToList();

            int toDelete = historyFiles.Count - HistoryLimit;
            for (int i = 0; i < toDelete; i++)
            {
                try
                {
                    File.Delete(historyFiles[i]);
                }
                catch (IOException)
                {
                    // A failed deletion of a stale copy must not fail the write.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same as above: pruning is best effort.
                }
            }
        }
        catch (IOException)
        {
            // Listing may race with external cleanup; pruning is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: pruning is best effort.
        }
    }

    public static string Format(StartupRoundReport report)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("=== Ronda di avvio (")
            .Append(report.Mode.ToString())
            .AppendLine(") ===");
        builder.Append("avviata ")
            .Append(report.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC · durata ")
            .Append(report.TotalMs.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" ms");

        foreach (StartupCheckRecord check in report.Checks)
        {
            string outcome = check.Outcome.ToString().ToUpperInvariant();
            builder.Append(string.Format(
                CultureInfo.InvariantCulture,
                "[{0,-7}] {1,-34} {2,6} ms  {3}",
                outcome,
                check.Id,
                check.DurationMs,
                check.Evidence));

            if (!string.IsNullOrEmpty(check.Reason))
            {
                builder.Append(" · ").Append(check.Reason);
            }

            builder.AppendLine();
        }

        builder.Append("Pass ")
            .Append(report.PassCount.ToString(CultureInfo.InvariantCulture))
            .Append(" · Fail ")
            .Append(report.FailCount.ToString(CultureInfo.InvariantCulture))
            .Append(" · Unknown ")
            .Append(report.UnknownCount.ToString(CultureInfo.InvariantCulture))
            .Append(" · Skipped ")
            .Append(report.SkippedCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        return builder.ToString();
    }
}
