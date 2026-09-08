# RAPPORTO S8 — `--unequip`, il clic sullo slot calibrato e il verdetto chiesto al filo

Incarico: `docs/agents/phases/AP-07/AP-07_A2A4_DEEPSEEK_S8_unequip_by_click.md`.
Livello di verifica: **Integrated** — non `Verified`: che il *clic* provochi
davvero l'unequip resta da provare sul client vivo con l'operatore. Qui si prova
che, dato un certo filo, il verdetto è quello giusto.

## 1. La misura della Parte 1 (obbligatoria, e conclude bene)

Con `--wire-inspect` sulle otto catture di `data/` si misura se il server
rimanda `equip` dopo un cambio di equipaggiamento. **La risposta è sì, e solo su
`data/equip_test.noscap`** — le altre sette non contengono né `eq` né `equip`.

Censimento per cattura (opcode `equip` / `eq` / `ivn`):

| cattura | `equip` | `eq` | `ivn` |
|---|---:|---:|---:|
| `equip_test.noscap` | **6** | **6** | **6** |
| `messaggi.noscap` | 0 | 0 | 4 |
| `nostale_combat.noscap` | 0 | 0 | 3 |
| `certificazione.noscap` | 0 | 0 | 1 |
| `nostale_01.noscap` | 0 | 0 | 0 |
| `nostale_live.noscap` | 0 | 0 | 0 |
| `calibrate_20260903_091408Z_round1.noscap` | 0 | 0 | 0 |
| `calibrate_20260903_091428Z_round2.noscap` | 0 | 0 | 0 |

I sei `equip` di `equip_test.noscap`, in ordine cronologico (timeline
`--timeline equip,eq,ivn`):

| # | ordine/istante | slot presenti | differenza dalla riga precedente |
|---|---|---|---|
| 1 | #488 · 22:46:38.344 | 0,2,4,5,6,9,10,11,12 | — |
| 2 | #921 · 22:46:41.444 (+3,1 s) | 0,2,4,5,9,10,11,12 | **slot 6 (vnum 309) sparito** |
| 3 | #2211 · 22:46:52.944 (+11,5 s) | 0,2,4,5,8,9,10,11,12 | slot 8 (vnum 518) comparso |
| 4 | #2451 · 22:46:54.744 (+1,8 s) | 0,2,4,5,9,10,11,12 | slot 8 sparito |
| 5 | #3067 · 22:47:00.044 (+5,3 s) | 0,2,4,5,9,10,12 | **slot 11 (vnum 284) sparito** |
| 6 | #3317 · 22:47:01.944 (+1,9 s) | 0,2,4,5,9,10,11,12 | slot 11 tornato |

**Due `equip` consecutive con contenuto diverso ci sono, a distanza di tempo
misurabile** (da 1,8 s a 11,5 s): è il segnale su cui si costruisce il verdetto.
La distanza minima osservata fra un `equip` e il successivo è **1,8 s**, ben
dentro la finestra di verifica predefinita di 1 s solo se l'operatore la
allarga — la finestra di default è 1 s e va dichiarata onestamente come più
stretta della latenza peggiore osservata (1,8 s). Il comando espone
`--verify-ms` per questo.

Il riscontro incrociato con `ivn` (lo stesso che ha validato il decoder in S7):
ogni vnum che esce da uno slot di `equip` appare in uno slot di `ivn` nello
stesso istante — `309` → `ivn 0 15.309…` (#920), `518` → `ivn 0 16.518…`
(#2450), `284` → `ivn 0 18.284…` (#3066). I due opcode si confermano a vicenda.

`eq` è ri-inviato negli stessi istanti di `equip` ma **byte-identico in tutti e
sei**: l'insieme visibile (5 vnum) non cambia mentre cambiano gli slot 6/8/11,
che non fanno parte dell'insieme `eq`. Conferma che il verdetto va costruito su
`equip`, non su `eq` — ed è per questo che il lettore del comando filtra
`EquipmentWireOpcode.Equip`.

## 2. File creati e modificati

- `src/NosAi.Runtime/Tactical/UnequipExecutor.cs` — nuovo (4 esiti, verdetto dal filo)
- `src/NosAi.Runtime/Tactical/UnequipCommand.cs` — nuovo (parsing, rifiuti, composizione live)
- `src/NosAi.Runtime/Program.cs` — solo registrazione `--unequip` + whitelist `KnownProbeFlags`
- `src/NosAi.ControlPanel/MainWindow.xaml` — card "Togli equipaggiamento" nella vista Equipaggiamento
- `src/NosAi.ControlPanel/MainWindow.xaml.cs` — `ApplyUnequip`/`OnUnequip`/`SummariseUnequip`
- `src/NosAi.ControlPanel/UnequipInspect.cs` — nuovo (stato calibrazione + slot letti dal file)
- `tests/NosAi.Runtime.Tests/UnequipExecutorTests.cs` — nuovo (12 test)
- `tests/NosAi.Runtime.Tests/UnequipCommandTests.cs` — nuovo (10 test)
- `tests/NosAi.ControlPanel.Tests/UnequipInspectTests.cs` — nuovo (3 test)

## 3. Build e test

Build (solo i progetti della sessione, mai la soluzione):

- `dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Debug` → **0 errori, 0 warning**
- `dotnet build src/NosAi.ControlPanel/NosAi.ControlPanel.csproj -c Debug` → **0 errori, 0 warning**

Test:

- `dotnet test tests/NosAi.Runtime.Tests --filter "FullyQualifiedName~Unequip"` → **Superati: 30, Non superati: 0**
- `dotnet test tests/NosAi.Runtime.Tests --filter "FullyQualifiedName~WornEquipmentFromWire|FullyQualifiedName~ClickTarget"` (regressione sulle parti lette) → **Superati: 39, Non superati: 0**
- `dotnet test tests/NosAi.ControlPanel.Tests -c Debug` (suite completa) → **Superati: 159, Non superati: 0**

La suite runtime completa non è stata rilanciata in questa sessione; i test
mirati sopra coprono i file nuovi e le dipendenze toccate (decoder `equip`,
`ClickTargetExecutor` come modello).

## 4. Cosa ho lasciato `Unknown`, e perché

1. **La corrispondenza `slot di equip` ↔ `EquipmentSlot` non è incrociata.**
   L'esecutore confronta lo slot richiesto con il numero di slot del pacchetto
   `equip` (`WornEquipmentSlot.Slot`) come lo stesso intero. È la lettura che il
   verdetto della specifica presuppone, ma **non è confermata dal filo contro
   `Item.dat`**: S7 l'aveva rimandata esplicitamente ("No mapping slot number →
   EquipmentSlot") e il catalogo `reference.db` non è presente sul volume
   `NOSAI-SSD` in questo ambiente, quindi non ho potuto chiuderla. I numeri
   osservati (slot 0,2,4,5,6,8,9,10,11,12 con vnum plausibili) sono compatibili
   con l'identità ma non la provano. Documentato nel commento dell'esecutore,
   non sepolto.

2. **Il trascinamento non è esprimibile.** L'API del gate (`Click`,
   `MoveAbsolute`) non ha un gesto di trascinamento; non l'ho aggirato scendendo
   sotto il gate. Riportato, come chiesto dalla specifica.

3. **La finestra di verifica di default (1 s) è più stretta della latenza
   peggiore osservata (1,8 s)** fra due `equip` consecutivi nella cattura. Non
   l'ho cambiata a tavolino: l'operatore la allarga con `--verify-ms` se serve.

## 5. Dove mi sono fermato

Nessun blocco. Ho letto (senza modificare) `GameTrafficObserver`,
`NosTaleWorldProtocolDecoder`, `NetworkGameplayProvider`,
`LiveObservationScope`: il canale live non pubblica `WornEquipment` in nessun
`GameplayObservation`, quindi il comando costruisce la propria catena di cattura
(`ClientNetworkObserver` → `WinDivertPacketSource` →
`ReassembledObservationSource` → `GameTrafficObserver`) invece di passare da
`LiveObservationScope`. Nessuno di quei file è mio, e non ne ho toccato nessuno.

## 6. Specifica e codice non concordavano

1. **`--arm-input` e l'armamento.** La specifica non lo nomina nella grammatica
   (`--unequip <slot> [--gesture …] [--verify-ms …]`), ma il comando attua e
   deve armare l'input. Ho seguito `ClickTargetCommand` (flag richiesto) e in
   più ho **armato davvero** la policy con
   `components.Safety.Set(SecurityPrincipal.Operator, SafetySwitch.LiveInput, true, …)`:
   `ClickTargetCommand` accetta `--arm-input` ma non lo usa per armare (solo per
   rifiutare se assente), quindi senza l'armamento esplicito il gate rifiuterebbe
   sempre con `live_input_disabled_by_policy`. È la differenza fra `Integrated`
   e un comando che non clicca mai.

2. **La corrispondenza slot (punto 4.1).** Il codice vince: il decoder non
   asserisce la mappatura, e l'esecutore la dichiara come assunzione invece di
   presentarla come fatto.

## 7. Id ancora aperti di `docs/TEST_RIMANDATI.md`

T-03, T-05, T-06, T-07, T-08, T-09, T-12, T-13, T-14, T-16, T-17.

Nota su **T-12**: la prima esecuzione vera di `--unequip` — con il filo
registrato mentre l'operatore preme il bottone del pannello — è la cattura che
chiude la seconda metà di T-12 (quale `InventoryKind`, candidato `Wear=8`,
produce un equip reale). Il verdetto di S8 si costruisce su `equip`, non su
`ivn`, e non dipende da T-12; ma quella registrazione avrebbe finalmente un modo
di chiudersi.

## Nota di controllo

`GuardAiClientTests.ManyRapidHeartbeats…` è rosso sotto carico parallelo e verde
in isolamento (RAPPORTO_S3, RAPPORTO_S5). Non l'ho considerato una regressione.
