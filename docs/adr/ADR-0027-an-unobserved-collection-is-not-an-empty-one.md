# ADR-0027 — Una collezione non osservata non è una collezione vuota

**Status:** Accepted — opzione A, applicata il 2026-09-07
**Date:** 2026-09-07

## Context

L'invariante portante di questo progetto è *«Unknown is not zero, false or empty»*
(`CLAUDE.md`, «Architecture invariants»). Ogni fatto di gioco importante lo
rispetta attraverso `WorldFact<T>`, che distingue un valore osservato da un
`Unknown` con motivazione, provenienza e freschezza.

Quattro campi di `Player` non lo rispettano
(`src/NosAi.Core/WorldModel/EntityContracts.cs:19`):

```csharp
EquatableArray<Skill> Skills,
EquatableArray<Cooldown> Cooldowns,
EquatableArray<InventoryItem> Inventory,
EquatableArray<EquipmentItem> Equipment
```

Una `EquatableArray<T>` vuota è l'unica cosa che questi campi sanno dire quando
nessuno li ha osservati, ed è indistinguibile dall'affermazione «osservato: il
personaggio non ha abilità / non indossa nulla / ha lo zaino vuoto».

### Misure

Verificate sul codice, non dedotte.

**Siti di costruzione di `Player`: 6 in tutto** — 4 in `src/`, 2 in `tests/`.

| Sito | `Skills` | `Cooldowns` | `Inventory` | `Equipment` |
|---|---|---|---|---|
| `WorldModelSnapshot.cs:42` (lo snapshot `Unknown`) | `Empty` | `Empty` | `Empty` | `Empty` |
| `GameplayObservationProjector.cs:120` | `Empty` | proiettato | proiettato | `Empty` |
| `AutoplayCommand.cs:~565` | `Empty` | `Empty` | `Empty` | `Empty` |
| `LiveCombatObserver.cs:140` | `Empty` | `Empty` | `Empty` | `Empty` |

**Nessun sito di produzione popola `Skills` o `Equipment`.** Non è un caso
limite: ogni snapshot che questo runtime abbia mai prodotto afferma che il
personaggio non ha alcuna abilità e non indossa nulla.

`WorldModelSnapshot.Unknown(reason, now)` è il caso più netto. Costruisce un
giocatore in cui *ogni* `WorldFact` è `Unknown(reason)` — posizione,
orientamento, vita, mappa — e poi dichiara quattro collezioni vuote. Uno
snapshot il cui nome è «Unknown» afferma quattro fatti positivi che nessuno ha
osservato.

**Siti di lettura in produzione: 14.** Un `grep` grezzo su `.Skills`/`.Cooldowns`/
`.Inventory`/`.Equipment` ne conta 41, ma la maggior parte non tocca `Player`:
sono righe `using` sul namespace `NosAi.Economy.Inventory`, membri di enum
(`GameFunctionKind.Inventory`), letture di `observation.Inventory` /
`profile.Inventory` / `state?.Inventory`, e citazioni nei commenti. Le letture
vere, verificate una per una:

| Campo | Siti | Dove |
|---|---|---|
| `Skills` | 3 | `CombatPlanner.cs:90`, `:267`, `:284` |
| `Cooldowns` | 2 | `CombatPlanner.cs:92`, `:291` |
| `Inventory` | 4 | `LoadoutPlanner.cs:63`, `:107`; `CollectCommand.cs:139`, `:156` |
| `Equipment` | 5 | `LoadoutPlanner.cs:37`, `:81`, `:108`, `:112`, `:116` |

Sono concentrate in tre file: `CombatPlanner` (5), `LoadoutPlanner` (7),
`CollectCommand` (2).

### Il difetto si è già manifestato due volte

1. `CombatPlanner.CheckSkillReady` rifiutava con `skill_not_found` sia quando la
   lista era stata letta e la skill mancava, sia quando la lista era vuota
   perché nessuno l'aveva letta — cioè *sempre*, su client reale. Il rifiuto
   dava la colpa al personaggio per un buco nei canali di osservazione.
   Corretto in `5e9bd93` con `player.Skills.Count == 0 ?
   "skill_list_not_observed" : "skill_not_found"`
   (`CombatPlanner.cs:284`): una **euristica**, non un fatto. Oggi è corretta
   solo perché nessun canale osserva mai una lista vuota reale. Il giorno in cui
   uno lo farà, un personaggio appena creato verrà descritto come «lista mai
   letta».

2. `CombatPlanner.GenerateCandidates` itera `player.Skills` e con la lista vuota
   non può produrre alcun candidato `UseSkill`. Questo ha reso `--combat-report`
   strutturalmente incapace di predire `--engage` per ogni mob fra le due
   portate (`a39197e`). Il report non era sbagliato per un errore di logica: era
   sbagliato perché il modello gli diceva, falsamente, che il personaggio non ha
   abilità.

Entrambe le volte la correzione ha aggirato il sintomo. La causa è la forma del
contratto.

## Opzioni

### A — `WorldFact<EquatableArray<T>>` sui quattro campi

Il campo diventa un fatto come tutti gli altri: `Unknown("skill_list_never_read")`
finché un canale non lo osserva.

- **Coerente** con l'invariante e con il resto del modello; nessun concetto nuovo
  da imparare.
- **Costo:** i 6 siti di costruzione cambiano firma; le 14 letture in `src/`
  diventano `player.Skills.Value` con un controllo `HasValue` dove serve una
  decisione. Sono concentrate in tre file, quindi il lavoro è più piccolo di
  quanto il conteggio grezzo suggerisse.
- **Effetto collaterale desiderato:** ogni lettura che oggi ignora
  silenziosamente la differenza *non compila più* finché non la affronta. È il
  motivo principale per preferire questa opzione: la migrazione è guidata dal
  compilatore, non dalla memoria di chi la fa.

### B — Un marcatore di provenienza affiancato

Le quattro collezioni restano `EquatableArray<T>`; si aggiunge una proprietà
`init` con default che dice, per ciascuna, se è stata osservata — lo stesso
trattamento additivo che `Player.Velocity` ha già
(`EntityContracts.cs:53`, «An init-only addition (not a positional parameter) so
every existing construction site keeps compiling»).

- **Costo minimo:** i 6 siti di costruzione continuano a compilare invariati.
- **Difetto:** non forza nessuna lettura ad affrontare la distinzione. Le 14
  letture esistenti restano com'erano, cioè sbagliate nello stesso modo di oggi,
  finché qualcuno non le rivede una per una — e nulla segnala quali. Riproduce
  la forma di `Velocity`, ma per un motivo diverso: lì il default era corretto
  (una velocità non ancora derivata *è* Unknown e nessuno la leggeva come zero),
  qui il default è precisamente l'affermazione falsa.

### C — Non fare nulla, e rendere esplicita la convenzione

Documentare che «vuoto» significa «non osservato» e che nessun canale può
osservare una collezione realmente vuota.

- **Onesto solo finché regge**, e regge per una ragione accidentale: nessun
  canale legge ancora quelle liste. `--loadout-report` legge già
  l'equipaggiamento reale dal client (`e597c0d`, `1d53c5d`) senza passare da
  `Player.Equipment`; il giorno in cui quel percorso confluisce nel World Model,
  la convenzione si rompe in silenzio.

## Decisione presa

**Opzione A, applicata.** L'utente aveva dato istruzione esplicita di portare
avanti il lavoro da solo e di fermarsi solo dove il suo intervento fosse
davvero necessario; questa non lo era — la misura era completa, la
raccomandazione documentata, e la migrazione è guidata dal compilatore. Se la
preferenza fosse B o C, l'intera modifica è un solo commit da revertire.

La raccomandazione era **A**, e la ragione decisiva non è
l'eleganza: è che A trasforma 14 letture da riesaminare a mano in 14 errori di
compilazione. B costa meno oggi e lascia aperto esattamente il difetto che ha
già prodotto due bug.

Con 6 costruzioni e 14 letture in tre file, A è una sessione di lavoro, non un
refactor di giornata. Se la si vuole comunque divisa, l'ordine è `Skills`
(3 letture, tutte in `CombatPlanner`, ed è il campo che ha causato entrambi i
problemi noti), poi `Cooldowns` (2, stesso file), poi `Equipment` e `Inventory`
insieme, perché condividono `LoadoutPlanner`.

## Conseguenze

- `CombatPlanner.cs:284` perde l'euristica `Count == 0` e diventa una lettura di
  fatto. Il test che oggi la fissa va aggiornato ad asserire il fatto, non il
  conteggio.
- `WorldModelSnapshot.Unknown` smette di affermare quattro fatti positivi.
- La spec pendente per DeepSeek
  (`docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_mob_absolute_vitals.md`) non è
  toccata: riguarda `EntitySighting`, non `Player`.
- Nessun cambiamento di comportamento osservabile su client reale, oggi:
  nessuno legge quelle liste attraverso un percorso che agisce. Il valore è
  interamente nel non ripetere i due bug quando il primo canale di osservazione
  arriverà.

## Cosa è successo davvero, applicandola

Il compilatore ha segnalato **esattamente** i 12 siti previsti in `src/`, più i
2 in `CollectCommand` già contati: nessuna sorpresa nella misura. Quello che la
misura non prevedeva sono le tre cose emerse durante la migrazione.

**Un difetto latente in `EquatableArray<T>`.** Il valore di un
`WorldFact<T>.Unknown` è `default(T)`, e `default(EquatableArray<T>)` non passa
dal costruttore: il campo resta un `ImmutableArray` di default, i cui membri
lanciano tutti. `GetHashCode` su uno snapshot che ne conteneva uno tirava
`NullReferenceException` — rilevato da un test di determinismo preesistente,
non da uno nuovo. Ogni membro legge ora attraverso un accessore che normalizza
il default a vuoto.

**`QuestGraphPlanner.AssessCollectProgress` può finalmente dire la verità.** Il
suo commento documentava già il difetto (*"un gap già presente uno strato più
su, in `GameplayObservationProjector` stesso, non qualcosa che questo metodo
possa risolvere"*) e lo aggirava trattando "vuoto" come Unknown — cioè
riportando come ignoto uno zaino realmente vuoto, l'errore speculare. Ora
distingue: fatto non osservato → Unknown con la sua motivazione, inventario
osservato e vuoto → zero noto. Un test cambia risposta, ed è quello il punto.

**Due nuovi rifiuti fail-closed.** Non sapere quali abilità siano in cooldown
non è sapere che questa non lo è: `IsSkillReady` e `CheckSkillReady` rifiutano
su una lista non osservata (`cooldown_list_not_observed`), e `LoadoutPlanner`
riporta `inventory_not_observed` / `equipment_not_observed` invece di far
passare un vincolo che nessuno ha potuto verificare. Finché erano array nudi
questi casi non erano esprimibili: una lista mai letta era una lista vuota,
cioè un permesso.
