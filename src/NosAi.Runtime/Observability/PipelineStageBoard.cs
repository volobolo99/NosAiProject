namespace NosAi.Runtime.Observability;

/// <summary>One stage's last known outcome, flattened for a dump or a panel.</summary>
/// <param name="Ok">Null when that stage has never produced a result.</param>
public sealed record StageOutcomeDump(string Stage, bool? Ok, string? Fault);

/// <summary>The last outcome of each stage on the canonical critical path.</summary>
public sealed class PipelineStageBoard
{
    public static readonly IReadOnlyList<string> Stages =
    [
        "Observe", "WorldState", "Simulation", "Ranking", "Orchestrator",
        "Planner", "Guard", "Trust", "Safety", "Execute", "Verify"
    ];

    public const string NeverRanFault = "stage_never_ran";

    private readonly object _lock = new();
    private readonly Dictionary<string, StageOutcomeDump> _last = new(StringComparer.Ordinal);

    /// <summary>Raised synchronously after a real runtime stage outcome is recorded.</summary>
    public event Action<StageOutcomeDump>? StageRecorded;

    public void Record(string stage, bool ok, string? fault = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        var outcome = new StageOutcomeDump(stage, ok, ok ? null : fault);
        lock (_lock)
        {
            _last[stage] = outcome;
        }

        // The board is observational. Subscribers must never be able to prevent
        // the runtime from recording a stage or executing its safety logic.
        try
        {
            StageRecorded?.Invoke(outcome);
        }
        catch
        {
            // A broken telemetry observer is not a runtime failure.
        }
    }

    public IReadOnlyList<StageOutcomeDump> Snapshot()
    {
        lock (_lock)
        {
            var list = new StageOutcomeDump[Stages.Count];
            for (int i = 0; i < Stages.Count; i++)
            {
                string name = Stages[i];
                list[i] = _last.TryGetValue(name, out StageOutcomeDump? dump)
                    ? dump
                    : new StageOutcomeDump(name, null, NeverRanFault);
            }

            return list;
        }
    }

    public static IReadOnlyList<StageOutcomeDump> UnknownAll()
    {
        var list = new StageOutcomeDump[Stages.Count];
        for (int i = 0; i < Stages.Count; i++)
            list[i] = new StageOutcomeDump(Stages[i], null, NeverRanFault);
        return list;
    }
}

public sealed record CommitPointRefusalDump(string Reason, DateTime? AtUtc);

public sealed record SessionAuthorityDump(
    bool IsActuating,
    string? RefusalReason,
    bool IsTerminal,
    string RuntimeIntegrity,
    string ClientIntegrity,
    bool WasProbed);

public sealed class HaltDiagnosticsContext
{
    public Func<CommitPointRefusalDump?> LastCommitPointRefusal { get; init; } = () => null;
    public Func<SessionAuthorityDump?> LastSessionAuthority { get; init; } = () => null;
    public Func<IReadOnlyList<StageOutcomeDump>> LastStageOutcomes { get; init; } = PipelineStageBoard.UnknownAll;
}
