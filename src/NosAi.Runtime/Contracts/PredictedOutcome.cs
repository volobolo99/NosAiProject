namespace NosAi.Runtime.Contracts;

/// <summary>What the simulation expects an action to cost and to risk.</summary>
/// <param name="UnmeasuredReason">
/// Why this prediction rests on a number nobody measured, or
/// <see langword="null"/> when every quantity it keys on came from real data.
/// </param>
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
    string? UnmeasuredReason = null)
{
    /// <summary>Whether every quantity this prediction keys on came from measured data.</summary>
    public bool IsMeasured => UnmeasuredReason is null;
}
