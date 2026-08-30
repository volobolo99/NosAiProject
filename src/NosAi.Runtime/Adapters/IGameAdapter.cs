namespace NosAi.Runtime.Adapters;

public interface IGameAdapter
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SendMovementCommandAsync(float targetX, float targetY, CancellationToken cancellationToken);

    /// <summary>Right-click interaction: targeting and attacking.</summary>
    Task SendTargetInteractionAsync(float targetX, float targetY, CancellationToken cancellationToken);
    Task SendSkillCastAsync(string skillSlot, CancellationToken cancellationToken);
    Task SendNosMateCommandAsync(char mateCommand, CancellationToken cancellationToken);
    bool IsClientHealthy();
}
