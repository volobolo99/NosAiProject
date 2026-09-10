# Architettura

## Documenti canonici
Il documento canonico degli invarianti architetturali è `docs/NOSAI_ARCHITECTURE_BASELINE.md`. Una descrizione architetturale estesa in italiano si trova in `docs/ARCHITETTURA.md`. Le decisioni architetturali accettate sono riportate nei file numerati della directory `docs/adr/`; il conteggio corrente è nell’inventario del repository.

## Catena di esecuzione
1. Observe
2. Sensor Fusion
3. World Model
4. Simulation/Prediction
5. Ranking
6. Orchestrator
7. Planner
8. Guard
9. Trust
10. Safety
11. Execute
12. Verify
13. Re-observe

## Progetti del codice
| Progetto             | File               | Responsabilità                                                                 |
|----------------------|--------------------|--------------------------------------------------------------------------------|
| NosAi.Core           | 4 file             | Contratti di pipeline, nessuna dipendenza                                          |
| NosAi.Protocol       | 4 file             | Protocollo di trasporto con frame binario a 12 byte e cifratura di sessione          |
| NosAi.Runtime        | 216 file           | Il cuore del sistema in esecuzione                                               |
| NosAi.Security       | 9 file             | Noise, CapBAC e framing autenticato                                              |
| NosAi.Storage        | 4 file             | Journal SQLite su volume NOSAI-SSD                                               |
| NosAi.Adapter        | 3 file             | Aggancio al processo di gioco                                                   |
| NosAi.GuardClient    | 5 file             | Client PC verso il nodo Guard mobile                                             |
| NosAi.GuardAi.App    | 11 file            | App MAUI Android che fa da nodo Guard                                            |
| NosAi.Host           | 3 file             | Host di processo                                                                 |
| NosAi.ControlPanel   | 32 file            | Pannello operatore WPF                                                           |

## Regola di modifica
Ogni cambiamento architetturale richiede un ADR (Architecture Decision Record) nella directory `docs/adr/`.


> Conteggi aggiornati e ownership delle cartelle: `docs/REPOSITORY_ORDER.md` e `docs/REPOSITORY_INVENTORY_2026-09-10.json`.
