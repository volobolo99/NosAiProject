namespace NosAi.Core.CharacterControl;

public sealed class FailClosedCharacterActionGuard : ICharacterActionGuard
{
    public bool IsAllowed(CharacterAction action, CharacterControlContext context)
    {
        if (!context.HasFreshObservation || !context.IsSafetyGateOpen)
            return false;
        if (context.ObservationAgeMs < 0 || context.ObservationAgeMs > 500)
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
