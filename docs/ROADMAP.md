# NosAi — Tabella di marcia

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Regola di avanzamento obbligatoria

Lo sviluppo segue **traguardi significativi verificabili**. Non è consentito saltare un traguardo per sviluppare funzionalità successive.

### Primo obiettivo operativo obbligatorio

Prima di proseguire con le implementazioni successive, NosAi deve raggiungere e superare un primo punto operativo minimo:

1. NosAi deve avviarsi sul PC in modo affidabile.
2. NosAi deve collegarsi al client di NosTale attraverso un'integrazione consentita, documentata e controllata.
3. NosAi deve leggere dal client i dati di base necessari e verificabili.
4. NosAi deve rilevare e acquisire i dati di base necessari del PC.
5. Guard AI deve essere avviabile sullo smartphone.
6. Guard AI deve collegarsi in modo autenticato e controllato a NosAi sul PC.
7. Guard AI deve ricevere e rilevare i primi dati di base provenienti da NosAi.
8. Il percorso PC ↔ smartphone deve essere verificato con test reali.
9. La dashboard deve essere operativa al 100% rispetto al livello di sviluppo raggiunto e deve permettere di osservare e controllare correttamente le funzionalità già validate.

Fino al superamento completo di questo primo obiettivo, le implementazioni successive devono essere limitate a ciò che serve per raggiungerlo, correggerlo, testarlo o renderlo affidabile.

## Regola di validazione continua

Ogni obiettivo significativo crea un **gate di validazione**.

Prima di iniziare il successivo devono essere verificati tutti i componenti coinvolti, inclusa la dashboard quando interessata.

Un obiettivo è considerato raggiunto soltanto quando:

- il codice interessato è integrato;
- i test automatici pertinenti hanno esito positivo;
- i test manuali e di integrazione richiesti hanno esito positivo;
- le comunicazioni tra i componenti coinvolti funzionano realmente;
- PC e smartphone sono stati testati quando pertinenti;
- la dashboard funziona al 100% delle funzionalità previste per quel livello;
- non sono presenti regressioni bloccanti nelle funzionalità già validate;
- il comportamento osservato corrisponde alla documentazione.

Un test fallito blocca il passaggio al successivo obiettivo. Il problema deve essere risolto e i test devono essere ripetuti con esito positivo.

## Fase 0 — Fondazione

- [x] Repository dedicato
- [x] Pulizia del repository
- [x] Architettura e regole di migrazione
- [x] Base decisionale deterministica
- [x] Confine Safety Gate
- [x] Protocollo Guard indipendente dal trasporto
- [x] Contratti WorldState / Goal / Action / Decision
- [x] Fondazione World Model
- [x] Fondazioni Party / Pet / Partner
- [x] Fondazione Coordinated Action Manager
- [x] Tactical Ranking + simulazione deterministica
- [x] Contratti/pipeline di Perception
- [x] Adapter Perception → WorldState
- [x] Contratti Agent Runtime, provider, risorse e policy
- [x] Ciclo autonomo multi-step con verifica
- [x] Recovery retry/replan e watchdog indipendente

## Fase 1 — Primo obiettivo operativo: PC ↔ NosTale ↔ smartphone ↔ dashboard

Questa fase ha priorità assoluta. Non si procede alla Fase 2 finché il gate non è superato.

### NosAi sul PC

- [ ] Avvio affidabile di NosAi
- [ ] Rilevamento e acquisizione dei dati di base del PC
- [ ] Collegamento al client di NosTale tramite adapter/integratore documentato
- [ ] Lettura dei dati di base necessari dal client
- [ ] Validazione della correttezza e provenienza dei dati
- [ ] Gestione controllata di client non disponibile, dati incompleti e disconnessione

### Guard AI sullo smartphone

- [ ] Avvio affidabile di Guard AI
- [ ] Configurazione del collegamento verso NosAi sul PC
- [ ] Sessione PC ↔ smartphone autenticata
- [ ] Scambio HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Ricezione dei primi dati di base da NosAi
- [ ] Verifica di provenienza, integrità e freschezza
- [ ] Gestione controllata di disconnessione e riconnessione

### Dashboard

- [ ] Avvio affidabile
- [ ] Collegamento al runtime corretto
- [ ] Visualizzazione dei dati effettivamente disponibili
- [ ] Stato PC, NosAi e Guard AI coerente con il runtime
- [ ] Controlli disponibili solo quando realmente implementati e autorizzati
- [ ] Gestione corretta degli stati di errore/disconnessione
- [ ] Test completo di tutte le funzioni previste per il livello corrente

### Gate obbligatorio della Fase 1

- [ ] Test PC completati con esito positivo
- [ ] Test smartphone completati con esito positivo
- [ ] Test PC ↔ smartphone completati con esito positivo
- [ ] Test NosAi ↔ client NosTale completati con esito positivo
- [ ] Test dashboard completati con esito positivo
- [ ] Test di errore, disconnessione e riconnessione completati con esito positivo
- [ ] Nessuna regressione bloccante
- [ ] Documentazione aggiornata e coerente

**Solo dopo il superamento completo del gate è consentito procedere.**

## Fase 2 — Guard e decisione sicura

- [x] Fondazione Guard AI
- [x] Confine policy Trust Tier 1–4
- [x] Integrazione Guard/Safety nel ciclo autonomo
- [x] Registro provider e policy local-first
- [x] Fondazione trace di valutazione runtime
- [ ] Guard AI produttivo PC/telefono
- [ ] Propagazione produttiva dello stato watchdog/recovery
- [ ] Integrazione telemetria
- [ ] Dashboard corrispondente completamente operativa
- [ ] Gate di test completo della fase

## Fase 3 — Percezione e memoria produttive

- [x] Fondazione visione ROI
- [x] Fondazione tracking temporale
- [x] Fondazione Game State Evaluator
- [ ] DXGI Direct Capture
- [ ] Triple Buffer lock-free
- [ ] Detector YOLO produttivo
- [ ] OCR glyph-hash + fallback/cache AI-OCR
- [ ] Tracking Kalman 2D produttivo
- [ ] Valutatore semantico specifico del gioco
- [ ] Memoria SQLite completa
- [ ] Telemetria sincronizzata PTS
- [ ] Rilevamento anomalie e recupero deterministico
- [ ] Dashboard aggiornata e completamente operativa per il livello raggiunto
- [ ] Gate di test completo della fase

## Fase 4 — Confine gioco

- [ ] Probe di sola lettura del client
- [ ] Adapter di azione basato sulla simulazione
- [ ] Adapter live controllato dietro Guard/Safety
- [ ] Dashboard aggiornata e verificata
- [ ] Gate di test completo della fase

## Fase 5 — Strategia e provider AI

- [ ] Progression Engine V2
- [ ] MAUT / UCB1 / HTN-MCTS
- [ ] Aggiornamenti evidenza Beta-Binomial
- [ ] Ciclo di vita strategie e persistenza mastery
- [ ] Knowledge Base
- [ ] Provider locale `llama.cpp`
- [ ] Provider cloud con escalation controllata dalla policy
- [ ] Benchmark hardware e profili runtime automatici
- [ ] Dashboard aggiornata e verificata
- [ ] Gate di test completo della fase

## Fase 6 — Integrazione e rilascio

- [x] CI con test/compilazione Python e build del runtime C#
- [ ] Test end-to-end deterministici
- [ ] Test di integrazione runtime
- [ ] Gate benchmark hardware
- [ ] Test completo PC ↔ smartphone
- [ ] Test completo dashboard
- [ ] Revisione di prontezza al rilascio

## Punti che richiedono implementazione o validazione esterna

Restano espliciti e non vengono trasformati silenziosamente in dichiarazioni di implementazione:

- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione specifica del client di gioco`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: compatibilità/ricerca anti-cheat`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione pacchetti/rete`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: bypass/injection specifici del client`

Il progetto non implementa bypass, evasione anti-cheat, manipolazione pacchetti o injection del client come parte dell'avvio minimo.

## Repository legacy

`volobolo99/NosAi` rimane esclusivamente un riferimento. Un componente è considerato migrato solo dopo revisione architetturale, reimplementazione selettiva e test.
