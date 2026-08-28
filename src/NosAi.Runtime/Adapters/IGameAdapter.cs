namespace NosAi.Runtime.Adapters;

public interface IGameAdapter
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SendMovementCommandAsync(float targetX, float targetY, CancellationToken cancellationToken);
    Task SendSkillCastAsync(string skillSlot, CancellationToken cancellationToken);
    Task SendNosMateCommandAsync(char mateCommand, CancellationToken cancellationToken);
    bool IsClientHealthy();
}
