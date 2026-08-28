# Persistenza SQLite e Shared Memory

## Scopo

Questo documento integra nel progetto le parti delle specifiche allegate relative a persistenza locale, tracciamento delle traiettorie e accesso a Shared Memory.

## Persistenza SQLite

`nosai.persistence.sqlite_logger.NosAiSqliteLogger` fornisce:

- database locale SQLite;
- modalità WAL;
- sincronizzazione `NORMAL`;
- cache SQLite configurabile;
- registrazione delle sessioni;
- registrazione batch delle traiettorie;
- vincolo di integrità tra sessione e traiettoria;
- indici per sessione e timestamp.

Il logger è pensato come componente osservazionale e analitico. Non deve diventare un percorso alternativo per autorizzare o eseguire azioni.

## Shared Memory

La specifica allegata definisce un blocco binario `PlayerStatusBlock` con identificativo giocatore, coordinate, HP, MP e stato di combattimento e un'estensione nativa Node.js tramite N-API.

L'integrazione nativa completa C++/Node richiede una toolchain compilabile e verificata sulla piattaforma target. Il contratto dati viene mantenuto separato dal runtime Python per evitare dipendenze native implicite.

## Prestazioni

I valori di latenza riportati nelle specifiche sono obiettivi di benchmark e non garanzie. Ogni implementazione Zero-Copy, memory mapping o N-API deve essere validata con benchmark riproducibili.

## Automazione Miniland

La parte della specifica che invia direttamente pacchetti di gioco o usa ritardi casuali con finalità di elusione di sistemi anti-cheat non viene integrata come automazione evasiva. Il progetto può invece fornire adapter di test e interfacce di simulazione per Miniland senza eludere controlli del servizio.
