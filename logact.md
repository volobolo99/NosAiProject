# logact — registro delle consegne

Ogni incarico consegnato da un agente lascia qui una riga. Non è un diario: è la
misura con cui si decide se un modo di lavorare costa più di quanto rende.

**Chi scrive questo file.** Lo scrive l'orchestratore, non l'agente, e la ragione
è che un agente **non conosce i propri token**: il consumo è noto solo al server
MCP *dopo* la chiamata, e alla sessione Claude solo a valle. Farlo scrivere
all'esecutore produrrebbe una stima, cioè un dato inventato — che in questo
progetto è vietato. Durata, token e modello vengono dal rapporto di consegna;
l'esito e i file dalla verifica dell'orchestratore.

**Colonne.** `durata` è il tempo che l'agente ha impiegato, dalla presa in carico
alla consegna. `token` sono prompt + completion, con la quota di cache hit
separata perché costa un decimo. `costo` è calcolato ai prezzi
`deepseek-v4-flash` di settembre 2026: input 0,22 $/M, output 0,66 $/M, cache hit
0,007 $/M. `esito` è quello che il server ha riportato, non quello che l'agente
ha dichiarato.

**Perché `blocked` non significa fallito.** È lo stato che il server assegna
quando l'agente dichiara un limite: spesso la consegna è completa e il limite è
«non ho potuto compilare». La colonna `consegna` dice cosa è realmente arrivato
su disco.

## Consegne

| incarico | ruolo | modello | data | durata | token | di cui cache | costo | esito | consegna |
|---|---|---|---|---:|---:|---:|---:|---|---|
| prova-collegamento | sonda | `deepseek-v4-flash` | 2026-09-07 | 7 s | 9 244 | 6 272 | 0.001 $ | completed | 1 file di prova |
| Q-140 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 437 s | 4 303 279 | 4 125 568 | 0.090 $ | blocked | 0 file: gia' implementato |
| Q-143 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 560 s | 1 121 803 | 1 000 192 | 0.062 $ | blocked | 3 test, non integrati (duplicati) |
| Q-144 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 525 s | 3 454 890 | 3 278 336 | 0.090 $ | blocked | 1 file: motivo di rifiuto corretto |
| Q-145 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 476 s | 1 317 170 | 1 200 640 | 0.061 $ | completed | 1 file: registro dei rifiuti |
| Q-146 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 939 s | 8 994 613 | 8 735 488 | 0.171 $ | blocked | 6 file: T-14 dal pannello |
| Q-147/1 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 550 s | 56 640 | 22 912 | 0.008 $ | api_error | 0 file: morto leggendo 61 KB |
| Q-147/2 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 543 s | 0 | 0 | 0.000 $ | api_error | 0 file: timeout a 180 s |
| Q-147/3 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 1174 s | 27 194 | 16 512 | 0.007 $ | api_error | 0 file: timeout a 180 s |
| Q-147a | logica pura | `deepseek-v4-flash` | 2026-09-08 | 111 s | 41 808 | 22 016 | 0.011 $ | completed | 2 file: validazione frazioni |
| Q-147b/1 | gestori | `deepseek-v4-flash` | 2026-09-08 | 543 s | 0 | 0 | 0.000 $ | api_error | 0 file |
| Q-147b/2 | gestori | `deepseek-v4-flash` | 2026-09-08 | 216 s | 155 391 | 104 704 | 0.024 $ | blocked | 0 file: budget esaurito in analisi |
| Q-147b/3 | gestori | `deepseek-v4-flash` | 2026-09-08 | 173 s | 110 934 | 72 064 | 0.020 $ | completed | 3 file: card T-09 |
| Q-148/1 | implementatore | `deepseek-v4-flash` | 2026-09-08 | 562 s | 475 553 | 414 592 | 0.028 $ | budget_exhausted | 0 file |
| Q-148/2 | logica+gestori | `deepseek-v4-flash` | 2026-09-08 | 390 s | 100 549 | 66 816 | 0.021 $ | completed | 3 file: card T-13 |
| Q-149 | logica+gestori | `deepseek-v4-flash` | 2026-09-08 | 344 s | 580 252 | 509 312 | 0.039 $ | blocked | 3 file: card T-08 |
| Q-151 | logica+gestori | `deepseek-v4-flash` | 2026-09-08 | 1027 s | 928 016 | 824 960 | 0.055 $ | budget_exhausted | 2 file su 3 |
| Q-151b | test | `deepseek-v4-flash` | 2026-09-08 | 350 s | 48 193 | 26 368 | 0.014 $ | blocked | 1 file: i test mancanti |
| Q-152 | logica+gestori | `deepseek-v4-flash` | 2026-09-08 | 727 s | 919 264 | 822 272 | 0.055 $ | completed | 3 file: rete automatica |

**Totali**: 19 consegne, 22 644 793 token, 0.76 $, 160 minuti di lavoro degli agenti.

## Che cosa dicono questi numeri

Le tre righe piu' care -- Q-140, Q-146 e Q-152, oltre otto milioni di token
ciascuna le prime due -- hanno in comune un perimetro che conteneva **file
esistenti**. Le tre piu' economiche a parita' di risultato -- Q-147a, Q-147b/3,
Q-148/2 -- avevano nel perimetro **solo file da creare**: niente da leggere,
niente da rileggere a ogni giro.

Q-140 e Q-143 sono costati insieme 0,15 $ per scoprire che il lavoro era gia'
fatto. E' l'errore che ha generato la regola: prima di delegare, cercare
l'artefatto nel codice o partire da una prova che fallisce.
