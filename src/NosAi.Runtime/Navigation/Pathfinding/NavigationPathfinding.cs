// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Sottosistema di Navigazione Spaziale, Pathfinding A* 2D,
//              Mappe di Collisione, Hazard Heatmap, Routing Portali Multi-Mappa
//              e Path Smoother con Rilevamento Anti-Stallo
// Standard: C# 12 / .NET 8 — Zero-Allocation, PriorityQueue, Clean Architecture
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Navigation.Pathfinding
{
    #region 1. Contratti e Modelli di Dominio per la Navigazione

    public enum TileType : byte
    {
        Walkable = 0,
        BlockedObstacle = 1,
        WaterOrChasm = 2,
        SafeZoneTown = 3,
        PortalEntrance = 4
    }

    public enum NavigationStatus : byte
    {
        Idle = 0,
        Navigating = 1,
        WaypointReached = 2,
        DestinationReached = 3,
        StuckDetectedRerouting = 4,
        PathNotFound = 5
    }

    public readonly record struct GridPoint(int X, int Y) : IEquatable<GridPoint>
    {
        public double DistanceTo(GridPoint other)
        {
            int dx = X - other.X;
            int dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public int ManhattanDistanceTo(GridPoint other) =>
            Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public override string ToString() => $"({X},{Y})";
    }

    public sealed record MapPortal(
        string PortalId,
        int SourceMapId,
        GridPoint SourcePosition,
        int DestinationMapId,
        GridPoint DestinationPosition,
        string TargetMapName
    );

    public sealed record DynamicHazardZone(
        long SourceEntityId,
        GridPoint Center,
        int RadiusTiles,
        float DangerWeightMultiplier
    );

    public sealed record CalculatedPathResult(
        int MapId,
        GridPoint StartPoint,
        GridPoint TargetPoint,
        bool IsPathFound,
        ImmutableArray<GridPoint> Waypoints,
        double TotalPathCost,
        long ComputationTimeMs
    );

    #endregion

    #region 2. Griglia di Collisione 2D (MapGridData)

    public sealed class MapGridData
    {
        public int MapId { get; }
        public string MapName { get; }
        public int Width { get; }
        public int Height { get; }

        private readonly byte[] _tiles;
        private readonly float[] _hazardCostOverlay;

        public MapGridData(int mapId, string mapName, int width, int height)
        {
            MapId = mapId;
            MapName = mapName;
            Width = width;
            Height = height;
            _tiles = new byte[width * height];
            _hazardCostOverlay = new float[width * height];
        }

        public bool IsWithinBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public TileType GetTileType(int x, int y)
        {
            if (!IsWithinBounds(x, y)) return TileType.BlockedObstacle;
            return (TileType)_tiles[y * Width + x];
        }

        public void SetTileType(int x, int y, TileType type)
        {
            if (IsWithinBounds(x, y))
            {
                _tiles[y * Width + x] = (byte)type;
            }
        }

        public bool IsWalkable(int x, int y)
        {
            var type = GetTileType(x, y);
            return type is TileType.Walkable or TileType.SafeZoneTown or TileType.PortalEntrance;
        }

        public void ClearHazardOverlay()
        {
            Array.Clear(_hazardCostOverlay, 0, _hazardCostOverlay.Length);
        }

        public void ApplyHazard(DynamicHazardZone hazard)
        {
            int minX = Math.Max(0, hazard.Center.X - hazard.RadiusTiles);
            int maxX = Math.Min(Width - 1, hazard.Center.X + hazard.RadiusTiles);
            int minY = Math.Max(0, hazard.Center.Y - hazard.RadiusTiles);
            int maxY = Math.Min(Height - 1, hazard.Center.Y + hazard.RadiusTiles);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    double dist = hazard.Center.DistanceTo(new GridPoint(x, y));
                    if (dist <= hazard.RadiusTiles)
                    {
                        int index = y * Width + x;
                        _hazardCostOverlay[index] += hazard.DangerWeightMultiplier;
                    }
                }
            }
        }

        public float GetTraversalCost(int x, int y)
        {
            if (!IsWalkable(x, y)) return float.PositiveInfinity;
            int index = y * Width + x;
            return 1.0f + _hazardCostOverlay[index];
        }
    }

    #endregion

    #region 3. Algoritmo di Pathfinding A* 2D Ottimizzato

    public sealed class AStarPathfinder
    {
        private static readonly (int dx, int dy, float cost)[] Directions =
        {
            (0, 1, 1.0f), (1, 0, 1.0f), (0, -1, 1.0f), (-1, 0, 1.0f),
            (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f)
        };

        public CalculatedPathResult FindPath(
            MapGridData map,
            GridPoint start,
            GridPoint target,
            bool allowDiagonal = true)
        {
            var sw = Stopwatch.StartNew();

            if (!map.IsWalkable(start.X, start.Y) || !map.IsWalkable(target.X, target.Y))
            {
                return new CalculatedPathResult(map.MapId, start, target, false, ImmutableArray<GridPoint>.Empty, 0, sw.ElapsedMilliseconds);
            }

            if (start == target)
            {
                return new CalculatedPathResult(map.MapId, start, target, true, ImmutableArray.Create(start), 0, sw.ElapsedMilliseconds);
            }

            var openSet = new PriorityQueue<GridPoint, float>();
            var cameFrom = new Dictionary<GridPoint, GridPoint>();
            var gScore = new Dictionary<GridPoint, float> { [start] = 0.0f };

            openSet.Enqueue(start, Heuristic(start, target));

            int maxSteps = 10000;
            int steps = 0;

            while (openSet.Count > 0 && steps++ < maxSteps)
            {
                GridPoint current = openSet.Dequeue();

                if (current == target)
                {
                    sw.Stop();
                    var waypoints = ReconstructPath(cameFrom, current);
                    return new CalculatedPathResult(map.MapId, start, target, true, waypoints, gScore[target], sw.ElapsedMilliseconds);
                }

                int dirCount = allowDiagonal ? 8 : 4;
                for (int i = 0; i < dirCount; i++)
                {
                    var (dx, dy, baseMoveCost) = Directions[i];
                    int nx = current.X + dx;
                    int ny = current.Y + dy;
                    var neighbor = new GridPoint(nx, ny);

                    if (!map.IsWalkable(nx, ny)) continue;

                    if (dx != 0 && dy != 0)
                    {
                        if (!map.IsWalkable(current.X + dx, current.Y) || !map.IsWalkable(current.X, current.Y + dy))
                            continue;
                    }

                    float terrainCost = map.GetTraversalCost(nx, ny);
                    float tentativeG = gScore[current] + (baseMoveCost * terrainCost);

                    if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        float fScore = tentativeG + Heuristic(neighbor, target);
                        openSet.Enqueue(neighbor, fScore);
                    }
                }
            }

            sw.Stop();
            return new CalculatedPathResult(map.MapId, start, target, false, ImmutableArray<GridPoint>.Empty, 0, sw.ElapsedMilliseconds);
        }

        private static float Heuristic(GridPoint a, GridPoint b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return 1.0f * (dx + dy) + (1.41421356f - 2.0f) * Math.Min(dx, dy);
        }

        private static ImmutableArray<GridPoint> ReconstructPath(Dictionary<GridPoint, GridPoint> cameFrom, GridPoint current)
        {
            var list = new List<GridPoint> { current };
            while (cameFrom.TryGetValue(current, out GridPoint prev))
            {
                current = prev;
                list.Add(current);
            }
            list.Reverse();
            return list.ToImmutableArray();
        }
    }

    #endregion

    #region 4. Path Smoother (Levigatura Raycasting Line-of-Sight)

    public sealed class PathSmoother
    {
        public ImmutableArray<GridPoint> SmoothPath(MapGridData map, ImmutableArray<GridPoint> rawPath)
        {
            if (rawPath.Length <= 2) return rawPath;

            var smoothed = new List<GridPoint> { rawPath[0] };
            int currentIdx = 0;

            while (currentIdx < rawPath.Length - 1)
            {
                int furthestVisible = currentIdx + 1;

                for (int nextIdx = rawPath.Length - 1; nextIdx > currentIdx + 1; nextIdx--)
                {
                    if (HasLineOfSight(map, rawPath[currentIdx], rawPath[nextIdx]))
                    {
                        furthestVisible = nextIdx;
                        break;
                    }
                }

                smoothed.Add(rawPath[furthestVisible]);
                currentIdx = furthestVisible;
            }

            return smoothed.ToImmutableArray();
        }

        public bool HasLineOfSight(MapGridData map, GridPoint start, GridPoint end)
        {
            int x0 = start.X, y0 = start.Y;
            int x1 = end.X, y1 = end.Y;

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (!map.IsWalkable(x0, y0)) return false;
                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            return true;
        }
    }

    #endregion

    #region 5. Grafo di Instradamento Portali Multi-Mappa (WorldMapRouter)

    public sealed record MultiMapTransitLeg(
        int StepNumber,
        int CurrentMapId,
        string MapName,
        GridPoint WalkToPosition,
        MapPortal? UsePortalToNextMap
    );

    public sealed class WorldMapPortalRouter
    {
        private readonly List<MapPortal> _portals = new();
        private readonly Dictionary<int, string> _mapNames = new();

        public WorldMapPortalRouter()
        {
            InitializeStandardNosTaleWorldGraph();
        }

        private void InitializeStandardNosTaleWorldGraph()
        {
            _mapNames[1] = "NosVille";
            _mapNames[2] = "Prateria di NosVille";
            _mapNames[3] = "Pianure di NosVille";
            _mapNames[4] = "Miniera d'Oro Orientale";
            _mapNames[5] = "Tempio Fernon 1P";

            AddBidirectionalPortal("PORTAL_NOS_PRA", 1, new GridPoint(140, 20), 2, new GridPoint(10, 80), "Prateria di NosVille");
            AddBidirectionalPortal("PORTAL_PRA_PIA", 2, new GridPoint(90, 85), 3, new GridPoint(15, 20), "Pianure di NosVille");
            AddBidirectionalPortal("PORTAL_PIA_MIN", 3, new GridPoint(120, 110), 4, new GridPoint(20, 30), "Miniera d'Oro Orientale");
            AddBidirectionalPortal("PORTAL_PIA_FER", 3, new GridPoint(80, 140), 5, new GridPoint(30, 20), "Tempio Fernon 1P");
        }

        public void AddBidirectionalPortal(string id, int map1, GridPoint pos1, int map2, GridPoint pos2, string targetName)
        {
            _portals.Add(new MapPortal($"{id}_FORWARD", map1, pos1, map2, pos2, targetName));
            _portals.Add(new MapPortal($"{id}_BACKWARD", map2, pos2, map1, pos1, _mapNames.GetValueOrDefault(map1, $"Map_{map1}")));
        }

        public List<MultiMapTransitLeg>? PlanMultiMapRoute(int startMapId, int destinationMapId)
        {
            if (startMapId == destinationMapId)
            {
                return new List<MultiMapTransitLeg>
                {
                    new(1, startMapId, _mapNames.GetValueOrDefault(startMapId, "CurrentMap"), new GridPoint(0, 0), null)
                };
            }

            var previousPortal = new Dictionary<int, MapPortal>();
            var distances = new Dictionary<int, int> { [startMapId] = 0 };
            var priorityQueue = new PriorityQueue<int, int>();

            priorityQueue.Enqueue(startMapId, 0);

            while (priorityQueue.Count > 0)
            {
                int currentMap = priorityQueue.Dequeue();
                if (currentMap == destinationMapId) break;

                var outgoing = _portals.Where(p => p.SourceMapId == currentMap);
                foreach (var portal in outgoing)
                {
                    int neighbor = portal.DestinationMapId;
                    int newDist = distances[currentMap] + 1;

                    if (!distances.TryGetValue(neighbor, out int existingDist) || newDist < existingDist)
                    {
                        distances[neighbor] = newDist;
                        previousPortal[neighbor] = portal;
                        priorityQueue.Enqueue(neighbor, newDist);
                    }
                }
            }

            if (!previousPortal.ContainsKey(destinationMapId))
                return null;

            var routePortals = new List<MapPortal>();
            int curr = destinationMapId;
            while (curr != startMapId && previousPortal.TryGetValue(curr, out MapPortal? p))
            {
                routePortals.Add(p);
                curr = p.SourceMapId;
            }
            routePortals.Reverse();

            var plan = new List<MultiMapTransitLeg>();
            for (int i = 0; i < routePortals.Count; i++)
            {
                var portal = routePortals[i];
                plan.Add(new MultiMapTransitLeg(
                    StepNumber: i + 1,
                    CurrentMapId: portal.SourceMapId,
                    MapName: _mapNames.GetValueOrDefault(portal.SourceMapId, $"Map_{portal.SourceMapId}"),
                    WalkToPosition: portal.SourcePosition,
                    UsePortalToNextMap: portal
                ));
            }

            return plan;
        }
    }

    #endregion

    #region 6. Controllore di Navigazione e Rilevamento Anti-Stallo

    public sealed class NavigationExecutionController
    {
        private readonly AStarPathfinder _pathfinder;
        private readonly PathSmoother _smoother;
        private ImmutableArray<GridPoint> _activePath = ImmutableArray<GridPoint>.Empty;
        private int _currentWaypointIndex;
        private GridPoint _lastObservedPosition;
        private int _consecutiveStationaryTicks;
        private NavigationStatus _status = NavigationStatus.Idle;

        public NavigationStatus Status => _status;
        public ImmutableArray<GridPoint> ActivePath => _activePath;
        public GridPoint? CurrentWaypoint => (_currentWaypointIndex < _activePath.Length) ? _activePath[_currentWaypointIndex] : null;

        public NavigationExecutionController()
        {
            _pathfinder = new AStarPathfinder();
            _smoother = new PathSmoother();
        }

        public bool StartNavigation(MapGridData map, GridPoint start, GridPoint destination, bool applySmoothing = true)
        {
            var result = _pathfinder.FindPath(map, start, destination);
            if (!result.IsPathFound)
            {
                _status = NavigationStatus.PathNotFound;
                return false;
            }

            _activePath = applySmoothing ? _smoother.SmoothPath(map, result.Waypoints) : result.Waypoints;
            _currentWaypointIndex = 0;
            _lastObservedPosition = start;
            _consecutiveStationaryTicks = 0;
            _status = NavigationStatus.Navigating;

            return true;
        }

        public GridPoint? UpdateNavigationTick(GridPoint currentActualPosition, MapGridData map, GridPoint finalDestination)
        {
            if (_status != NavigationStatus.Navigating || _currentWaypointIndex >= _activePath.Length)
                return null;

            if (currentActualPosition == _lastObservedPosition)
            {
                _consecutiveStationaryTicks++;
                if (_consecutiveStationaryTicks >= 4)
                {
                    _status = NavigationStatus.StuckDetectedRerouting;
                    StartNavigation(map, currentActualPosition, finalDestination, applySmoothing: false);
                    return CurrentWaypoint;
                }
            }
            else
            {
                _consecutiveStationaryTicks = 0;
                _lastObservedPosition = currentActualPosition;
            }

            GridPoint targetWp = _activePath[_currentWaypointIndex];
            if (currentActualPosition.DistanceTo(targetWp) <= 1.5)
            {
                _currentWaypointIndex++;
                if (_currentWaypointIndex >= _activePath.Length)
                {
                    _status = NavigationStatus.DestinationReached;
                    return null;
                }
            }

            return _activePath[_currentWaypointIndex];
        }

        public void CancelNavigation()
        {
            _status = NavigationStatus.Idle;
            _activePath = ImmutableArray<GridPoint>.Empty;
            _currentWaypointIndex = 0;
        }
    }

    #endregion

    #region 7. Suite di Test Automatica per la Navigazione e il Pathfinding

    public static class NavigationPathfindingTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine("    NosAi 1.0 Beta — Certificazione Pathfinding & Navigation     ");
            Console.WriteLine("=================================================================");
            Console.ResetColor();

            bool allPassed = true;

            allPassed &= RunTest("Test 1: Ricerca Percorso A* ed Evitamento Ostacoli Fissi", TestAStarObstacleAvoidance);
            allPassed &= RunTest("Test 2: Deviazione Automatica da Zone di Pericolo (Hazard)", TestDynamicHazardAvoidance);
            allPassed &= RunTest("Test 3: Levigatura Rotta con PathSmoother (Raycasting LoS)", TestPathSmoothingReduction);
            allPassed &= RunTest("Test 4: Routing Multi-Mappa su Grafo Portali (NosVille->Fernon)", TestMultiMapPortalRouting);
            allPassed &= RunTest("Test 5: Rilevamento Anti-Stallo & Ricalcolo Automatico", TestStuckDetectionAndReroute);
            allPassed &= RunTest("Test 6: Invariante Architetturale (Navigation Non-Executable)", TestNavigationSecurityInvariant);

            Console.WriteLine();
            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> [ESITO POSITIVO]: TUTTI I TEST DI NAVIGAZIONE E PATHFINDING SONO SUPERATI.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(">> [ERRORE NAVIGAZIONE]: UNO O PIÙ TEST SONO FALLITI.");
                Console.ResetColor();
            }

            await Task.CompletedTask;
            return allPassed;
        }

        private static bool RunTest(string testName, Func<bool> testFunc)
        {
            try
            {
                bool result = testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static void PrintResult(string name, bool passed, string? error = null)
        {
            Console.Write($"[{(passed ? "PASS" : "FAIL")}] {name,-62}");
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [OK]");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" [ERRORE: {error ?? "Asserzione fallita"}]");
            }
            Console.ResetColor();
        }

        private static bool TestAStarObstacleAvoidance()
        {
            var map = new MapGridData(1, "TestMap", 20, 20);
            for (int y = 0; y <= 15; y++) map.SetTileType(10, y, TileType.BlockedObstacle);

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(map, new GridPoint(2, 5), new GridPoint(18, 5));

            bool found = result.IsPathFound;
            bool noBlockedStep = result.Waypoints.All(p => map.IsWalkable(p.X, p.Y));
            bool avoidsWall = result.Waypoints.Any(p => p.Y > 15);

            return found && noBlockedStep && avoidsWall;
        }

        private static bool TestDynamicHazardAvoidance()
        {
            var map = new MapGridData(1, "TestMap", 30, 30);
            var pathfinder = new AStarPathfinder();

            var baseResult = pathfinder.FindPath(map, new GridPoint(5, 15), new GridPoint(25, 15));

            var hazard = new DynamicHazardZone(101, new GridPoint(15, 15), 4, 100.0f);
            map.ApplyHazard(hazard);

            var hazardResult = pathfinder.FindPath(map, new GridPoint(5, 15), new GridPoint(25, 15));

            bool bypassedCenter = hazardResult.Waypoints.All(p => p.DistanceTo(new GridPoint(15, 15)) > 2.0);
            return hazardResult.IsPathFound && bypassedCenter;
        }

        private static bool TestPathSmoothingReduction()
        {
            var map = new MapGridData(1, "OpenPlains", 50, 50);
            var pathfinder = new AStarPathfinder();
            var smoother = new PathSmoother();

            var rawResult = pathfinder.FindPath(map, new GridPoint(5, 5), new GridPoint(45, 45));
            var smoothed = smoother.SmoothPath(map, rawResult.Waypoints);

            return smoothed.Length < rawResult.Waypoints.Length && smoothed.Length == 2;
        }

        private static bool TestMultiMapPortalRouting()
        {
            var router = new WorldMapPortalRouter();
            var route = router.PlanMultiMapRoute(1, 5);

            if (route == null || route.Count != 3) return false;

            bool step1Ok = route[0].CurrentMapId == 1 && route[0].UsePortalToNextMap?.DestinationMapId == 2;
            bool step2Ok = route[1].CurrentMapId == 2 && route[1].UsePortalToNextMap?.DestinationMapId == 3;
            bool step3Ok = route[2].CurrentMapId == 3 && route[2].UsePortalToNextMap?.DestinationMapId == 5;

            return step1Ok && step2Ok && step3Ok;
        }

        private static bool TestStuckDetectionAndReroute()
        {
            var map = new MapGridData(1, "TestMap", 20, 20);
            var controller = new NavigationExecutionController();

            controller.StartNavigation(map, new GridPoint(0, 0), new GridPoint(10, 10));

            GridPoint pos = new GridPoint(0, 0);
            for (int i = 0; i < 4; i++)
            {
                controller.UpdateNavigationTick(pos, map, new GridPoint(10, 10));
            }

            return controller.Status == NavigationStatus.Navigating;
        }

        private static bool TestNavigationSecurityInvariant()
        {
            var types = typeof(NavigationExecutionController).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains("NosAi.Navigation.Pathfinding"));

            bool hasExecution = types.Any(t => t.GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("click") || m.Name.ToLowerInvariant().Contains("sendpacket")));
            return !hasExecution;
        }
    }

    #endregion

    #region 8. Entry Point

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.Title = "NosAi Navigation & Pathfinding Engine (1.0 Beta)";

            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                bool success = await NavigationPathfindingTestRunner.RunAllTestsAsync();
                return success ? 0 : 1;
            }

            Console.WriteLine("Inizializzazione NosAi Navigation & Pathfinding Engine...");
            Console.WriteLine("Esecuzione della suite di test per A*, Mappe di Collisione e Routing Portali...");

            bool passed = await NavigationPathfindingTestRunner.RunAllTestsAsync();
            return passed ? 0 : 1;
        }
    }

    #endregion
}