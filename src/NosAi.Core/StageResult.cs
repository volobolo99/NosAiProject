namespace NosAi.Core;

/// <summary>The outcome of running one <see cref="PipelineStage"/> for one cycle.</summary>
/// <param name="Stage">Which stage produced this result.</param>
/// <param name="Ok">Whether the stage completed within budget and without a fault.</param>
/// <param name="ElapsedTicks">Wall time for the stage, from <see cref="IMonotonicClock"/>.</param>
/// <param name="Fault">The reason <paramref name="Ok"/> is false, or <see cref="FaultCode.None"/>.</param>
public readonly record struct StageResult(PipelineStage Stage, bool Ok, long ElapsedTicks, FaultCode Fault);

/// <summary>
/// Why a stage did not produce a usable result. Values are stable across
/// releases: the journal (Gate 1) persists them as-is, and a renumbering would
/// silently reinterpret every historical record.
/// </summary>
public enum FaultCode : ushort
{
    None = 0,
    Timeout = 1,
    ScopeDenied = 2,
    Replay = 3,
    Thermal = 4,
    Network = 5,
    Journal = 6,

    /// <summary>
    /// A wire frame failed structural or authentication checks (bad version,
    /// declared length past <c>MaxPayloadLength</c>, truncated frame, or a tag
    /// that does not match). Not in docs/ROADMAP_ESECUTIVA.md's original
    /// enumeration; added because Gate 1's own acceptance criteria ("frame
    /// corrotto ... scartato, FaultCode registrato") require a value none of
    /// the others accurately describe.
    /// </summary>
    FrameInvalid = 7,

    /// <summary>
    /// A process/module attach attempt failed: target process not found, expected
    /// module missing, module hash mismatch, or the OS denied the handle. Also
    /// not in docs/ROADMAP_ESECUTIVA.md's original enumeration, for the same
    /// reason as <see cref="FrameInvalid"/>: <c>IGameProcessAdapter.TryAttach</c>
    /// needs a fault value none of the others describes.
    /// </summary>
    AttachFailed = 8
}
