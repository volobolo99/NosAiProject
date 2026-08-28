using System.Diagnostics;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Humanizer;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Adapters;

/// <summary>Controlled client boundary. All live execution requires explicit safety authorization.</summary>
public sealed class NosTaleGameAdapter : IGameAdapter
{
    private readonly IGuardAi _guardAi;
    private readonly IHumanizer _humanizer;
    private readonly LiveInputAuthorization _authorization;
    private Process? _gameProcess;
    private bool _initialized;

    public NosTaleGameAdapter(IGuardAi guardAi, IHumanizer humanizer, LiveInputAuthorization authorization)
    {
        _guardAi = guardAi ?? throw new ArgumentNullException(nameof(guardAi));
        _humanizer = humanizer ?? throw new ArgumentNullException(nameof(humanizer));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _gameProcess = Process.GetProcessesByName("NosTaleX").FirstOrDefault();
        _initialized = true;
        return Task.CompletedTask;
    }

    public async Task SendMovementCommandAsync(float targetX, float targetY, CancellationToken cancellationToken)
    {
        var action = new CandidateAction("Movement", ActionKind.Utility, TrustTier.Tier4, 0);
        Authorize(action);
        await _humanizer.MoveMouseHumanlikeAsync(new ScreenPoint(400, 300), new TargetDescriptor(new ScreenPoint((int)targetX, (int)targetY), 20, 20, "GameWorldTarget"), cancellationToken);
    }

    public async Task SendSkillCastAsync(string skillSlot, CancellationToken cancellationToken)
    {
        var action = new CandidateAction($"SkillCast:{skillSlot}", ActionKind.Utility, TrustTier.Tier4, 0);
        Authorize(action);
        await _humanizer.PressKeyHumanlikeAsync(skillSlot, cancellationToken);
    }

    public async Task SendNosMateCommandAsync(char mateCommand, CancellationToken cancellationToken)
    {
        var action = new CandidateAction($"NosMate:{mateCommand}", ActionKind.Utility, TrustTier.Tier4, 0);
        Authorize(action);
        await _humanizer.PressKeyHumanlikeAsync(mateCommand.ToString(), cancellationToken);
    }

    public bool IsClientHealthy()
    {
        if (!_initialized || _gameProcess is null) return false;
        try { return !_gameProcess.HasExited; } catch { return false; }
    }

    private void Authorize(CandidateAction action)
    {
        if (!_initialized) throw new InvalidOperationException("GameAdapter non inizializzato.");
        var decision = _guardAi.Evaluate(action, TrustTier.Tier3);
        if (!_authorization.CanExecute(action, decision, IsClientHealthy()))
            throw new UnauthorizedAccessException($"Azione non autorizzata dal Safety Gate: {action.Id}");
    }
}
