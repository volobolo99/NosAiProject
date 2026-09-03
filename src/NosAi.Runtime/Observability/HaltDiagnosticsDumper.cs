using System.Linq;
using System.Text.Json;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Observability;

/// <summary>
/// The photograph written when the breaker transitions to halt.
/// </summary>
/// <remarks>
/// One file per transition, named with a sortable UTC stamp, under
/// <c>data/</c> (gitignored). No secrets, no keys, no operator machine paths
/// beyond the relative dump path itself.
/// </remarks>
public sealed record HaltDiagnosticDump(
    string PreviousState,
    string NewState,
    DateTimeOffset TransitionedAtUtc,
    IReadOnlyList<bool> FailureWindow,
    int FailuresInWindow,
    int WindowOccupancy,
    int Halts,
    double CurrentCooldownSeconds,
    CommitPointRefusalDump? LastCommitPointRefusal,
    SessionAuthorityDump? LastSessionAuthority,
    IReadOnlyList<StageOutcomeDump> LastStageOutcomes,
    string DumpPath)
{
    public object ToWire() => new
    {
        previousState = PreviousState,
        newState = NewState,
        transitionedAtUtc = TransitionedAtUtc,
        failureWindow = FailureWindow,
        failuresInWindow = FailuresInWindow,
        windowOccupancy = WindowOccupancy,
        halts = Halts,
        currentCooldownSeconds = CurrentCooldownSeconds,
        lastCommitPointRefusal = LastCommitPointRefusal is { } commit
            ? new { reason = commit.Reason, atUtc = commit.AtUtc }
            : null,
        lastSessionAuthority = LastSessionAuthority is { } authority
            ? new
            {
                isActuating = authority.IsActuating,
                refusalReason = authority.RefusalReason,
                isTerminal = authority.IsTerminal,
                runtimeIntegrity = authority.RuntimeIntegrity,
                clientIntegrity = authority.ClientIntegrity,
                wasProbed = authority.WasProbed
            }
            : null,
        lastStageOutcomes = LastStageOutcomes.Select(s => new
        {
            stage = s.Stage,
            ok = s.Ok,
            fault = s.Fault
        }).ToArray()
    };
}

/// <summary>Writes one dump file per halt transition.</summary>
public sealed class HaltDiagnosticsDumper
{
    /// <summary>Default directory, matching the gitignored runtime data root.</summary>
    public const string DefaultDirectory = "data";

    public const string FilePrefix = "halt-";
    public const string FileSuffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directory;
    private readonly HaltDiagnosticsContext _context;
    private readonly object _lock = new();
    private int _written;

    public HaltDiagnosticsDumper(string? directory = null, HaltDiagnosticsContext? context = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        _context = context ?? new HaltDiagnosticsContext();
    }

    /// <summary>How many dump files this instance has written.</summary>
    public int Written
    {
        get { lock (_lock) return _written; }
    }

    /// <summary>Subscribes to a controller so a halt transition writes a dump.</summary>
    /// <remarks>
    /// I/O failure here is diagnostic. The halt has already happened inside
    /// <see cref="RecoveryController.HandleFailure"/>; throwing back into that
    /// call would make a missing photograph look like the breaker itself failed.
    /// </remarks>
    public void Attach(RecoveryController recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        recovery.Halted += transition =>
        {
            try
            {
                _ = Write(transition);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    /// <summary>Photographs the transition and writes one file for it.</summary>
    public HaltDiagnosticDump Write(RecoveryHaltTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        Directory.CreateDirectory(_directory);

        string stamp = transition.TransitionedAtUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff") + "Z";
        string path = UniquePath(Path.Combine(_directory, FilePrefix + stamp + FileSuffix));

        var dump = new HaltDiagnosticDump(
            PreviousState: transition.PreviousState.ToString(),
            NewState: transition.NewState.ToString(),
            TransitionedAtUtc: transition.TransitionedAtUtc,
            FailureWindow: transition.FailureWindow,
            FailuresInWindow: transition.FailuresInWindow,
            WindowOccupancy: transition.WindowOccupancy,
            Halts: transition.Halts,
            CurrentCooldownSeconds: transition.CurrentCooldown.TotalSeconds,
            LastCommitPointRefusal: _context.LastCommitPointRefusal(),
            LastSessionAuthority: _context.LastSessionAuthority(),
            LastStageOutcomes: _context.LastStageOutcomes() ?? PipelineStageBoard.UnknownAll(),
            DumpPath: path);

        File.WriteAllText(path, JsonSerializer.Serialize(dump.ToWire(), JsonOptions));
        lock (_lock)
            _written++;
        return dump;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string directory = Path.GetDirectoryName(path) ?? ".";
        string name = Path.GetFileNameWithoutExtension(path);
        for (int n = 2; n < 1000; n++)
        {
            string candidate = Path.Combine(directory, $"{name}-{n}{FileSuffix}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{FileSuffix}");
    }
}
