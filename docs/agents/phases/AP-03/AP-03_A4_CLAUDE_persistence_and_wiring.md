# AP-03 / A4 — Claude (standing in for Cursor) — Persistence + Runtime Wiring

Give Map Reconstruction its two missing pieces: durable storage (so a
partially-explored map survives a process restart -- the literal AP-03
Definition of Done, "una mappa parzialmente esplorata può essere salvata,
aggiornata e ripresa senza perdere la storia precedente") and the runtime
glue that actually calls A2's projector + A3's merge algorithm once per map
instead of never.

## Already built (read, do not modify)

- `src/NosAi.Core/WorldModel/Reconstruction/MapObservationBatch.cs` (AP-03/A1).
- `src/NosAi.Core/WorldModel/Reconstruction/MapReconstructionFusion.cs` (AP-03/A3):
  `static MapModel Merge(MapModel current, MapObservationBatch batch, DateTime nowUtc)`.
  Idempotent: a batch that changes nothing returns `current` by reference,
  unchanged `Version`.
- `src/NosAi.Runtime/WorldModel/Fusion/MapGridObservationProjector.cs` (AP-03/A2):
  `static MapObservationBatch Project(in MapGrid grid, MapId mapId, DateTime observedAtUtc)`.
  Emits one `Tile` per cell of an already-loaded grid, `Cached`, in one pass.
- `src/NosAi.Runtime/Navigation/MapGridExtractor.cs` (pre-existing, real):
  `TryResolveDedicatedMapsDirectory(out string path, out string? reason)`
  finds `<NOSAI-SSD>\NosAi\data\maps`; `TryInfo(string mapsDirectory, int mapId, out MapGrid grid, out string? fileHash, out string? failureReason)`
  loads one already-extracted `.grid` file by the client's own **numeric**
  map id.
- `src/NosAi.Storage/SqliteEventJournal.cs` + `SqliteJournalOptions.cs` +
  `VolumeLocator.cs` (pre-existing, real, **not owned by you -- do not
  edit them**): the canonical pattern for a WAL/FULL/busy_timeout=5000
  SQLite store on the `NOSAI-SSD` labeled volume. Reuse `SqliteJournalOptions`
  (it already carries `VolumeLabel`/`FileName` and the three fixed pragma
  values) and `VolumeLocator.ResolveDatabasePath(options)` as-is -- construct
  your own `MapModelStore` with a different `FileName` (e.g. `"nosai-maps.db"`),
  do not add a second options type.

## The map id convention you must use

The World Model's `MapId` (`NosAi.Core.WorldModel.Identifiers.MapId`, a
string wrapper) is produced by `GameplayObservationProjector.Project` as
literally `$"map-{numericId}"` (see `GameplayObservationProjector.cs` lines
44 and 91), or the sentinel `"unknown-map"` when no map is observed yet
(`GameplayObservationProjector.UnknownMapSentinelId`, also what
`WorldModelSnapshot.Unknown` uses). `MapGridExtractor`/`MapGridObservationProjector`
need the client's raw **numeric** id (an `int`). Write a small, private
parser (`"map-" + int.TryParse` on the remainder) rather than assuming any
other format, and treat anything that does not match (including
`"unknown-map"`) as "no real map known this cycle" -- never guess a numeric
id.

## OWN (new files only; no other file)

- `src/NosAi.Storage/MapModelStore.cs`
- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs`
- `tests/NosAi.Core.Tests/MapModelStoreTests.cs` (SQLite-backed types are
  tested from `NosAi.Core.Tests` in this repo even when they live in
  `NosAi.Storage` -- see the existing `SqliteEventJournalTests.cs` for the
  precedent; do not create a new test project).
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceTests.cs`
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopMapWiringTests.cs`

## MODIFY, additive only (do not change existing behavior for callers who don't opt in)

- `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs`
- `src/NosAi.Runtime/Program.cs`

## 1. `MapModelStore`

One row per `MapId`, storing the latest `MapModel` for that map (not a
history of every version -- `MapModel.Version` and the merge algorithm's own
"never loses a tile" guarantee already give you the history that matters;
storing every intermediate version would be an unbounded table for no
consumer). Same policy discipline as `SqliteEventJournal`: apply
`journal_mode=WAL`, `synchronous=FULL`, `busy_timeout=5000` immediately after
opening and verify each one actually took effect (throw if not) before
touching any table.

```csharp
public sealed class MapModelStore : IDisposable
{
    public MapModelStore(string databasePath, SqliteJournalOptions options);
    public static MapModelStore OpenFromVolume(SqliteJournalOptions options)
        => new(VolumeLocator.ResolveDatabasePath(options), options);

    public void Save(MapModel map);
    public bool TryLoad(MapId mapId, out MapModel map); // map is always assigned, even on false -- match the TryXxx convention already used everywhere in this codebase (MapGridExtractor.TryInfo, VolumeLocator.TryResolve, ...), never a nullable out.
    public void Dispose();
}
```

Serialize `MapModel` with full fidelity -- every `Tile`'s and `Portal`'s own
`WorldFact<T>` classification (`Source`, `Confidence`, `ObservedAtUtc`,
`HasObservedValue`, `Reason`), not just the bare value. Do **not** hand
`MapModel` itself to `System.Text.Json.JsonSerializer` by reflection:
`EquatableArray<T>` has no parameterless constructor `JsonSerializer` can
deserialize into, so a round trip through the domain type directly will
serialize but fail to deserialize. Define a small private DTO layer
(record types mirroring `MapModel`/`Tile`/`Portal`/`WorldFact<T>`/`MapBounds`,
using `List<T>` instead of `EquatableArray<T>` and enum names/underlying
values as plain fields) and convert explicitly in both directions. Store the
serialized form as a `TEXT` or `BLOB` column, your choice -- either is fine
as long as the round trip is exact. `Landmarks` (a `Polygon` list) has no
producer yet in this phase, but persist it too rather than silently
dropping it, since a future phase populating it must not lose data written
by this one.

Test with a real temp-file SQLite database (never `:memory:` -- the WAL
pragma verification is part of what you are testing) covering: round trip of
a map with tiles, portals and bounds; round trip of `MapModel.Unknown(...)`;
`TryLoad` on a map id never saved returns `false` with an unchanged `map`
default; saving the same `MapId` twice overwrites rather than duplicating
(second `Save` wins, `TryLoad` returns the latest); the three pragma values
verified the same way `SqliteEventJournalTests.cs` already verifies them for
the event journal (do not skip this -- it is the whole reason this type
exists instead of a plain file).

## 2. `MapReconstructionSource`

The runtime glue. Owns the expensive path (grid lookup, projection, SQLite)
and, critically, **must not repeat it every fusion tick** once a map's
reconstruction is already known: `WorldModelFusionLoop` ticks every ~500ms
(`WorldModelFusionLoop.DefaultInterval`) for the lifetime of a session on
one map, and the client's static grid never changes mid-session -- caching
the result of the first successful reconstruction for a given `MapId` and
returning it unchanged on every subsequent tick until the map id itself
changes is not an optimization you may skip, it is what makes this design
viable at all (re-projecting a several-thousand-cell grid and touching
SQLite twice a second forever would be exactly the kind of unbounded
per-tick cost `docs/NOSAI_ARCHITECTURE_BASELINE.md` S:9 rules out).

```csharp
public sealed class MapReconstructionSource : IDisposable
{
    public MapReconstructionSource(
        SqliteJournalOptions? storeOptions = null,
        string? mapsDirectoryOverride = null,
        IRuntimeLogger? logger = null);

    /// <summary>
    /// Reads the current map id off <paramref name="networkSnapshot"/>.Map.Id
    /// (already set by GameplayObservationProjector every cycle) and returns
    /// the best available MapModel for it: the cached in-memory result if
    /// this is the same map as last call; otherwise loads any persisted
    /// MapModel, projects+merges fresh grid evidence, persists and caches the
    /// result. Returns networkSnapshot.Map unchanged (never throws, never
    /// fabricates) when no real map id is known this cycle.
    /// </summary>
    public MapModel Resolve(WorldModelSnapshot networkSnapshot, DateTime nowUtc);

    public void Dispose();
}
```

Fail-soft at every step, same discipline as `ScreenVitalsCapture`
(AP-02/A4) -- never throw out of `Resolve`:

- `MapGridExtractor.TryResolveDedicatedMapsDirectory` fails (no `NOSAI-SSD`
  volume) -> resolve with an empty batch (`MapObservationBatch.Empty(mapId,
  "maps_directory_not_available", nowUtc)`), still merge it (a no-op against
  an empty batch, per A3) so `Resolve` still returns a well-formed
  `MapModel` rather than special-casing this branch.
- `MapModelStore.OpenFromVolume` throws at construction (same missing
  volume, or the DB file cannot be opened) -> catch it in your own
  constructor and continue with a `null` store: reconstruction still works
  for the lifetime of this process, it just cannot survive a restart this
  run. Log it once via the optional `logger`, do not throw out of the
  constructor.
- `MapGridExtractor.TryInfo` fails (map id not extracted, corrupt file) ->
  same empty-batch treatment as the missing directory.
- The current map id does not parse (see convention above) -> return
  `networkSnapshot.Map` unchanged; do not touch the store or the cache.

Write `MapReconstructionSourceTests.cs` against a real temp `.grid` file (use
`BinaryMapGridLoader`'s format directly, same style as
`MapGridExtractorTests.cs`) and a real temp-file `MapModelStore`, covering:
first `Resolve` call for a known map produces real tiles and persists them;
a second `Resolve` call for the *same* map id does not re-touch the grid
file or the store (assert via a call-count wrapper/spy around
`MapGridExtractor.TryInfo` is not practical since it's a static method --
instead assert behaviorally: delete/corrupt the maps directory between the
two calls and confirm the second call still returns the same result rather
than degrading, which is only possible if it used the cache and never
touched the directory again); a map change (`Resolve` called with a
different `Map.Id`) does re-run the pipeline; a restart (`new
MapReconstructionSource` against the same database file) recovers the
previously persisted `MapModel` without needing the grid file present a
second time; no real map id known this cycle returns the snapshot's own
`Map` unchanged and touches neither the grid directory nor the store.

## 3. Wiring `WorldModelFusionLoop`

Additive optional constructor parameter, same treatment as AP-02/A4's
`visualSource`:

```csharp
Func<WorldModelSnapshot, MapModel>? mapSource = null
```

Note the shape is deliberately different from `visualSource`
(`Func<VisualObservation>`, no input): map reconstruction is meaningless
without knowing *which* map the player is currently on, and that is only
known from this cycle's own network-projected snapshot (`Map.Id`), so the
delegate must take the in-progress snapshot as input. In `RunOnce`, call it
last (after the existing `_visualSource` branch, against `result`, the same
local your AP-02 vitals fusion already produces) and replace `result.Map`
with its return value:

```csharp
if (_mapSource is not null)
{
    try
    {
        MapModel map = _mapSource(result);
        result = result with { Map = map };
    }
    catch (Exception ex)
    {
        _logger.Error("World Model fusion loop's map source threw; keeping this cycle's map unchanged.", ex);
    }
}
```

A `mapSource` that throws must never fail the cycle -- same treatment as
`_visualSource`. Default `null` -> zero behavior change for every existing
caller/test. Add a constant for the log message reason if you follow the
existing `VisualSourceThrewReason`-style convention, but this is optional
polish, not required.

Extend the XML doc remarks with a short "AP-03/A4: optional map
reconstruction" paragraph mirroring the existing "AP-02/A4" one already on
this class -- do not delete or rewrite the AP-01/AP-02 remarks already
there.

## 4. Wiring `Program.cs`

Under the same existing `--fuse-world-model` flag (no new flag -- map
reconstruction is an enrichment of the same cycle, exactly the precedent
AP-02/A4 already set for vitals), construct a `MapReconstructionSource` the
same way `visualCapture` is constructed today (`using` declaration, disposed
after `fusion` on the way out -- declare it *before* the `fusion` variable
for the same reverse-unwind-order reason already commented there), and pass
`mapSource: mapReconstruction!.Resolve` into the `WorldModelFusionLoop`
constructor alongside the existing `visualSource:` argument.

## Tests / build

```
dotnet build src/NosAi.Storage/NosAi.Storage.csproj -c Release
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~MapModelStoreTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~MapReconstructionSourceTests|FullyQualifiedName~WorldModelFusionLoopMapWiringTests"
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

All must be green with 0 warnings/0 errors on the two builds, no regression
in the two full test suites (record exact pass counts, the same way every
prior phase's status doc has). `dotnet` is at `/root/.dotnet/dotnet`; add it
to `PATH` first if the shell does not already have it
(`export PATH="$PATH:/root/.dotnet"`).

## Report back

Files created/modified, the exact `Resolve` caching behavior you
implemented (confirm it does NOT re-touch the grid/store on an unchanged map
id -- this is the one thing most likely to be gotten wrong and is a real
performance bug, not a style nit, if missed), build/test evidence, known
gaps, and anything you found in A1/A2/A3 that looks wrong (do not fix their
files yourself -- report it for AP-03/A5's audit and AP-03/A6's
integration).
