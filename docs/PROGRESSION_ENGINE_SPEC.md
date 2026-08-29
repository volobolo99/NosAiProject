# NosAi — Specifica del motore di progressione

## Stato

Base di progettazione — Passo 3B.2.

## Scopo

Il Motore di Progressione è il pianificatore strategico responsabile di massimizzare la progressione del personaggio verso un obiettivo esplicito, riducendo al minimo tempo, spreco di risorse, probabilità di fallimento e rischio non necessario.

Non controlla direttamente il gioco. Produce piani e candidati di obiettivi/azioni valutati per Play AI, mentre Guard AI valuta strategia, rischio, vincoli e requisiti di protezione.

## Principio di progettazione

NosAi deve comportarsi come un giocatore altamente ottimizzato: osserva lo stato corrente, prevede gli esiti probabili, valuta le alternative, seleziona il percorso di progressione con il miglior risultato atteso, esegue tramite Play AI, misura il risultato e conserva la conoscenza verificata per il riutilizzo.

## Obiettivo di ottimizzazione

Il pianificatore valuta i percorsi candidati mediante una funzione di utilità configurabile basata su:

- progressione verso l'obiettivo corrente;
- tempo previsto per il completamento;
- consumo previsto di risorse e perdita di oggetti;
- probabilità di successo o fallimento;
- rischio di morte o stato non recuperabile;
- valore futuro, inclusi sbloccabili, equipaggiamento, materiali, missioni e dipendenze di progressione;
- confidenza nella conoscenza strategica sottostante.

Nessuna singola metrica, come XP/ora, è sufficiente.

## Flusso runtime

```text
Osservazione del gioco
      ↓
WorldState normalizzato
      ↓
Obiettivo + Profilo personaggio + Strategie note
      ↓
Generazione candidati
      ↓
Previsione / valutazione esiti
      ↓
Valutazione rischio e vincoli di Guard AI
      ↓
Miglior piano / proposta della prossima azione
      ↓
Esecuzione tramite Play AI
      ↓
Risultato osservato
      ↓
Valutazione + telemetria
      ↓
Aggiornamento Knowledge Base
```

## Concetti fondamentali

### Obiettivo

Un obiettivo contiene risultato desiderato, priorità, vincoli, scadenza o preferenza temporale e limiti accettabili di rischio e risorse.

Esempi: avanzamento di livello, completamento di una catena di missioni, completamento di un dungeon, ottenimento di un oggetto, miglioramento dell'equipaggiamento o raggiungimento di una configurazione obiettivo.

### Percorso candidato

Un percorso candidato è una sequenza o un orizzonte breve di attività possibili. Ogni candidato contiene durata prevista, probabilità di successo, costo delle risorse, rischio, valore di progressione atteso e confidenza.

### Strategia

Una strategia è conoscenza riutilizzabile che descrive come un determinato profilo di personaggio dovrebbe affrontare uno specifico contesto. Le chiavi della strategia dovrebbero includere, quando disponibili:

- classe o categoria del personaggio;
- intervallo di livello;
- profilo di configurazione/equipaggiamento;
- contenuto o attività;
- obiettivo;
- condizioni rilevanti del gruppo o del contesto.

Le strategie sono versionate e devono conservare evidenza e statistiche di validazione.

## Ciclo di vita della conoscenza

```text
Osservata / proposta
        ↓
Strategia sperimentale
        ↓ evidenza sufficiente
Strategia validata
        ↓ risultati superiori ripetuti
Strategia preferita
        ↓ regressione rilevata
Declassata / rivalutata
```

Il sistema non deve sovrascrivere conoscenza validata solo perché una singola esecuzione nuova ha prodotto un risultato migliore o peggiore. Sono necessarie evidenza statistica e riproducibilità.

## Trasferimento a nuovi personaggi

La conoscenza viene condivisa a livello di strategia e non legata permanentemente a un singolo personaggio. Un nuovo personaggio può ereditare strategie validate applicabili e personalizzarle usando configurazione, equipaggiamento, risorse e prestazioni osservate proprie.

## Punteggio di padronanza

Il sistema espone un **Punteggio di Padronanza** da 0 a 100 che rappresenta quanto il comportamento corrente si avvicina alla migliore strategia validata o al riferimento per il contesto valutato.

La padronanza è contestuale e non soltanto globale. Il modello dati deve supportare almeno:

- padronanza globale;
- padronanza per classe/categoria;
- padronanza per intervallo di livello;
- padronanza per attività/contenuto;
- padronanza specifica per obiettivo.

Il punteggio deve essere basato sull'evidenza e contenere metadati sufficienti a spiegare perché è cambiato. Non rappresenta una dichiarazione di perfezione assoluta nel gioco.

## Confine Guard AI

Guard AI non è una seconda Play AI. È il livello strategico di protezione e valutazione. Può rifiutare, limitare, declassare o richiedere una rivalutazione di un piano/azione quando vengono violati rischio, incertezza, limiti delle risorse o vincoli di sicurezza.

## Confine Play AI

Play AI è l'agente orientato all'esecuzione. Riceve obiettivi, piani e azioni approvati ed è responsabile della loro esecuzione tramite gli adapter disponibili del gioco. Riporta osservazioni ed esiti alla catena di pianificazione.

## Contratti richiesti

L'implementazione deve esporre contratti espliciti per:

- `Goal`
- `CharacterProfile`
- `ProgressionState`
- `CandidatePath`
- `StrategyRecord`
- `Prediction`
- `RiskAssessment`
- `MasterySnapshot`
- `ExecutionResult`

Questi contratti devono rimanere indipendenti dal trasporto ed essere testabili senza un client di gioco.

## Requisito di priorità al determinismo

La prima implementazione deve funzionare con dati WorldState sintetici o simulati. Un adapter del client di gioco è una sorgente di input e non un prerequisito per testare il pianificatore.

## Esclusioni di questo passo

- implementazione dell'input diretto al gioco;
- manipolazione di pacchetti;
- elusione dell'anti-cheat;
- aggiramento del client;
- pipeline produttiva di visione;
- ottimizzazione finale dell'LLM.

Questi elementi rimangono confini di integrazione espliciti dove necessari e non devono essere rimossi silenziosamente dall'architettura.
