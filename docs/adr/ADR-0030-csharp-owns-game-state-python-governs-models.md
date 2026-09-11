# ADR-0030 — Lo stato di gioco appartiene al runtime C#; Python governa i modelli

**Status:** Accepted — deciso dall'orchestratore il 2026-09-11
**Date:** 2026-09-11

## Context

La roadmap pone la domanda cosi':

> Quale stack possiede lo stato di gioco: il runtime C# o `nosai/` Python.

E il contratto **C-204** «Sincronizzazione fra runtime C# e `nosai/` Python» e'
bloccato con la motivazione *«nessun canale dichiarato fra i due stack»*.

La domanda e' urgente perche' finche' resta aperta ogni riga scritta su uno dei
due lati e' a rischio di essere cancellata.

### Misurato il 2026-09-11

| | C# | Python |
|---|---|---|
| Righe | **188.762** | 24.214 |
| Test | **3.759 verdi**, 0 falliti, 10 saltati | 10 falliti su 5 file |
| Contiene | World Model, catalogazione di item, mob e skill, navigazione, cattura pacchetti, parsing opcode, StrategyPlanner, ciclo di Gate3, sicurezza autorevole | hub MCP: binding dei ruoli, evidenze, apprendimento, politica, cruscotto, e la catena di delega |

Il ciclo che conta per il prodotto — percezione, decisione, azione — sta
interamente in C#, ed e' sensibile alla latenza. Il lavoro di `nosai/` e' di
un'altra natura: decide *quale modello* fa *quale lavoro*, con quale costo e con
quale evidenza. Nella sessione del 2026-09-11 quel ruolo si e' dimostrato reale:
il catalogo dei modelli, i binding dei sedici dipendenti, il guardiano quotidiano.

## Decision

**Il runtime C# possiede lo stato di gioco.** `nosai/` Python non ne tiene una
copia e non lo sincronizza.

**Python possiede il governo dei modelli**: catalogo, binding, evidenze,
apprendimento, costi, politica. Il C# non decide quale modello usare.

Il canale fra i due **non e' uno scambio di stato**. E' due flussi asimmetrici:

1. **C# verso Python, in uscita**: telemetria e registro delle decisioni, per
   alimentare evidenze e apprendimento. Solo append, mai richiesto durante il
   ciclo di gioco, quindi la sua latenza non entra nel percorso critico.
2. **Python verso C#, in ingresso**: configurazione. Quale modello per quale
   ruolo, quale politica di costo. Letta all'avvio e su cambio dichiarato, non a
   ogni tick.

Nessuno stato condiviso mutabile. Nessun attraversamento del confine per tick.

## Consequences

- **C-204 cambia oggetto** e si rimpicciolisce: non «sincronizzare due world
  model», ma «dichiarare due canali asimmetrici». Il primo e' un file o una coda
  in sola aggiunta; il secondo e' la lettura di una configurazione. Nessuno dei
  due richiede un protocollo bidirezionale.
- Se il Perception Agent Python dovesse servire una decisione di gioco, la
  risposta non e' dargli lo stato: e' che quella decisione appartiene al C#. Un
  modello di visione produce **un'osservazione**, che entra nel World Model del
  C#; non produce lo stato.
- **Rischio accettato**: il C# diventa il collo di bottiglia di ogni cambiamento
  sullo stato, e la squadra di modelli non puo' toccarlo senza la capacita' C#
  della catena. Quella capacita' e' stata costruita il 2026-09-11 (ADR-0029 e il
  contratto `code-agent-csharp-004`), quindi il rischio e' mitigato ma non nullo:
  i file C# grandi restano piu' difficili da delegare di quelli Python.
- Va detto esplicitamente in `ARCHITECTURE.md`, perche' oggi il silenzio su
  questo punto e' la ragione per cui esistono due meta' di sistema.
