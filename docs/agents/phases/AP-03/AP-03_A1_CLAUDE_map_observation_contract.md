# AP-03 / A1 — Claude — Map Observation Batch Contract

Add the observation-side contract Map Reconstruction needs: raw tile/portal
evidence for one map, produced by one extraction/scan pass, before it is
merged into the persistent `MapModel`. `MapModel`, `Tile`, `TileCoordinate`,
`TileTraversability`, `Portal`, `MapBounds` already exist (AP-01, `MapModel.cs`,
`SpatialContracts.cs`) — do not redefine them. Reuse `Tile` itself as the
per-coordinate observation unit; it already carries `WorldFact<TileTraversability>`.

READ: `docs/ROADMAP_ESECUTIVA.md` S:AP-03; `src/NosAi.Core/WorldModel/MapModel.cs`;
`src/NosAi.Core/WorldModel/SpatialContracts.cs`; `src/NosAi.Core/WorldModel/Identifiers.cs`;
`src/NosAi.Core/WorldModel/EquatableArray.cs`; `src/NosAi.Core/WorldModel/WorldModelClassification.cs`.

OWN: `src/NosAi.Core/WorldModel/Reconstruction/MapObservationBatch.cs` (new file,
new subfolder) and its direct tests
`tests/NosAi.Core.Tests/WorldModel/Reconstruction/MapObservationBatchTests.cs`.
No other file.

## Contract

```csharp
namespace NosAi.Core.WorldModel.Reconstruction;

public sealed record MapObservationBatch(
    MapId MapId,
    EquatableArray<Tile> Tiles,
    EquatableArray<Portal> Portals,
    string SourceDescription,
    DateTime ObservedAtUtc)
{
    public static MapObservationBatch Empty(MapId mapId, string reason, DateTime? observedAtUtc = null);
}
```

`SourceDescription` is a short human-readable provenance tag (e.g.
`"client-map-grid"`), not a `WorldFact` itself — each `Tile`/`Portal` already
carries its own classification. `Empty` represents "this scan pass produced no
evidence" (grid not loaded, map unreachable, etc.) — never fabricate tiles.

## Require

- Immutable record, structural equality (uses `EquatableArray<T>` for both
  collections, never a bare array/list — matches `WorldModelSnapshot`'s
  determinism requirement).
- `Empty` never stamps `DateTime.UtcNow` implicitly when `observedAtUtc` is
  given — same discipline as `MapModel.Unknown`/`WorldFact<T>.Unknown` (this
  exact bug class has been found and fixed five times already in AP-01/AP-02;
  do not reintroduce it).
- No dependency on anything under `src/NosAi.Runtime/` — this is a Core
  contract, consumed by a Runtime-side projector (AP-03/A2) that you do not
  own.
- Complete file, compiles, tests included. No TODO/stub.
