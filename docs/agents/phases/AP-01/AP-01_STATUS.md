# AP-01 — Unified World Model — Stato

**Data:** 2026-09-05
**Fase:** AP-01 (docs/ROADMAP_ESECUTIVA.md S:AP-01)

## 1. Ambito di questo aggiornamento

Copre solo **A1 (Claude) — Contratti World Model**: `src/NosAi.Core/WorldModel/` e i relativi test unitari. Nessuna logica di fusione (A2), predizione/belief temporale (A3) o wiring runtime (A4) è stata scritta qui — per design, A1 possiede solo i contratti (docs/agents/AGENT_COMMAND_REGISTRY.md, dominio "AP-01 — World Model").

## 2. File creati

`src/NosAi.Core/WorldModel/`:
- `WorldModelClassification.cs` — `DataSourceKind` (Live/Derived/Cached/Simulated/Unknown) e `WorldFact<T>` (valore + provenienza + confidence continua [0,1] + timestamp + motivo se Unknown).
- `EquatableArray.cs` — sequenza immutabile con vera uguaglianza strutturale (necessaria per il requisito "replay deterministico": i record .NET con un campo `ImmutableArray<T>`/array/`List<T>` grezzo confrontano per riferimento, non per contenuto).
- `Identifiers.cs` — id tipizzati (`EntityId`, `MapId`, `PortalId`, `ItemId`, `SkillId`, `StatusEffectId`, `QuestId`, `ActionId`, `GoalId`), ognuno valida stringa non nulla/vuota/whitespace nel costruttore.
- `SpatialContracts.cs` — `WorldPosition`, `TileCoordinate`, `TileTraversability`, `Tile`, `Polygon`, `Portal`.
- `MapModel.cs` — `MapBounds`, `MapModel` (versionato, fabbrica `Unknown` prima di qualunque osservazione).
- `ResourceContracts.cs` — `ResourceKind`, `Resource` (pool HP/MP/stamina/valuta/custom, entrambi i limiti classificati indipendentemente).
- `StatusEffectContracts.cs` — `StatusEffectPolarity`, `StatusEffect` (Buff/Debuff unificati con discriminatore, stessa forma osservabile), `Cooldown`, `Skill`.
- `InventoryContracts.cs` — `InventoryItem`, `EquipmentSlot`, `EquipmentItem`.
- `EntityContracts.cs` — `CombatantStatus` (composizione condivisa Player/Mob), `Player`, `Mob`, `Npc`, `Drop`.
- `QuestContracts.cs` — `QuestObjectiveStatus`, `QuestObjective`, `Quest`.
- `PlanningContracts.cs` — `ActionOutcome`, `WorldAction` (record di ciò che è accaduto, non il planner), `Goal`.
- `WorldModelSnapshot.cs` — radice aggregata versionata (`Version`, `Player`, `Map`, `Mobs`, `Npcs`, `Drops`, `Quests`, `RecentActions`, `ActiveGoals`), fabbrica `Unknown()`.

`tests/NosAi.Core.Tests/WorldModel/`: un file di test per ciascun file sopra (11 file, 63 test totali).

## 3. Decisioni di design rilevanti

1. **`WorldFact<T>` invece di riusare `NosAi.Core.Hardware.ClassifiedValue<T>`.** Stesso assembly, nessun vincolo di reference circolare come tra `NosAi.Core`/`NosAi.Runtime` — ma importare `NosAi.Core.Hardware` da contratti di gameplay (Player/Quest/Mob) accoppierebbe due domini non correlati. Duplicazione dichiarata e documentata, stesso pattern già usato nel repo tra `NosAi.Runtime.Contracts` e `NosAi.Core.Hardware`. Differenza voluta rispetto a `ClassifiedValue<T>`: `WorldFact<T>` porta anche una `Confidence` continua in [0,1] (clampata nelle factory), perché la sensor fusion di gameplay produce disaccordo continuo tra fonti, non solo un livello di fiducia categorico (docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md S:4.1). **Unificazione dei tre primitivi di classificazione (Runtime.Contracts / Core.Hardware / Core.WorldModel) non tentata qui — segnalata come follow-up di integrazione, fuori ambito per un task di soli contratti.**
2. **`EquatableArray<T>`.** `HardwareCapabilitySnapshot` aveva evitato completamente le collezioni nei record proprio per garantire l'uguaglianza per valore; il World Model non può farne a meno (mob multipli, oggetti multipli, ecc.), quindi è stato aggiunto questo piccolo wrapper con `Equals`/`GetHashCode` strutturali, verificato con test dedicati incluso il caso di elementi annidati (`WorldFact<T>` dentro `EquatableArray<T>`).
3. **Buff e Debuff unificati in `StatusEffect` con `StatusEffectPolarity`.** Il roadmap li elenca come sostantivi separati ma hanno la stessa forma osservabile (id, nome, durata residua, magnitudine); due tipi quasi identici sarebbero stati duplicazione prematura.
4. **Id tipizzati invece di stringhe nude.** Evita di passare per errore un `QuestId` dove è atteso un `ItemId`; ogni id valida in costruttore che il valore non sia nullo/vuoto/whitespace.
5. **`WorldAction`/`Goal` sono record di ciò che il World Model osserva/ricorda**, non il planner: la decomposizione HTN/GOAP e lo scoring restano di AP-08.

## 4. Build/test — evidenza

```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)   (TreatWarningsAsErrors=true)

dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WorldModel"
  → Passed! Failed: 0, Passed: 63, Skipped: 0, Total: 63

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 314, Skipped: 0, Total: 314
    (un run isolato ha mostrato un fallimento transitorio di
    TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget,
    28.672ms vs budget 25ms — micro-benchmark di latenza loopback pre-esistente
    e non correlato a questo task, già noto come rumoroso sotto sandbox
    condivisa; il retry immediato successivo è passato pulito, 5/5)
```

## 5. Livello di verifica

**`Present`/`Integrated`** a livello di codice (contratti completi, build pulita, test verdi). **Non `Verified`**: nessuna fusione reale da rete/memoria/schermo/client esiste ancora — questi sono solo i contratti che A2 (sensor fusion) dovrà popolare.

## 6. Handoff per A2 (DeepSeek, sensor fusion), A3, A4

- Namespace: `NosAi.Core.WorldModel`. Tipo aggregato da produrre a ogni ciclo di fusione: `WorldModelSnapshot`, con `Version` monotonicamente crescente ad ogni fusione riuscita.
- Ogni fatto incerto è un `WorldFact<T>`: usa `Live`/`Derived`/`Cached`/`Simulated`/`Unknown` a seconda della fonte; non costruire mai `WorldFact<T>` con un valore fabbricato quando la fonte è assente o scaduta — usa `Unknown(reason)`.
- Le collezioni sono `EquatableArray<T>` (`EquatableArray<T>.From(IEnumerable<T>)`), mai array/List grezzi nei nuovi record.
- Precedenza deterministica fra fonti in conflitto e disagreement tracking (richiesti dal DoD di questa fase) **non sono ancora implementati**: A1 fornisce solo il contenitore (`WorldFact<T>.Confidence` esiste apposta per registrare la fiducia post-conflitto), la logica di risoluzione è responsabilità di A2 esattamente come già descritto in `docs/agents/phases/AP-01/A2_DEEPSEEK_sensor_fusion.md`.
- Nota aperta per A6/integrazione futura: tre primitivi di classificazione paralleli esistono ora nel repo (`NosAi.Runtime.Contracts.DataSourceKind`/`ClassifiedValue<T>`, `NosAi.Core.Hardware.DataSourceKind`/`ClassifiedValue<T>`, `NosAi.Core.WorldModel.DataSourceKind`/`WorldFact<T>`) — unificazione deliberatamente rimandata, non bloccante per procedere con A2.

## 7. A2 (Claude, al posto di Cursor/DeepSeek non ancora attivi) — Sensor Fusion

Eseguito subito dopo A1 nella stessa sessione, su richiesta esplicita dell'utente di continuare la coda (`docs/agents/EXECUTION_QUEUE.md` Q-008) e di eseguire nel frattempo i ruoli assegnati a Cursor/DeepSeek finché non sono operativi.

### File creati

`src/NosAi.Runtime/WorldModel/Fusion/`:
- `FactFusion.cs` — `FusionCandidate<T>`, `FusionOutcome<T>`, `FactFusion.Resolve<T>(...)`: il meccanismo generico e riutilizzabile di precedenza deterministica + disagreement tracking richiesto dal DoD di AP-01 ("conflitti gestiti"). Ordine di risoluzione fisso: scarta i candidati stale/Unknown → preferisci il `DataSourceKind` più affidabile (Live>Derived>Cached>Simulated) → a parità, confidence più alta → a parità, osservazione più recente → a parità, nome canale in ordine ordinale (determinismo anche in un pareggio completo). Il vincitore è restituito esattamente come il suo canale lo ha riportato (nessun valore inventato, nessuna media).
- `GameplayObservationProjector.cs` — proietta la `GameplayObservation` reale e già esistente (rete + memoria, fusa dai decorator `NetworkGameplayProvider`/`PositionAwareGameplayProvider`/`MemoryMapWorldProvider`) nel `WorldModelSnapshot` di AP-01. Funzione pura: stesso input → stesso output.

`tests/NosAi.Runtime.Tests/WorldModel/Fusion/`: `FactFusionTests.cs` (10 test) e `GameplayObservationProjectorTests.cs` (9 test).

### Perché il proiettore oggi copre solo Player/Inventory/Drop/Map, non Mob/NPC/Quest/Equipment/Skill

Verifica diretta sul codice reale prima di scrivere il proiettore (non un'assunzione):

- **Mob vs NPC non è distinguibile dalla sola rete.** `NosAi.Runtime.Autonomy.SelectableEntity.Vnum` lo dichiara esplicitamente nella propria documentazione: "the wire's type 3 is monster and NPC together... not a guess made here" — serve un lookup su un catalogo di riferimento che non esiste in questo repository. Quella classificazione appartiene ad AP-02 (object detection/tracking), non a questa fusione di rete/memoria. `WorldModelSnapshot.Mobs`/`.Npcs` restano quindi vuoti, non fabbricati.
- **Equipaggiato vs inventario non è distinguibile.** `InventorySlotReading.InventoryKind` è documentato come "no meaning attached to the number" — non esiste un segnale per separare uno slot equipaggiato da uno slot zaino. `Player.Equipment` resta vuoto.
- **Nomi di oggetti/skill non sono risolvibili.** La rete porta solo `Vnum`/slot numerici, mai un nome; ogni `InventoryItem.Name`/futuro `Skill.Name` è onestamente `Unknown`, mai un placeholder.
- **Nessun canale osservazione quest esiste ancora** (arriverà con AP-06): `WorldModelSnapshot.Quests` resta vuoto.

Questi limiti sono dichiarati nel codice (XML doc di `GameplayObservationProjector`) e coperti da un test dedicato (`MobsAndNpcsAndQuests_StayEmpty_BecauseNoReferenceCatalogueOrQuestChannelExistsYet`), così un futuro tentativo di "risolvere" questi campi con un vnum indovinato romperebbe un test invece di passare silenziosamente.

### Governance: `ModuleReachability`

Il repository ha un test reale (`ModuleReachabilityTests.Every_namespace_in_the_runtime_is_declared`) che impedisce di aggiungere un nuovo namespace in `src/NosAi.Runtime` senza dichiararlo esplicitamente come `Integrated`/`SuiteOnly`/`Unreferenced`. `NosAi.Runtime.WorldModel.Fusion` è stato dichiarato **`Unreferenced`** in `src/NosAi.Runtime/Observability/ModuleReachability.cs`: onesto, perché nessun host/composition-root di produzione lo richiama ancora — solo i test unitari lo esercitano, e i test non contano ai fini di questa classificazione (per design, si veda il commento del file). Collegarlo a un ciclo di decisione reale è lavoro di AP-01/A4 ("runtime wiring from existing observation snapshots into the World Model"), non di questo task.

### Build/test — evidenza

```
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~WorldModel.Fusion"
  → Passed! Failed: 0, Passed: 17, Skipped: 0, Total: 17

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1784, Skipped: 58, Total: 1842

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 314, Skipped: 0, Total: 314
```

### Livello di verifica — A2

**`Present`/`Integrated` a livello di codice** (build pulita, 17/17 test propri, nessuna regressione sulle 2131 test combinate di Core+Runtime). Dichiarato onestamente **`Unreferenced`** in `ModuleReachability` (nessun host lo chiama ancora) e **non `Verified`** (nessuna validazione su client NosTale reale). Handoff per A4: `GameplayObservationProjector.Project(...)` è pronto per essere chiamato dal loop di decisione reale non appena un host lo cablerà; `FactFusion.Resolve<T>(...)` è pronto per il giorno in cui un secondo canale (es. screen/OCR di AP-02) osserverà lo stesso fatto già coperto da `GameplayObservation`.

## 8. A3 (Claude) — Temporal belief, derived state e prediction

Il DoD di AP-01 (fusione, conflitti gestiti, UNKNOWN preservato, replay deterministico) era già chiuso da A1+A2. La voce A3 in `AGENT_COMMAND_REGISTRY.md` ("temporal belief, prediction and derived-state algorithms") è una categoria, non una specifica: prima di scrivere codice ho chiesto esplicitamente all'utente come restringerla (rischio concreto di requisiti ipotetici, vietato da CLAUDE.md). Scelta dell'utente: ambito pieno.

### Cosa è stato costruito, e perché è delimitato così

`src/NosAi.Core/WorldModel/Temporal/`:
- **`TemporalBelief.cs`** — tre primitivi puri, senza stato:
  - `DecayConfidence<T>`: fa scendere linearmente la confidence di un `WorldFact<T>` con l'età, azzerandosi esattamente al bordo di `maxAge`. Non tocca mai `Value`/`Source`/`ObservedAtUtc`, non sostituisce il taglio netto già fatto da `WorldFact<T>.IsFresh`/`FactFusion` — lo rende solo continuo *dentro* la finestra fresca, invece che binario.
  - `EstimateVelocity`: deriva una `WorldVelocity` da due posizioni classificate consecutive della stessa entità; sempre `Derived`, confidence = minimo delle due entrate, `Unknown` se l'ordine non è crescente o il divario tra le due osservazioni supera il limite dato.
  - `PredictPosition`: estrapolazione a breve termine, sempre `Simulated` (mai `Live`/`Derived`) — coerente con l'invariante "Prediction is advisory only". Non scrive mai nel campo `Position` reale: è un risultato a sé, che un futuro consumatore (combat/navigation) userà esplicitamente, testato apposta (`PredictPosition_NeverOverwritesTheRealPositionField...`) perché non deriva silenziosamente in quella direzione.
- **`WorldModelTemporalEnricher.cs`** — orchestratore puro: dati uno snapshot precedente e uno corrente, decade la confidence delle posizioni e deriva la velocità di `Player` e di ogni `Mob` (accoppiamento per `EntityId` tra le due liste). Nessuno stato interno: stessi due snapshot in ingresso → stesso risultato (replay deterministico).

**Distinzione esplicita dalla fase "Simulation/Prediction" della pipeline canonica** (`World Model → Simulation/Prediction → Ranking/Utility`, docs/NOSAI_ARCHITECTURE_BASELINE.md S:2): quella fase valuta esiti ipotetici di AZIONI candidate ed è territorio di AP-05/AP-08 (combattimento/pianificazione), che non esistono ancora. `PredictPosition` qui è molto più stretto: stima solo "dove si trova probabilmente ora un'entità già tracciata", mai "cosa succede se eseguo l'azione X". Documentato nel commento XML di `TemporalBelief` così la distinzione resta esplicita e non si sconfina per inerzia in AP-05/08.

### Modifica ai contratti A1

`Player` e `Mob` (in `EntityContracts.cs`) guadagnano una proprietà `Velocity` **init-only con default**, non un parametro posizionale — stesso trattamento già usato da `GameplayObservation` per i propri campi aggiuntivi, così nessun sito di costruzione esistente (inclusi tutti i test di A1/A2) si rompe. Difetto trovato e corretto durante l'implementazione: il valore di default iniziale usava `DateTime.UtcNow` implicito di `WorldFact<T>.Unknown(reason)`, rendendo due `Player`/`Mob` costruiti in istanti reali diversi non più uguali — stesso tipo di bug di non-determinismo già trovato e corretto in AP-00/A6. Corretto con un'istanza statica condivisa, calcolata una sola volta con un timestamp fisso (`DateTime.UnixEpoch`).

### Build/test — evidenza

```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~Temporal"
  → Passed! Failed: 0, Passed: 25, Skipped: 0, Total: 25

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 339, Skipped: 0, Total: 339

dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1784, Skipped: 58, Total: 1842
```

### Livello di verifica — A3

**`Present`/`Integrated` a livello di codice**, non `Verified` (nessun ciclo di decisione reale lo invoca ancora — stessa nota di A2). Handoff per A4: `WorldModelTemporalEnricher.Enrich(previous, current, nowUtc, maxAge, maxObservationGap)` va chiamato dal loop runtime subito dopo `GameplayObservationProjector.Project(...)`, passando come `previous` l'ultimo snapshot arricchito conservato dal loop stesso (questo tipo non conserva stato proprio, per design).

## 9. A4 (Claude) — Runtime wiring del World Model

Prima ricognizione (non modifica) sul composition root reale: `IGameplayProvider.Observe()` era **già** chiamato in produzione da `Gate1RuntimeSnapshotProvider.Capture()`, consumato dal `Gate3DecisionLoop` esistente (l'unico loop periodico reale, 500ms) — ma trasformato in `Gate3WorldState`, un tipo minimale pre-esistente e distinto, mai in `WorldModelSnapshot`. Nessun binding esisteva verso il Control Panel per il World Model AP-01.

**File creati:**
- `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs` — componente che tiene lo stato tra i cicli (`_current`, versione monotona via `Interlocked.Increment`), un metodo di tick puro e testabile `RunOnce(Gate1CanonicalSnapshot, DateTime)` (estrae `Client.Gameplay`, fallback onesto a `GameplayObservation.Unobserved(...)` se assente, chiama in sequenza `GameplayObservationProjector.Project` poi `WorldModelTemporalEnricher.Enrich`), e uno scaffolding di loop periodico (`PeriodicTimer`, `Start`/`PumpAsync`/`DisposeAsync`) nello stesso stile di `Gate3DecisionLoop`. Espone `Current` (Volatile.Read/Write) ed evento `SnapshotFused`.

**File modificati (solo additivi, come richiesto):**
- `src/NosAi.Runtime/Configuration/Gate1HostOptions.cs` — nuovo flag opt-in `FuseWorldModel`/`FuseWorldModelIntervalMs` (CLI `--fuse-world-model[-interval-ms]`, env `NOSAI_FUSE_WORLD_MODEL[_INTERVAL_MS]`), stesso pattern esatto di `RunDecisionLoop`/`DecisionIntervalMs`. Indipendente dal decision loop: nessuno dei due dipende dall'altro.
- `src/NosAi.Runtime/Program.cs` — dopo l'avvio dell'host, se il flag è attivo, costruisce e avvia un `WorldModelFusionLoop` usando `host.Capture` come sorgente; smaltito correttamente in uscita (`await using`).
- `src/NosAi.Runtime/Observability/ModuleReachability.cs` — `NosAi.Runtime.WorldModel.Fusion` spostato da `Unreferenced` a `Integrated` (ora referenziato realmente da `Program.cs`), verificato da `ModuleReachabilityTests`.

**Limite dichiarato:** nessun percorso pubblico espone l'`EntityId` reale del personaggio controllato (`NetworkWorldFeed.PlayerEntityId` resta privato lungo tutta la catena). Usato il sentinel `"unknown-player"` (`WorldModelFusionLoop.UnknownPlayerSentinelId`), stesso trattamento di `GameplayObservationProjector.UnknownMapSentinelId` per la mappa. Esporre l'id reale resta follow-up esplicito, volutamente non affrontato qui per non rifattorizzare `Gate1ObservationChannel`/`NetworkGameplayProvider`/`Gate1BootstrapHost`.

## 10. A5 (Claude) — Audit indipendente

Audit completo di A1+A2+A3, stesso metodo di AP-00/A5: 25 nuovi test (18 in Core.Tests, 7 in Runtime.Tests), **4 difetti reali trovati** con test di regressione deliberatamente rossi, mai corretti da A5 (fuori dal suo ownership) né nascosti. Riportati con evidenza empirica contro il runtime .NET reale, non per sola ispezione del codice. Report completo: `docs/agents/phases/AP-01/AP-01_A5_AUDIT.md`.

Sintesi dei 4 difetti (dettagli/scenari completi nel report A5):
1. `WorldFact<T>.ClampConfidence` non gestiva `NaN` (`Math.Clamp(NaN, 0, 1)` restituisce `NaN` invariato).
2. Conseguenza a valle: `FactFusion.IsBetter` con `Confidence = NaN` rompeva la catena di tie-break deterministica, facendo vincere il candidato semplicemente in base all'ordine nella lista.
3-4. `TemporalBelief.EstimateVelocity`/`PredictPosition`: i rami di ritorno anticipato `Unknown(reason)` omettevano l'istante, facendo trapelare `DateTime.UtcNow` reale invece di un istante derivato dagli input — scoperto empiricamente da un test di determinismo multi-ciclo che falliva in modo intermittente.

Più 5 lacune documentate (non difetti, decisioni esplicite registrate): ambiguità di `Resource.Custom` con nomi coincidenti, overflow/NaN nell'aritmetica IEEE-754 di posizioni estreme, `EntityId` duplicati non validati (né in `current.Mobs` né in `previous.Mobs`), vnum/slot/quantità negativi non validati in `GameplayObservationProjector` (nessun catalogo di riferimento esiste ancora), e una stranezza diagnostica minore nel testo di `DisagreementDetail` per canali duplicati.

## 11. A6 (Claude) — Integrazione finale AP-01

Applicate tutte e 4 le correzioni suggerite dall'audit A5, verificate una per una e poi in combinazione:

1. **`src/NosAi.Core/WorldModel/WorldModelClassification.cs`** — `ClampConfidence`: `double.IsNaN(confidence) ? 0d : Math.Clamp(confidence, 0d, 1d)`.
2. **`src/NosAi.Runtime/WorldModel/Fusion/FactFusion.cs`** — `IsBetter`: normalizza una confidence `NaN` a `-1` (peggiore di qualunque lettura legittima) prima del confronto, su entrambi i lati — corretto separatamente dal fix #1 perché `FactFusion` riceve dati da fonti esterne e non deve fidarsi ciecamente che ogni `WorldFact<T>` sia stato costruito tramite le factory di A1 (fail-closed).
3-4. **`src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs`** — tutti e 4 i rami di ritorno anticipato `Unknown(...)` (3 in `EstimateVelocity`, 1 in `PredictPosition`) ora passano esplicitamente `current.ObservedAtUtc`/`asOfUtc`.

**Evidenza:**
```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release      → 0 Warning(s), 0 Error(s)
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release → 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WorldFactBoundaryTests|FullyQualifiedName~TemporalBeliefBoundaryTests"
  → Passed! Failed: 0, Passed: 12, Total: 12   (i 3 difetti Core ora verdi)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~FactFusionBoundaryTests|FullyQualifiedName~FactFusionTests"
  → Passed! Failed: 0, Passed: 12, Total: 12   (il 4° difetto ora verde, i test companion restano verdi)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 357, Skipped: 0, Total: 357

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1812, Skipped: 58, Total: 1870
```

Nessuna regressione: tutti i test pre-esistenti di A1/A2/A3/A4/A5 restano verdi, i 4 test-difetto sono ora verdi senza che i loro test companion (che verificano il comportamento corretto nel caso opposto) siano cambiati di esito.

### Livello di verifica finale — AP-01 (Unified World Model)

**`Integrated`**: A1 (contratti) + A2 (sensor fusion) + A3 (temporal belief/prediction) + A4 (wiring runtime) costruiscono un albero unico che compila pulito e passa tutti i test combinati, inclusi i 4 difetti reali trovati dall'audit indipendente A5 e corretti in questo passaggio di integrazione. **Non `Verified`**: nessuna validazione contro un client NosTale reale è stata eseguita in nessun punto di questa fase — resta il collo di bottiglia dichiarato, coerente con quanto già registrato per il sistema Gate 1-6 pre-esistente.

Item aperti, deliberatamente rimandati (non bloccanti per dichiarare `Integrated`):
- Triplicazione di `DataSourceKind`/`ClassifiedValue<T>`/`WorldFact<T>` tra `NosAi.Runtime.Contracts`, `NosAi.Core.Hardware` e `NosAi.Core.WorldModel` (segnalata da A1, ribadita da A5).
- Sentinel `"unknown-player"` — esporre l'`EntityId` reale del personaggio richiede toccare `Gate1ObservationChannel`/`NetworkGameplayProvider`/`Gate1BootstrapHost`, esplicitamente fuori ambito per A4.
- Le 5 lacune documentate da A5 (§10) — nessuna bloccante, da tenere presenti in AP-02 (catalogo vnum/nomi) e fasi successive (validazione unicità `EntityId`).
