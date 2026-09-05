# NosAi — World Model unificato (AP-01)

**Versione contratto:** `WorldModelContract.SchemaVersion = 1`  
**Data:** 2026-09-05  
**Stato:** Implemented (contratti + test diretti); integrazione con Sensor Fusion (A2) e runtime (A4) a carico di A6.

Questo documento descrive i contratti C# in `src/NosAi.Core/WorldModel/` prodotti da AP-01/A1 e la loro copertura di test/benchmark prodotta da AP-01/A5. Va letto insieme a `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` § 4.2 e a `docs/ROADMAP_ESECUTIVA.md` AP-01.

## 1. Scopo e confini

Il World Model è l'unico stato semantico consumato da Simulation/Prediction, Ranking e Planner. Il pacchetto `NosAi.Core.WorldModel`:

- definisce contratti **immutabili e versionati** per Player, Map, Tile/Polygon, Portal, Mob, NPC, Drop, Quest, InventoryItem, EquipmentItem, Skill, Buff, Debuff, Cooldown, Resource, Action e Goal;
- attribuisce a **ogni fatto importante** provenance, confidence, timestamp e freshness tramite `WorldFact<T>`;
- preserva `UNKNOWN` come stato di prima classe: un fatto sconosciuto non è mai 0, `false` o vuoto;
- non contiene campi privilegiati: nessun indirizzo di memoria, handle di processo, pacchetto grezzo, credenziale o stato lato server;
- non propone né autorizza azioni: Guard/Trust/Safety restano a valle e decidono sul runtime.

`NosAi.Core` non referenzia nulla (`docs/ROADMAP_ESECUTIVA.md` S:1.3), quindi il World Model non può usare `NosAi.Runtime.Contracts.ClassifiedValue<T>`. `WorldFact<T>` ne replica la semantica con lo stesso vocabolario di wire (`LIVE`/`DERIVED`/`CACHED`/`SIMULATED`/`UNKNOWN`), così la mappatura da `FusedFact<T>` (A2) è 1:1 e senza tabella di traduzione.

## 2. `WorldFact<T>`

| Campo | Significato |
|---|---|
| `Source` | `FactSource`: `Unknown` (=0, quindi un `default` è UNKNOWN, mai LIVE), `Live`, `Derived`, `Cached`, `Simulated`. |
| `Channel` | `SensorChannel`: `Network`, `Memory`, `Screen`, `Local`; `None` solo per UNKNOWN. |
| `Confidence` | `float` in `[0, 1]`, mai NaN; 0 per UNKNOWN. |
| `ObservedAtUnixMillis` | Timestamp Unix ms fornito dal chiamante (`IMonotonicClock.UnixMillis`); il contratto non legge mai l'orologio di sistema. |
| `HasValue` | `true` solo per fatti osservati. `Value` lancia `InvalidOperationException` su UNKNOWN; usare `TryGetValue`. |
| `Reason` | Motivo dell'UNKNOWN (obbligatorio) o nota facoltativa su un fatto noto. |

Invarianti imposti dal costruttore (violazione = eccezione): UNKNOWN ⇒ nessun valore, confidence 0, canale `None`, reason non vuota; noto ⇒ valore presente e canale ≠ `None`; confidence in `[0,1]`; timestamp ≥ 0.

Freshness: `FreshnessAt(now, maxAge)` → `Fresh` (età ≤ budget, inclusivo), `Stale`, `Unknown`. `AgeAt(now)` è `null` per UNKNOWN e non è mai negativa (orologio che torna indietro ⇒ 0).

Attuabilità: `IsActionable` è vero solo per fatti noti con source `Live`/`Derived`/`Cached`. `Simulated` è pianificabile ma **mai** attuabile (ADR-0016 lato runtime).

Factory: `Live`, `Derived`, `Cached`, `Simulated` (canale forzato a `Local`), `Unknown(reason, at)`, `NotObserved(at)`; `AsCached()` ri-etichetta senza toccare valore, canale o timestamp.

## 3. Entità e aggregato

| Tipo | File | Note |
|---|---|---|
| `MapId`, `EntityId`, `QuestId`, `ItemId`, `SkillId`, `EffectId`, `ActionId`, `WorldGoalId` | `Identifiers.cs` | Identità osservabili dal client; tutte con `None`/`IsNone`. `WorldGoalId` è distinto da `Planning.GoalId`. |
| `MapCell`, `MapBounds`, `TileState`, `TileObservation`, `MapPolygon`, `PortalState`, `MapState` | `Spatial.cs` | `MapState.TileAt` restituisce UNKNOWN per celle mai osservate. I poligoni sono chiusi, con area (shoelace) e rilevamento degenerazione. |
| `Orientation`, `EntityKind`, `EntityPresence`, `Vital`, `PlayerState`, `MobState`, `NpcState`, `DropState`, `EntityCollections` | `Entities.cs` | `Vital.Ratio` è `null` se il massimo è ignoto/zero. Le collezioni sono ordinate per id (`EntityCollections.SortById`). |
| `QuestStatus`, `QuestObjective`, `QuestState`, `InventoryBag`, `InventoryItem`, `EquipmentSlot`, `EquipmentItem`, `SkillState`, `StatusEffectPolarity`, `StatusEffectState`, `CooldownScope`, `CooldownState`, `ResourceKind`, `ResourceState` | `Progression.cs` | Buff e debuff condividono `StatusEffectState`; la polarità li distingue. Gli helper temporali (`IsReadyAt`, `IsActiveAt`, `RemainingAt`) restituiscono `null` quando il modello non può rispondere. |
| `ActionCategory`, `ActionAvailability`, `ActionOutcome`, `ActionRecord`, `GoalKind`, `GoalStatus`, `GoalRecord` | `Intent.cs` | Registrano conoscenza su azioni e goal; nessuna autorità di esecuzione. |
| `WorldModelSnapshot` | `WorldModelSnapshot.cs` | Aggregato immutabile: `SchemaVersion`, `Revision`, `ObservedAtUnixMillis` + tutte le collezioni. |
| `WorldModelCanonicalText` | `WorldModelCanonicalText.cs` | Forma testuale canonica (culture-invariant, escape dei separatori) e digest FNV-1a 64 bit. |

`WorldModelSnapshot`:

- `Empty(at)`: tutti i 15 fatti scalari UNKNOWN con reason `"not observed"`, collezioni vuote.
- `Advance(at)`: nuova revisione (`Revision + 1`, `checked`) con timestamp non regressivo; il contenuto si aggiorna con `with`.
- `Facts()`: enumerazione deterministica di ogni `IWorldFact`; da qui `HasSimulatedFact`, `IsActionable`, `KnownFactCount`, `UnknownFactCount`.
- `AreVitalsFreshAt(now, maxAge)`: HP, MP, `Alive` e posizione tutti `Fresh`.
- `Validate()` / `ThrowIfInvalid()` (fail-closed): versione schema, revisione e timestamp non negativi, vitali validi, bounds positivi, tile dentro i bounds, id mob/npc/drop strettamente crescenti, chiavi uniche in ogni collezione, quantità non negative, progress goal in `[0,1]`, nessun fatto più recente dello snapshot.
- Uguaglianza e `GetHashCode` per **contenuto** (gli `ImmutableArray` sono confrontati elemento per elemento via `ImmutableSequence`), quindi due snapshot costruiti dalle stesse osservazioni sono uguali e hanno lo stesso digest: è la proprietà su cui poggia il replay deterministico richiesto dal DoD di AP-01.

## 4. Mappatura da Sensor Fusion (per A4/A6)

| `FusedFact<T>` (A2, runtime) | `WorldFact<T>` (A1, core) |
|---|---|
| `Value.Source` (`DataSourceKind`) | `Source` via testo wire (`ToWire` → `FactSourceText.TryParseWire`) |
| `Winner` (`SensorKind?`) | `Channel` (`null` ⇒ fatto UNKNOWN con `SensorChannel.None`) |
| `Confidence` | `Confidence` |
| `Value.ObservedAtUtc` | `ObservedAtUnixMillis` (`DateTimeOffset.ToUnixTimeMilliseconds`) |
| `Value.HasValue == false` | `WorldFact<T>.Unknown(Value.FailureReason ?? "fusion unknown", at)` |
| `IsFresh` | non copiato: la freshness si ricalcola con `FreshnessAt(now, budget)` al momento dell'uso |

Le entità osservate da A2 (`SelectableEntity`, `InventorySlotReading`, `GroundItem`, `SkillReady`, …) vanno proiettate in `MobState`/`InventoryItem`/`DropState`/`SkillState` popolando solo i fatti effettivamente osservati; il resto resta `NotObserved`. Nessun valore va inventato per completare un record.

## 5. Test e benchmark (A5)

Percorso: `tests/NosAi.Core.Tests/WorldModel/`.

| File | Copre |
|---|---|
| `WorldModelFixtures.cs` | Fixture deterministica (timestamp fissi `T0..T2`, snapshot `Populated()` a revisione 7 con 15 fatti UNKNOWN). Solo test; mai sul percorso di produzione. |
| `WorldFactTests.cs` | `default` ⇒ UNKNOWN; rifiuto di valore/canale/confidence su UNKNOWN; confidence ai bordi (0, 1, NaN, ±∞); timestamp negativi; freshness inclusiva; orologio all'indietro; SIMULATED mai attuabile; `AsCached`; round-trip testo wire; uguaglianza per valore; `ToString` invariant. |
| `WorldModelSnapshotTests.cs` | `Empty` tutto UNKNOWN e valido; fixture valida; uguaglianza/digest per contenuto; un fatto cambiato o una sola provenance cambiata ⇒ digest diverso; SIMULATED ⇒ non attuabile; `Advance`; ogni regola di `Validate`; array `default` trattati come vuoti; lookup tile; poligoni; helper temporali con `null`; identificatori `None`. |
| `WorldModelCanonicalTextTests.cs` | Un rigo per fatto, nessun duplicato; UNKNOWN scritto come `∅` mai come `0`; provenance/confidence/timestamp nel testo; escape di `| = [ ] \n \\`; **digest pinnati** (`Populated` = `0x2D9BF29FBE8B0DC9`, `Empty(T0)` = `0xED1653B61EA4DB38`). |
| `WorldModelBudgetTests.cs` | Benchmark riproducibili con soglia 2 000 µs/op e < 1 KiB allocati per revisione. |

Comandi (dalla radice del repo):

```text
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter FullyQualifiedName~NosAi.Core.Tests.WorldModel
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldModelBudgetTests --logger "console;verbosity=detailed"
```

Valori misurati il 2026-09-05 (container Linux x64, .NET 8.0, Release, 2 000 iterazioni; non è hardware target):

| Operazione | µs/op |
|---|---|
| `Advance` + `with` su un fatto | 0,7 |
| `Validate()` | 36,5 |
| `Facts()` + `IsActionable` | 16,8 |
| `ComputeDigest()` | 167,0 |
| allocazione per revisione | 152 B |

Criteri di accettazione: tutte le operazioni sotto 2 000 µs/op (due ordini di grandezza sotto il ciclo Gate3); un cambio della forma canonica fa fallire `DigestIsPinnedForReplay` e richiede il bump di `SchemaVersion` più il re-pin del digest.

## 6. Regola sui digest pinnati

`DigestIsPinnedForReplay` è la sentinella del replay: se cambia il testo canonico (nuovo campo, nuovo ordine, nuovo formato) il test fallisce. La procedura corretta è: incrementare `WorldModelContract.SchemaVersion`, aggiornare questo documento, ri-pinnare i due digest nel test. Non è mai corretto "aggiustare" il digest senza bump di versione.
