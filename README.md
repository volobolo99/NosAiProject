# NosAiProject

Implementazione sorgente di **NosAi**, runtime di intelligenza artificiale per NosTale.

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane **1.0 Beta** finché il creatore non richiede esplicitamente un cambiamento.

## Stato del progetto

Il repository è la sorgente di sviluppo ufficiale. Il repository legacy `volobolo99/NosAi` è utilizzato esclusivamente come riferimento: il codice viene analizzato e reimplementato selettivamente, senza copia indiscriminata.

Il runtime realizza un ciclo autonomo controllato: osservazione → orchestrazione → autorizzazione → esecuzione → verifica → nuova osservazione → recupero e ripianificazione adattivi.

Sono presenti EventBus tipizzato e bounded, WorldState versionato, riduzione del contesto per VRAM, RecoveryController adattivo, circuit breaker, watchdog hardware/runtime, nucleo di cifratura per sessioni effimere, logger SQLite per sessioni/traiettorie e controller Miniland tramite adapter.

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
                  retry/replan/degraded   modalità/cooling
                            └───────┬──────────┘
                                    ↓
                               Ri-osservazione
                                    └──→ WorldState(vN+1)
```

WorldState è la fonte canonica dello stato corrente. EventBus e trace registrano provenienza, decisioni, controlli di sicurezza, risultati, recuperi e valutazioni.

## Componenti aggiunti

- `nosai/runtime/context_slimming.py` — compressione dello storico diagnostico orientata alla VRAM.
- `nosai/runtime/hardware_watchdog.py` — watchdog termico e I/O con Cooling Phase.
- `nosai/security/ephemeral_session.py` — nucleo X25519 + HKDF-SHA256 + ChaCha20-Poly1305 per sessioni effimere.
- `proto/nosai_network_v1.proto` — contratto Protobuf v3 per i flussi di rete/UI ad alta frequenza.
- `nosai/persistence/sqlite_logger.py` — persistenza locale di sessioni e traiettorie tramite SQLite/WAL.
- `nosai/miniland/automation.py` — controller Miniland e automazione pesca tramite `MinilandAdapter`.

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
- SQLite registra dati analitici locali senza diventare automaticamente la fonte canonica del WorldState.
- Miniland utilizza un adapter esplicito per separare il controller dall'I/O del client.
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
- `docs/CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md` — trasporto sicuro e chiavi effimere.
- `docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md` — persistenza e fondazioni Shared Memory.
- `docs/GLOSSARIO.md` — terminologia ufficiale.
- `docs/CHANGELOG.md` — storico delle modifiche.

## Principi

1. Sicurezza e autorizzazione esplicita.
2. Percorso critico deterministico e verificabile.
3. WorldState come fonte canonica dello stato corrente.
4. Separazione tra decisione ed esecuzione.
5. Recupero adattivo e verifica a ciclo chiuso.
6. Osservabilità e provenienza dei dati.
7. Persistenza separata dallo stato operativo canonico.
8. Adapter espliciti per le integrazioni esterne.
9. Testabilità senza client di gioco reale.
10. Integrazioni live dietro traguardi espliciti.
