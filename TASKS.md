# Task

## Coda canonica
La coda dei task si trova in `docs/agents/EXECUTION_QUEUE.md`. Contiene voci con identificatore da Q-001 in avanti.

## Stati
Gli stati ammessi per un task sono:
- PENDING
- IN_PROGRESS
- DONE
- BLOCKED

## Come si assegna un task
I task vengono assegnati al modello meno costoso capace di completarli. La regola di routing dei modelli e la tabella dei costi sono definite in `COST_POLICY.md`. I ruoli degli agenti e il modello assegnato a ciascuno sono definiti in `AGENTS.md`.

## Registro dei costi
I dettagli del modello, numero di chiamate, costo stimato ed esito di ogni task vengono registrati in `data/ai_task_ledger.jsonl`.
