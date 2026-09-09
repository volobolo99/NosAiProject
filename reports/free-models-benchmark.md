# Banco di prova dei modelli gratuiti OpenRouter

Generato da script dai risultati misurati, non redatto a mano: i numeri vengono dai file
`results.jsonl`, `results_rest.jsonl` e `vision_results.json` prodotti dalle chiamate reali.

- Data della misura: 2026-09-09T15:54Z
- Catalogo OpenRouter: 431 modelli, di cui 21 a costo zero
- Quota gratuita dell'account: 1000 richieste al giorno (10 crediti acquistati)

## Come si e' misurato

Due incarichi di infilling costruiti sui due livelli reali della catena, giudicati dal
validatore gia' presente nel repository, `validate_implementation` di `scripts/code_agent.py`,
piu' i test di comportamento sui casi di bordo:

- **A (semplice)**: parsing di telemetria con dataclass, 16 controlli. Livello oggi assegnato a DeepSeek V4 Flash.
- **B (complesso)**: limitatore a finestra scorrevole con stato, 14 controlli. Livello oggi assegnato a Qwen3 Coder 30B.

Entrambi i banchi sono stati validati prima dell'uso: una soluzione di riferimento passa
16/16 e 14/14, lo scheletro vuoto passa 0. Un banco che nessuno puo' fallire, o che nessuno
puo' passare, non misura nulla.

## Esiti per modello

| Modello | Tipo | A (16) | B (14) | Secondi medi |
|---|---|---|---|---|
| `qwen/qwen3-coder-30b-a3b-instruct` | a pagamento | 16/16 | 14/14 | 20 |
| `deepseek-v4-flash` | a pagamento | 16/16 | 14/14 | 148 |
| `nex-agi/nex-n2.5-mini:free` | gratuito | 16/16 | 14/14 | 8 |
| `openrouter/free` | gratuito | 16/16 | 14/14 | 9 |
| `dots-studio/dots-3-note-preview:free` | gratuito | 16/16 | 14/14 | 13 |
| `nvidia/nemotron-3-super-120b-a12b:free` | gratuito | 16/16 | 14/14 | 22 |
| `cohere/north-mini-code:free` | gratuito | 16/16 | 14/14 | 27 |
| `poolside/laguna-s-2.1:free` | gratuito | 16/16 | 14/14 | 34 |
| `nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free` | gratuito | 16/16 | 14/14 | 54 |
| `nex-agi/nex-n2.5-pro:free` | gratuito | 16/16 | 14/14 | 60 |
| `nvidia/nemotron-3.5-lightning:free` | gratuito | 16/16 | 14/14 | 230 |
| `nvidia/nemotron-3-ultra-550b-a55b:free` | gratuito | 16/16 | 14/14 | 422 |
| `qwen2.5-coder:7b` | locale | 9/16 | 14/14 | 14 |
| `liquid/lfm-2.5-2.6b:free` | gratuito | 0/16 | 3/14 | 24 |
| `google/gemma-4-26b-a4b-it:free` | gratuito | 429 provider | 14/14 | 29 |
| `google/gemma-4-31b-it:free` | gratuito | 429 provider | 429 provider | - |
| `poolside/laguna-xs-2.1:free` | gratuito | 429 provider | 429 provider | - |

## Classifica dei gratuiti a qualita' piena

Ordinati per secondi medi: tutti hanno superato ogni controllo su entrambi i livelli.

| # | Modello | Secondi medi | A | B |
|---|---|---|---|---|
| 1 | `nex-agi/nex-n2.5-mini:free` | 8 | 8 | 7 |
| 2 | `openrouter/free` | 9 | 5 | 13 |
| 3 | `dots-studio/dots-3-note-preview:free` | 13 | 8 | 18 |
| 4 | `nvidia/nemotron-3-super-120b-a12b:free` | 22 | 14 | 31 |
| 5 | `cohere/north-mini-code:free` | 27 | 20 | 34 |
| 6 | `poolside/laguna-s-2.1:free` | 34 | 23 | 46 |
| 7 | `nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free` | 54 | 57 | 50 |
| 8 | `nex-agi/nex-n2.5-pro:free` | 60 | 72 | 49 |
| 9 | `nvidia/nemotron-3.5-lightning:free` | 230 | 169 | 290 |
| 10 | `nvidia/nemotron-3-ultra-550b-a55b:free` | 422 | 556 | 289 |

## Lettura degli schermi di gioco

Ritagli reali del Perception Agent, 124x12 pixel, ingranditi 4 volte. La verita' e' nel nome
del file: `nostale_hp_full_7305.bmp` e `nostale_mp_full_1420.bmp`.

| Modello | HP 7305 | MP 1420 | Secondi medi |
|---|---|---|---|
| `google/gemini-2.5-flash-lite` | letto | letto | 2.0 |
| `nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free` | letto | letto | 3.9 |
| `dots-studio/dots-3-note-preview:free` | letto | letto | 8.5 |
| `nex-agi/nex-n2.5-pro:free` | letto | letto | 10.7 |
| `openrouter/free` | letto | letto | 16.2 |
| `google/gemma-4-26b-a4b-it:free` | 429 | letto | 14.2 |
| `google/gemma-4-31b-it:free` | 429 | 429 | - |
| `thinkingmachines/inkling:free` | 403 | 403 | - |

## Costo per incarico misurato

| Modello | Costo di un incarico | Token in uscita |
|---|---|---|
| `qwen/qwen3-coder-30b-a3b-instruct` | 0.00018 e 0.00025 USD (dichiarato dal provider) | 493 e 673 |
| `deepseek-v4-flash` | circa 0.0073 USD (da listino, uscita) | 11039 e 12352 |
| modelli `:free` | 0 USD | - |

## Limiti di questa misura

- Due incarichi non sono una prova di parita' generale: dicono che su questi due compiti,
  con questi controlli, il risultato e' stato indistinguibile da quello dei modelli a pagamento.
- La disponibilita' e' parte della qualita': Gemma 4 e Poolside XS hanno risposto 429 in modo
  ripetuto anche a una richiesta per volta, e vanno considerati inaffidabili finche' non migliorano.
- I tempi risentono della coda del provider e vanno riletti periodicamente.
