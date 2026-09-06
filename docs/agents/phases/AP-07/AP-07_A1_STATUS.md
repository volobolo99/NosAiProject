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

## AP-07/A2+A4 — indagine mirata: genuinamente bloccato, non solo rimandato

Stessa disciplina "investigate before speccing" già applicata a AP-04
(`--scout`), AP-05 (`--engage`) e AP-06 (`--collect`, dove l'indagine ha
trovato un canale reale inatteso). Qui il risultato è diverso e va
detto altrettanto chiaramente: **non esiste alcuna fetta onesta e
costruibile oggi**, né per l'esecuzione né per la verifica, per nessuno
dei tre `LoadoutActionKind` (`Equip`/`Unequip`/`Upgrade`). Non è un
rinvio per pigrizia — è confermato per ispezione diretta, non assunto:

1. **Nessuna primitiva di esecuzione esiste, in nessuno dei due
   sistemi.** `NosAi.Runtime.Contracts.ActionType` (il sistema Gate 1-6
   preesistente) non ha nemmeno una voce `Equip`/`Unequip`/`Upgrade` —
   a differenza di `CollectGroundItem`/`RestAndRecover`, che almeno
   esistono come voci dichiarate-ma-non-implementate
   (`InputActionEffector.cs`: `"action_not_implemented"`), qui non c'è
   proprio un concetto modellato. Grep su `Equip`/`Unequip`/`wear`/`put`
   in tutto `src/NosAi.Runtime`: zero risultati.
2. **Nessun tasto/hotkey esiste per equipaggiare.** A differenza di
   skill/consumabili (`KeybindsCheck.RuntimeIntentPrefixes`,
   `skill.`/`consumable.`), equipaggiare in NosTale è un'interazione UI
   (drag-and-drop o doppio click su uno slot inventario/equipaggiamento),
   non una hotkey. Un click su un punto schermo è tecnicamente possibile
   (`IInputBackend.Click`/`MoveAbsolute` esistono, riusabili), ma
   **manca il layout**: nessuna calibrazione screen-space per gli slot
   del pannello inventario/equipaggiamento esiste in questo repository
   (`ScreenProjectionCalibration`/`CalibratedScreenProjection` proiettano
   coordinate di **mondo di gioco** su schermo per mirare a un bersaglio,
   non coordinate fisse di un pannello UI — un problema diverso, non
   ancora affrontato da nessun codice esistente).
3. **Nessun canale di verifica esiste per lo stato equipaggiato.**
   `docs/PROTOCOLLO_NOSTALE.md`: zero menzioni di equip/wear/gear —
   nessun opcode di rete per un cambio di equipaggiamento è mai stato
   identificato, tantomeno decodificato. Il canale già reale
   (`InventorySlotReading` via `ivn`, usato per `--collect`/AP-06) non
   basta: `GameplayObservationProjector`'s stesso commento dichiara
   `InventorySlotReading.InventoryKind` privo di significato noto, quindi
   "equipaggiato" vs "nello zaino" **non è distinguibile** dallo stesso
   dato che ha risolto `Collect`. `Player.Equipment` resta vuoto per lo
   stesso motivo (non un'omissione di questa fase).

**Differenza dalla stessa indagine per AP-05/AP-06**: lì un percorso
onesto e parziale esisteva (vitali player per AP-05, canale network reale
per Collect in AP-06) — qui nessuno dei due lati (esecuzione, verifica)
ha nemmeno un punto di appoggio parziale. Forzare comunque una specifica
DeepSeek `--equip`/`--upgrade` oggi significherebbe o inventare un
meccanismo di click su coordinate mai calibrate (dato fabbricato
spacciato per un layout reale) o dichiarare verificato un cambio di
equipaggiamento che il canale dati non può confermare — esattamente ciò
che CLAUDE.md vieta.

**Conclusione: AP-07/A2+A4 non è specificabile per DeepSeek in questo
momento.** Non una decisione da prendere con l'utente (a differenza di
AP-05, dove due strade erano entrambe percorribili): qui manca
l'infrastruttura di base su entrambi i lati. Cosa servirebbe prima,
segnalato per riferimento futuro, non avviato qui:

- una calibrazione screen-space per il pannello inventario/equipaggiamento
  (stesso genere di lavoro di `ScreenProjectionAutoCalibrator`, ma per
  un pannello UI fisso invece che per la proiezione mondo→schermo);
  oppure una mappatura nota slot-di-rete → slot-schermo, se il layout
  del pannello è fisso e documentabile senza calibrazione dinamica;
- un opcode di rete per il cambio di equipaggiamento, se esiste ed è
  semplicemente non ancora identificato in `docs/PROTOCOLLO_NOSTALE.md`
  (da verificare con una cattura dedicata, non assunto assente per
  sempre — la stessa cautela già usata prima di dichiarare `Collect`
  bloccato, che si è rivelata sbagliata).

Segnalato in `docs/agents/DEEPSEEK_TASKS.md` come gap di infrastruttura,
non come task pronto.

## AP-07/A2+A4 — seconda indagine (2026-09-06): un varco reale trovato, non tutto il blocco

Stessa disciplina "investigate before speccing", ri-applicata su
istruzione esplicita dell'utente di riprendere il lavoro a ritmo alto.
Le tre ragioni di blocco sopra sono state ri-verificate per ispezione
diretta, non assunte valide per sempre:

1. **Primitiva di esecuzione**: confermato ancora assente, ma non è la
   priorità — equipaggiare è comunque un click su un pannello UI (vedi
   punto 2), non una hotkey, quindi "aggiungere un `ActionType`" non
   sbloccherebbe nulla di eseguibile da solo. Non affrontato qui.
2. **Calibrazione screen-space del pannello**: confermato ancora assente
   **e resta il vero collo di bottiglia** — nessun codice può sapere dove
   sono gli otto slot senza un operatore che conferma un ritaglio reale.
   **Consegnato ora**: `src/NosAi.Runtime/Perception/InventoryPanelRoiCalibration.cs`
   (Claude, A1) — stesso schema di `DialogRoiCalibration`/`TargetRoiCalibration`,
   esteso a un ritaglio per ciascuno degli otto `EquipmentSlot`, tutti e
   otto insieme o nessuno (il pannello è aperto per intero o non lo è,
   non esiste uno stato di calibrazione parziale onesto). 12 test verdi.
3. **Canale di verifica**: **parzialmente confutato**. `GameTrafficObserver.cs`'s
   `InventorySlotReading.InventoryKind` ora documenta i valori candidati
   da una fonte esterna reale — OpenNos (GPL, già vaulted in
   `third_party/sources/opennos`), `OpenNos.Domain/InventoryType.cs`:
   `Equipment=0, Main=1, Etc=2, Miniland=3, Specialist=6, Costume=7,
   Wear=8, Bazaar=9, Warehouse=10`. Incrociato con successo un valore già
   osservato in questo repository: `Etc=2` combacia esattamente con la
   cattura reale `ivn 2 34.2006.1.0` (un drop raccolto). `Equipment=0` e
   `Wear=8` — i candidati per "equipaggiato" — restano **non incrociati**:
   il codice server di OpenNos legge gli oggetti indossati specificamente
   via `Wear`, il che lo rende il candidato meglio supportato dei due, ma
   nessuno dei due può essere usato per decidere alcunché su un percorso
   non diagnostico finché una cattura reale di un equip non conferma quale
   kind assume lo slot. Il campo resta un `int` grezzo, non un enum, per
   lo stesso motivo.

**Conclusione aggiornata**: AP-07/A2+A4 non è ancora specificabile per un
comando `--equip`/`--unequip` reale — mancano ancora la calibrazione
confermata da un operatore reale *e* la cattura che confermi `Wear=8`.
**È però specificabile un passo intermedio reale e onesto**: la
calibrazione stessa, esattamente come `TargetRoiCalibration`/
`DialogRoiCalibration` sono nate `Present` prima di essere confermate.
Specifica DeepSeek in
`docs/agents/phases/AP-07/AP-07_A2A4_DEEPSEEK_inventory_panel_calibration.md`
(`--calibrate-inventory-panel`, solo calibrazione, nessuna esecuzione).
