namespace NosAi.Runtime.Contracts;

/// <summary>What the simulation expects an action to cost and to risk.</summary>
/// <param name="UnmeasuredReason">
/// Why this prediction rests on a number nobody measured, or
/// <see langword="null"/> when every quantity it keys on came from real data.
/// </param>
/// <param name="ExpectedTimeIsMeasured">
/// Whether <paramref name="ExpectedTimeMs"/> is a mean of durations actually
/// observed, rather than the per-action-type constant used before any round of
/// that kind has run.
/// </param>
/// <remarks>
/// Separate from <paramref name="UnmeasuredReason"/> because the two decide
/// different things. An unmeasured <i>cost</i> stops the safety gate; an
/// unmeasured <i>duration</i> stops nothing -- it only means the ranking must
/// not prefer one action over another on the strength of a number nobody
/// measured. Folding them together would make every prediction refuse until
/// durations had accumulated, which is a refusal about the wrong thing.
/// </remarks>
/// <remarks>
/// <para>
/// The last parameter exists because a prediction and a guess are not the same
/// object, and until now they were. <c>SimulationEngine</c> assigned a single
/// literal cost to every skill -- 35 MP, whichever skill it was -- and
/// <c>GuardPolicyEngine</c>'s one quantitative refusal keys on the risk computed
/// from it. A skill whose real cost is far higher was predicted safe, and the
/// gate authorised it on that basis.
/// </para>
/// <para>
/// Nothing here fabricates a replacement number: the reason is carried, and the
/// policy engine decides what an unmeasured prediction may authorise. That keeps
/// the safety decision in the safety component rather than encoded as a magic
/// risk value the simulation would have to pick.
/// </para>
/// </remarks>
public sealed record PredictedOutcome(
    Guid CandidateId,
    int ExpectedHpDelta,
    int ExpectedMpDelta,
    int ExpectedTimeMs,
    float SuccessProbability,
    float RiskScore,
    string StateSignatureAfter,
    string? UnmeasuredReason = null,
    bool ExpectedTimeIsMeasured = false)
{
    /// <summary>Whether every quantity this prediction keys on came from measured data.</summary>
    public bool IsMeasured => UnmeasuredReason is null;
}
