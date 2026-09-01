namespace NosAi.Core;

/// <summary>
/// One stage's contract on the critical path: a synchronous, allocation-free
/// try-pattern (INV-07), never <c>async</c> (docs/ROADMAP_ESECUTIVA.md S:2.4,
/// S:5.4) so a stage cannot silently hand control to the thread pool and lose
/// the deadline budget its caller is accounting for.
/// </summary>
/// <typeparam name="TIn">The stage's input, typically a previous <see cref="PipelineStage"/>'s output.</typeparam>
/// <typeparam name="TOut">The stage's output on success.</typeparam>
public interface IPipelineStage<TIn, TOut>
{
    /// <summary>Which position on the critical path this instance implements.</summary>
    PipelineStage Stage { get; }

    /// <summary>
    /// Attempts to produce <paramref name="output"/> from <paramref name="input"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when <paramref name="output"/> is valid and
    /// <paramref name="fault"/> is <see cref="FaultCode.None"/>. A
    /// <see langword="false"/> result must still set <paramref name="fault"/> to
    /// a specific reason: there is no code path where "did not work" is left
    /// unexplained.
    /// </returns>
    bool TryExecute(in TIn input, out TOut output, out FaultCode fault);
}
