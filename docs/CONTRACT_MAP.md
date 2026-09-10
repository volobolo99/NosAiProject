# NosAiProject — Mappa dei contratti

**Fonte generatrice:** `contracts/ledger.json`  
**Ultimo ledger letto:** 2026-09-09  
**Scopo:** una sola mappa per permettere a un modello Direttore e agli agenti esecutori di trovare rapidamente contratto, codice, test, stato e blocco senza rileggere l’intero repository.

> Questo documento è derivato dal ledger. Non si modifica a mano: quando cambia un contratto, aggiornare `contracts/ledger.json` e rigenerare questa mappa.

## Riepilogo

| Stato | Numero |
|---|---:|
| `MERGED` | 16 |
| `DRAFT` | 8 |
| `TEST_VERIFIED` | 2 |
| **Totale** | **26** |

Il livello di un contratto non è il livello del prodotto: `MERGED` indica presenza di codice/test secondo il ledger, mentre `TEST_VERIFIED` richiede la verifica indicata nella voce. La promozione a `VERIFIED` richiede evidenza dell’ambiente reale quando prevista dalla roadmap.

## Mappa completa

| CID | Gate | Contratto | Stato | File obiettivo | Test | Blocco/nota |
|---|---|---|---|---|---|---|
| C-001 | AP-00 — Ambiente, test harness e bridge nativo | MERGED | Build e suite .NET riproducibili | `scripts/build.ps1` | `scripts/test.ps1` |  |
| C-002 | AP-00 — Ambiente, test harness e bridge nativo | MERGED | Suite Python e CI | `pyproject.toml` | `tests/` |  |
| C-003 | AP-00 — Ambiente, test harness e bridge nativo | DRAFT | Harness AddressSanitizer | `scripts/run_asan_pipeline.py` | `tests/test_asan_pipeline.py` | Il file non esiste. La FASE 5 di .claude/CLAUDE.md lo invoca: finche' manca, il collaudo dinamico non ha su cosa girare. |
| C-004 | AP-00 — Ambiente, test harness e bridge nativo | DRAFT | Gatekeeper dei test Python | `scripts/gatekeeper.py` | `tests/test_gatekeeper.py` | Il file non esiste ed e' invocato dalla FASE 5. |
| C-005 | AP-00 — Ambiente, test harness e bridge nativo | DRAFT | Bridge ctypes verso il modulo nativo | `nosai/bridge/native.py` | `tests/test_native_bridge.py` | Nessun modulo nativo da caricare. ctypes compare oggi solo in nosai/storage/volume.py, per API Windows. |
| C-006 | AP-00 — Ambiente, test harness e bridge nativo | TEST_VERIFIED | Instradamento dei modelli: DeepSeek nativo, mai da OpenRouter | `scripts/mcp_server.py` | `tests/test_model_routing.py` | call_openrouter solleva ValueError su qualunque model_id contenente 'deepseek': la regola e' imposta dal codice, non dalla disciplina. deepseek-v4-flash e' un modello di ragionamento e spende max_tokens in reasoning_content prima di content, quindi un content vuoto solleva un errore invece di tornare stringa vuota. |
| C-101 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | MERGED | Sorgente di pacchetti astratta | `src/NosAi.Runtime/LiveIntegration/Capture/IPacketSource.cs` | `tests/NosAi.Runtime.Tests/LiveIntegration/Capture` | CapturedPacket e' un readonly record struct (DateTime TimestampUtc, ReadOnlyMemory<byte> Raw) a IPacketSource.cs:12; la dimensione dipende dal runtime, non e' un layout fissato. |
| C-102 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | MERGED | Cattura live via WinDivert | `src/NosAi.Runtime/LiveIntegration/Capture/WinDivertPacketSource.cs` | `tests/NosAi.Runtime.Tests/CaptureEngineTests.cs` | Coda a capacita' fissa QueueCapacity = 4096 (riga 51); i drop sono contati in Dropped (riga 89). |
| C-103 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | MERGED | Motore di cattura del traffico di gioco | `src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs` | `tests/NosAi.Runtime.Tests/CaptureEngineTests.cs` |  |
| C-104 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | MERGED | Parsing e registro degli opcode | `src/NosAi.Protocol/WireProtocol.cs` | `tests/NosAi.Runtime.Tests/OutEntityRecordedCaptureTests.cs` | 33 sorgenti toccano gli opcode. Il lavoro residuo e' la copertura degli opcode ancora Unknown, non l'infrastruttura. |
| C-105 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | DRAFT | Hook di memoria o DLL nel client | `da definire` | `da definire` | Solo 8 sorgenti citano 'hook' e nessuno realizza un hook di memoria o DLL. Serve decidere se questa strada si apre davvero: cambia il confine tecnico del prodotto. |
| C-106 | AP-01 — Intercettazione pacchetti, hook e parsing opcode | MERGED | Replay deterministico di una cattura | `src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs` | `tests/NosAi.Runtime.Tests/DecideReplayAsOfCaptureTests.cs` |  |
| C-201 | AP-02 — Dispatcher, correlazione entita' e sincronizzazione Python | MERGED | Dispatcher thread-safe degli eventi | `da confermare fra i 16 sorgenti che citano dispatch` | `da confermare` | Il contratto va riscritto con la firma esatta prima di toccare il codice. |
| C-202 | AP-02 — Dispatcher, correlazione entita' e sincronizzazione Python | MERGED | Correlazione degli identificativi di entita' | `src/NosAi.Core/WorldModel/` | `tests/NosAi.Core.Tests/` | 58 sorgenti toccano l'identita' delle entita': e' l'area piu' coperta del Gate. |
| C-203 | AP-02 — Dispatcher, correlazione entita' e sincronizzazione Python | MERGED | Fusione delle osservazioni nel World Model | `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs` | `tests/NosAi.Runtime.Tests/` |  |
| C-204 | AP-02 — Dispatcher, correlazione entita' e sincronizzazione Python | DRAFT | Sincronizzazione fra runtime C# e nosai/ Python | `da definire` | `da definire` | Non esiste un canale dichiarato fra i due stack. Prima di scriverlo serve decidere quale dei due possiede lo stato: vedi la domanda aperta in fondo al file. |
| C-301 | AP-03 — Decisione autonoma e recupero | MERGED | Pianificazione gerarchica HTN | `da confermare fra i 4 sorgenti che citano HTN` | `da confermare` | ADR-0028 discute se il livello HTN/GOAP resta o si rimuove: leggerlo prima di estendere. |
| C-302 | AP-03 — Decisione autonoma e recupero | MERGED | Pianificazione GOAP | `da confermare fra i 5 sorgenti che citano GOAP` | `da confermare` |  |
| C-303 | AP-03 — Decisione autonoma e recupero | MERGED | Orchestratore strategico | `da confermare fra i 30 sorgenti che citano Orchestrator` | `tests/test_orchestrator.py` |  |
| C-304 | AP-03 — Decisione autonoma e recupero | DRAFT | Macchina a stati finiti esplicita | `da definire` | `da definire` | Zero sorgenti contengono FSM o StateMachine. La decisione oggi passa da Planner (51 sorgenti) e Orchestrator (30): introdurre una FSM accanto a questi duplicherebbe l'autorita' di decisione. Serve dire se sostituisce o affianca. |
| C-305 | AP-03 — Decisione autonoma e recupero | MERGED | Recupero dopo disconnessione | `da confermare fra i 44 sorgenti che citano recovery o reconnect` | `da confermare` |  |
| C-401 | AP-04 — Hardening, fuzzing e rilascio | MERGED | Cifratura di sessione e autenticazione | `src/NosAi.Protocol/SessionCipher.cs` | `tests/test_session_cipher.py` |  |
| C-402 | AP-04 — Hardening, fuzzing e rilascio | DRAFT | Fuzzing sui pacchetti corrotti | `da definire` | `da definire` | Zero occorrenze di fuzz nel repository. Il bersaglio naturale e' il parsing opcode di C-104. |
| C-403 | AP-04 — Hardening, fuzzing e rilascio | MERGED | Superficie di sicurezza del runtime | `src/NosAi.Security/` | `tests/test_crypto_auth.py` |  |
| C-404 | AP-04 — Hardening, fuzzing e rilascio | DRAFT | Procedura di rilascio verificata | `docs/BUILD_TEST_RELEASE.md` | `scripts/validate.ps1` | La procedura esiste come documento; manca l'esecuzione verificata end-to-end che la chiuda. |
| ORCH-001 | AP-09 — Tooling di orchestrazione multi-modello (non e' un gate di prodotto) | TEST_VERIFIED | Routing per costo, registro dei consumi, messaggi fra agenti | `nosai/orchestration/routing.py` | `tests/test_orchestration.py` |  |

## Regole di utilizzo per gli agenti

1. Cercare prima il CID in questa tabella, poi aprire il file obiettivo e il test associato.
2. Se lo stato è `DRAFT`, leggere il blocco prima di scrivere codice: non creare stub per “chiudere” il contratto.
3. Se firma o test sono “da confermare”, ispezionare il codice reale e aggiornare il ledger con una firma verificata.
4. Un agente modifica solo il proprio perimetro; l’integratore aggiorna ledger, mappa, indice e changelog dopo i test.
5. Non promuovere un contratto sulla sola presenza del file: servono i comandi e l’evidenza registrati nella voce `verification`.
6. I contratti che cambiano la sicurezza, la proprietà dello stato o l’autorità di esecuzione richiedono audit indipendente.

## Domande aperte correnti

1. C-003/C-004/C-005: la FASE 5 del protocollo presuppone codice nativo e ASan, che il progetto non ha. O si apre un ramo nativo, o quei tre contratti restano DRAFT per sempre e la FASE 5 va riscritta sugli stack reali (dotnet test, pytest).
2. C-204: quale stack possiede lo stato di gioco, il runtime C# o nosai/ Python. Finche' non e' deciso, il canale fra i due non si puo' progettare.
3. C-304: una FSM esplicita sostituisce Planner/Orchestrator o li affianca. Affiancarli senza dirlo creerebbe due autorita' di decisione.

## Collegamenti canonici

- Fonte dati: `contracts/ledger.json`
- Vocabolario degli stati: `docs/PROTOCOL_TOKENS.md`
- Ordine e ownership: `docs/REPOSITORY_ORDER.md`
- Coordinamento agenti: `docs/AGENT_COORDINATION.md`
- Mappa sistema: `docs/SYSTEM_MAP.md`
- Roadmap: `docs/ROADMAP_ESECUTIVA.md`
