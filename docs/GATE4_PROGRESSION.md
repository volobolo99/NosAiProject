# NosAi — Gate 4: Progression Engine V2 & Knowledge Base

**Versione:** 1.0 Beta
**Aggiornato:** 2026-08-30
**Codice:** `src/NosAi.Runtime/Gate4/`

## Scopo

Gate 4 decide **cosa perseguire**, non come eseguirlo. Risolve il DAG delle
missioni, tiene l'evidenza statistica sulle strategie e sceglie fra quelle
disponibili.

L'esecuzione resta di Gate 3, che la autorizza e la verifica.

## Componenti

| Componente | Ruolo |
|---|---|
| `QuestDependencyNode` | nodo del DAG: prerequisiti, livelli, materiali, oro |
| `ProgressionEngineV2` | risoluzione del DAG e pianificazione del passo successivo |
| `BetaBinomialEvidence` | aggiornamento bayesiano su successi e fallimenti |
| `Ucb1StrategySelector` | selezione con bilanciamento esplorazione/sfruttamento |
| `KnowledgeBaseManager` | ciclo di vita delle strategie |
| `StrategyLifecycleStatus` | `Candidate → Evaluating → Verified → Mastered`, oppure `Deprecated` |

## Il modello di evidenza

Da prior uniforme (α=1, β=1), dopo *n* prove:

- tasso atteso di successo: `α / (α+β)`
- confidenza: `1 − e^(−0,15·n)`
- mastery: `0,70 · tasso + 0,30 · confidenza`

Soglie del ciclo di vita:

| Stato | Condizioni |
|---|---|
| `Mastered` | prove ≥ 5 **e** mastery ≥ 0,90 **e** tasso ≥ 0,92 |
| `Verified` | prove ≥ 3 **e** tasso ≥ 0,70 |
| `Deprecated` | prove ≥ 5 **e** tasso < 0,40 |
| `Evaluating` | tutti gli altri casi |

Con successi consecutivi da prior uniforme, il primo *n* che raggiunge `Mastered`
è **12**: a n=10 il tasso è 0,9167 (sotto 0,92) e la mastery 0,8747 (sotto 0,90).

Questo numero non è un dettaglio: il test della suite ne eseguiva 10 e falliva. La
correzione è stata nel test, non nelle soglie — abbassare la soglia per far passare
un ciclo arbitrario avrebbe reso la suite verde indebolendo proprio la definizione
di "padroneggiato".

## Provenienza dei dati di gioco

`QuestDependencyNode.Provenance` distingue due cose che è pericoloso confondere:

| Valore | Significato |
|---|---|
| `Verified` | requisiti verificati contro il client reale |
| `Provisional` | ordinamento modellato, requisiti numerici **non verificati** |

**Il default è `Provisional`**: la verifica va dichiarata, non presunta.

Oggi **tutti** i nodi sono `Provisional`. La struttura della catena è reale — ogni
sblocco SP richiede davvero il precedente — ma i livelli, i materiali e l'oro non
sono stati controllati da nessuno contro il gioco. `ProgressionEngineV2.UnverifiedQuestIds`
elenca esattamente cosa resta da verificare.

Questa è una limitazione dichiarata, non un difetto nascosto. Presentare numeri
plausibili come verificati sarebbe stato peggio che dichiararli provvisori.

## La catena SP1–SP8

L'enum `SpecialistCardType` dichiarava otto carte, ma il DAG conteneva nodi solo
per **SP1 e SP2**: il pianificatore non poteva arrivare oltre SP2 mentre il tipo
diceva il contrario, e nulla lo segnalava.

Ora la catena è completa, ogni sblocco richiede il precedente, e requisiti e costi
crescono monotonicamente. `PlannableSpecialistCards` espone cosa il DAG sa
davvero raggiungere, perché un enum con otto valori non è prova che otto siano
raggiungibili.

## Invarianti garantite dai test

`--gate4-test` (11 controlli):

- i prerequisiti del DAG sono rispettati: senza le missioni precedenti, uno sblocco
  SP non è disponibile per quanto alto sia il livello;
- l'aggiornamento bayesiano converge e la varianza si riduce;
- UCB1 seleziona una strategia fra i candidati;
- la scala del ciclo di vita è rispettata: `Verified` prima di `Mastered`, e
  `Mastered` solo con l'evidenza che lo sostiene;
- fallimenti ripetuti portano a `Deprecated` invece di lasciare la strategia
  selezionabile;
- registrare evidenza per una strategia sconosciuta è rifiutato, non crea la
  strategia in silenzio;
- ogni carta dichiarata nell'enum è raggiungibile dal DAG;
- gli sblocchi SP formano una catena unica e ordinata, con requisiti crescenti;
- i dati non verificati sono dichiarati tali;
- la valutazione è deterministica e pura.

## Cosa Gate 4 **non** fa

- **Nessun dato di gioco è verificato.** Vedi sopra: la struttura è modellata, i
  numeri no.
- **Non è collegato allo stato reale del client.** Il motore pianifica su un
  `CharacterProgressionProfile` che nulla popola ancora dal client NosTale.
- **Non esegue.** Gate 4 propone; Gate 3 autorizza, esegue e verifica.

## Esecuzione

```bash
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --gate4-test
```
