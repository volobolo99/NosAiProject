namespace NosAi.Adapter.DirectEngine;

/// <summary>What became of a request.</summary>
public enum EngineOutcome
{
    /// <summary>Not an outcome. A defaulted result is not a success.</summary>
    Unknown = 0,

    /// <summary>The client was commanded and accepted the command.</summary>
    Executed = 1,

    /// <summary>Nothing was attempted; <see cref="EngineActionResult.Refusal"/> says why.</summary>
    Refused = 2,

    /// <summary>Something was attempted and did not complete.</summary>
    Failed = 3
}

/// <summary>
/// The answer to one <see cref="EngineActionRequest"/>: what happened, when, and
/// on whose refusal if it did not.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="EngineOutcome.Executed"/> means the client acted.</b> It is not a
/// label applied because the call returned. The same defect this repository has
/// already fixed once in Gate 3 — a completion reported with no execution behind
/// it — would be far worse here, where the caller is deciding whether the
/// character has moved before choosing what to do next.
/// </para>
/// <para>
/// <see cref="Refusal"/> is non-null exactly when the outcome is not
/// <see cref="EngineOutcome.Executed"/>, so a caller never has to guess whether an
/// absent reason means success.
/// </para>
/// </remarks>
public sealed record EngineActionResult
{
    private EngineActionResult(
        EngineCapability capability,
        string correlationId,
        EngineOutcome outcome,
        EngineRefusal? refusal,
        DateTime requestedAtUtc,
        DateTime completedAtUtc)
    {
        Capability = capability;
        CorrelationId = correlationId;
        Outcome = outcome;
        Refusal = refusal;
        RequestedAtUtc = requestedAtUtc;
        CompletedAtUtc = completedAtUtc;
    }

    public EngineCapability Capability { get; }

    /// <summary>Carried over from the request, so a result can be matched to its decision.</summary>
    public string CorrelationId { get; }

    public EngineOutcome Outcome { get; }

    /// <summary>Non-null exactly when <see cref="Outcome"/> is not <see cref="EngineOutcome.Executed"/>.</summary>
    public EngineRefusal? Refusal { get; }

    public DateTime RequestedAtUtc { get; }

    public DateTime CompletedAtUtc { get; }

    /// <summary>How long the attempt took, request to answer.</summary>
    public TimeSpan Elapsed => CompletedAtUtc - RequestedAtUtc;

    public bool Executed => Outcome == EngineOutcome.Executed;

    public static EngineActionResult Success(in EngineActionRequest request, DateTime completedAtUtc) =>
        new(request.Capability, request.CorrelationId, EngineOutcome.Executed, null,
            request.RequestedAtUtc, completedAtUtc);

    public static EngineActionResult Refused(
        in EngineActionRequest request, EngineRefusal refusal, DateTime completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new EngineActionResult(request.Capability, request.CorrelationId, EngineOutcome.Refused, refusal,
            request.RequestedAtUtc, completedAtUtc);
    }

    public static EngineActionResult Failed(
        in EngineActionRequest request, EngineRefusal refusal, DateTime completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new EngineActionResult(request.Capability, request.CorrelationId, EngineOutcome.Failed, refusal,
            request.RequestedAtUtc, completedAtUtc);
    }

    public override string ToString() =>
        Refusal is null ? $"{Capability}:{Outcome}" : $"{Capability}:{Outcome}:{Refusal}";
}
