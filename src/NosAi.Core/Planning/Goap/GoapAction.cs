namespace NosAi.Core.Planning.Goap;

public readonly record struct GoapFact(string Key, int Value);

public sealed record GoapAction(
    string Id,
    ReadOnlyMemory<GoapFact> Preconditions,
    ReadOnlyMemory<GoapFact> Effects,
    int Cost,
    PlanStep Step);
