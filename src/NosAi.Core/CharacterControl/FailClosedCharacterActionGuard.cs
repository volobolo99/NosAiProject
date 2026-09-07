namespace NosAi.Core.CharacterControl;

/// <summary>
/// The last line before an act: refuses unless the picture it was decided from
/// is present, current and confident, the boundary that emits is open, and the
/// action itself names everything its kind needs.
/// </summary>
/// <remarks>
/// <para>
/// Every branch refuses. There is no path here that turns a missing fact into a
/// permission, which is what makes it safe to put last -- and also what makes an
/// unwired copy of it invisible: a guard nothing calls refuses nothing, and
/// looks exactly like a guard that is working.
/// </para>
/// <para>
/// It answers <see langword="bool"/> and not a reason on purpose: the caller
/// holds the values it was given and can report them, while a guard that
/// explained itself would be a second place where the policy is written down.
/// A caller reporting the measured age beside the bound tells the operator which
/// condition failed without either side restating the other's rules.
/// </para>
/// </remarks>
public sealed class FailClosedCharacterActionGuard : ICharacterActionGuard
{
    public bool IsAllowed(CharacterAction action, CharacterControlContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HasFreshObservation || !context.IsSafetyGateOpen)
            return false;
        // A negative age is not "very fresh": it is a clock that disagrees with
        // itself, and nothing decided from it can be trusted.
        if (context.ObservationAgeMs < 0 || context.ObservationAgeMs > context.MaxObservationAgeMs)
            return false;
        if (action.Confidence < context.ConfidenceThreshold)
            return false;
        if (string.IsNullOrWhiteSpace(action.Id))
            return false;
        return action.Kind switch
        {
            CharacterActionKind.Move or CharacterActionKind.Stop => true,
            CharacterActionKind.BasicAttack or CharacterActionKind.UseSkill or CharacterActionKind.Interact or CharacterActionKind.Pickup or CharacterActionKind.UseItem => action.Target is not null && !string.IsNullOrWhiteSpace(action.FunctionId),
            _ => false
        };
    }
}
