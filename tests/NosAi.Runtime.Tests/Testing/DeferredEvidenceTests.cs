using System;
using System.Collections.Generic;
using System.IO;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class DeferredEvidenceTests : IDisposable
{
    private readonly string _root;

    public DeferredEvidenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nosai-evidence-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void Writing_an_evidence_leaves_the_file_and_a_latest_pointer()
    {
        IReadOnlyList<DeferredCriterion> declared = DeferredEvidence.CriteriaFor("T-14");
        var criteria = new List<DeferredCriterion>
        {
            declared[0] with { Satisfied = true },
            declared[1],
            declared[2],
        };

        var record = new DeferredEvidenceRecord(
            "T-14",
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            Complete: true,
            Criteria: criteria,
            Measurements: new Dictionary<string, string> { ["packets_before_discovery"] = "12" },
            Provenance: DataSourceKind.Live,
            Attachments: new List<string> { "data/nostale_prelogin.noscap" },
            IncompleteReason: null);

        string evidencePath = DeferredEvidence.Write(record, _root);

        Assert.True(File.Exists(evidencePath));
        Assert.True(File.Exists(Path.Combine(_root, "T-14", DeferredEvidence.LatestFileName)));

        DeferredEvidencePointer? pointer = DeferredEvidence.ReadPointer("T-14", _root);
        Assert.NotNull(pointer);
        Assert.Equal(evidencePath, pointer.EvidencePath);

        DeferredEvidenceRecord? latest = DeferredEvidence.ReadLatest("T-14", _root);
        Assert.NotNull(latest);
        Assert.Equal("T-14", latest.TestId);
        Assert.True(latest.Complete);
        Assert.Equal(DataSourceKind.Live, latest.Provenance);

        Assert.Equal(criteria.Count, latest.Criteria.Count);
        for (int i = 0; i < criteria.Count; i++)
        {
            Assert.Equal(criteria[i].Id, latest.Criteria[i].Id);
            Assert.Equal(criteria[i].Satisfied, latest.Criteria[i].Satisfied);
        }

        Assert.Equal(record.Measurements.Count, latest.Measurements.Count);
        Assert.Equal("12", latest.Measurements["packets_before_discovery"]);
        Assert.Equal(new List<string> { "data/nostale_prelogin.noscap" }, latest.Attachments);
        Assert.Null(latest.IncompleteReason);
    }

    [Fact]
    public void The_latest_pointer_follows_the_most_recent_write()
    {
        var first = NewRecord(
            "T-05",
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["vitals_samples"] = "12" },
            complete: false,
            incompleteReason: "deferred for a later round");
        var second = NewRecord(
            "T-05",
            new DateTime(2026, 9, 8, 12, 5, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["vitals_samples"] = "15" },
            complete: true);

        string firstPath = DeferredEvidence.Write(first, _root);
        string secondPath = DeferredEvidence.Write(second, _root);

        Assert.NotEqual(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));

        DeferredEvidenceRecord? latest = DeferredEvidence.ReadLatest("T-05", _root);
        Assert.NotNull(latest);
        Assert.Equal(new DateTime(2026, 9, 8, 12, 5, 0, DateTimeKind.Utc), latest.ClosedUtc);
        Assert.Equal("15", latest.Measurements["vitals_samples"]);

        DeferredEvidencePointer? pointer = DeferredEvidence.ReadPointer("T-05", _root);
        Assert.NotNull(pointer);
        Assert.Equal(secondPath, pointer.EvidencePath);
    }

    [Fact]
    public void The_criteria_are_declared_in_code()
    {
        IReadOnlyList<DeferredCriterion> t14 = DeferredEvidence.CriteriaFor("T-14");
        Assert.Equal(3, t14.Count);
        Assert.Equal("capture_armed_before_client", t14[0].Id);
        Assert.Equal("handshake_recorded", t14[1].Id);
        Assert.Equal("character_load_observed", t14[2].Id);
        Assert.All(t14, criterion => Assert.False(criterion.Satisfied));
        Assert.All(t14, criterion => Assert.Null(criterion.Detail));

        IReadOnlyList<DeferredCriterion> t05 = DeferredEvidence.CriteriaFor("T-05");
        Assert.Equal(2, t05.Count);
        Assert.Equal("vitals_read_from_wire", t05[0].Id);
        Assert.Equal("vitals_provenance_live", t05[1].Id);
        Assert.All(t05, criterion => Assert.False(criterion.Satisfied));

        Assert.Empty(DeferredEvidence.CriteriaFor("T-99"));
    }

    [Fact]
    public void An_identifier_never_written_reads_as_null()
    {
        Assert.Null(DeferredEvidence.ReadPointer("T-14", _root));
        Assert.Null(DeferredEvidence.ReadLatest("T-14", _root));
    }

    [Fact]
    public void Writing_rejects_an_identifier_that_could_escape_the_evidence_root()
    {
        string[] invalidIds =
        {
            "T-14/..",
            "T-14\\..",
            "T-14/",
            "\\T-14",
            ".",
            "..",
            "bad:id",
            "bad?name",
            "bad|name",
        };

        foreach (string testId in invalidIds)
        {
            var record = new DeferredEvidenceRecord(
                testId,
                new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
                Complete: true,
                Criteria: new List<DeferredCriterion>(),
                Measurements: new Dictionary<string, string>(),
                Provenance: DataSourceKind.Live,
                Attachments: new List<string>(),
                IncompleteReason: null);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => DeferredEvidence.Write(record, _root));
            Assert.StartsWith("invalid_test_id:", exception.Message);
        }

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Writing_twice_with_the_same_timestamp_keeps_both_evidences()
    {
        var closedUtc = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var first = NewRecord(
            "T-14",
            closedUtc,
            new Dictionary<string, string> { ["run"] = "first" },
            complete: true);
        var second = NewRecord(
            "T-14",
            closedUtc,
            new Dictionary<string, string> { ["run"] = "second" },
            complete: true);

        string firstPath = DeferredEvidence.Write(first, _root);
        string secondPath = DeferredEvidence.Write(second, _root);

        Assert.NotEqual(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));

        string firstStamp = Path.GetFileName(Path.GetDirectoryName(firstPath)!);
        string secondStamp = Path.GetFileName(Path.GetDirectoryName(secondPath)!);
        Assert.StartsWith("20260908_120000_000Z", firstStamp);
        Assert.Equal("20260908_120000_000Z_2", secondStamp);

        // The first evidence was not overwritten: it still carries the first run.
        Assert.Contains("\"run\": \"first\"", File.ReadAllText(firstPath));
        Assert.DoesNotContain("\"run\": \"second\"", File.ReadAllText(firstPath));

        DeferredEvidenceRecord? latest = DeferredEvidence.ReadLatest("T-14", _root);
        Assert.NotNull(latest);
        Assert.Equal("second", latest.Measurements["run"]);
    }

    [Fact]
    public void A_corrupted_latest_pointer_is_reported_as_invalid_data()
    {
        var record = NewRecord(
            "T-14",
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["packets_before_discovery"] = "12" },
            complete: true);

        DeferredEvidence.Write(record, _root);

        string latestPath = Path.Combine(_root, "T-14", DeferredEvidence.LatestFileName);
        File.WriteAllText(latestPath, "{ not valid json");

        Assert.Throws<InvalidDataException>(() => DeferredEvidence.ReadPointer("T-14", _root));
        Assert.Throws<InvalidDataException>(() => DeferredEvidence.ReadLatest("T-14", _root));
    }

    [Fact]
    public void A_corrupted_evidence_file_is_reported_as_invalid_data()
    {
        var record = NewRecord(
            "T-14",
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["packets_before_discovery"] = "12" },
            complete: true);

        string evidencePath = DeferredEvidence.Write(record, _root);
        File.WriteAllText(evidencePath, "{ not valid json");

        Assert.Throws<InvalidDataException>(() => DeferredEvidence.ReadLatest("T-14", _root));
    }

    private static DeferredEvidenceRecord NewRecord(
        string testId,
        DateTime closedUtc,
        IReadOnlyDictionary<string, string> measurements,
        bool complete,
        string? incompleteReason = null)
    {
        IReadOnlyList<DeferredCriterion> declared = DeferredEvidence.CriteriaFor(testId);
        return new DeferredEvidenceRecord(
            testId,
            closedUtc,
            complete,
            new List<DeferredCriterion>(declared),
            measurements,
            DataSourceKind.Live,
            new List<string> { "data/nostale_prelogin.noscap" },
            incompleteReason);
    }
}
