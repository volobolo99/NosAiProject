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
| C-007 | Driver di automazione UI per test autonomi del ControlPanel | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Driver di automazione UI per il ControlPanel: tests/NosAi.ControlPanel.UiTests/UiAutopilot.cs (riusa Win32InputBackend per il click fisico e DxgiDesktopDuplicationSource per la cattura schermo, entrambi gia' pubblici in NosAi.Runtime) + primo scenario NosAi.ControlPanel.UiTests/NavigationUiTests.cs. Scheletro locale (qwen2.5-coder:7b, corretto a mano: l'unica API inventata era Win32InputBackend.Instance, che non esiste), infilling Qwen3 Coder 30B via scripts/code_agent.py (costo reale 0.001585 USD, preflight APPROVED). Due difetti trovati e corretti dopo l'infilling, prima della verifica reale: (1) il ritaglio schermo allocava l'intero buffer desktop una volta per riga invece che una sola volta; (2) il click fisico non portava la finestra in primo piano, quindi SendInput colpiva qualunque finestra fosse sopra sullo schermo (primo run reale: click 'riuscito' ma PageTitle restava 'Panoramica'). Aggiunta SetForegroundWindow prima di ogni click. Dopo la correzione: dotnet test verde su ambiente reale (avvio ControlPanel.exe, click fisico su NavClient, PageTitle verificato 'Client NosTale', screenshot di evidenza salvato in data/ui_autopilot/, gitignored). dotnet test su NosAi.ControlPanel.Tests invariato: 225/225 verde, nessuna regressione dagli AutomationId aggiunti in MainWindow.xaml. Corretto lo stesso giorno: la categoria xunit UiAutopilot era stata esclusa di default da scripts/validate.ps1 e dai due workflow CI (andava lanciata a mano); l'operatore ha respinto il cancello manuale ("chi te lo ha chiesto, non devo attivare nulla a mano") e l'esclusione e' stata rimossa — gira nella corsa normale, nessun comando a mano. Limite dichiarato, non risolto: assume la finestra sul monitor primario (DxgiDesktopDuplicationSource cattura un solo output). Copre solo il ControlPanel; l'aggancio al gameplay reale (dietro il gate ADR-0003, che l'operatore non ha messo in discussione) resta da fare.
  - note: Riusa Win32InputBackend (click fisico via SendInput) e DxgiDesktopDuplicationSource (cattura schermo), gia' pubblici in NosAi.Runtime: nessuna seconda implementazione di input/cattura. Non tocca ADR-0003 (il gate di sicurezza del gameplay): pilota solo la finestra del ControlPanel, non il client di gioco.
| C-008 | Driver di test end-to-end sul client di gioco reale: unequip autonomo via CLI di produzione | VERIFIED |
  - updated: 2026-09-12
  - metrics: Percorso reale verificato in tre run consecutivi con l'operatore al client vivo (2026-09-12): (1) gioco in background -> rifiuto onesto 'client_window_not_located'; (2) gioco in primo piano -> finestra trovata, cattura di rete aperta, cursore mosso sul pixel calibrato corretto, rifiuto onesto 'unequip_no_equip_reading'; (3) stesso esito con inventario aperto. Causa isolata leggendo UnequipExecutor.cs: il comando standalone legge il pacchetto equip una sola volta, istantaneamente, senza attendere; quel pacchetto arriva solo da un vero equip/unequip lato client, oggi generabile solo da un umano per il percorso standalone (--equip/--unequip aprono una cattura di rete propria, invariato da C-312, che ha risolto il caso solo dentro il loop di autoplay riusando l'osservazione gia' condivisa dal ciclo). Limite dichiarato, non risolto qui. dotnet build NosAi.sln pulito, 0 errori. Nessun infilling cloud: riuso integrale di codice di produzione gia' verificato (evidenza C-404), zero logica nuova oltre invocazione del processo e asserzioni; scritto direttamente perche' i test sono responsabilita' di Claude (sezione 14 CLAUDE.md).
  - note: Zero codice di attuazione nuovo: invoca via ToolRunner (riuso di NosAi.ControlPanel.ToolRunner, stesso helper di MainWindow.xaml.cs) il comando gia' in produzione 'dotnet NosAi.Runtime.dll --unequip Weapon --gesture single --arm-input', che passa per la catena autorizzata da ADR-0003 (GatedInputBackend -> CommitPointValidator -> ActuationAuthority.Commanded -> UnequipExecutor). Nessun bypass, nessuna seconda via di attuazione. Rifiuta onestamente (Assert.Fail nominato) se il client non e' in esecuzione o la dll Release non e' compilata; non presuppone un esito specifico del filo (Confirmed/StillWorn), solo che il click sia stato emesso.
| C-009 | Calibratore dell'offset di memoria per l'equipaggiamento indossato (WornEquipment), sul modello di PlayerVitalsCalibrator | VERIFIED |
  - updated: 2026-09-12
  - metrics: Prova dal vivo eseguita con l'operatore al client reale (2026-09-12), esito onesto negativo: il filtro iniziale su Opcode Equip non ha mai visto nulla (Equip non arriva mai durante il gioco normale); passato a Opcode Eq dopo diagnosi con una riga di debug temporanea (poi rimossa) che ha mostrato 7 pacchetti Eq reali con conteggio slot variabile 5->4->5, prova diretta di un cambiamento reale osservato. Cross-verificato contro le definizioni indipendenti di pacchetto di Rutherther/NosSmooth (MIT, github.com/Rutherther/NosSmooth): eq porta esattamente i 10 slot visivi in ordine fisso (Hat, Armor, MainWeapon, SecondaryWeapon, Mask, Fairy, CostumeSuit, CostumeHat, WeaponSkin, WingSkin - EqPacket.cs/InEquipmentSubPacket.cs), equip porta il set completo indicizzato per slot id reale con dettaglio rarita'/potenziamento (EquipPacket.cs/EquipSubPacket.cs) e non parte per un equip/unequip ordinario. Con Eq corretto: Round 1 ha visto la lettura, scelto l'ancora, scansionato la memoria - zero indirizzi hanno retto il controllo sull'intero array a passo 4 byte. Ipotesi di array contiguo a passo fisso FALSIFICATA da un test reale, non da un ragionamento: per contratto nessun secondo tentativo con un passo indovinato segue un risultato negativo. Verificato anche NosSmooth.Local (stesso autore, github.com/Rutherther/NosSmooth.Local): PlayerManager.cs conferma in modo indipendente gli stessi offset 0x20 (PlayerObject) e 0x24 (CharacterId) gia' in NosTaleClientLayout.cs, ma non contiene bindings di memoria per equipaggiamento/inventario (l'ecosistema NosSmooth legge equip solo via pacchetti, non da un array di memoria dedicato) - nessuna scorciatoia disponibile li'. Limite dichiarato e ora empiricamente confermato: la lettura autonoma dell'equipaggiamento senza dipendere dal filo resta non risolta con l'ipotesi provata; un tentativo successivo dovrebbe partire da un layout non contiguo o da campi di larghezza mista (WeaponSkin/WingSkin sono short da 2 byte contro int da 4 per gli altri otto campi), non da un secondo passo indovinato sulla stessa ipotesi.
  - note: Nasce da un limite di protocollo confermato empiricamente su C-008: il pacchetto 'equip' arriva solo su cambiamento reale, mai su richiesta, quindi --unequip/--equip/--loadout-report non vedono nulla senza un umano che tocchi l'equipaggiamento nella finestra giusta. Stesso schema a tre fasi di PlayerVitalsCalibrator.cs (ascolta il filo per una verita' nota, scansiona la memoria per quella verita', un secondo giro con una verita' DIVERSA elimina le coincidenze), adattato da una coppia (max,current) a un array di slot: MemoryScanner.Scan sul vnum del primo slot occupato, poi KeepMatchingArray verifica che TUTTI gli altri slot noti combacino a passo 4 byte dalla base derivata. Ipotesi di passo NON assunta oltre la prova: zero candidati sopravvissuti e' un risultato onesto, non un secondo tentativo indovinato. Ancoraggio finale via PointerAnchorHunter.Report, identico a vitals. Produce e prova un candidato; non scrive ancora la costante in NosTaleClientLayout.cs ne' un EquipmentMemoryReader di produzione (contratto di follow-up, dopo una prova reale riuscita).

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
| C-306 | Farming: dispatch di StrategicGoalKind.Farming verso EngageCommand | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Implementato in 1bb01f0 (2026-09-11), mai registrato nel ledger grande fino ad ora (esisteva solo come contratto Fase-1 standalone in contracts/autoplay-farming-dispatch-012.json). Verificato contro il codice reale il 2026-09-12: DispatchFarming in src/NosAi.Runtime/Tactical/AutoplayCommand.cs:354, StrategyPlanner.AssessFarmingUrgency in src/NosAi.Core/WorldModel/Strategy/StrategyPlanner.cs:225, StrategicGoalKind.Farming = 4 in StrategyContracts.cs:19. Sceglie il bersaglio con NosAi.Runtime.Autonomy.TargetSelector.TrySelect ed esegue un attacco base via EngageCommand.ExecuteOneRound; AutoplayDispatch.Engaged = 6 / FarmingSkippedNoTarget = 7 (AutoplayCommand.cs:130-133). Test in AutoplayCommandTests.cs: AFarmingPlan_WithNoObservedMobs_IsFarmingSkippedNoTarget_AndNeverTouchesInput, AFarmingPlan_WithAnAttackableMobInRange_DispatchesToEngageCommand_UnderAutoplayAuthority. Suite NosAi.Runtime.Tests verde.
| C-307 | StrategicGoalKind.Collect e StrategyPlanner.AssessCollectUrgency | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Implementato in 9f812bf (2026-09-11), mai registrato nel ledger grande fino ad ora (esisteva solo come contratto Fase-1 standalone in contracts/collect-goal-kind-013.json). Verificato contro il codice reale il 2026-09-12: StrategicGoalKind.Collect = 7 in StrategyContracts.cs:24, AssessCollectUrgency in StrategyPlanner.cs:259 -- urgency 0.0/'no_viable_drop' se nessun drop ha mai posizione nota, 1.0/'drop_in_reach' se almeno un drop e' entro maxRangeTiles, 0.5/'drop_out_of_reach' se noto ma fuori portata, stesso principio gia' usato da AssessFarmingUrgency. Deliberatamente senza un modulo 'planner' separato: nessuna valutazione del valore del loot. Prerequisito di C-308. Suite NosAi.Core.Tests verde.
| C-308 | Collect: dispatch di StrategicGoalKind.Collect verso CollectCommand | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Implementato in 8b1d5b8 (2026-09-11), mai registrato nel ledger grande fino ad ora (esisteva solo come contratto Fase-1 standalone in contracts/collect-dispatch-014.json, registrato di per se' in dbcdd7a ma mai nel ledger.json). Verificato contro il codice reale il 2026-09-12: DispatchCollect in AutoplayCommand.cs:401, ObserveDrops (gemello di ObserveMobs) in AutoplayCommand.cs:588; AutoplayDispatch.Collected = 8 / CollectSkippedNoTarget = 9 (AutoplayCommand.cs:136-139). Sceglie il drop con distanza euclidea minima da playerPosition fra quelli con Position nota e chiama CollectCommand.ExecuteOneRound con before/after deliberatamente uguali (il ciclo legge una volta sola per giro). Test in AutoplayCommandTests.cs: ACollectPlan_WithNoGameplayObservation_IsCollectSkippedNoTarget_AndNeverTouchesInput, ACollectPlan_WithADropInReach_DispatchesToCollectCommand_UnderAutoplayAuthority. Suite NosAi.Runtime.Tests verde.
| C-310 | Optimization: meta' decisionale del dispatch di StrategicGoalKind.Optimization (selezione candidato + verifica in borsa, senza il click) | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Implementato in e33e1ce (2026-09-12), verificato contro il codice reale: DispatchOptimization in AutoplayCommand.cs:478; AutoplayDispatch.OptimizationCandidateReady = 10 / OptimizationSkippedNoCandidate = 11 (AutoplayCommand.cs:152-155). Usa LoadoutPlanner.GenerateEmptySlotCandidates (gia' MERGED) per scegliere un candidato, poi verifica che il suo Item sia osservato in Player.Inventory a uno SlotIndex noto; nessuna chiamata a EquipExecutor in questo contratto. Test in AutoplayCommandTests.cs: AnOptimizationPlan_WithACandidateAtAKnownBagSlot_IsOptimizationCandidateReady, AnOptimizationPlan_WithNoResolvableCandidate_IsOptimizationSkippedNoCandidate, AnOptimizationPlan_WithACandidateWhoseBagSlotIsNotRead_IsOptimizationSkippedNoCandidate. 6 refusal reason dei tipi Equip*/BagPanelRoiCalibration ancora irraggiungibili sono dichiarati in RefusalReasonRegisterTests.Declared con motivo esplicito 'scheletro'. preflight_contract_check APPROVED. Suite NosAi.Runtime.Tests verde (i soli 2 GuardAdmissionTests flaky per contention socket, confermati non regressivi da rerun isolato).
  - note: Ambito ridotto in corsa (amendment_2026-09-12 dentro contracts/equip-command-executor-wiring-016.json): il contratto originale prevedeva anche il click reale via EquipExecutor.Equip, rimandato a un contratto di follow-up. QUEL FOLLOW-UP E' C-312, gia' MERGED: la premessa dell'amendment (serve un secondo GameTrafficObserver per l'EntityId) si e' rivelata piu' stretta del necessario, vedi C-312.resolved_limitation.
| C-312 | Optimization: click reale via EquipExecutor, verificato sul filo (completa C-310) | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Filiera completa: scheletro (Fase 2, locale) -> infilling (Fase 3, locale/Qwen3 Coder 30B su consenso esplicito dell'operatore per EquipCommand.cs dopo 8 tentativi gratuiti fatti/falliti) -> revisione manuale dei due difetti sopra -> preflight_contract_check APPROVED -> suite completa verde: 3881 test .NET (0 falliti, 10 ignorati) + pytest 100%. Nuovi enum AutoplayDispatch.Optimized=12/OptimizationNotConfirmed=13/OptimizationRefused=14/OptimizationSkippedNoGesture=15 (append-only, 0-11 invariati). Nuovo flag --optimization-gesture in Program.cs (nessun default indovinato, stesso principio di --recover-slot); --equip stesso collegato per la prima volta al dispatcher di Program.cs (non lo era mai stato, a differenza di --unequip). RefusalReasonRegisterTests aggiornato: 4 reason ora coperte da test rimosse da Declared, 2 nuove (equip_input_backend_not_gated, equip_equip_feed_unavailable) aggiunte perche' richiedono un client reale, mirror esatto delle omologhe di UnequipCommand. Limite dichiarato e non risolto qui: nessuna calibrazione data/perception/bag-panel-roi.calibration esiste ancora e --optimization-gesture non e' mai stato confermato empiricamente -- richiede l'operatore al client reale, come per T-12.
  - note: resolved_limitation: l'amendment di C-310 credeva servisse un secondo GameTrafficObserver per un WornEquipment con EntityId. Verificato falso: UnequipExecutor.Verify (il template) non legge mai EntityId (zero occorrenze), quindi il canale gia' condiviso da ogni ciclo di autoplay (GameplayObservation.Equipment, un WornEquipmentReading senza EntityId) basta. EquipExecutor.Equip/Verify prendono percio' Func<WornEquipmentReading?> invece di Func<WornEquipment?> -- deviazione dichiarata dal template, non un'omissione. Due bug trovati leggendo il codice generato prima di fidarsene: Confirmed applicava la regola 'tutti gli slot o nessuno' di InventoryPanelRoiCalibration (18 EquipmentSlot fissi) a una borsa che scorre e non ha un conteggio fisso -- corretto a mano; e Equip non controllava la corrispondenza di risoluzione client/calibrazione, un vero gap di sicurezza (click su pixel sbagliati con una calibrazione stantia) introdotto dal mio stesso contratto -- aggiunto il controllo, mirror di UnequipExecutor.
| C-313 | Strumento di calibrazione bag-panel-roi: --calibrate-bag-panel (chiude il gap strumentale lasciato da C-312) | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: Scheletro locale (local_generate_skeleton, costo zero, corretto a mano solo per lo stile del namespace file-scoped). Infilling locale (code_agent.py): 2 tentativi bloccati da un falso positivo del confronto firme (spazio spurio dopo la parentesi su firme multi-riga) e una costante AcquireAttempts dimenticata dal modello -- materializzato a mano dal codice altrimenti corretto del tentativo rifiutato, dopo averlo letto per intero. HudCropWriter guadagna TrySaveBagPanelPreview/BagPanelPreviewFileName senza toccare l'esistente (preview separata dall'inventario, cosi' calibrare un pannello non sovrascrive l'altro). --calibrate-bag-panel collegato in Program.cs, stesso schema di --calibrate-inventory-panel ma senza il conteggio fisso di token. 13 nuovi test in BagPanelCalibrationProbeTests.cs, tutti verdi. preflight_contract_check APPROVED. Suite completa: 3894 test .NET (0 falliti, 10 ignorati). Lo strumento e' pronto ma la calibrazione vera resta ineseguibile senza un client reale: quello e' il passo successivo, quando l'operatore e' al client (come gia' per T-12).
  - note: Aperto perche' l'operatore ha chiesto di procedere con la calibrazione bag-panel-roi al client, e verificato che lo strumento per farlo non esisteva: C-312 aveva costruito solo il formato del file (BagPanelRoiCalibration.Confirmed/Load/Save/Resolve), non il probe interattivo -- lo stesso ruolo che InventoryPanelCalibrationProbe/--calibrate-inventory-panel gia' hanno per l'equipaggiamento (T-12, 2026-09-06). Speculare a quel probe, ma NON tutto-o-niente: la borsa scorre, quindi BagPanelRoiCalibration.Confirmed (gia' cosi' da C-312) accetta un numero qualunque di slot (almeno uno), mai un conteggio fisso di 18 come i EquipmentSlot dichiarati.

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
| C-311 | Innesto parziale C# in scripts/code_agent.py (tree_sitter, non ast) | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: 17 test in tests/test_code_agent_parziale.py (10 preesistenti invariati, 7 nuovi per il ramo C#). Due bug del locale corretti a mano: apostrofo non escapato in stringa (SyntaxError reale), ricerca metodi limitata ai figli diretti della radice (non trovava mai un metodo reale, tutte le classi del progetto stanno in un namespace). preflight_contract_check APPROVED. Suite Python completa verde.
  - note: innesta_funzioni usava ast.parse (Python puro): su un file .cs sollevava sempre SyntaxError, quindi solo_funzioni respingeva ogni incarico C# con 'Innesto parziale rifiutato' anche su codice valido, nonostante .claude/CLAUDE.md sezione 7 dichiari gia' che solo_funzioni copre entrambi i linguaggi. Prerequisito pratico per completare C-310 su AutoplayCommand.cs senza riemetterlo per intero.
| C-315 | Innesto parziale Python riconosce i metodi di classe | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: 27/27 test target verdi, suite Python completa senza regressioni, preflight APPROVED, commit 7b6507e
  - note: _intervalli_funzioni camminava solo albero.body (funzioni di primo livello): ogni incarico con solo_funzioni su un metodo Python falliva sempre con 'assente dallo scheletro', anche quando il metodo esisteva -- osservato il 2026-09-12 sull'incarico C-314. Ora cammina anche dentro le ClassDef a qualunque profondita', con textwrap.dedent per i frammenti rientrati, precedenza alla funzione di modulo sull'omonimo metodo, e ValueError se lo stesso nome di metodo compare su due classi diverse. innesta_funzioni non e' stata toccata.
| C-314 | Potatura dei binding orfani dal registro ruoli MCP | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: 6/6 test nuovi verdi (tests/test_role_binding_prune.py), suite Python completa senza regressioni, preflight APPROVED su entrambi i file, commit 20936fd
  - note: RoleBindingRegistry.__init__ chiamava solo ensure_default_bindings (solo inserimenti): un ruolo rimosso da DEFAULT_EMPLOYEE_ROLES (fusione product_manager+game_ai_architect in product_architect) lasciava una riga orfana che mcp_role_catalog elencava e propose/choose rifiutavano con KeyError. prune_unknown_bindings cancella le righe orfane con due salvaguardie (insieme noto vuoto, o cancellazione che svuoterebbe la tabella) e __init__ la chiama dopo ensure_default_bindings. Incarico diviso in due file con un solo test condiviso: due passate (gratis_lento) hanno fallito riscrivendo il file intero o troncando; su Qwen3 Coder 30B (consenso operatore gia' registrato per C-315) un tentativo ha rivelato un bug reale in innesta_funzioni (indentazione di metodo sbagliata, corretto separatamente) e uno un difetto di sequenziamento dell'incarico (le due modifiche non potevano superare insieme un test che le richiede entrambe, se validate un file alla volta); risolto separando il gate di test fra le due passate.
| C-316 | Watchdog periodico del MCP Chief (R-208) | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: 6/6 test verdi, preflight APPROVED, commit 51f472d
  - note: run_health_tick chiama solo chief.health_check() (mai un metodo mutante) e appende un record JSONL schema mcp.chief.watchdog.v1; read_recent_ticks rilegge le ultime N righe in ordine cronologico. scripts/mcp_chief_watchdog.py e' il CLI (--tail N). Un bug reale trovato in revisione manuale (ModelRouter.from_config chiamato senza l'argomento 'policy', che avrebbe fatto fallire ogni invocazione reale) e' stato corretto dopo aver scritto un test che lo riproduceva isolando ROOT/load_config dal vero data/mcp/. Non sostituisce chief.health_check(): lo osserva soltanto.

## Gate 5 — AP-03 - Map Reconstruction (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-501 | MapObservationBatch + MapReconstructionFusion.Merge + MapGridObservationProjector | MERGED |
  - updated: 2026-09-12
  - metrics: 5+8+7 test (MapObservationBatch, MapReconstructionFusion.Merge incluso un test di determinismo end-to-end, MapGridObservationProjector)
  - note: AP-03 (Map Reconstruction). Unica fonte reale: la griglia statica del client (NosAi.Runtime.Navigation.MapGrid, estratta dagli archivi reali via MapGridExtractor), gia' reale e gia' testata prima di questa fase. MapObservationBatch (evidenza tile/portali per una passata) + MapReconstructionFusion.Merge (unione last-write-wins per coordinata/id, bounds monotoni, idempotente) + MapGridObservationProjector (bridge MapGrid -> MapObservationBatch, un Tile per cella, sempre classificato Cached mai Live, nessun portale generato: la griglia non porta identita'/destinazione dei portali). Nessuna classificazione Mob/NPC/oggetti da visione: stesso blocco ML/dati di AP-02, non riaffrontato qui. Vedi docs/agents/phases/AP-03/AP-03_STATUS.md.

## Gate 6 — AP-04 - Exploration & Navigation (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-601 | ExplorationContracts + ExplorationPlanner (footprint, ranking frontiera, NavigationPlan) | MERGED |
  - updated: 2026-09-12
  - metrics: test in ExplorationPlannerTests.cs; --scout consegnato e testato da DeepSeek (commit 1891e7d, d011815, 7080fcd)
  - note: AP-04 (Exploration & Navigation). ExplorationContracts + MovementExecutionContracts (ExplorationFootprint, FrontierCandidate, NavigationWaypoint, NavigationPlan) + ExplorationPlanner (footprint, ranking frontiera, NavigationPlan a singolo waypoint, stessa mappa). Routing multi-mappa via portali esplicitamente rimandato: nessuna fonte dati reale (vedi docs/agents/phases/AP-04/AP-04_A1_STATUS.md). Comando operatore --scout (MovementVerificationProjector) consegnato da DeepSeek dopo indagine su Gate3Runtime che ha trovato candidate generation chiusa/hardcoded e nessun effettore reale per MoveToPosition. Vedi docs/agents/phases/AP-04/AP-04_STATUS.md.

## Gate 7 — AP-05 - Combat Intelligence (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-701 | CombatContracts + CombatPlanner (candidate generation + hard constraints, parziale) | MERGED |
  - updated: 2026-09-12
  - metrics: test in CombatPlannerTests.cs sulla sola generazione candidati/hard constraints
  - note: AP-05 (Combat Intelligence). CombatContracts (CombatActionKind, CombatActionCandidate, CombatConstraintCheck, CombatSimulationResult, ComboStep/ComboPlan) + CombatPlanner parziale: GenerateCandidates/CheckHardConstraints da dati reali (Player.Skills/Cooldowns x Mob in range). Simulazione/combo/apprendimento cross-sessione deliberatamente non affrontati: nessun dato reale di danno/costo skill in AP-01. Verifica combattimento scelta come solo-vitali-player (onesta ma parziale: conferma il costo risorsa, non il colpo sul bersaglio) per non restare bloccati indefinitamente sul gap OCR/ONNX. Vedi docs/agents/phases/AP-05/AP-05_STATUS.md.
| C-317 | SceneManagerFinder: strumento diagnostico standalone per il puntatore scene manager | TEST_VERIFIED |
  - updated: 2026-09-12
  - metrics: 5/5 test verdi, suite completa 2915/0, preflight APPROVED, commit c5ce629
  - note: NosTaleClientLayout.SceneManagerSignature e' un pattern di 25 byte gia' documentato come inaffidabile (ha risolto 0xFFFFFFFF filler sul client live il 2026-09-03). Questo contratto scansiona le regioni di memoria del modulo cercando un dword che superi sia il controllo strutturale esistente (TryConfirmSceneManager) sia un oracolo di autoidentita': la lista Player del candidato deve contenere l'entity id del personaggio, letto fresco nella stessa passata. File nuovo, indipendente: nessun chiamante esistente toccato (SceneManagerSignature, TryResolveScene, WalkCommand, ScoutCommand, Program.cs) per istruzione esplicita dell'operatore -- resta uno strumento di ricerca, non integrato nella pipeline di produzione senza conferma separata. Eseguito live contro il client NosTale in esecuzione il 2026-09-12: nessun candidato ha superato l'oracolo di autoidentita' in questa passata. Non e' un successo da dichiarare: lo strumento e' verificato (test strutturali verdi, nessuna regressione sulla suite completa) ma non ha ancora risolto un puntatore durevole. Sblocca (quando risolvera' un candidato, con conferma esplicita dell'operatore) T-13 in docs/TEST_RIMANDATI.md.

## Gate 8 — AP-06 - Quest Intelligence (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-801 | QuestGraphContracts/QuestGraphPlanner + CollectCommand (--collect, verifica di rete live) | MERGED |
  - updated: 2026-09-12
  - metrics: 7 test (CollectCommandTests.cs) + audit indipendente (AP-06_A5_AUDIT.md), un difetto trovato e corretto in A6
  - note: AP-06 (Quest Intelligence). QuestGraphContracts/QuestGraphPlanner (grafo quest tipizzato) + CollectCommand (--collect <x> <y> <vnum> [<requiredCount>] [--watch <n>], consegnato da DeepSeek): cammina via WalkCommand.Execute riusato, verifica via cattura di rete live reale (LiveScope + GameplayObservationProjector + AssessCollectProgress prima/dopo). Unico obiettivo quest con canale dati reale indipendente dal gap OCR/ML. Audit indipendente (AP-06_A5_AUDIT.md) ha trovato e corretto un difetto (Run non gestiva un argomento vuoto). Limite condiviso non introdotto da questa consegna: OccupancyView sempre null, si rifiuterebbe al primo passo contro un client reale senza un feed di occupazione live. Vedi docs/agents/phases/AP-06/AP-06_STATUS.md.

## Gate 10 — AP-07 - Equipment / Loadout (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-1001 | LoadoutContracts (9 dimensioni DoD) + LoadoutPlanner.GenerateEquipCandidates | MERGED |
  - updated: 2026-09-12
  - metrics: test in LoadoutContractsTests.cs; verificato nel sorgente il 2026-09-07 che LoadoutPlanner.GenerateEquipCandidates e' chiamato da LoadoutReportCommand.cs
  - note: AP-07 (Equipment/Loadout). LoadoutContracts: LoadoutActionKind (Equip/Unequip/Upgrade), LoadoutActionCandidate, LoadoutConstraintCheck, LoadoutEvaluation (le nove dimensioni esatte della DoD: DPS, survivability, resource efficiency, sinergie, enemy-specific performance, movement/utility, costo upgrade, opportunity cost, quest relevance). LoadoutPlanner.GenerateEquipCandidates esiste e viene chiamato da LoadoutReportCommand.cs (verificato nel sorgente il 2026-09-07, nota di revisione in AP-07_A1_STATUS.md che corregge lo stato dichiarato al momento della scrittura). Dipende solo da InventoryItem/EquipmentItem/EquipmentSlot/Player (AP-01, gia' Integrated) e da EnrichedQuestObjective/QuestGraphPlanner (AP-06, Present). Il bridging verso il Safety Gate per equip/unequip/upgrade reale resta aperto (vedi P16 in docs/GUIDA_COMPLETAMENTO_100.md). Vedi docs/agents/phases/AP-07/AP-07_A1_STATUS.md.

## Gate 11 — AP-08 - Strategic Autonomy (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-1101 | StrategyContracts/StrategyPlanner (3/7 StrategicGoalKind con dati reali) + AutoplayCommand (--autoplay) | MERGED |
  - updated: 2026-09-12
  - metrics: 13 test (AutoplayCommandTests.cs) + audit indipendente
  - note: AP-08 (Strategic Autonomy). StrategyContracts/StrategyPlanner: 3 dei 7 StrategicGoalKind valutabili oggi con dati reali (Survival, QuestUrgency, Exploration); gli altri 4 non hanno un assessor per mancanza di dati reali. AutoplayCommand (--autoplay [--cycles <n>] [--recover-slot <slot>], consegnato da DeepSeek): ExecuteOneCycle (puro, testabile) sceglie al piu' uno tra ScoutCommand.ExecuteOneRound/RecoverCommand.ExecuteOneRound da StrategyPlanner.SelectStrategicPlan. Limiti rigidi imposti dall'operatore: solo Survival/Exploration dispatchati, tetto di 20 cicli mai clampato, autorita' sempre --autoplay, nessun bypass Guard/Safety. HTN/GOAP restano non affrontati (dipendono da gap di esecuzione ancora aperti, coerente con ADR-0031: nessuna FSM esplicita). Vedi docs/agents/phases/AP-08/AP-08_STATUS.md e AP-08_A1_STATUS.md.

## Gate 12 — AP-09 - Memory / Adaptive Knowledge (100%)

| CID | Titolo | Stato |
|---|---|---|
| C-1201 | AdaptiveKnowledgeContracts + FileSystemAdaptiveKnowledgeStore + AdaptiveKnowledgeIngestionEngine + KnowledgeStrategyBridge | MERGED |
  - updated: 2026-09-12
  - metrics: test in AdaptiveKnowledgeStoreTests.cs e AdaptiveKnowledgeIngestionEngineTests.cs
  - note: AP-09 (Memory/Adaptive Knowledge). Scoperta preliminare: a differenza di AP-04..AP-08 questa fase non parte da zero. Memory/AdaptiveKnowledgeContracts.cs (KnowledgeScope: 7 valori Universal/Progression/Class/Specialist/Context/Character/Environment; KnowledgeStatus: 7 stati del lifecycle Discovered..Deprecated), FileSystemAdaptiveKnowledgeStore (store reale su filesystem, scrittura atomica file temp+move), InMemoryStore/MemoryTypes (rifiuta un record con provenance Unknown a meno che non sia esplicitamente Reasoning), Knowledge/AdaptiveKnowledgeIngestionEngine (ForbiddenMarkers rifiuta fonti che menzionano gm/admin/server database/packet injection/exploit/hack/dupe/bot prima che una candidate knowledge venga salvata; EvidenceKnowledgeValidator promuove Candidate->Tested->Validated->Verified solo con evidenza osservata indipendentemente), KnowledgeStrategyBridge (AdaptiveStrategyMemory interroga lo store per obiettivo, filtra solo Validated/Verified). Presente fin dal primo commit dell'attuale main: non scritto nella sessione AP-00->AP-08. Vedi docs/agents/phases/AP-09/AP-09_A1_STATUS.md.

## Domande aperte

- C-003/C-004/C-005: RISOLTA da ADR-0029 del 2026-09-11 (docs/adr/ADR-0029-phase5-dotnet-pytest-no-native-branch.md) — nessun ramo nativo, la FASE 5 si misura con dotnet build/test e pytest, non con ASan. I tre contratti chiudono come scopi che non esistono (DROPPED).
- C-204: RISOLTA da ADR-0030 del 2026-09-11 (docs/adr/ADR-0030-csharp-owns-game-state-python-governs-models.md) — il runtime C# possiede lo stato di gioco, nosai/ Python governa i modelli. Due canali asimmetrici, implementati e testati (TEST_VERIFIED).
- C-304: RISOLTA da ADR-0031 del 2026-09-11 (docs/adr/ADR-0031-no-explicit-fsm.md) — nessuna FSM esplicita: Planner/Orchestrator (HTN/GOAP) non sono nel percorso di esecuzione (ADR-0028), quindi non c'e' autorita' da duplicare. Il contratto chiude (DROPPED); si riapre con un caso nominato.
- C-105: RISOLTA da ADR-0032 del 2026-09-11 (docs/adr/ADR-0032-no-memory-hook-yet.md) — nessun hook di memoria o DLL injection nel client ora; il canale resta rete + lettura esterna di memoria via DllImport. Si riapre con un caso nominato.

## Note

- gates[].gate is a legacy grouping, NOT a product AP number. contracts[].product_phases maps to ROADMAP_ESECUTIVA. Existing status and verification are historical evidence, not revalidated by documentation reconciliation.
- C-103, C-104, C-106, C-203 and C-401 signatures were resolved from canonical source symbols on 2026-09-10. Status remains historical unless the linked verification command is executed.
