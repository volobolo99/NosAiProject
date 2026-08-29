# NosAi — Requisiti

## 1. Regola di avanzamento obbligatoria

Lo sviluppo deve procedere per traguardi significativi verificabili.

Prima di sviluppare funzionalità successive, NosAi deve raggiungere il primo traguardo operativo minimo:

1. NosAi avviabile sul PC.
2. Collegamento al client di NosTale tramite un'integrazione consentita e documentata.
3. Lettura verificata dei dati di base necessari dal client.
4. Acquisizione verificata dei dati di base necessari del PC.
5. Guard AI avviabile sullo smartphone.
6. Collegamento autenticato e controllato tra Guard AI e NosAi sul PC.
7. Ricezione e rilevamento dei primi dati di base da parte di Guard AI.

Il traguardo non è considerato completato finché i test PC, smartphone, collegamento PC ↔ smartphone e collegamento NosAi ↔ client non hanno dato esito positivo.

Ogni obiettivo significativo successivo richiede un proprio ciclo di integrazione, test, verifica completa e aggiornamento della documentazione. Un test fallito blocca l'avanzamento fino alla correzione e alla ripetizione dei test con esito positivo.

## 2. Requisiti funzionali

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

### RF-13 — Integrazione client
NosAi deve disporre di un punto di integrazione controllato per acquisire dal client di NosTale i dati di base necessari, con gestione esplicita di indisponibilità, dati incompleti ed errori.

### RF-14 — Dati del PC
NosAi deve poter rilevare i dati di base del PC necessari al funzionamento, al monitoraggio e alla gestione delle risorse.

### RF-15 — Guard AI smartphone
Guard AI deve poter essere avviato sullo smartphone, autenticarsi con NosAi sul PC e ricevere i dati di base previsti dal contratto di comunicazione.

## 3. Requisiti non funzionali

- Determinismo sul percorso critico.
- Testabilità senza client di gioco reale per i componenti che non richiedono integrazione live.
- Separazione dei componenti tramite contratti.
- Provenienza delle informazioni.
- Configurazione esplicita dei limiti runtime.
- Recupero osservabile e verificabile.
- Nessuna esecuzione diretta da parte dei provider decisionali.
- Integrazione live separata dalla base di test.
- Gestione esplicita degli errori e delle disconnessioni.
- Validazione obbligatoria a ogni traguardo significativo.

## 4. Requisiti produttivi futuri

- EventBus persistente e replay deterministico.
- Persistenza SQLite estesa.
- Trasporto LAN autenticato completo.
- Pipeline di percezione produttiva.
- Adapter di gioco validato.
- Provider `llama.cpp` locale.
- Benchmark hardware reali.
- Valutazione predizione-vs-realtà.
- Integrazione produttiva PC ↔ smartphone.

## 5. Criterio di completamento

Un requisito non deve essere marcato come completato sulla sola base dell'esistenza del relativo codice. Deve essere verificato attraverso i test pertinenti e, quando richiesto, attraverso un test di integrazione reale.

La documentazione deve riflettere esclusivamente lo stato realmente verificato del progetto.
