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
| NosAi.Core           | conteggio nell’inventario             | Contratti di pipeline, nessuna dipendenza                                          |
| NosAi.Protocol       | conteggio nell’inventario             | Protocollo di trasporto con frame binario a 12 byte e cifratura di sessione          |
| NosAi.Runtime        | conteggio nell’inventario           | Il cuore del sistema in esecuzione                                               |
| NosAi.Security       | conteggio nell’inventario             | Noise, CapBAC e framing autenticato                                              |
| NosAi.Storage        | conteggio nell’inventario             | Journal SQLite su volume NOSAI-SSD                                               |
| NosAi.Adapter        | conteggio nell’inventario             | Aggancio al processo di gioco                                                   |
| NosAi.GuardClient    | conteggio nell’inventario             | Client PC verso il nodo Guard mobile                                             |
| NosAi.GuardAi.App    | conteggio nell’inventario            | App MAUI Android che fa da nodo Guard                                            |
| NosAi.Host           | conteggio nell’inventario             | Host di processo                                                                 |
| NosAi.ControlPanel   | conteggio nell’inventario            | Pannello operatore WPF                                                           |

## Regola di modifica
Ogni cambiamento architetturale richiede un ADR (Architecture Decision Record) nella directory `docs/adr/`.


> Conteggi aggiornati e ownership delle cartelle: `docs/REPOSITORY_ORDER.md` e `docs/REPOSITORY_INVENTORY_2026-09-10.json`.


> Per i conteggi aggiornati usare `docs/REPOSITORY_INVENTORY_2026-09-10.json`; questa pagina descrive responsabilità, non una fotografia numerica.
