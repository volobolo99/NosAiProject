# Test di connettivita' dei modelli

## Metodo
Il test di connettivita' dei modelli è stato eseguito il 2026-09-09 su un sistema Windows 11 con GPU RTX 5060. Il metodo utilizzato è stato una sola chiamata di completions per ogni modello, con un prompt minimo che chiede un JSON con i campi status, model_role e message. I risultati del test sono stati registrati nel file `reports/model-connectivity-test.json`.

## Risultati
| Provider | Modello | Configurato | Raggiungibile | Risposta valida | Latenza | Token stimati | Costo stimato | Stato |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Ollama locale | `qwen2.5-coder:7b` | si | si | si | 8600.8 ms | 67 in / 28 out | 0 (locale) | READY |
| Ollama locale | `qwen-worker:latest` | si | si | si | 9672.3 ms | 67 in / 28 out | 0 (locale) | READY |
| DeepSeek (API nativa) | `deepseek-v4-flash` | si | si | no | 333.0 ms | n/d | 0 (nessun token) | AUTH_ERROR |
| OpenRouter | `qwen/qwen3-coder-30b-a3b-instruct` | si | si | si | 2634.5 ms | 67 in / 27 out | 0.00001225 USD | READY_WITH_WARNINGS |
| OpenRouter | `google/gemini-2.5-flash-lite` | si | si | si | 532.1 ms | 52 in / 31 out | 0.0000176 USD | READY_WITH_WARNINGS |
| OpenRouter | `qwen/qwen3-32b` | si | si | si | 3705.0 ms | 67 in / 165 out | n/d | READY_WITH_WARNINGS |

## Diagnosi
- DeepSeek: la chiamata a `https://api.deepseek.com/chat/completions` ha restituito HTTP 401, e la stessa risposta e' arrivata dall'endpoint di saldo. Categoria `AUTH_ERROR`. Il problema e' di autenticazione, non di rete: la connessione TCP si stabilisce in 56 ms. La chiave rifiutata e' definita nell'ambiente utente di Windows, in `HKCU\Environment`, non nel file `.env`. Correzione: rigenerare la chiave e reinstallarla con `setx`.
- I tre modelli OpenRouter sono `READY_WITH_WARNINGS` perché con la configurazione attiva rispondono 401. I valori in tabella sono stati misurati con la chiave di inferenza del file `.env`.
- Con la configurazione attiva i soli modelli utilizzabili oggi sono i due locali di Ollama.

## Stato del modello locale
- Versione di Ollama: 0.33.3
- Modelli installati: `qwen2.5-coder:7b`, `qwen-worker:latest`, `hf.co/mlabonne/Meta-Llama-3.1-8B-Instruct-abliterated-GGUF:latest`
- Uso della GPU: 100% (4748056984 byte su 4748056984 caricati in VRAM)
- Velocita' in token al secondo: 47,5 token al secondo

## Componenti bloccati dalle stesse credenziali
- **DeepSeek (API nativa)**:
  - `tools/deepseek-mcp`
  - `tools/traffic_inspector/runner.py`
  - Tier 'simple' di `scripts/code_agent.py`
- **OpenRouter (provisioning key)**:
  - `scripts/mcp_server.py`
  - `scripts/code_agent.py` per i tier 'complex' e per il preflight
  - `tools/traffic_inspector/runner.py`

## Routing consigliato
- Documentazione e scheletri a `qwen2.5-coder:7b` in locale a costo zero.
- Codice complesso, integrazione e refactoring strutturale a `qwen/qwen3-coder-30b-a3b-instruct`.
- Analisi visiva e classificazione rapida a `google/gemini-2.5-flash-lite`, che è anche il più veloce con 532 ms.
- Il ruolo di codice semplice, test ripetitivi e analisi log resta scoperto finché DeepSeek non torna disponibile. Il sostituto più economico già funzionante è `qwen/qwen3-coder-30b-a3b-instruct`.

Prezzi di listino, da `scripts/model_prices.json` verificato il 2026-09-09:

| Modello | Prezzo di listino |
|---|---|
| `qwen2.5-coder:7b` | 0.00 / 0.00 USD per milione di token (ingresso / uscita) |
| `qwen/qwen3-coder-30b-a3b-instruct` | 0.07 / 0.28 USD per milione di token (ingresso / uscita) |
| `google/gemini-2.5-flash-lite` | 0.10 / 0.40 USD per milione di token (ingresso / uscita) |
| `deepseek-v4-flash` | 0.22 / 0.66 USD per milione di token (ingresso / uscita) |
| `qwen/qwen3-32b` | non a listino in `scripts/model_prices.json` |

DeepSeek e' riportato nella fascia di picco, che e' la stima prudente; fuori picco il prezzo si dimezza.

## Azioni manuali necessarie
1. Rigenerare la chiave DeepSeek su https://platform.deepseek.com/api_keys e installarla con `setx DEEPSEEK_API_KEY`, poi aprire un terminale nuovo.
2. Spostare la chiave di provisioning OpenRouter nella variabile `OPENROUTER_PROVISIONING_KEY` e mettere in `OPENROUTER_API_KEY` la chiave di inferenza, oppure rimuovere del tutto la variabile d'ambiente per lasciare vincere il file `.env`.
3. Riavviare Claude Code dopo ogni rotazione di chiave.

## Rischi residui
- L'identificativo `deepseek-v4-flash` non è stato confermato dal provider, perché il 401 blocca la richiesta prima della risoluzione del modello.
- `qwen-worker:latest` e `qwen/qwen3-32b` non hanno un prezzo in `scripts/model_prices.json`, quindi per `qwen/qwen3-32b` il costo non è stimabile.
- Il test verifica una singola richiesta per modello, quindi non dice nulla su stabilità sotto carico, limiti di frequenza o comportamento con prompt lunghi.
- Le latenze dei modelli locali includono il caricamento del modello in memoria e non sono confrontabili con quelle dei provider remoti a modello già caldo.

---

La stesura di questo report e' stata delegata al modello locale `qwen2.5-coder:7b`. La voce di diagnosi su DeepSeek e la tabella dei prezzi di listino sono state inserite da uno script a partire dai dati misurati, perche' il modello locale le ha perse in tre tentativi consecutivi. Tabella dei risultati, sezioni obbligatorie e assenza di numeri inventati sono stati verificati automaticamente contro `reports/model-connectivity-test.json`.
