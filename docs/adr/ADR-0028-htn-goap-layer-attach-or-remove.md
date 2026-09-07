# ADR-0028 — Lo strato HTN/GOAP: attaccarlo o toglierlo

**Status:** Accepted — **opzione C**, scelta dall'utente il 2026-09-07
**Date:** 2026-09-07

## Context

`CLAUDE.md` nomina lo stadio due volte. Nel flusso canonico:

> Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility
> → Strategic Orchestrator → **HTN/GOAP** → Guard → …

e nei requisiti di autonomia, come istruzione diretta:

> Do not hardcode a static macro where a model/planner is required. Prefer
> strategic goals → HTN/GOAP → reactive recovery.

Quello strato **esiste**, è provato, e **non è attaccato a niente**.

### Cosa c'è, misurato il 2026-09-07

`src/NosAi.Core/Planning/` — **435 righe, 18 tipi pubblici**:

| Tipo | Cosa fa |
|---|---|
| `DeterministicGoapPlanner` | ricerca in avanti deterministica e **limitata** (`maxNodes`, default 256), con `FaultCode` in uscita. Non è uno stub |
| `LexicographicOrchestrator` | sceglie fra azioni ordinate per classe di obiettivo e utilità |
| `DeadlinePlanner` | pianifica entro una scadenza |
| `SequenceRoutine` / `SelectorRoutine` | i due nodi di un behaviour tree |
| `PlannerGoalStack`, `PlannerGoalId`, `GoalClass` | lo stack di obiettivi, senza allocazione |
| `PlanStep`, `ActionIntent`, `GoapFact`, `GoapAction` | i contratti che li legano |

`src/NosAi.Core/Safety/` — **97 righe**: `RetryBudgetController`, il recovery
reattivo che la stessa frase di `CLAUDE.md` nomina dopo l'HTN/GOAP.

Provati da quattro file di test (152 righe), tutti verdi.

`ModuleReachability` dichiara entrambi `Unreferenced`, e nessun file di
produzione li raggiunge.

### Cosa pianifica davvero, oggi

- **`Gate3Runtime.ActionPlanner.PlanCandidates`** produce direttamente una lista
  di candidati d'azione dallo stato del mondo. Non cerca: valuta.
- **`StrategyPlanner`** (190 righe) assegna urgenze — sopravvivenza, recupero —
  e non produce un piano di passi.

Nessuno dei due è HTN o GOAP. Fra «quali azioni sono possibili adesso» e «quale
sequenza di passi porta all'obiettivo» c'è esattamente lo strato che non è
attaccato.

### Il costo dell'attacco, misurato

Il seme non è un'interfaccia da implementare: sono **due produttori che non
esistono**.

```
grep -rn "PlannerWorldState|GoapFact" src/ --include=*.cs
  → solo le dichiarazioni in NosAi.Core/Planning e due citazioni in un commento
```

- **`PlannerWorldState`** è la forma compatta che il pianificatore attraversa
  (`ReadOnlyMemory<EntitySnapshot>`, struct blittabili). **Nessun sito la
  costruisce**: `IPlannerWorldStateBuilder` non ha implementazioni. Il World Model
  vivo produce `NosAi.Runtime.WorldModel.WorldState` e `WorldModelSnapshot`, che
  hanno un'altra forma e portano provenienza e freschezza per campo — quello che
  la forma compatta, per come è fatta, non porta.
- **`GoapFact(string Key, int Value)`** è la forma dei predicati su cui il
  planner cerca. Nessuno li deriva dal World Model, e derivarli è la decisione
  vera: **quali fatti del mondo diventano predicati**, e cosa succede a un fatto
  `Unknown` quando lo si riduce a una coppia chiave/intero.

Quest'ultimo punto non è meccanico ed è il motivo per cui questo ADR esiste
invece di un task. `GoapFact` non sa dire *sconosciuto*. Un mondo in cui «il
personaggio ha una pozione» è `Unknown` non si rappresenta con `("has_potion", 0)`
senza mentire, ed è esattamente la menzogna che ADR-0016 vieta: *l'ignoto non
autorizza un atto*.

## Options

### A — Attaccarlo, con un contratto onesto per l'ignoto

Costruire i due produttori mancanti, e **prima** decidere come un `WorldFact`
`Unknown` attraversa la riduzione a `GoapFact`. Tre sotto-opzioni reali: il fatto
sconosciuto non entra fra i predicati (il piano non può dipenderne); entra come
predicato a tre valori (e `GoapFact` cambia forma); oppure il planner riceve solo
fatti osservati e rifiuta di pianificare quando ne manca uno che una precondizione
nomina.

**Costo**: il produttore di stato, la riduzione a predicati, la decisione
sull'ignoto, e il punto d'innesto in `Gate3Runtime` fra ranking e Guard.
**Guadagno**: lo stadio che `CLAUDE.md` richiede smette di essere assente, e la
pianificazione a più passi diventa possibile — oggi non lo è.
**Rischio**: cablaggio inerte se il piano prodotto non ha un consumatore che
davvero lo esegua, e un pianificatore che pianifica su predicati che mentono
sull'ignoto è peggio di nessun pianificatore.

### B — Toglierlo

435 + 97 righe e i loro test spariscono. Il repository smette di contenere un
pezzo che nessuno chiama.

**Costo**: `CLAUDE.md` continuerebbe a nominare uno stadio di cui non esiste più
alcuna implementazione, e la frase «do not hardcode a static macro where a
model/planner is required» resterebbe senza il pianificatore che la rende
seguibile. Quando lo stadio servirà, si riscriverà — e la ricerca GOAP limitata
che esiste oggi è già scritta e provata.
**Guadagno**: onestà del registro; niente più codice che sembra pronto e non lo è.

### C — Lasciarlo dov'è, dicendo che è una decisione presa

Nessun costo immediato. È lo stato attuale, con la differenza che smetterebbe di
essere un'omissione e diventerebbe una scelta scritta: *lo strato esiste come
riferimento, non è nel percorso di esecuzione, e non lo sarà finché AP-08 non
avrà bisogno di pianificare a più passi*.

## Recommendation

**C ora, A quando un caso reale lo chiede.** Non per prudenza: perché A senza un
caso d'uso concreto produrrebbe una riduzione a predicati inventata a tavolino, e
quella riduzione è la parte che decide se il pianificatore dice il vero. Il caso
d'uso c'è quando esisterà un obiettivo che il ciclo attuale non sa raggiungere in
un passo — e oggi `--autoplay` non ne ha uno.

**B è sconsigliata** per una ragione misurata e non estetica: sarebbe l'unico
punto del repository in cui `CLAUDE.md` nomina uno stadio e il codice non ne ha
nemmeno il contratto. Le 532 righe non costano manutenzione — non hanno
dipendenze, non entrano in nessun percorso, e i loro test girano in millisecondi.

Quello che C **richiede** è una cosa sola, ed è ciò che la rende diversa dallo
stato di oggi: `ModuleReachability` deve dire che l'irraggiungibilità è
deliberata e datata, non un debito. Finché dice solo `Unreferenced`, ogni
rassegna futura ci ricascherà.

## Consequences

- **C, scelta e applicata il 2026-09-07.** Le tre voci di `ModuleReachability`
  (`NosAi.Core.Planning`, `NosAi.Core.Planning.Goap`, `NosAi.Core.Safety`) dicono
  ora che l'irraggiungibilità è deliberata, con la data e il rimando a questo
  ADR; la voce Q-111 che le teneva aperte è chiusa. Nessun codice di produzione
  è cambiato — è esattamente il punto dell'opzione C: ciò che cambia è che il
  registro smette di leggersi come un debito.

  **Ciò che riapre la questione** è un fatto, non un ripensamento: il primo
  obiettivo che il ciclo di Gate 3 non sappia raggiungere in un passo. Quando
  esisterà, si torna qui e si parte dall'opzione A — e il suo primo passo resta
  quello scritto sopra, che non è codice.
- Se sceglie **A**: il primo passo non è codice, è decidere come l'ignoto
  attraversa `GoapFact`. Prima di quello, qualunque produttore scritto sarebbe da
  riscrivere.
- Se sceglie **B**: la rimozione porta via anche i quattro file di test, e
  `CLAUDE.md` va corretto nello stesso passaggio — lasciare il flusso canonico
  che nomina uno stadio inesistente sarebbe la stessa incoerenza al contrario.
