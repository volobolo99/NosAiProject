using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.LiveIntegration;
using NosAi.Runtime.WorldModel.Fusion;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// Reports whether a real, currently-known chain of observed portals
/// (<see cref="MultiMapRoutePlanner"/>, AP-04) can get the operator's
/// character from its current map to <paramref name="destinationMapId"/>
/// -- and if so, what it is. Read-only: this probe never walks, never
/// presses a key, and never persists anything. Same commanded-authority
/// family as <see cref="TargetChainProbe"/> -- a diagnostic, not an
/// executor.
/// </summary>
public static class RouteProbe
{
    public const string Flag = "--route";

    /// <param name="destinationMapId">The numeric map id to route to (same numbering <see cref="ClientMemorySession.TryReadMapId"/> reports).</param>
    /// <param name="destinationX">Target X position on the destination map.</param>
    /// <param name="destinationY">Target Y position on the destination map.</param>
    public static int Run(int destinationMapId, float destinationX, float destinationY)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? playerFailure))
            {
                Console.WriteLine($"[REFUSED] player_unreadable:{playerFailure}");
                return 1;
            }

            if (!session.TryReadMapId(out int currentMapId, out string? mapFailure))
            {
                Console.WriteLine($"[REFUSED] map_id_unreadable:{mapFailure}");
                return 1;
            }

            var startMap = new MapId(string.Create(CultureInfo.InvariantCulture, $"map-{currentMapId}"));
            var destinationMap = new MapId(string.Create(CultureInfo.InvariantCulture, $"map-{destinationMapId}"));

            using var mapReconstruction = new MapReconstructionSource(logger: null);
            IReadOnlyDictionary<MapId, MapModel> knownMaps = mapReconstruction.LoadAllKnownMaps();

            NavigationPlan plan = MultiMapRoutePlanner.PlanRoute(
                knownMaps,
                startMap,
                new WorldPosition(player.X, player.Y),
                destinationMap,
                new WorldPosition(destinationX, destinationY),
                DateTime.UtcNow);

            if (!plan.IsReachable.HasValue || !plan.IsReachable.Value)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[UNREACHABLE] {startMap} -> {destinationMap}: {plan.IsReachable.Reason}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"Mappe conosciute: {knownMaps.Count}. Nessuna catena di portali osservati collega le due mappe."));
                return 1;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"=== Rotta {startMap} -> {destinationMap} ({plan.Waypoints.Count} tappe) ==="));
            for (int i = 0; i < plan.Waypoints.Count; i++)
            {
                NavigationWaypoint waypoint = plan.Waypoints[i];
                string portalNote = waypoint.UsePortal is { } portalId
                    ? string.Create(CultureInfo.InvariantCulture, $" -> usa portale {portalId}")
                    : string.Empty;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i + 1}. {waypoint.MapId}: ({waypoint.Position.X:F1}, {waypoint.Position.Y:F1}){portalNote}"));
            }

            return 0;
        }
    }
}
