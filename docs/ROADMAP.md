# NosAi — Tabella di marcia

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Regola di avanzamento e traguardi obbligatori

Lo sviluppo segue una sequenza vincolante basata su **traguardi significativi verificabili**. Non è consentito saltare un traguardo per procedere con funzionalità successive.

### Primo traguardo operativo obbligatorio

Prima di procedere con le successive implementazioni di NosAi, il progetto deve raggiungere e verificare il seguente punto minimo operativo:

1. NosAi deve poter essere avviato sul PC.
2. NosAi deve potersi collegare al client di NosTale tramite un'integrazione consentita e documentata.
3. NosAi deve poter leggere i dati di base necessari dal client senza dichiarare disponibili dati non ancora verificati.
4. NosAi deve poter rilevare e acquisire i dati di base necessari del PC.
5. Guard AI deve poter essere avviato sullo smartphone.
6. Guard AI deve potersi collegare in modo autenticato e controllato a NosAi sul PC.
7. Guard AI deve poter ricevere e rilevare i primi dati di base necessari provenienti da NosAi.
8. L'intero percorso PC ↔ smartphone deve essere verificato con test reali prima di considerare raggiunto il traguardo.

Finché questo traguardo non è stato raggiunto e superato con esito positivo, le implementazioni successive devono essere limitate a ciò che è necessario per raggiungerlo, correggerlo, testarlo o renderlo affidabile.

## Regola di validazione continua

Ogni volta che viene raggiunto un obiettivo significativo, prima di iniziare il successivo devono essere eseguiti i test pertinenti e deve essere verificato il funzionamento completo delle implementazioni coinvolte.

Un traguardo è considerato superato soltanto quando:

- il codice interessato è integrato;
- i test automatici pertinenti hanno esito positivo;
- i test manuali o di integrazione richiesti hanno esito positivo;
- le comunicazioni tra componenti coinvolte funzionano realmente;
- non sono presenti regressioni bloccanti nelle funzionalità già validate;
- il risultato osservato corrisponde a quanto dichiarato dalla documentazione.

Un test fallito blocca l'avanzamento del traguardo successivo finché il problema non è stato risolto e i test non sono stati ripetuti con esito positivo.

## Fase 0 — Fondazione pulita

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

## Fase 1 — Primo traguardo operativo: PC ↔ client ↔ smartphone

Questa fase ha priorità assoluta rispetto alle funzionalità successive.

### NosAi sul PC

- [ ] Avvio affidabile di NosAi
- [ ] Rilevamento e acquisizione dei dati di base del PC
- [ ] Collegamento al client di NosTale tramite adapter/integratore documentato
- [ ] Lettura dei dati di base necessari dal client
- [ ] Validazione della correttezza e provenienza dei dati letti
- [ ] Gestione controllata di client non disponibile, dati incompleti e disconnessione

### Guard AI sullo smartphone

- [ ] Avvio affidabile di Guard AI
- [ ] Individuazione/configurazione del collegamento verso NosAi sul PC
- [ ] Sessione PC ↔ smartphone autenticata
- [ ] Scambio HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Ricezione dei primi dati di base da NosAi
- [ ] Verifica della provenienza, integrità e freschezza dei dati ricevuti
- [ ] Gestione controllata di disconnessione e riconnessione

### Gate obbligatorio della Fase 1

- [ ] Test PC completati con esito positivo
- [ ] Test smartphone completati con esito positivo
- [ ] Test PC ↔ smartphone completati con esito positivo
- [ ] Test NosAi ↔ client di NosTale completati con esito positivo
- [ ] Test di errore e riconnessione completati con esito positivo
- [ ] Nessuna regressione bloccante
- [ ] Documentazione aggiornata e coerente

**Solo dopo il superamento completo di questo gate è consentito procedere alle fasi successive.**

## Fase 2 — Guard e decisione sicura

- [x] Fondazione Guard AI
- [x] Confine policy Trust Tier 1–4
- [x] Integrazione Guard/Safety nel ciclo autonomo
- [x] Registro provider e policy local-first
- [x] Fondazione trace di valutazione runtime
- [ ] Guard AI produttivo PC/telefono
- [ ] Propagazione produttiva dello stato watchdog/recovery
- [ ] Integrazione telemetria
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
- [ ] Valutatore semantico completo specifico del gioco
- [ ] Memoria SQLite
- [ ] Telemetria sincronizzata PTS
- [ ] Rilevamento anomalie e recupero deterministico
- [ ] Gate di test completo della fase

## Fase 4 — Confine gioco

- [ ] Probe di sola lettura del client
- [ ] Adapter di azione basato sulla simulazione
- [ ] Adapter live controllato dietro Guard/Safety
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
- [ ] Gate di test completo della fase

## Fase 6 — Gate di integrazione e rilascio

- [x] CI con test/compilazione Python e build del runtime C#
- [ ] Test end-to-end deterministici
- [ ] Test di integrazione runtime
- [ ] Gate benchmark hardware
- [ ] Test completo PC ↔ smartphone
- [ ] Revisione di prontezza al rilascio

## Punti di implementazione esterna

Restano espliciti e non vengono trasformati silenziosamente in dichiarazioni di implementazione:

- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione specifica del client di gioco`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: compatibilità/ricerca anti-cheat`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione pacchetti/rete`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: bypass/injection specifici del client`

Il progetto pulito non implementa bypass, evasione anti-cheat, manipolazione pacchetti o injection del client come parte dell'avvio minimo.

## Repository legacy

`volobolo99/NosAi` rimane esclusivamente un riferimento. Un componente è considerato migrato solo dopo revisione architetturale, reimplementazione selettiva e test.
