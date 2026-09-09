# Politica di costo

## Principio
Il modello meno costoso capace di completare il task verrà utilizzato.

## Tabella di routing
| Tipo di lavoro | Modello | Accesso |
|---------------|---------|---------|
| Documentazione, file Markdown, JSON, YAML, template, riassunti e scheletri | qwen2.5-coder:7b | Ollama locale |
| Codice semplice, test ripetitivi, debug iniziale, refactoring limitati e analisi ordinaria dei log | DeepSeek V4 Flash | API nativa DeepSeek |
| Codice complesso, integrazione fra moduli, debug difficile e refactoring strutturale | Qwen3 Coder 30B A3B Instruct | OpenRouter |
| Analisi visiva dello schermo, classificazione rapida e analisi economica di immagini e log | Gemini 2.5 Flash Lite | OpenRouter |
| Architettura, decisioni critiche, revisione del codice critico e gestione dei conflitti | Claude | Sessione Claude |

## Classi di costo
- Zero per il modello locale
- Bassa per DeepSeek V4 Flash e Gemini 2.5 Flash Lite
- Media per Qwen3 Coder 30B
- Massima per Claude

## Registro dei consumi
Per ogni task vengono registrati i seguenti campi nel file `data/ai_task_ledger.jsonl`:
- Identificatore del task
- Modello utilizzato
- Numero di chiamate
- Costo stimato
- Esito

Il file del registro è in formato JSON Lines, con una riga JSON per ogni task eseguito. Lo strumento `scripts/doc_agent.py` scrive automaticamente una riga di registro a ogni incarico documentale.

## Divieti
- Non usare Claude per attività che il modello locale può completare seguendo uno schema.
- Non rigenerare documenti già validati.
- Non passare contesti completi quando basta un riepilogo.
- Non avviare agenti duplicati sullo stesso lavoro.
- Chiamare DeepSeek tramite OpenRouter è vietato ed è bloccato in `scripts/mcp_server.py`.
- Si parallelizza soltanto su task indipendenti.
