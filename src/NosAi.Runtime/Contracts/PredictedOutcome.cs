namespace NosAi.Runtime.Contracts;

public sealed record PredictedOutcome(
    Guid CandidateId,
    int ExpectedHpDelta,
    int ExpectedMpDelta,
    int ExpectedTimeMs,
    float SuccessProbability,
    float RiskScore,
    string StateSignatureAfter);
