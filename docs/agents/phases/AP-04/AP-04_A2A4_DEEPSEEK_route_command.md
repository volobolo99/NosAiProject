# AP-04 / A2+A4 — DeepSeek — Multi-map enumeration + `--route` diagnostic command

## Why this task exists

`AP-04_A1_STATUS.md` §"Terza indagine" (Claude, A3, this session) closed
the algorithm half of multi-map routing:
`src/NosAi.Core/WorldModel/Exploration/MultiMapRoutePlanner.cs` plans a
route across observed `Portal`s (BFS, never fabricates a connection) and
returns a `NavigationPlan`. It is pure -- it takes an
`IReadOnlyDictionary<MapId, MapModel>` snapshot as an argument, it never
loads one itself. **Two real gaps stand between that algorithm and
anything an operator can actually see:**

1. `MapModelStore` (AP-03/A4, `src/NosAi.Storage/MapModelStore.cs`) can
   only `Save`/`TryLoad` **one** map at a time -- there is no way to ask
   it "every map you currently hold," which `MultiMapRoutePlanner.PlanRoute`
   needs to build its snapshot.
2. Nothing calls `MultiMapRoutePlanner.PlanRoute` anywhere in the runtime.

**This task is A2 (the store/adapter enumeration) + A4 (a new read-only
diagnostic command), scoped deliberately small**: it makes real portal
data (already accumulating via `--scout`/`--autoplay`, Q-070/Q-071)
answerable as "can I get from here to map X, and how" -- it does **not**
add any new walking/execution authority. The new command reports a plan,
it does not act on one. Actually walking a multi-map route (deciding
when a crossing has happened, continuing to the next leg) is a
materially larger task -- new state machine, new interaction with
`WalkCommand`/portal-crossing detection mid-route -- and is explicitly
**not** this task's scope.

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/Exploration/MultiMapRoutePlanner.cs`
  (already on `main`, Claude/A3): read `PlanRoute` and its own tests
  (`tests/NosAi.Core.Tests/WorldModel/Exploration/MultiMapRoutePlannerTests.cs`)
  in full. Do not change this file: it is pure and already covered.
- `src/NosAi.Storage/MapModelStore.cs`: read the whole file, especially
  `Save`/`TryLoad`'s SQL and locking pattern (`_connection`, `_lock`,
  the `map_models` table: `map_id TEXT PRIMARY KEY, payload TEXT NOT NULL`).
  §1 below adds one new public method that reuses this exact table and
  locking discipline -- do not add a second table or change the schema.
- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs`: read
  `Resolve`, `RecordPortalCrossing`, and `LoadPersistedOrUnknown`/
  `PersistIfPossible` (the `_store` field, nullable -- a missing
  NOSAI-SSD volume is best-effort, never a gate). §2 below adds one new
  public method with the exact same "best-effort, `_store is null` means
  empty, log and continue on a read failure" shape as those two.
- `src/NosAi.Runtime/Navigation/TargetChainProbe.cs`: read `Run` in full
  -- §3 below's new `RouteProbe.Run` mirrors its exact shape (return `2`
  when not Windows, `1` on any refusal, `0` on success; `Console.WriteLine`
  reporting, no side effects, no execution authority).
  `tests/NosAi.Runtime.Tests/TargetChainProbeTests.cs` shows the
  established convention this task follows too: only a probe's pure
  helper (here, none is needed -- `RouteProbe.Run` is a thin composition
  of already-tested pieces) gets a unit test; the live `Run()` entry
  point itself does not, same as `ScoutCommand`/`AutoplayCommand.RunWindows`.
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs`, lines ~226-290: read
  how it opens a session (`ClientMemorySession.TryAttach`), reads the
  player (`TryReadPlayer`) and the map id (`TryReadMapId`), and derives
  a `MapId` (`new MapId(string.Create(CultureInfo.InvariantCulture, $"map-{mapId}"))`)
  -- §3 reuses this exact pattern, does not invent a new one.
- `src/NosAi.Runtime/Program.cs`, lines ~360-371 (`--find-mapid`/
  `--target-chain` flag wiring): §4 adds one new flag the identical way.
- `tests/NosAi.Core.Tests/MapModelStoreTests.cs`,
  `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceTests.cs`:
  read their `TestVolume`/temp-database helpers in full -- §1/§2's tests
  reuse them, do not rebuild test infrastructure.

## OWN (new files only)

- `src/NosAi.Runtime/Navigation/RouteProbe.cs`
- `tests/NosAi.Runtime.Tests/RouteProbeTests.cs` (only if a pure helper
  ends up worth unit-testing -- see §3's note; do not create this file
  if there is nothing pure to test in isolation from a live client)

## MODIFY

- `src/NosAi.Storage/MapModelStore.cs` (one new public method)
- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs` (one
  new public method)
- `src/NosAi.Runtime/Program.cs` (one new flag branch)
- `tests/NosAi.Core.Tests/MapModelStoreTests.cs` (new tests only)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceTests.cs`
  (new tests only)

Do not touch `MultiMapRoutePlanner.cs`, `PortalCrossingDetector.cs`,
`MapObservationBatch.cs`, `MapReconstructionFusion.cs`,
`ScoutCommand.cs`, `AutoplayCommand.cs`, `WalkCommand.cs`, or
`TargetChainProbe.cs`.

## Scope, stated explicitly

- **Report-only.** `RouteProbe` never calls `WalkCommand.Execute` and
  never presses a key. It reads real data (persisted maps, current
  position) and prints a real plan. Acting on that plan is a separate,
  later task.
- **Best-effort enumeration, never a gate.** §1/§2 mirror
  `PersistIfPossible`'s own discipline: a store that cannot be read is
  logged and treated as empty, never thrown from a caller's ordinary
  path.
- **No caching, no new state.** `LoadAllKnownMaps` (§2) always re-reads
  from the store when called -- it does not touch or populate `Resolve`'s
  own `_cachedMapId`/`_cachedResult` fields, and is not itself cached.
  This is a diagnostic path, not a hot per-cycle one.
- `WorldMapPortalRouter` (`src/NosAi.Runtime/Navigation/Pathfinding/NavigationPathfinding.cs`,
  the Gate-era hardcoded-graph router) is **not** touched or replaced --
  it is a different, pre-canonical system. `RouteProbe` is a new,
  independent command against the AP-01/AP-03/AP-04 canonical types only.

## 1. `MapModelStore.ListMapIds` (A2)

Add as a new public method, immediately after `TryLoad` and before `Dispose`:

```csharp
    /// <summary>Every map id this store currently holds a persisted <see cref="MapModel"/> for.</summary>
    public IReadOnlyCollection<MapId> ListMapIds()
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT map_id FROM map_models";

            var ids = new List<MapId>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                ids.Add(new MapId(reader.GetString(0)));

            return ids;
        }
    }
```

**Test** (add to `MapModelStoreTests.cs`, reusing its existing temp-database
setup -- no new test infrastructure):

- An empty store's `ListMapIds()` returns an empty collection.
- After `Save`-ing two different `MapModel`s (different `Id`s),
  `ListMapIds()` returns both ids (order-independent assertion, e.g.
  `Assert.Equal(new[] { idA, idB }.OrderBy(...), actual.OrderBy(...))`
  or two `Assert.Contains` calls).
- Saving a **second** `MapModel` for an **already-persisted** id (the
  existing `Save`'s `ON CONFLICT ... DO UPDATE` path) does not duplicate
  it in `ListMapIds()` -- still exactly one entry for that id.

## 2. `MapReconstructionSource.LoadAllKnownMaps` (A2)

Add as a new public method, immediately after `RecordPortalCrossing` and
before `Dispose`:

```csharp
    /// <summary>
    /// Every map this store currently holds a persisted <see cref="MapModel"/>
    /// for, loaded fresh -- for a caller that needs a multi-map snapshot
    /// (<see cref="NosAi.Core.WorldModel.Exploration.MultiMapRoutePlanner.PlanRoute"/>),
    /// not the single-current-map read <see cref="Resolve"/> exists for.
    /// Best-effort, like every other read/write in this class: a missing
    /// volume or a read failure yields an empty (or partial) result, never
    /// a thrown exception on a caller's ordinary path.
    /// </summary>
    public IReadOnlyDictionary<MapId, MapModel> LoadAllKnownMaps()
    {
        var maps = new Dictionary<MapId, MapModel>();
        if (_store is null)
            return maps;

        try
        {
            foreach (MapId id in _store.ListMapIds())
            {
                if (_store.TryLoad(id, out MapModel map))
                    maps[id] = map;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("MapReconstructionSource failed to enumerate persisted maps; returning whatever loaded so far.", ex);
        }

        return maps;
    }
```

**Test** (add to `MapReconstructionSourceTests.cs`, reusing its existing
`TestVolume`/`TempMapsDir`/`NullRuntimeLogger` helpers):

- A fresh source with nothing ever `Resolve`d or recorded returns an
  empty dictionary from `LoadAllKnownMaps()`.
- After `RecordPortalCrossing` (or a plain `Resolve` that persists) for
  two different maps, `LoadAllKnownMaps()` returns a dictionary with
  both `MapId`s as keys and the correct `MapModel` as each value
  (assert on `.Portals`/`.Id`, not full-object equality, to keep the
  test resilient to unrelated field changes).
- A second, independent `MapReconstructionSource` against the same
  `TestVolume` options sees maps persisted by the first one (proves this
  reads from the shared store, not an in-memory cache local to one
  instance).

## 3. `RouteProbe` (A4, new file)

Create `src/NosAi.Runtime/Navigation/RouteProbe.cs`:

```csharp
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
```

Nothing here is pure/isolable from a live client (`ClientMemorySession.TryAttach`
requires a real process), so **no unit test file is required for this
one** -- `MultiMapRoutePlanner`'s own tests already cover the planning
logic this composes, and `LoadAllKnownMaps`/`ListMapIds` are covered by
§1/§2. If, while writing this, any piece of `Run` turns out to be
cleanly extractable as a pure helper (the way `TargetChainProbe.Compare`
is), extracting and testing it the same way is fine -- but do not force
one that does not naturally exist.

## 4. Wiring in `Program.cs`

Immediately after the existing `--target-chain` block:

```csharp
        if (args.Any(a => string.Equals(a, "--target-chain", StringComparison.OrdinalIgnoreCase)))
            return NosAi.Runtime.Navigation.TargetChainProbe.Run();
```

add:

```csharp

        // Reports whether a chain of already-observed portal crossings
        // (Q-070/Q-071) can get the operator from the current map to the
        // named one, and what it is. Read-only: plans, never walks.
        if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.RouteProbe.Flag, StringComparison.OrdinalIgnoreCase)))
        {
            int routeFlagIndex = Array.FindIndex(args, a =>
                string.Equals(a, NosAi.Runtime.Navigation.RouteProbe.Flag, StringComparison.OrdinalIgnoreCase));
            if (routeFlagIndex + 3 >= args.Length
                || !int.TryParse(args[routeFlagIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int routeDestinationMapId)
                || !float.TryParse(args[routeFlagIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float routeDestinationX)
                || !float.TryParse(args[routeFlagIndex + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out float routeDestinationY))
            {
                Console.WriteLine("[REFUSED] --route requires <destinationMapId> <x> <y>");
                return 1;
            }

            return NosAi.Runtime.Navigation.RouteProbe.Run(routeDestinationMapId, routeDestinationX, routeDestinationY);
        }
```

`Program.cs` also declares a `KnownProbeFlags` `HashSet<string>` (around
line 898, listing `--scout`, `--engage`, `--target-chain`, ...) --
add `"--route"` to that set the same way every other command flag is
listed there.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~MapModelStoreTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~MapReconstructionSourceTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

All green, 0 warnings/0 errors beyond the one pre-existing unrelated
xUnit2031 warning already on `main`, no regression in either full suite.

## Known limitation, stated plainly

`RouteProbe.Run` has no unit test (same convention as
`TargetChainProbe.Run`/`ScoutCommand.RunWindows`) and is not exercised
against a real client crossing a real portal chain in this task. Report
`Present` for §1/§2 (real, tested, isolated from any live client) and
`Present` for §3/§4 (compiles, composes already-tested pieces
correctly, but end-to-end behavior against a real multi-map portal
chain is unverified) -- `Integrated` only once a human runs `--route`
against a real client that has already crossed at least one real portal
during `--scout`/`--autoplay` and confirms the printed plan is correct.

## Report back

Files modified; build/test evidence with exact pass counts;
verification level (`Present`) and why not `Integrated`; anything found
in `MapModelStore.cs`/`MapReconstructionSource.cs`/`MultiMapRoutePlanner.cs`
that looks wrong (do not fix it yourself — report it).
