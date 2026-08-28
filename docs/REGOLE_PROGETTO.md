# NosAi — Regole del progetto

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## 1. Governance della versione

La versione corrente è **1.0 Beta**. Nessuna implementazione, rifattorizzazione, documentazione o automazione può modificarla senza richiesta esplicita del creatore.

## 2. Separazione delle responsabilità

- Percezione osserva e produce dati semantici.
- World Model possiede lo stato semantico canonico.
- Party, Pet e Partner sono modellati separatamente.
- Coordinated Action Manager propone azioni coordinate.
- Tactical Ranking ordina i candidati.
- Orchestrator coordina i moduli.
- Guard valuta il contesto operativo.
- Safety costituisce il confine finale per le azioni protette.
- Game/client I/O è isolato dietro adapter espliciti.
- I provider LLM forniscono dati decisionali e non eseguono direttamente il sistema.

## 3. Percorso deterministico

Il percorso critico deve poter essere testato senza client di gioco reale. La simulazione e il lookahead sono i principali strumenti di validazione prima dell'esecuzione live.

## 4. Recovery e Watchdog

RecoveryController e Watchdog sono componenti attivi del controllo runtime.

Recovery può cambiare strategia, ripianificare, effettuare retry, usare modalità degradate, attivare Cooling e riprendere l'esecuzione secondo policy e condizioni osservate.

Watchdog può adattare modalità runtime e budget operativi sulla base delle condizioni del runtime e dell'hardware.

La precedente regola che limitava Recovery e Watchdog esclusivamente a riduzione o blocco dell'esecuzione non fa più parte delle regole del progetto.

Le azioni protette devono comunque rispettare i confini di autorizzazione configurati.

## 5. Percezione

Pipeline produttiva prevista: DXGI Direct Capture, Triple Buffer lock-free, HSV multi-ROI, YOLO, OCR glyph-hash con fallback/cache AI-OCR, Kalman 2D temporale e Game State Evaluator.

Le fondamenta presenti non devono essere descritte come backend produttivi finché non sono realmente validati.

## 6. Rete

Il bring-up iniziale utilizza comunicazione locale/LAN autenticata, messaggi tipizzati, heartbeat, stato e protezione da replay.

## 7. Persistenza e apprendimento

Le conoscenze validate devono mantenere evidenza e provenienza. Un singolo risultato fallito o non verificato non deve diventare automaticamente conoscenza verificata.

## 8. Repository legacy

`volobolo99/NosAi` è esclusivamente riferimento. Il codice deve essere analizzato e reimplementato selettivamente.

## 9. Integrità documentale

La documentazione deve distinguere chiaramente tra implementato, fondazione e pianificato. Non deve dichiarare produttivo un componente non validato.

## 10. Lingua

La documentazione del progetto deve essere scritta in italiano. Codice, identificatori, API, protocolli, nomi tecnici obbligatori e contenuti che richiedono la lingua originale possono rimanere in inglese.
