using System.Diagnostics;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Humanizer;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Adapters;

/// <summary>Controlled client boundary. All live execution requires explicit safety authorization.</summary>
public sealed class NosTaleGameAdapter : IGameAdapter
{
    private readonly IGuardAi _guardAi;
    private readonly IHumanizer _humanizer;
    private readonly LiveInputAuthorization _authorization;
    private readonly TrustTier _operatingTier;
    private Process? _gameProcess;
    private bool _initialized;

    /// <param name="operatingTier">
    /// The trust ceiling this adapter runs under. It is required rather than
    /// assumed: the previous code hard-coded Tier3 here while every action
    /// declared Tier4, so the guard answered TRUST_TIER_EXCEEDED to all of them
    /// and no game command could ever execute, whatever the safety policy said.
    /// The ceiling is an authorization decision and belongs to the runtime.
    /// </param>
    public NosTaleGameAdapter(IGuardAi guardAi, IHumanizer humanizer, LiveInputAuthorization authorization,
        TrustTier operatingTier)
    {
        _guardAi = guardAi ?? throw new ArgumentNullException(nameof(guardAi));
        _humanizer = humanizer ?? throw new ArgumentNullException(nameof(humanizer));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _operatingTier = operatingTier;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _gameProcess = Process.GetProcessesByName("NosTaleX").FirstOrDefault();
        _initialized = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Walks the character by left-clicking the destination. The start point
    /// passed here is only a fallback: the humanizer reads the real cursor
    /// position, so a stale assumption no longer skews the whole trajectory.
    /// </summary>
    public async Task SendMovementCommandAsync(float targetX, float targetY, CancellationToken cancellationToken)
    {
        var action = new CandidateAction("Movement", ActionKind.Move, TrustTier.Tier2_SemiAutonomous, 0);
        Authorize(action);
        var target = new TargetDescriptor(new ScreenPoint((int)targetX, (int)targetY), 20, 20, "GameWorldTarget");
        await _humanizer.ClickTargetAsync(target, MouseButton.Left, cancellationToken);
    }

    /// <summary>Targets or attacks an entity with the right mouse button.</summary>
    public async Task SendTargetInteractionAsync(float targetX, float targetY, CancellationToken cancellationToken)
    {
        var action = new CandidateAction("TargetInteraction", ActionKind.Combat, TrustTier.Tier3_AutonomousRestricted, 0);
        Authorize(action);
        var target = new TargetDescriptor(new ScreenPoint((int)targetX, (int)targetY), 20, 20, "GameWorldEntity");
        await _humanizer.ClickTargetAsync(target, MouseButton.Right, cancellationToken);
    }

    public async Task SendSkillCastAsync(string skillSlot, CancellationToken cancellationToken)
    {
        // A skill cast is combat, not a utility keystroke: the guard cannot apply
        // combat policy to an action classified as something else.
        var action = new CandidateAction($"SkillCast:{skillSlot}", ActionKind.Combat, TrustTier.Tier3_AutonomousRestricted, 0);
        Authorize(action);
        await _humanizer.PressKeyHumanlikeAsync(skillSlot, cancellationToken);
    }

    public async Task SendNosMateCommandAsync(char mateCommand, CancellationToken cancellationToken)
    {
        var action = new CandidateAction($"NosMate:{mateCommand}", ActionKind.Utility, TrustTier.Tier2_SemiAutonomous, 0);
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
        var decision = _guardAi.Evaluate(action, _operatingTier);
        if (!_authorization.CanExecute(action, decision, IsClientHealthy()))
            throw new UnauthorizedAccessException($"Azione non autorizzata dal Safety Gate: {action.Id}");
    }
}
