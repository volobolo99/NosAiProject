using System.Collections.Immutable;
using NosAi.Economy.Inventory;
using NosAi.Navigation.Pathfinding;

namespace NosAi.Runtime.Capabilities;

/// <summary>
/// Read/plan-only capability boundary for Navigation and Economy.
/// This layer deliberately does not execute game input, network commands or transactions.
/// </summary>
public interface INavigationCapability
{
    CalculatedPathResult PlanPath(MapGridData map, GridPoint start, GridPoint destination, bool smooth = true);
    NavigationStatus Status { get; }
    ImmutableArray<GridPoint> ActivePath { get; }
}

public interface IEconomyCapability
{
    EconomicStateSnapshot Snapshot();
    UpgradeRiskEvaluation EvaluateUpgrade(int currentLevel, bool specialist, bool protectedByScroll, long playerGold);
    (DisposalChannel RecommendedChannel, long NetGainGold, string Rationale) EvaluateDisposal(InventorySlot slot, bool hasBazaarMedal, bool requiredForCrafting);
}

public sealed class NavigationCapability : INavigationCapability
{
    private readonly AStarPathfinder _pathfinder = new();
    private readonly PathSmoother _smoother = new();
    private readonly NavigationExecutionController _controller = new();

    public NavigationStatus Status => _controller.Status;
    public ImmutableArray<GridPoint> ActivePath => _controller.ActivePath;

    public CalculatedPathResult PlanPath(MapGridData map, GridPoint start, GridPoint destination, bool smooth = true)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = _pathfinder.FindPath(map, start, destination);
        if (!result.IsPathFound || !smooth)
            return result;

        return result with { Waypoints = _smoother.SmoothPath(map, result.Waypoints) };
    }

    public bool Start(MapGridData map, GridPoint start, GridPoint destination, bool smooth = true)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _controller.StartNavigation(map, start, destination, smooth);
    }

    public GridPoint? Tick(GridPoint actualPosition, MapGridData map, GridPoint destination)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _controller.UpdateNavigationTick(actualPosition, map, destination);
    }

    public void Cancel() => _controller.CancelNavigation();
}

public sealed class EconomyCapability : IEconomyCapability
{
    private readonly InventoryEconomyOrchestrator _orchestrator = new();

    public EconomicStateSnapshot Snapshot() => _orchestrator.CaptureEconomicSnapshot();

    public UpgradeRiskEvaluation EvaluateUpgrade(int currentLevel, bool specialist, bool protectedByScroll, long playerGold)
    {
        if (currentLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(currentLevel));
        if (playerGold < 0)
            throw new ArgumentOutOfRangeException(nameof(playerGold));

        return _orchestrator.UpgradeSimulator.EvaluateUpgrade(currentLevel, specialist, protectedByScroll, playerGold);
    }

    public (DisposalChannel RecommendedChannel, long NetGainGold, string Rationale) EvaluateDisposal(InventorySlot slot, bool hasBazaarMedal, bool requiredForCrafting)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return _orchestrator.ArbitrageEvaluator.EvaluateItemDisposal(slot, hasBazaarMedal, requiredForCrafting);
    }
}

/// <summary>Composition root for the capability layer.</summary>
public sealed record NosAiCapabilityKernel(INavigationCapability Navigation, IEconomyCapability Economy)
{
    public static NosAiCapabilityKernel CreateDefault() => new(new NavigationCapability(), new EconomyCapability());
}
