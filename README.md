# NosAiProject

Implementazione sorgente di **NosAi**, runtime di intelligenza artificiale per NosTale.

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane **1.0 Beta** finché il creatore non richiede esplicitamente un cambiamento.

## Stato del progetto

Il repository è la sorgente di sviluppo ufficiale. Il repository legacy `volobolo99/NosAi` è utilizzato esclusivamente come riferimento: il codice viene analizzato e reimplementato selettivamente, senza copia indiscriminata.

Il runtime realizza un ciclo autonomo controllato: osservazione → orchestrazione → Guard/Safety → esecuzione → verifica → nuova osservazione → recupero e ripianificazione adattivi.

Sono presenti inoltre EventBus tipizzato, WorldState versionato, riduzione del contesto per VRAM, RecoveryController adattivo e watchdog hardware/runtime.

## Architettura

```text
Sessione / Scheduler / Risorse / Policy / Provider
                         │
                    Event / Trace Bus
                         │
Percezione → WorldState(vN) → Simulazione → Ranking Tattico
                         │                    │
                         └── Party/Pet/Partner ┘
                                      │
                                  Orchestrator
                                      │
                                Agent Planner
                                      │
                              Guard / Trust / Safety
                                      │
                             Play AI / Executor
                                      │
                                  Verificatore
                                      │
                         RecoveryController adattivo
                            │                  │
                     Context Slimming       Watchdog
                            │                  │
                    retry/replan       modalità/cooling
                            └───────┬──────────┘
                                    ↓
                               Ri-osservazione
                                    └──→ WorldState(vN+1)
```

WorldState è la fonte canonica dello stato corrente. EventBus e trace registrano provenienza, decisioni, controlli di sicurezza, risultati, recuperi e valutazioni.

## Modello di comunicazione

- La Percezione comunica con il World Model tramite `PerceptionWorldAdapter`.
- Il World Model fornisce snapshot immutabili alla Simulazione.
- La Simulazione produce risultati per il Tactical Ranking.
- Il Tactical Ranking produce candidati ordinati per l'Orchestrator.
- L'Orchestrator costruisce piani runtime limitati.
- I Decision Provider producono dati decisionali e non eseguono direttamente strumenti o I/O.
- Guard, Trust e Safety costituiscono il percorso di autorizzazione delle azioni protette.
- Executor/Game Adapter costituisce il confine di esecuzione.
- Il Verifier confronta risultato e nuova osservazione.
- RecoveryController può adattare strategia, ripianificare, cambiare modalità runtime, entrare in modalità degradata/cooling e riprendere l'esecuzione secondo policy e condizioni osservate.
- Watchdog può cambiare modalità runtime e applicare limiti operativi in funzione di condizioni runtime e hardware.
- EventBus è osservazionale e non genera da solo effetti di esecuzione.

## Documentazione

- `docs/METADATI_PROGETTO.md` — metadati ufficiali.
- `docs/REGOLE_PROGETTO.md` — regole e vincoli del progetto.
- `docs/ARCHITETTURA.md` — architettura e comunicazioni.
- `docs/STATO_IMPLEMENTAZIONE.md` — registro dell'implementazione.
- `docs/ROADMAP.md` — roadmap e traguardi.
- `docs/REQUISITI.md` — requisiti funzionali e non funzionali.
- `docs/CONTRIBUTING.md` — regole per contribuire.
- `docs/TESTING.md` — strategia e procedure di test.
- `docs/SICUREZZA.md` — modello di sicurezza.
- `docs/DEPLOYMENT.md` — installazione, configurazione e avvio.
- `docs/OSSERVABILITA.md` — EventBus, trace, audit e replay.
- `docs/RECOVERY_WATCHDOG.md` — recupero adattivo e controllo hardware/runtime.
- `docs/PERCEZIONE.md` — pipeline di percezione e stato di implementazione.
- `docs/RETE_LAN.md` — comunicazione locale/LAN.
- `docs/LLM_PROVIDER.md` — provider decisionali e instradamento.
- `docs/CONTRATTI.md` — contratti tra componenti.
- `docs/GLOSSARIO.md` — terminologia ufficiale.
- `docs/CHANGELOG.md` — storico delle modifiche.

## Principi

1. Sicurezza e autorizzazione esplicita.
2. Percorso critico deterministico e verificabile.
3. WorldState come fonte canonica dello stato corrente.
4. Separazione tra decisione ed esecuzione.
5. Recupero adattivo e verifica a ciclo chiuso.
6. Osservabilità e provenienza dei dati.
7. Testabilità senza client di gioco reale.
8. Integrazioni live dietro traguardi espliciti.
