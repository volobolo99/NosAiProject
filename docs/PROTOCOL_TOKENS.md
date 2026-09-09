# PROTOCOL_TOKENS — vocabolario dei segnali compatti

Dizionario dei tag con cui la squadra comunica. Un tag sostituisce una frase:
si scrive il tag, mai la sua spiegazione. Chi legge risolve qui.

Registro di stato: `contracts/ledger.json`. Questo file definisce i simboli,
il ledger porta i valori.

## CID — identificativo di contratto

`[CID: C-NNN]` — un contratto atomico: un file bersaglio, una firma, un test.
Numerazione progressiva per Gate: C-0xx = GATE 0, C-1xx = GATE 1, C-2xx = GATE 2,
C-3xx = GATE 3, C-4xx = GATE 4.

Un CID non si riusa e non si rinumera. Un contratto abbandonato resta nel ledger
con `[STATE: DROPPED]` e il motivo.

## SID — identificativo di struttura

`[SID: S-XXX-NN]` — un tipo di dato con layout dichiarato, condiviso fra moduli
o fra linguaggi. Prefissi:

| Prefisso | Dominio |
|---|---|
| `S-PKT-` | pacchetto di rete o suo frammento |
| `S-HOOK-` | struttura scambiata con un hook di memoria/DLL |
| `S-ENT-` | entità di gioco e sua identità |
| `S-EVT-` | evento del dispatcher |
| `S-FSM-` | stato o transizione di una macchina a stati |

Un SID porta sempre `struct_size` in byte nel ledger. `struct_size: null` significa
layout non ancora fissato, non "zero".

## INV — invarianti

`[INV: NOME]` — proprietà che il codice deve garantire e il test deve dimostrare.
Un invariante dichiarato senza test che lo eserciti non vale.

| Tag | Significato |
|---|---|
| `[INV: BOUNDS_CHECKED]` | ogni accesso indicizzato è validato contro la lunghezza reale del buffer; nessuna lettura oltre il limite su input malformato |
| `[INV: THREAD_SAFE]` | invocabile da più thread senza sincronizzazione esterna; nessuna corsa sullo stato condiviso |
| `[INV: ZERO_ALLOC_LOOP]` | il percorso caldo non alloca sull'heap dopo il warm-up; misurato, non asserito |
| `[INV: NO_PARTIAL_STATE]` | su errore non lascia stato scritto a metà: o l'operazione completa, o non ha effetti |
| `[INV: FAIL_CLOSED]` | in caso di incertezza nega invece di permettere |
| `[INV: UNKNOWN_PRESERVED]` | il dato non osservato resta distinguibile da zero, falso e vuoto lungo tutta la catena |
| `[INV: DETERMINISTIC]` | stesso input, stesso output: nessuna dipendenza da orologio, ordine di thread o entropia non dichiarata |

## STATE — stato di pipeline

Catena obbligatoria, in avanti soltanto. Un contratto che torna indietro perde lo
stato raggiunto e riparte dal punto in cui è caduto.

```
[STATE: DRAFT] -> [STATE: SKELETON_OK] -> [STATE: INFILLED] -> [STATE: VERIFIED] -> [STATE: MERGED]
```

| Tag | Significato | Chi lo assegna |
|---|---|---|
| `[STATE: DRAFT]` | contratto scritto, nessun file prodotto | Claude, FASE 1 |
| `[STATE: SKELETON_OK]` | firme, tipi e mock esistono e compilano; corpi vuoti | `local_generate_skeleton`, FASE 2 |
| `[STATE: INFILLED]` | corpi riempiti, firme immutate; non ancora collaudato | `cloud_infill_implementation`, FASE 3 |
| `[STATE: PREFLIGHT_OK]` | il codice generato corrisponde al contratto | `preflight_contract_check`, FASE 4 |
| `[STATE: VERIFIED]` | collaudo passato; vedi le due varianti sotto | FASE 5 |
| `[STATE: MERGED]` | committato su `main` e allineato a GitHub | Claude |

Due varianti di `VERIFIED`, perché il progetto ha due stack e un solo tag mentirebbe:

| Tag | Quando | Evidenza richiesta |
|---|---|---|
| `[STATE: ASAN_VERIFIED]` | codice nativo C/C++ | build con `-fsanitize=address` pulita sul test |
| `[STATE: TEST_VERIFIED]` | codice gestito C#/.NET o Python | `dotnet test` o `pytest` verde sul test dichiarato |

Stati fuori catena:

| Tag | Significato |
|---|---|
| `[STATE: BLOCKED]` | fermo su una dipendenza esterna; il ledger porta il motivo esatto |
| `[STATE: DROPPED]` | contratto abbandonato; resta nel ledger con il motivo |

## Regole d'uso

Un messaggio operativo è fatto di tag più i dati che i tag non contengono.
`[CID: C-101] [STATE: INFILLED] 3 test rossi` è un messaggio completo.

Nessun tag si assegna senza l'evidenza che lo giustifica: `[STATE: TEST_VERIFIED]`
richiede il comando eseguito e il suo esito, non l'intenzione di eseguirlo.

`struct_size`, `signature` e `test_file` si copiano da un `grep` fatto adesso,
mai dedotti dal nome del file.
