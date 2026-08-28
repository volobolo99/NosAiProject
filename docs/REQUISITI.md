# NosAi — Requisiti

## 1. Requisiti funzionali

### RF-01 — Osservazione
Il sistema deve acquisire osservazioni e trasformarle in dati semantici.

### RF-02 — Stato canonico
Il sistema deve mantenere uno `WorldState` versionato con provenienza e confidenza.

### RF-03 — Simulazione
Il sistema deve poter simulare candidati e produrre risultati predittivi senza eseguire l'azione reale.

### RF-04 — Ranking
Il sistema deve ordinare i candidati secondo punteggio, confidenza, rischio, ricompensa attesa ed evidenza quando disponibili.

### RF-05 — Pianificazione
L'Orchestrator e il Planner devono trasformare i risultati di dominio in piani runtime limitati.

### RF-06 — Autorizzazione
Le azioni protette devono attraversare i componenti di autorizzazione configurati prima dell'esecuzione.

### RF-07 — Esecuzione
L'esecuzione deve avvenire esclusivamente tramite l'Executor/Game Adapter.

### RF-08 — Verifica
Ogni azione deve poter essere verificata confrontando risultato e nuova osservazione.

### RF-09 — Recupero
Il sistema deve supportare retry, replan, modalità degradata e Cooling secondo policy e condizioni osservate.

### RF-10 — Watchdog
Il sistema deve monitorare condizioni runtime e hardware e poter cambiare modalità operativa.

### RF-11 — Osservabilità
Il sistema deve produrre eventi e trace correlabili per sessione, esecuzione e attività.

### RF-12 — Provider
Il sistema deve poter selezionare provider decisionali sulla base di policy e risorse disponibili.

## 2. Requisiti non funzionali

- Determinismo sul percorso critico.
- Testabilità senza client di gioco reale.
- Separazione dei componenti tramite contratti.
- Provenienza delle informazioni.
- Configurazione esplicita dei limiti runtime.
- Recupero osservabile e verificabile.
- Nessuna esecuzione diretta da parte dei provider decisionali.
- Integrazione live separata dalla base di test.

## 3. Requisiti produttivi futuri

- EventBus persistente e replay deterministico.
- Persistence SQLite.
- Trasporto LAN autenticato.
- Pipeline di percezione produttiva.
- Adapter di gioco validato.
- Provider `llama.cpp` locale.
- Benchmark hardware reali.
- Valutazione predizione-vs-realtà.
