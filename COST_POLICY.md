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

## Prezzi verificati
| Modello | Ingresso (USD/mio token) | Uscita (USD/mio token) | Data | Fonte |
|---------|--------------------------|------------------------|------|--------|
| qwen2.5-coder:7b | 0 | 0 | 2026-09-09 | OpenRouter |
| DeepSeek V4 Flash | 0,22 | 0,66 | 2026-09-09 | DeepSeek |
| Qwen3 Coder 30B A3B Instruct | 0,07 | 0,28 | 2026-09-09 | OpenRouter |
| Gemini 2.5 Flash Lite | 0,10 | 0,40 | 2026-09-09 | OpenRouter |

## Classi di costo

Le classi derivano dai prezzi verificati sopra e dal listino leggibile dai programmi in
`scripts/model_prices.json`, che resta l'unica fonte: qui non si dichiara una classe diversa.

- **Zero**: qwen2.5-coder:7b su Ollama locale.
- **Bassa**: Qwen3 Coder 30B A3B Instruct, Gemini 2.5 Flash Lite e DeepSeek V4 Flash. Fra questi
  Qwen3 e' il piu' economico per token e DeepSeek il piu' caro: il codice semplice va comunque a
  DeepSeek per competenza, non per prezzo.
- **Massima**: Claude, che non e' fatturato per token dal progetto ma resta la risorsa piu' cara.

## Registro dei consumi
I campi registrati per ogni task sono: identificatore, modello, numero di chiamate, costo stimato ed esito. Questi dati vengono registrati nel file `data/ai_task_ledger.jsonl`.

## Divieti
- Non usare Claude per attività che il modello locale può completare seguendo uno schema.
- Non rigenerare documenti già validati.
- Non passare contesti completi quando basta un riepilogo.
- Non avviare agenti duplicati sullo stesso lavoro.
- Chiamare DeepSeek tramite OpenRouter è vietato ed è bloccato in `scripts/mcp_server.py`.
- Si parallelizza solo su task indipendenti.
