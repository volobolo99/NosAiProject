using System.Diagnostics;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;

namespace NosAi.Runtime.Adapters;

/// <summary>
/// Fail-closed boundary between tactical decisions and a future NosTale client adapter.
/// This foundation deliberately does not inject input, manipulate packets, or bypass protections.
/// </summary>
public sealed class NosTaleGameAdapter : IGameAdapter
{
    private readonly IGuardAi _guardAi;
    private Process? _gameProcess;
    private bool _initialized;

    public NosTaleGameAdapter(IGuardAi guardAi)
        => _guardAi = guardAi ?? throw new ArgumentNullException(nameof(guardAi));

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = Process.GetProcessesByName("NosTaleX");
        _gameProcess = processes.FirstOrDefault();
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task SendMovementCommandAsync(float targetX, float targetY, CancellationToken cancellationToken)
        => RejectLiveExecutionAsync("Movement", cancellationToken);

    public Task SendSkillCastAsync(string skillSlot, CancellationToken cancellationToken)
        => RejectLiveExecutionAsync($"SkillCast:{skillSlot}", cancellationToken);

    public Task SendNosMateCommandAsync(char mateCommand, CancellationToken cancellationToken)
        => RejectLiveExecutionAsync($"NosMate:{mateCommand}", cancellationToken);

    public bool IsClientHealthy()
    {
        if (!_initialized) return false;
        if (_gameProcess is null) return false;
        try { return !_gameProcess.HasExited; }
        catch { return false; }
    }

    private Task RejectLiveExecutionAsync(string actionName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_initialized)
            throw new InvalidOperationException("GameAdapter non inizializzato.");
        if (!IsClientHealthy())
            throw new InvalidOperationException("Client NosTale non attivo o non disponibile.");

        var candidate = new CandidateAction(actionName, ActionKind.Utility, TrustTier.Tier4, 0);
        var decision = _guardAi.Evaluate(candidate, TrustTier.Tier3);
        if (!decision.Allowed)
            throw new UnauthorizedAccessException($"Azione bloccata dal Guard AI: {decision.Reason}");

        throw new NotSupportedException(
            "Live execution non ancora abilitata: il Game Adapter resta fail-closed fino al completamento del runtime Humanizer/Safety e dei test di bring-up.");
    }
}
