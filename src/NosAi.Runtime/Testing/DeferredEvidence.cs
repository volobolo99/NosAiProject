using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Declares a single acceptance criterion of a deferred test together with the observed outcome.
/// </summary>
/// <param name="Id">Stable identifier of the criterion.</param>
/// <param name="Description">User-facing description of the criterion, in Italian.</param>
/// <param name="Satisfied">Whether the criterion was satisfied by the observed evidence.</param>
/// <param name="Detail">Optional detail explaining how the criterion was checked or why it was not satisfied.</param>
public sealed record DeferredCriterion(string Id, string Description, bool Satisfied, string? Detail = null);

/// <summary>
/// Full evidence written to disk when a deferred test is closed: which criteria were declared,
/// which were satisfied, the measurements observed, the provenance of the data and the attachments produced.
/// </summary>
/// <param name="TestId">Identifier of the deferred test, for example "T-14".</param>
/// <param name="ClosedUtc">Moment in time the deferred test was closed, in UTC.</param>
/// <param name="Complete">Whether the closure is complete or still partial.</param>
/// <param name="Criteria">The declared criteria with their observed satisfaction state.</param>
/// <param name="Measurements">Named measurements observed while closing the test.</param>
/// <param name="Provenance">Where the observed data came from.</param>
/// <param name="Attachments">Paths of the artifacts produced while closing the test.</param>
/// <param name="IncompleteReason">Reason the closure is incomplete, when it is.</param>
public sealed record DeferredEvidenceRecord(
    string TestId,
    DateTime ClosedUtc,
    bool Complete,
    IReadOnlyList<DeferredCriterion> Criteria,
    IReadOnlyDictionary<string, string> Measurements,
    DataSourceKind Provenance,
    IReadOnlyList<string> Attachments,
    string? IncompleteReason);

/// <summary>
/// A pointer to the most recent evidence folder of a deferred test.
/// </summary>
/// <param name="TestId">Identifier of the deferred test the pointer refers to.</param>
/// <param name="ClosedUtc">Closing time stored in the pointed evidence, in UTC.</param>
/// <param name="EvidencePath">Full path of the evidence document the pointer refers to.</param>
/// <param name="Complete">Whether the pointed evidence marks a complete closure.</param>
public sealed record DeferredEvidencePointer(string TestId, DateTime ClosedUtc, string EvidencePath, bool Complete);

/// <summary>
/// Writes deferred-test closing evidence to disk and reads it back.
/// </summary>
/// <remarks>
/// Criteria are declared here, in code, and are never re-read from a document. A criterion that is
/// re-read from a document can change without anyone noticing, and the recorded evidence would then be
/// for a different criterion than the one the test actually verified.
/// </remarks>
public static class DeferredEvidence
{
    /// <summary>
    /// Default root folder used when no root is supplied; relative to the current working directory.
    /// </summary>
    public const string DefaultRoot = "data/evidence";

    /// <summary>
    /// File name of the evidence document inside each timestamped folder.
    /// </summary>
    public const string EvidenceFileName = "evidence.json";

    /// <summary>
    /// File name of the pointer to the most recent evidence folder of a test.
    /// </summary>
    public const string LatestFileName = "latest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Returns the criteria declared for a deferred test id.
    /// </summary>
    /// <remarks>
    /// Every returned criterion has <see cref="DeferredCriterion.Satisfied"/> equal to false and a null detail:
    /// this is the empty model that the caller fills in with what was actually observed. An id without
    /// declared criteria is a fact, not an error, and yields an empty list.
    /// </remarks>
    /// <param name="testId">Identifier of the deferred test, compared case-insensitively.</param>
    /// <returns>The declared criteria, all unsatisfied, or an empty list for an unknown test id.</returns>
    public static IReadOnlyList<DeferredCriterion> CriteriaFor(string testId)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(testId, "T-14"))
        {
            return new[]
            {
                new DeferredCriterion("capture_armed_before_client", "La cattura è armata prima che il processo del client esista.", false),
                new DeferredCriterion("handshake_recorded", "La registrazione comincia dall'inizio della conversazione: il SYN della connessione è nel file.", false),
                new DeferredCriterion("character_load_observed", "Osservati sul filo gli opcodi stat, in e almeno uno fra ivn ed equip.", false),
            };
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(testId, "T-05"))
        {
            return new[]
            {
                new DeferredCriterion("vitals_read_from_wire", "Almeno una lettura di vitals del giocatore decodificata dal filo.", false),
                new DeferredCriterion("vitals_provenance_live", "Le letture di vitals portano provenienza LIVE.", false),
            };
        }

        return Array.Empty<DeferredCriterion>();
    }

    /// <summary>
    /// Writes one deferred evidence record and refreshes the latest pointer for its test id.
    /// </summary>
    /// <remarks>
    /// The evidence is written to <c>root/testId/timestamp/evidence.json</c> and the pointer to
    /// <c>root/testId/latest.json</c>. A null or blank root means <see cref="DefaultRoot"/>.
    /// I/O errors are never swallowed: evidence that was not written must not look written.
    /// </remarks>
    /// <param name="record">The evidence to persist.</param>
    /// <param name="root">Root evidence folder; when null or blank, <see cref="DefaultRoot"/> is used.</param>
    /// <returns>The full path of the written <see cref="EvidenceFileName"/>.</returns>
    /// <exception cref="ArgumentException">The test id is null, blank, or could escape the evidence folder.</exception>
    /// <exception cref="IOException">All timestamped folder names up to the _99 suffix are already taken.</exception>
    public static string Write(DeferredEvidenceRecord record, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.TestId);

        // Reject ids that could write outside the evidence folder instead of silently normalizing them.
        // Path.GetInvalidFileNameChars() is host-dependent: on Linux it is only {'\0','/'}, which would
        // let ids like "bad:id" through on the ubuntu-latest CI runner even though they are meant to be
        // rejected everywhere. The set below is fixed regardless of host OS.
        if (ContainsInvalidTestIdChar(record.TestId)
            || record.TestId.Contains('/')
            || record.TestId.Contains('\\')
            || record.TestId == "."
            || record.TestId == "..")
        {
            throw new ArgumentException($"invalid_test_id:{record.TestId}");
        }

        string baseRoot = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
        string testRoot = Path.Combine(baseRoot, record.TestId);
        string stamp = record.ClosedUtc.ToUniversalTime().ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "Z";
        string directory = Path.Combine(testRoot, stamp);

        if (Directory.Exists(directory))
        {
            // Two writes with the same timestamp must not overwrite each other: try _2 .. _99.
            string? freeDirectory = null;
            for (int attempt = 2; attempt <= 99 && freeDirectory is null; attempt++)
            {
                string candidate = Path.Combine(testRoot, stamp + "_" + attempt.ToString(CultureInfo.InvariantCulture));
                if (!Directory.Exists(candidate))
                {
                    freeDirectory = candidate;
                }
            }

            if (freeDirectory is null)
            {
                throw new IOException($"evidence_directory_taken:{directory}");
            }

            directory = freeDirectory;
        }

        Directory.CreateDirectory(directory);
        string evidencePath = Path.Combine(directory, EvidenceFileName);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(record, JsonOptions));

        var pointer = new DeferredEvidencePointer(record.TestId, record.ClosedUtc.ToUniversalTime(), evidencePath, record.Complete);
        File.WriteAllText(Path.Combine(testRoot, LatestFileName), JsonSerializer.Serialize(pointer, JsonOptions));

        return evidencePath;
    }

    /// <summary>
    /// Reads the latest pointer written for a test id.
    /// </summary>
    /// <param name="testId">Identifier of the deferred test.</param>
    /// <param name="root">Root evidence folder; when null or blank, <see cref="DefaultRoot"/> is used.</param>
    /// <returns>The pointer, or null when no pointer was ever written for the test id.</returns>
    /// <exception cref="ArgumentException">The test id is null or blank.</exception>
    /// <exception cref="InvalidDataException">The pointer file exists but cannot be deserialized; a corrupted file is not a missing file.</exception>
    public static DeferredEvidencePointer? ReadPointer(string testId, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);

        string baseRoot = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
        string path = Path.Combine(baseRoot, testId, LatestFileName);

        if (!File.Exists(path))
        {
            return null;
        }

        DeferredEvidencePointer? pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<DeferredEvidencePointer>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"evidence_pointer_unreadable:{path}");
        }

        return pointer ?? throw new InvalidDataException($"evidence_pointer_unreadable:{path}");
    }

    /// <summary>
    /// Reads the evidence record the latest pointer of a test id refers to.
    /// </summary>
    /// <param name="testId">Identifier of the deferred test.</param>
    /// <param name="root">Root evidence folder; when null or blank, <see cref="DefaultRoot"/> is used.</param>
    /// <returns>The evidence record, or null when no evidence was ever written for the test id.</returns>
    /// <exception cref="ArgumentException">The test id is null or blank.</exception>
    /// <exception cref="InvalidDataException">The evidence file exists but cannot be deserialized; a corrupted file is not a missing file.</exception>
    public static DeferredEvidenceRecord? ReadLatest(string testId, string? root = null)
    {
        DeferredEvidencePointer? pointer = ReadPointer(testId, root);
        if (pointer is null)
        {
            return null;
        }

        if (!File.Exists(pointer.EvidencePath))
        {
            return null;
        }

        DeferredEvidenceRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<DeferredEvidenceRecord>(File.ReadAllText(pointer.EvidencePath), JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"evidence_unreadable:{pointer.EvidencePath}");
        }

        return record ?? throw new InvalidDataException($"evidence_unreadable:{pointer.EvidencePath}");
    }

    // Fixed set of characters a test id may never contain, independent of the host filesystem
    // (Windows and Unix disagree on which of ':' '?' '|' etc. are invalid filename characters).
    private static bool ContainsInvalidTestIdChar(string id)
    {
        const string Invalid = ":?|*\"<>";
        foreach (char c in id)
        {
            if (c < 0x20 || c == 0x7F || Invalid.IndexOf(c) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
