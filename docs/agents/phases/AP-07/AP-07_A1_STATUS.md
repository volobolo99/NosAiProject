# AP-07 / A1+A3 — Stato

**Avviato per la stessa eccezione dichiarata già usata per AP-04/05/06**
(`docs/agents/EXECUTION_QUEUE.md` "Criterio di avanzamento fase"): questi
contratti dipendono solo da `InventoryItem`/`EquipmentItem`/
`EquipmentSlot`/`Player` (AP-01, già `Integrated`) e da
`EnrichedQuestObjective`/`QuestGraphPlanner` (AP-06, `Present`), non da
lavoro ancora in corso in AP-05.

## Ambito

`docs/ROADMAP_ESECUTIVA.md` S:AP-07: "Modello di build che valuta
statistiche, DPS, survivability, resource efficiency, sinergie,
enemy-specific performance, movement/utility, costo upgrade, opportunity
cost e quest relevance. DoD: equip/upgrade/inventory actions sono
motivate, autorizzate e verificate."

## Consegnato

`src/NosAi.Core/WorldModel/Loadout/LoadoutContracts.cs`:

- `LoadoutActionKind` — `Equip`/`Unequip`/`Upgrade`.
- `LoadoutActionCandidate` — validato per `Kind` (stesso trattamento di
  `CombatActionCandidate`/`QuestObjectiveTarget`): Equip richiede
  item+slot, Unequip richiede solo slot, Upgrade richiede solo item.
- `LoadoutConstraintCheck` — stesso shape di `CombatConstraintCheck`.
- `LoadoutEvaluation` — le nove dimensioni esatte della DoD (DPS,
  survivability, resource efficiency, sinergie, enemy-specific
  performance, movement/utility, costo upgrade, opportunity cost, quest
  relevance) come input grezzi, non punteggio — stesso principio di
  `CombatSimulationResult`/`FrontierCandidate`.

`src/NosAi.Core/WorldModel/Loadout/LoadoutPlanner.cs` (A3, parziale e
onesto, puro/stateless):

- `GenerateUnequipCandidates`/`GenerateUpgradeCandidates` — reali, da
  `Player.Equipment` (unico dato già noto e sufficiente).
- `CheckHardConstraints` — reale per tutti e tre i `Kind`: disponibilità
  item in inventario (`Quantity > 0`), occupazione slot, item
  effettivamente equipaggiato. **Non controlla un costo di upgrade**
  (materiali/valuta): nessun dato reale esiste su `EquipmentItem`.
- `CountActiveQuestNeedsFor` — quante obiettivi quest attivi e non
  soddisfatti (Collect/Deliver) richiedono un dato item, incrociando
  **dati reali già noti di AP-06** (`EnrichedQuestObjective`,
  `QuestGraphPlanner.IsObjectiveSatisfied`) invece di stimarlo. È l'unica
  dimensione di `LoadoutEvaluation` che questa fase popola davvero, con
  un segnale onesto (conteggio intero), non un punteggio inventato.

## Perché `GenerateEquipCandidates` non esiste

`InventoryItem` (AP-01) porta identità/quantità/posizione-nello-zaino
(`SlotIndex`), **mai** a quale `EquipmentSlot` (arma/elmo/armatura/...)
l'oggetto corrisponderebbe se equipaggiato. Nessun codice in questo
repository decodifica la categoria di un item. Generare automaticamente
un candidato Equip richiederebbe indovinare questa corrispondenza —
esattamente il tipo di inferenza fabbricata che il progetto vieta.
`CheckHardConstraints` giudica comunque correttamente un candidato Equip
**se fornito da altrove** (un operatore, o una futura fonte che
classifica gli item) — manca solo la generazione automatica, non il
giudizio sui vincoli.

## Perché `LoadoutEvaluation` resta quasi tutta non popolata

Stesso genere di gap già trovato per le skill in AP-05
(`AP-05_A1_STATUS.md` §"Indagine su Gate3Runtime per skill/attacco"):
`EquipmentItem`/`InventoryItem` non portano alcuna statistica di
combattimento (attacco/difesa/elemento), né un costo di upgrade in
materiali/valuta. `GameReferenceDatabase` (il catalogo dati reale del
client) dichiara esplicitamente di non decodificare semanticamente quei
campi. Fabbricare qui numeri di DPS/survivability/sinergia sarebbe
esattamente il tipo di dato simulato spacciato per reale che
l'architettura vieta. Segnalato come gap dati reali in
`docs/agents/DEEPSEEK_TASKS.md`, non aggirato con una formula inventata.

## Test

`tests/NosAi.Core.Tests/WorldModel/Loadout/LoadoutContractsTests.cs` +
`LoadoutPlannerTests.cs`: 27 test, tutti verdi. `dotnet build NosAi.sln -c Release`:
0 errori (1 warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **525/525**,
0 falliti (498 precedenti + 27 nuovi, zero regressioni).

## Deliberatamente non affrontato qui

- **Generazione candidati Equip automatica**: bloccata dall'assenza di
  dati di categoria item (vedi sopra) — candidato per "Candidati da
  investigare" in `DEEPSEEK_TASKS.md`.
- **Popolamento reale di `LoadoutEvaluation`** (tranne `QuestRelevance`):
  bloccato dall'assenza di statistiche reali per item/upgrade — stesso
  gap dati di AP-05, stessa fonte sospetta (`GameReferenceDatabase`/dati
  client non decodificati).
- **Esecuzione equip/upgrade autorizzata**: comporrebbe con
  Guard/Trust/Safety reali, stesso principio già applicato in AP-04
  (`--scout`) e ancora aperto in AP-05 — non affrontato qui.

**Livello di verifica:** `Present` — contratti e algoritmo parziale
scritti, testati, compilano puliti; non ancora `Integrated` in nessun
ciclo runtime.
