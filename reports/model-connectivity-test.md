# Test di connettivita' dei modelli

## Metodo
Il test di connettivita' dei modelli è stato eseguito il 2026-09-09 su un host Windows 11 con GPU RTX 5060, utilizzando Ollama versione 0.33.3. Il metodo consisteva in una sola chiamata di completions per ogni modello, con un prompt minimo che chiedeva un JSON contenente i campi status, model_role e message. Le credenziali sono state lette da HKCU\Environment e, in mancanza, dal file `.env`, cioe' come le vedrebbe un terminale nuovo. I dati grezzi di ogni misura stanno in `reports/model-connectivity-test.json`.

## Risultati
| Provider | Modello | Configurato | Raggiungibile | Risposta valida | Latenza | Token stimati | Costo stimato | Stato |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Ollama locale | `qwen2.5-coder:7b` | si | si | si | 12582.0 ms | 67 in / 28 out | 0 (locale) | READY |
| Ollama locale | `qwen-worker:latest` | si | si | si | 10580.1 ms | 67 in / 28 out | 0 (locale) | READY |
| DeepSeek (API nativa) | `deepseek-v4-flash` | si | si | si | 2135.1 ms | 158 in / 173 out | 0.00014894 USD | READY |
| OpenRouter | `qwen/qwen3-coder-30b-a3b-instruct` | si | si | si | 2271.3 ms | 67 in / 27 out | 0.00001225 USD | READY |
| OpenRouter | `google/gemini-2.5-flash-lite` | si | si | si | 616.1 ms | 52 in / 31 out | 0.0000176 USD | READY |
| OpenRouter | `qwen/qwen3-32b` | si | si | si | 3800.1 ms | 67 in / 126 out | n/d | READY |

## Credenziali
La prima esecuzione del test, nella stessa giornata, aveva trovato DeepSeek in AUTH_ERROR e i tre modelli OpenRouter in READY_WITH_WARNINGS. Le due credenziali sono state poi corrette dall'operatore e il test e' stato rifatto per intero.

La variabile OPENROUTER_PROVISIONING_KEY presente nell'ambiente utente contiene una copia della chiave di inferenza, non la chiave di provisioning. Il confronto delle impronte mostra che ha lo stesso valore della chiave in `.env`, e l'endpoint /api/v1/key risponde is_provisioning_key false. Questo significa che la chiave di provisioning di OpenRouter non e' piu' memorizzata sul computer. Non serve per l'inferenza e nulla e' bloccato, ma `scripts/provision_openrouter_key.py` non potrebbe creare nuove chiavi finche' non viene recuperata dalla dashboard OpenRouter o rigenerata.

La variabile OPENROUTER_API_KEY e' stata rimossa dall'ambiente utente, quindi ora vale la chiave di inferenza scritta in `.env`, per la quale is_provisioning_key vale false. Le completions rispondono HTTP 200.

## Stato del modello locale
Ollama: versione 0.33.3, modelli installati qwen2.5-coder:7b, qwen-worker:latest e hf.co/mlabonne/Meta-Llama-3.1-8B-Instruct-abliterated-GGUF:latest. qwen2.5-coder:7b gira interamente su GPU: 4748056984 byte su 4748056984 caricati in VRAM, cioe' il 100 per cento, a 47,5 token al secondo. qwen-worker:latest e' il modello derivato definito in `Modelfile.nosai` a partire da qwen2.5-coder:7b.

## Costi e comportamento dei modelli
La prova su deepseek-v4-flash ha costato piu' delle altre per due ragioni. Primo, il modello di ragionamento ha speso 673 caratteri di reasoning prima di rispondere, per un totale di 173 token in uscita contro i 27 di qwen/qwen3-coder-30b-a3b-instruct. Secondo, il listino: deepseek-v4-flash costa 0.66 USD per milione di token in uscita contro i 0.28 di qwen/qwen3-coder-30b-a3b-instruct, cioe' 2.4 volte tanto. I due effetti si moltiplicano.

Prezzi di listino in USD per milione di token, da `scripts/model_prices.json` verificato il 2026-09-09:

| Modello | Ingresso / Uscita |
|---|---|
| `qwen2.5-coder:7b` | 0.00 / 0.00 |
| `qwen/qwen3-coder-30b-a3b-instruct` | 0.07 / 0.28 |
| `google/gemini-2.5-flash-lite` | 0.10 / 0.40 |
| `deepseek-v4-flash` | 0.22 / 0.66 |
| `qwen/qwen3-32b` | non a listino in `scripts/model_prices.json` |

DeepSeek e' riportato nella fascia di picco, che e' la stima prudente; fuori picco il prezzo si dimezza.

## Routing consigliato
- Documentazione, scheletri e riassunti a qwen2.5-coder:7b in locale a costo zero.
- Codice semplice, test ripetitivi e analisi log a deepseek-v4-flash, tenendo conto che il suo ragionamento gonfia i token in uscita.
- Codice complesso, integrazione e refactoring strutturale a qwen/qwen3-coder-30b-a3b-instruct.
- Analisi visiva e classificazione rapida a google/gemini-2.5-flash-lite.
- Architettura e decisioni critiche a Claude.

## Azioni manuali necessarie
- Recuperare o rigenerare la chiave di provisioning OpenRouter e metterla in OPENROUTER_PROVISIONING_KEY, che oggi contiene un duplicato della chiave di inferenza.
- Ricordare che `setx` non tocca le sessioni gia' aperte e che il server MCP legge le credenziali all'avvio: dopo una rotazione servono un terminale nuovo e il riavvio di Claude Code.

## Rischi residui
- qwen-worker:latest e qwen/qwen3-32b non hanno un prezzo in `scripts/model_prices.json`, quindi per qwen/qwen3-32b il costo non e' stimabile con il listino del progetto.
- Il test verifica una singola richiesta per modello, quindi non dice nulla su stabilita' sotto carico, limiti di frequenza o comportamento con prompt lunghi.

---

La stesura di questo report e' stata delegata al modello locale `qwen2.5-coder:7b`. La tabella dei prezzi di listino, il rimando ai dati grezzi e la correzione della frase sui costi sono stati inseriti da uno script a partire da `scripts/model_prices.json`, perche' il modello locale aveva scambiato i prezzi in ingresso con quelli in uscita. Tabella dei risultati, sezioni obbligatorie e assenza di numeri inventati sono stati verificati automaticamente contro `reports/model-connectivity-test.json`.
