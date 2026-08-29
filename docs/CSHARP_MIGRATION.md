# NosAi — Migrazione del runtime a C#

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## Decisione

Il runtime principale di NosAi viene stabilito in **C# / .NET 8 su Windows**. I moduli Python esistenti vengono mantenuti come risorse di ricerca, prototipazione e compatibilità, mentre i componenti equivalenti destinati alla produzione vengono migrati dietro contratti stabili.

## Primi confini migrati

- punto di ingresso del runtime;
- contratti per Action e Trust Tier;
- valutazione deterministica di Guard AI;
- confine Safety Gate a chiusura sicura;
- fondazione della selezione dei candidati tramite Utility AI;
- fondazione della telemetria e del Mastery Score.

## Regola di migrazione

Non deve essere eseguita una traduzione cieca del linguaggio. Devono essere preservati architettura e contratti; ogni confine del runtime deve essere implementato nativamente in C# e accompagnato dai relativi test. Python rimane disponibile quando offre un vantaggio concreto per sperimentazione, ricerca ML o strumenti.

## Struttura corrente del runtime C#

```text
src/NosAi.Runtime/
├── Contracts/
├── Guard/
├── PlayAi/
├── Safety/
├── Telemetry/
└── Program.cs
```

## Stato della sicurezza

Il Safety Gate C# è deliberatamente a **chiusura sicura**. Finché non sono disponibili un adapter di gioco validato e un'integrazione completa di Guard, questa fondazione runtime non autorizza l'esecuzione reale.

## Prossimi obiettivi di migrazione

1. Primo avvio minimo di Play AI PC + Play Guard + Guard AI.
2. Contratti del World Model.
3. Contratti della percezione e confine produttivo di acquisizione Windows.
4. Integrazione dell'Orchestrator.
5. Simulazione e Ranking tattico.
6. Telemetria e memoria persistenti.
7. Adapter di gioco controllato.

La versione del progetto rimane **1.0 Beta** finché il creatore non la modifica esplicitamente.
