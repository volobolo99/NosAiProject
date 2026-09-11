# NosAi — Master Roadmap

_Generato automaticamente da `update_contract_state` a partire da `contracts/ledger.json`. Non modificare a mano: verra' sovrascritto alla prossima chiamata._
**Stack**: C#/.NET 8 — 564 sorgenti in src/, 377 test in tests/ · nosai/ — 78 moduli, 32 file di test · assente: nessun .cpp/.hpp di progetto, nessun harness ASan
**Vocabolario**: docs/PROTOCOL_TOKENS.md

> status riflette il codice presente su main al 2026-09-09, verificato per grep. Un contratto MERGED ha file e test sul disco; verification.last_result dice se il collaudo e' stato eseguito in questa sessione o no. struct_size null significa layout non ancora fissato, mai zero.

## Gate 0 — Ambiente, test harness e bridge nativo (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-001 | Build e suite .NET riproducibili | MERGED |
| C-002 | Suite Python e CI | MERGED |
| C-003 | Harness AddressSanitizer | DROPPED |
  - updated: 2026-09-11
  - metrics: ADR-0029 del 2026-09-11: la FASE 5 si misura con dotnet build/test e pytest. Il progetto non ha codice nativo di prima mano (i soli .cpp/.h sono riferimento third_party e l'header WinDivert; il ledger stesso registra native: assente), e l'accesso alla memoria del client avviene in C# via DllImport. Un harness ASan non ha un soggetto su cui girare. Si riapre se nascera un modulo nativo di prima mano.
  - note: Ha senso solo se il progetto acquisisce codice nativo; oggi non ne ha.
  - blocker: Il file non esiste. La FASE 5 di .claude/CLAUDE.md lo invoca: finche' manca, il collaudo dinamico non ha su cosa girare.
| C-004 | Gatekeeper dei test Python | DROPPED |
  - updated: 2026-09-11
  - metrics: ADR-0029 del 2026-09-11: il gatekeeper dei test Python e il tests_command dell incarico, che scripts/code_agent.py esegue e il cui fallimento ripristina lo scheletro. Un secondo cancello duplicherebbe l autorita sullo stesso giudizio. Misurato nella sessione: 10 deleghe rifiutate proprio da quel meccanismo.
  - blocker: Il file non esiste ed e' invocato dalla FASE 5.
| C-005 | Bridge ctypes verso il modulo nativo | DROPPED |
  - updated: 2026-09-11
  - metrics: ADR-0029 del 2026-09-11: non serve un bridge ctypes verso un modulo che non esiste. Se servira leggere memoria da Python, la strada e chiedere al runtime C# che la legge gia via DllImport (Win32ProcessAdapter, WinDivertPacketSource, ClientNetworkObserver), non aprire un secondo accesso allo stesso processo: due lettori della memoria di un client sono due modi di sbagliare. Coerente con ADR-0030, che assegna al C# la proprieta dello stato di gioco.
  - blocker: Nessun modulo nativo da caricare. ctypes compare oggi solo in nosai/storage/volume.py, per API Windows.
| C-006 | Instradamento dei modelli: DeepSeek nativo, mai da OpenRouter | TEST_VERIFIED |
  - updated: 2026-09-09
  - metrics: 10 test verdi; chiamata reale a deepseek-v4-flash riuscita
  - note: call_openrouter solleva ValueError su qualunque model_id contenente 'deepseek': la regola e' imposta dal codice, non dalla disciplina. deepseek-v4-flash e' un modello di ragionamento e spende max_tokens in reasoning_content prima di content, quindi un content vuoto solleva un errore invece di tornare stringa vuota.

## Gate 1 — Intercettazione pacchetti, hook e parsing opcode (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-101 | Sorgente di pacchetti astratta | MERGED |
  - note: CapturedPacket e' un readonly record struct (DateTime TimestampUtc, ReadOnlyMemory<byte> Raw) a IPacketSource.cs:12; la dimensione dipende dal runtime, non e' un layout fissato.
| C-102 | Cattura live via WinDivert | MERGED |
  - note: Coda a capacita' fissa QueueCapacity = 4096 (riga 51); i drop sono contati in Dropped (riga 89).
| C-103 | Motore di cattura del traffico di gioco | MERGED |
| C-104 | Parsing e registro degli opcode | MERGED |
  - note: 33 sorgenti toccano gli opcode. Il lavoro residuo e' la copertura degli opcode ancora Unknown, non l'infrastruttura.
| C-105 | Hook di memoria o DLL nel client | DROPPED |
  - updated: 2026-09-11
  - metrics: ADR-0032 del 2026-09-11: nessun hook di memoria o DLL injection nel client ora. Il canale di osservazione resta quello esistente (cattura di rete + lettura esterna di memoria via DllImport, gia' MERGED e testato). Si riapre con un caso nominato: un dato che quel canale non possa fornire in alcun modo.
  - blocker: Solo 8 sorgenti citano 'hook' e nessuno realizza un hook di memoria o DLL. Serve decidere se questa strada si apre davvero: cambia il confine tecnico del prodotto.
| C-106 | Replay deterministico di una cattura | MERGED |

## Gate 2 — Dispatcher, correlazione entita' e sincronizzazione Python (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-201 | Dispatcher thread-safe degli eventi | MERGED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto individuato il 2026-09-11: BoundedEventBus in src/NosAi.Runtime/Gate2/Gate2Runtime.cs:90, thread-safe (Channel<RuntimeEvent> bounded con SingleReader=true/SingleWriter=false, ConcurrentBag<Action<RuntimeEvent>> per i subscriber, contatori aggiornati con Interlocked). Dispatch loop asincrono senza allocazioni (DispatchLoopAsync, righe 151-167: WaitToReadAsync + TryRead + foreach sui subscriber). Nel percorso live: istanziata in Gate2RuntimeEngine (riga 547) ed esposta come property EventBus; Gate2IntegratedEngine la compone nella pipeline observation batch -> world model fold -> bounded event bus -> WAL persistence. Test dedicati: TestEventBusDroppingAsync e TestEventBusSecurityInvariant in Gate2Runtime.cs, invocati da tests/NosAi.Runtime.Tests/Gate2Tests.cs via Gate2TestRunner. Candidato escluso: NetworkWorldFeed (Perception/Network/NetworkWorldFeed.cs) usa una List non thread-safe e distribuzione diretta sincrona, non un bus limitato.
  - note: Il contratto va riscritto con la firma esatta prima di toccare il codice.
| C-202 | Correlazione degli identificativi di entita' | MERGED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto individuato il 2026-09-11, in due parti coerenti col titolo "correlazione degli identificativi": (1) EntityId, struct in src/NosAi.Core/WorldModel/Identifiers.cs:10, la chiave primitiva deterministica usata come Dictionary<EntityId,Mob> in tutto il World Model; preserva Unknown via DataSourceKind invece di inventare un default. (2) La correlazione vera e propria e' WorldModelTemporalEnricher.EnrichMobs (src/NosAi.Core/WorldModel/Temporal/WorldModelTemporalEnricher.cs:47): costruisce il dizionario delle entita' del ciclo precedente per EntityId e le confronta con quelle nuove, marcando "no_prior_sighting_of_this_entity" quando non trova corrispondenza invece di inventarla. GameplayObservationProjector.ProjectEntities (src/NosAi.Runtime/WorldModel/Fusion/GameplayObservationProjector.cs:350) e' il punto che deriva l'EntityId dalla SelectableEntity osservata in rete. Test dedicati: tests/NosAi.Core.Tests/WorldModel/IdentifiersTests.cs e tests/NosAi.Core.Tests/WorldModel/Temporal/WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.cs. Gap noto e documentato dal test stesso (riga 181, "DocumentedGap"): con EntityId duplicati nel ciclo precedente, il matching prende quello che compare per ultimo nella lista, comportamento accettato ma non garantito univoco.
  - note: 58 sorgenti toccano l'identita' delle entita': e' l'area piu' coperta del Gate.
| C-203 | Fusione delle osservazioni nel World Model | MERGED |
| C-204 | Sincronizzazione fra runtime C# e nosai/ Python | TEST_VERIFIED |
  - updated: 2026-09-11
  - metrics: Blocco rimosso da ADR-0030 del 2026-09-11 (due flussi asimmetrici, nessuno stato condiviso; il campo 'blocker' e' testo residuo pre-ADR). Entrambi i canali implementati e testati. Canale 2 (Python -> C#, in ingresso): src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs legge data/mcp/role_bindings.json, 6/6 test verdi, commit ba31c2e. Canale 1 (C# -> Python, in uscita, solo append): src/NosAi.Runtime/Observability/DecisionTelemetryWriter.cs registra ogni Gate3LoopCycle in JSONL, collegato in Gate3DecisionLoop tramite parametro opzionale telemetry (default null, comportamento invariato se assente), 4 test sul writer + 2 sul collegamento nel loop, tutti verdi. Suite completa NosAi.Runtime.Tests: 2830+ passati, 0 falliti attribuibili a queste modifiche (osservato un fallimento isolato e non riproducibile in GuardAdmissionTests/GuardNegativeTests su socket reali, confermato transitorio da rerun immediati, pre-esistente e indipendente da questo contratto).

## Gate 3 — Decisione autonoma e recupero (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-301 | Pianificazione gerarchica HTN | MERGED |
  - updated: 2026-09-11
  - metrics: ADR-0034 del 2026-09-11 (docs/adr/ADR-0034-htn-goap-close-under-adr-0028.md): applica la decisione di ADR-0028 (opzione C, 2026-09-07). Simbolo esatto: SequenceRoutine e SelectorRoutine in src/NosAi.Core/Planning/DeterministicRoutine.cs, PlannerGoalStack in src/NosAi.Core/Planning/PlannerGoalStack.cs. Layer deliberatamente non collegato al percorso di esecuzione finche' AP-08 non richiede pianificazione a piu' passi; nessun test individua questi simboli per nome, la copertura verde riguarda l'insieme dei 4 file in tests/NosAi.Core.Tests/Planning/ (ADR-0028).
  - note: ADR-0028 discute se il livello HTN/GOAP resta o si rimuove: leggerlo prima di estendere.
| C-302 | Pianificazione GOAP | MERGED |
  - updated: 2026-09-11
  - metrics: ADR-0034 del 2026-09-11 (docs/adr/ADR-0034-htn-goap-close-under-adr-0028.md): applica la decisione di ADR-0028 (opzione C, 2026-09-07). Simbolo esatto: DeterministicGoapPlanner in src/NosAi.Core/Planning/Goap/DeterministicGoapPlanner.cs, testato in tests/NosAi.Core.Tests/Planning/DeterministicGoapPlannerTests.cs. Layer deliberatamente non collegato al percorso di esecuzione finche' AP-08 non richiede pianificazione a piu' passi.
| C-303 | Orchestratore strategico | MERGED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto individuato il 2026-09-11: NosAiOrchestrator in nosai/core/orchestrator.py, testato in tests/test_orchestrator.py. Codice Python vivo: usato come dipendenza del costruttore da ClosedLoopRuntime in nosai/runtime/closed_loop.py, e da nosai/runtime/orchestrator_bridge.py e nosai/tactical/combat_engine.py. Non appartiene allo strato C# di ADR-0028 (LexicographicOrchestrator in src/NosAi.Core/Planning/, deliberatamente non collegato): ruolo concettuale simile, percorso opposto.
| C-304 | Macchina a stati finiti esplicita | DROPPED |
  - updated: 2026-09-11
  - metrics: ADR-0031 del 2026-09-11: nessuna macchina a stati esplicita. La domanda era mal posta: ADR-0028 ha misurato che Planner e Orchestrator NON sono nel percorso di esecuzione (ModuleReachability li dichiara Unreferenced per scelta datata), quindi non c e un autorita da duplicare. Il percorso vivo e StrategyPlanner piu il ciclo di Gate3. Una FSM inventata prima di un comportamento che la richieda fisserebbe una partizione degli stati scelta a tavolino, che e la parte che decide se la macchina dice il vero. Si riapre con un caso nominato, e un interblocco di sicurezza va costruito come guardia stretta, non come FSM generale.
  - blocker: Zero sorgenti contengono FSM o StateMachine. La decisione oggi passa da Planner (51 sorgenti) e Orchestrator (30): introdurre una FSM accanto a questi duplicherebbe l'autorita' di decisione. Serve dire se sostituisce o affianca.
| C-305 | Recupero dopo disconnessione | MERGED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto individuato il 2026-09-11: RecoveryController in src/NosAi.Runtime/Safety/RecoveryController.cs:104, con gli enum RecoveryStrategy (Retry/Replan/DegradedReplan/Cooling/HaltAndAlert) e RecoveryState (Closed/Throttled/Halted/Probing). E' il circuit breaker del ciclo decisionale (finestra sliding di fallimenti, escalation ladder, Halted = FAIL_CLOSED, transizioni atomiche = NO_PARTIAL_STATE), coerente col titolo del Gate 3 "Decisione autonoma e recupero" e con product_phases AP-08. Usato da Gate3ExecutionOrchestrator (Gate3Runtime.cs:1113, esposto come property Recovery a riga 1126); src/NosAi.Core/Safety/RetryBudgetContracts.cs:6 lo dichiara esplicitamente "the authoritative one". Test dedicati: tests/NosAi.Runtime.Tests/RecoveryCircuitBreakerTests.cs. Distinto da GuardReconnectPolicy (src/NosAi.GuardClient/GuardReconnectPolicy.cs:34): quella e' la policy di reconnect a livello di trasporto/socket del Guard client (Gate 1, backoff esponenziale, nessuna sliding window), usata da GuardConnectionService; il nome del contratto ("disconnessione") ricorda quella, ma l'invariante e il Gate collocano il contratto sul circuit breaker decisionale, non sul socket.

## Gate 4 — Hardening, fuzzing e rilascio (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-401 | Cifratura di sessione e autenticazione | MERGED |
| C-402 | Fuzzing sui pacchetti corrotti | TEST_VERIFIED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto (i campi target_file/signature/blocker del ledger erano rimasti a "da definire"/al blocker pre-harness): src/NosAi.Runtime/Testing/WireProtocolFuzzTestRunner.cs, static bool WireProtocolFuzzTestRunner.RunAll(), sei controlli deterministici su WireHeader.TryRead (src/NosAi.Protocol/WireProtocol.cs, non modificato) con seed fisso 1337. Test dedicato: tests/NosAi.Runtime.Tests/WireProtocolFuzzTests.cs. Il blocker "zero occorrenze di fuzz nel repository" non e' piu' vero dal 2026-09-11: l'harness esiste, --wire-fuzz-test 6/6 PASS, dotnet test 2832/2841 (9 ignorati, 0 falliti). Nessun lavoro nuovo in questo aggiornamento: solo la registrazione dei simboli gia' costruiti nella sessione precedente.
  - blocker: Zero occorrenze di fuzz nel repository. Il bersaglio naturale e' il parsing opcode di C-104.
| C-403 | Superficie di sicurezza del runtime | MERGED |
  - updated: 2026-09-11
  - metrics: Simbolo esatto individuato il 2026-09-11: "superficie di sicurezza" e' l'intero namespace src/NosAi.Security/ (5 controlli, tutti FAIL_CLOSED - negano per default su qualsiasi anomalia), non un singolo file: (1) HmacCapabilityValidator in CapabilityValidator.cs, validazione CapBAC; (2) FrameCodec/FrameTagCalculator in FrameCodec.cs, integrita' del frame; (3) SlidingWindowSequenceGuard in SequenceGuard.cs, protezione replay (finestra 1024-bit); (4) NoiseXxSession in NoiseSession.cs, handshake/transport Noise_XX_25519_ChaChaPoly_SHA256, Failed e' terminale; (5) FrameOpCode in FrameOpCode.cs, set chiuso di opcode. Unico consumer: src/NosAi.Host/NosAiHost.cs (riferimenti puntuali a righe 72-454). Test C# dedicati per 2 dei 5: tests/NosAi.Core.Tests/CapabilityValidatorTests.cs (8 test) e tests/NosAi.Core.Tests/FrameCodecTests.cs (9 test); SlidingWindowSequenceGuard e NoiseXxSession non hanno un file di test dedicato, solo esercizio indiretto via NosAiHost. Il test_file precedente del ledger (tests/test_crypto_auth.py) e' un refuso: testa nosai.network.crypto_auth.NosAiCryptoAuthManager, modulo Python lato client, non questo namespace C# - stesso tipo di errore gia' trovato e corretto per C-303. Distinto da C-401 (src/NosAi.Protocol/SessionCipher.cs, gia' RESOLVED): key exchange diverso (P-256 ECDH vs X25519), cipher diverso (AES-GCM vs ChaCha20Poly1305), livello diverso (sessione vs frame).
| C-404 | Procedura di rilascio verificata | VERIFIED |
  - updated: 2026-09-11
  - metrics: Completato il 2026-09-11 (stessa giornata di dcd6991, con l'operatore al computer): --dxgi-probe reale (dotnet run ... -- --dxgi-probe) ha aperto Desktop Duplication su 1920x1200 e catturato un frame con 4096 colori distinti (pixel live, non un buffer nero). --input-probe reale ha verificato mouse assoluto su 3 punti (errore massimo 0px, cursore ripristinato) e iniezione tastiera (VK_F24) osservata dall'hook a basso livello e mai ricevuta da alcuna applicazione, in 70 ms totali, esito 0. Combinato con l'evidenza gia' registrata in dcd6991 (Python 536/536, .NET Release 2838/2840 con i soli 2 falliti transitori da contention di suite gia' corretti in questa sessione, 22 suite di certificazione EXIT=0), la sezione real-environment (punto 9) della procedura di docs/BUILD_TEST_RELEASE.md e' ora chiusa per intero.
  - blocker: La procedura esiste come documento; manca l'esecuzione verificata end-to-end che la chiuda.

## Gate 9 — Tooling di orchestrazione multi-modello (non e' un gate di prodotto) (100%)

| CID | Titolo | Stato |
|---|---|---|
| ORCH-001 | Routing per costo, registro dei consumi, messaggi fra agenti | TEST_VERIFIED |
  - updated: 2026-09-09
| ORCH-002 | Drift-check CI per l'indice funzioni (manifest + shard) | TEST_VERIFIED |
  - updated: 2026-09-11
  - metrics: CI reale verificata su GitHub Actions: primo run (34626562602) fallito per un bug reale nel drift-check (confrontava source_revision byte-per-byte, impossibile da matchare per un commit che si autoreferenzia); corretto e rirun (34626851749) verde in 15s. pytest tests/test_function_index.py -q verde; suite Python completa invariata.
| ORCH-003 | Edges di chiamata testuali (C#/Python) nell'indice funzioni, non un call graph semantico | TEST_VERIFIED |
  - updated: 2026-09-11
  - metrics: pytest tests/test_function_index.py -q verde; schema v3 con calls risolte su fixture C#/Python, nessuna eccezione su nodi non riconosciuti
| ORCH-004 | Rendering deterministico di MASTER_ROADMAP.md (niente modello, niente parafrasi) | TEST_VERIFIED |
  - updated: 2026-09-11
  - metrics: 15 test verdi in tests/test_contract_ledger.py; suite Python completa (551 test) invariata; verificato a mano su ledger reale che tutte le domande_aperte risultano fedeli
  - note: Sostituisce regenerate_roadmap() che passava l'intero ledger dentro un prompt libero a un 7B locale: quel modello riportava domande gia' chiuse come ancora aperte e mischiava note del ledger (phase_mapping_note, signature_resolution_note) in righe di contratti a cui non appartenevano. Un rendering deterministico non puo' commettere quell'errore: ogni riga del Markdown e' copiata cosi' com'e' da un campo del ledger.
| C-309 | Isolamento dei 5 tool MCP per ruolo: chiude il bypass di doc_agent.py e il portatore di ruolo mancante per preflight/diagnostics | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Suite Python completa verde dopo l'implementazione: tests/test_doc_agent_authorization.py nuovo (9 test), tests/test_doc_agent_local_result.py con fixture corretto. preflight_contract_check APPROVED. Generato a costo zero dal locale via scripts/code_agent.py (tier local, solo_funzioni).
  - note: bdb6f5f aveva isolato per ruolo i 5 tool di scripts/mcp_server.py, ma lasciava due vie aperte: scripts/doc_agent.py produceva gli stessi risultati senza mai chiamare require_capability, e preflight_contract_check/deep_reasoner_solve_crash non comparivano nel tools: di nessun sub-agente configurato, quindi restavano raggiungibili solo dalla sessione root il cui employee_id proprio (employee.orchestrator_cto) non possiede ne' 'contracts' ne' 'diagnostics'.

## Domande aperte

- C-003/C-004/C-005: RISOLTA da ADR-0029 del 2026-09-11 (docs/adr/ADR-0029-phase5-dotnet-pytest-no-native-branch.md) — nessun ramo nativo, la FASE 5 si misura con dotnet build/test e pytest, non con ASan. I tre contratti chiudono come scopi che non esistono (DROPPED).
- C-204: RISOLTA da ADR-0030 del 2026-09-11 (docs/adr/ADR-0030-csharp-owns-game-state-python-governs-models.md) — il runtime C# possiede lo stato di gioco, nosai/ Python governa i modelli. Due canali asimmetrici, implementati e testati (TEST_VERIFIED).
- C-304: RISOLTA da ADR-0031 del 2026-09-11 (docs/adr/ADR-0031-no-explicit-fsm.md) — nessuna FSM esplicita: Planner/Orchestrator (HTN/GOAP) non sono nel percorso di esecuzione (ADR-0028), quindi non c'e' autorita' da duplicare. Il contratto chiude (DROPPED); si riapre con un caso nominato.
- C-105: RISOLTA da ADR-0032 del 2026-09-11 (docs/adr/ADR-0032-no-memory-hook-yet.md) — nessun hook di memoria o DLL injection nel client ora; il canale resta rete + lettura esterna di memoria via DllImport. Si riapre con un caso nominato.

## Note

- gates[].gate is a legacy grouping, NOT a product AP number. contracts[].product_phases maps to ROADMAP_ESECUTIVA. Existing status and verification are historical evidence, not revalidated by documentation reconciliation.
- C-103, C-104, C-106, C-203 and C-401 signatures were resolved from canonical source symbols on 2026-09-10. Status remains historical unless the linked verification command is executed.
