# NosAi — Dashboard Live Data & Practical Test Contract

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE PROJECT REQUIREMENT

## 1. Obiettivo

La Windows `.exe` di NosAi è la Dashboard e il Control Panel ufficiale dell'AI. Deve mostrare dati **reali e correnti** provenienti dal runtime e dalle sorgenti di osservazione autorizzate, senza sostituirli con valori simulati quando il gioco è collegato.

La Dashboard deve inoltre essere il punto operativo per eseguire e documentare i test pratici sul client NosTale nel laboratorio privato.

La UI estetica può essere progettata in seguito; questi contratti vengono prima della grafica.

## 2. Regola Live Data

Quando il client reale è collegato:

`Client/PC -> Observation Sources -> Sensor Fusion -> WorldState -> Runtime -> Dashboard`

La Dashboard non può creare gameplay truth.

Ogni dato visualizzato deve avere, quando applicabile:

- `Value`;
- `Source` (`Network`, `Memory`, `Screen`, `Local`, `Operator`, `Unknown`);
- `ObservedAtUtc`;
- `AgeMs`;
- `Confidence`;
- `WorldStateVersion`;
- stato `Observed | Derived | Predicted | Cached | Unknown`.

Se una sorgente manca, il valore è `UNKNOWN`, `unavailable` o `not connected`. Mai `0`, mai un valore ricordato, mai un valore inventato.

## 3. Frequenza e freschezza

La Dashboard non deve essere il collo di bottiglia del runtime.

- Il runtime mantiene il proprio loop di osservazione e decisione indipendentemente dalla UI.
- La Dashboard deve ricevere/leggere snapshot aggiornati senza bloccare il runtime.
- Target iniziale UI: aggiornamento percepibile entro **250 ms** quando il runtime produce dati freschi.
- I dati ad alta frequenza (frame/telemetria) possono essere campionati per la UI, mantenendo nel runtime la frequenza necessaria all'AI.
- Ogni pannello deve mostrare la freschezza del dato.
- Se `AgeMs` supera la soglia del relativo dato, il pannello deve diventare `STALE` e non presentarlo come live.

## 4. Pannelli minimi obbligatori

### A. Runtime / Session

- stato runtime;
- connessione client;
- sessione;
- versione wire/runtime;
- uptime;
- ultimo errore;
- Guard / Trust / Safety;
- Watchdog / Recovery.

### B. Hardware

- CPU utilizzo e temperatura quando disponibile;
- RAM;
- GPU utilizzo;
- VRAM;
- temperatura GPU quando disponibile;
- potenza quando disponibile;
- SSD/storage;
- profilo risorse e tier inferenza.

### C. Perception / Eye AI

- frame reale del client quando disponibile;
- ROI;
- detections;
- OCR;
- entità;
- HP/MP;
- coordinate;
- sorgente e confidenza;
- stato di freschezza.

### D. World Model

- posizione del personaggio;
- mappa/area;
- entità osservate;
- target;
- combattimento;
- quest state osservato/derivato;
- inventario/equipaggiamento quando osservabili;
- provenance per ogni campo.

### E. AI Decision

- goal corrente;
- candidati;
- ranking;
- piano;
- Guard decision;
- Trust tier;
- Safety result;
- execution intent;
- verification result;
- decision trace strutturato.

Non viene mostrato il chain-of-thought privato del modello.

### F. Network / Memory / Screen / Local

Per ogni fonte:

- stato;
- ultimo campione;
- timestamp;
- latenza/age;
- errori;
- contatori;
- qualità/confidenza;
- motivo di `UNKNOWN` se non utilizzabile.

## 5. Practical Test Center

La Dashboard deve contenere un'area **Test Center**. I test devono essere eseguibili con il client reale, quando il test richiede il client, e devono produrre evidenza persistente.

Ogni test espone:

- ID;
- categoria;
- precondizioni;
- azione richiesta all'operatore, se necessaria;
- comando/procedura;
- timeout;
- osservazioni attese;
- criteri PASS/FAIL/UNKNOWN;
- evidenza raccolta;
- timestamp;
- versione runtime;
- session ID;
- sorgenti utilizzate.

### Pilastro T1 — Attach & Live Observation

Test pratici:

1. rilevamento processo/client;
2. attach autorizzato;
3. acquisizione osservazioni;
4. freschezza snapshot;
5. perdita e ripristino della sorgente;
6. distinzione `UNKNOWN` / `LIVE`.

### Pilastro T2 — Screen / Vision

Test pratici:

1. acquisizione finestra client;
2. ROI HUD;
3. detection di elementi visibili;
4. HP/MP bar;
5. OCR quando l'atlante/modello è disponibile;
6. movimento dell'elemento e tracking;
7. frame stale/drop/recovery.

### Pilastro T3 — Network Observation

Test pratici:

1. cattura del traffico visibile al client;
2. framing;
3. decoder applicabile;
4. correlazione evento -> timestamp;
5. pacchetto sconosciuto senza inferenza privilegiata;
6. perdita/ripristino traffico.

### Pilastro T4 — World Model

Test pratici:

1. posizione osservata;
2. entità osservate;
3. target;
4. stato combattimento;
5. fusione multi-sorgente;
6. conflitto tra sorgenti;
7. decadimento per stale observation.

### Pilastro T5 — Navigation

Test pratici:

1. acquisizione posizione;
2. costruzione/aggiornamento mappa;
3. richiesta destinazione;
4. generazione percorso;
5. verifica avanzamento;
6. ostacolo inatteso;
7. replan;
8. recovery dopo perdita di osservazione.

### Pilastro T6 — Combat

Test pratici:

1. rilevamento ingresso combattimento;
2. target selection;
3. cooldown observation;
4. candidate ranking;
5. Guard/Trust/Safety;
6. azione consentita;
7. verifica risultato;
8. interruzione sicura.

### Pilastro T7 — Quest / Interaction

Test pratici:

1. rilevamento obiettivo osservabile;
2. interazione;
3. cambio di stato;
4. verifica progresso;
5. failure/replan;
6. completamento osservato.

### Pilastro T8 — Character / Inventory / Progression

Test pratici:

1. osservazione statistiche;
2. inventario;
3. equipaggiamento;
4. uso item;
5. progressione;
6. decisione di ottimizzazione;
7. verifica post-azione.

### Pilastro T9 — Autonomous Loop

Test pratici end-to-end:

`Observe -> Fuse -> WorldState -> Predict -> Rank -> Plan -> Guard -> Trust -> Safety -> Execute -> Verify -> Re-observe`

Il test deve registrare l'intera catena e identificare il primo punto di fallimento.

### Pilastro T10 — Resilience / Safety

Test pratici:

1. client chiuso;
2. finestra persa;
3. rete assente;
4. osservazione stale;
5. watchdog;
6. recovery;
7. emergency stop;
8. restart runtime;
9. dashboard chiusa durante l'esecuzione;
10. verifica fail-closed.

## 6. Human-in-the-loop obbligatorio per i test fisici

Quando un test richiede un evento reale nel gioco, la Dashboard deve mostrare chiaramente:

`AZIONE RICHIESTA ALL'OPERATORE`

Esempi:

- muovi il personaggio;
- entra in combattimento;
- seleziona un bersaglio;
- apri una schermata;
- raccogli un oggetto;
- cambia area;
- chiudi/riapri il client;
- provoca una condizione di rete controllata.

L'operatore deve poter confermare `ESEGUITO` oppure `SALTA`. L'evento viene registrato nel test evidence.

La richiesta all'operatore non deve trasformarsi in un bypass di Guard/Trust/Safety.

## 7. Evidence

Ogni test pratico deve poter esportare almeno:

- manifest test;
- snapshot prima/durante/dopo;
- observation metadata;
- decision trace strutturato;
- Guard/Trust/Safety verdict;
- execution result;
- verification result;
- errori e recovery;
- hash-chain/journal reference quando disponibile.

## 8. Separazione Test / Simulazione

La Dashboard deve distinguere esplicitamente:

- `REAL CLIENT TEST` — client reale collegato;
- `DIAGNOSTIC` — runtime senza gameplay;
- `REPLAY` — dati registrati;
- `SIMULATION` — mondo simulato.

Un risultato di simulation/replay non può essere mostrato come prova di funzionamento live.

## 9. Sicurezza

La Dashboard è una superficie di osservazione e controllo operatore. Non è il Safety Gate.

I comandi sensibili seguono sempre:

`Dashboard -> Runtime Control API -> Guard -> Trust -> Safety -> Execute -> Verify`

La chiusura o il crash della Dashboard non può autorizzare né mantenere un'azione vietata.

## 10. Definition of Done

La Dashboard sarà considerata realmente integrata con il gioco solo quando sarà possibile, sul PC di test:

1. avviare l'`.exe`;
2. collegare il client reale;
3. vedere dati reali con timestamp e provenance;
4. vedere `UNKNOWN` quando una fonte non è disponibile;
5. eseguire almeno un test pratico per ciascun pilastro implementato;
6. raccogliere PASS/FAIL/UNKNOWN ed evidenza;
7. interrompere in sicurezza;
8. ripetere il test e confrontare i risultati;
9. distinguere chiaramente test reali da replay/simulazione;
10. produrre un report riproducibile.

**Divieto:** nessun pannello può essere marcato `Verified` solo perché la UI mostra un valore. La verifica richiede evidenza dal runtime e, per i test live, dal client reale.
