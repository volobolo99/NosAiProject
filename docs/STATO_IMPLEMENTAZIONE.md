# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-09-01

## Regola di avanzamento

Il progetto non avanza in base al semplice completamento di singoli file. Avanza attraverso **obiettivi significativi verificabili**.

Il primo obiettivo operativo obbligatorio è raggiungere un collegamento reale e verificato:

`NosAi PC ↔ client NosTale ↔ rete ↔ Guard AI smartphone`

con acquisizione dei primi dati di base del client e del PC e visualizzazione/gestione corretta nella dashboard al livello raggiunto.

Ogni obiettivo significativo crea un gate. Il gate deve essere superato con test pertinenti prima di iniziare implementazioni successive. Un test fallito blocca l'avanzamento fino alla correzione e alla ripetizione del test con esito positivo.

---

## Classificazione di maturità adottata

| Livello | Significato |
|---|---|
| **Present** | Il codice o il contratto esiste nel repository. |
| **Partial** | Il blocco esiste ma è incompleto, simulato o non sufficientemente collegato. |
| **Integrated** | Il blocco è collegato ad altri componenti rilevanti del runtime. |
| **Verified** | Il blocco è coperto da test credibili o evidenze esecutive pertinenti. |
| **Operational** | Il blocco è confermato nel flusso reale previsto dal progetto. |

Questa classificazione deve essere usata per distinguere chiaramente tra presenza del codice e maturità operativa.

---

## Stato sintetico corrente

Il **Gate 1 è superato con evidenza reale**. Il registro autorevole di quella
evidenza è `docs/GATE1_CHECKLIST.md`: questo documento vi si allinea e non è mai
la fonte da cui dedurre lo stato del Gate 1.

La valutazione corrente è la seguente:

- il primo circuito reale `NosTale → runtime PC → telefono Android` è chiuso e osservato, su USB e su Wi-Fi;
- molte aree oltre il Gate 1 sono **Present** o **Integrated**, nessuna è **Verified**;
- restano tre limiti dichiarati sul canale, elencati sotto, **nessuno dei quali blocca il Gate 1**;
- il gameplay del client (HP, mappa, entità) resta **UNKNOWN** finché l'operatore non accende l'osservazione (`--observe-game` / Control Panel) e il driver consegna un `stat`. Il percorso è costruito: `WinDivertPacketSource` → `ReassembledObservationSource.ForNosTaleWorld` → `GameTrafficObserver` + `NosTaleWorldProtocolDecoder` → `NetworkWorldFeed` → `NetworkGameplayProvider` → snapshot Gate 1. Assente l'opzione, il motivo resta `gameplay_provider_not_available`. Senza driver l'host parte comunque e il motivo nominato di `TryOpen` finisce nel log. Nessun valore viene inventato al suo posto.

Quel che resta dei tre limiti del canale Guard, ripreso dalla checklist:

1. la cifratura del payload (wire v3, ADR-0009) è **implementata e provata in locale contro il processo runtime reale, non ancora sul dispositivo fisico**;
2. la sessione resta una sola, ma l'ammissione ora è concorrente (ADR-0011): uno squatter silenzioso non tiene più fuori il telefono. Resta possibile occupare i posti *parlando* e poi fermandosi;
3. le chiavi di identità non sono più in chiaro (ADR-0010): DPAPI sul PC, Android Keystore sul telefono. Resta che DPAPI lega all'account Windows e non a un TPM, e che il Keystore **non è stato provato su dispositivo**.

---

## 🟢 Present o Integrated a livello di codice

- Contratti fondamentali e base decisionale deterministica.
- Confine Safety Gate e integrazione Orchestrator.
- Autorizzazione runtime a capability (M020–M027): il runtime ora sa **chi** chiede, non solo quanto è rischiosa l'azione. `SecurityPrincipal` (operatore, dispositivo Guard, agente autonomo, sottosistema) × `RuntimeCapability` (osservare, chiedere un comando, agire nel gioco, input, injection, leggere traffico, leggere memoria), con `Gate1AuthorizationPolicy` **fail-closed**: principal sconosciuto, capability sconosciuta e tutto ciò che non è in allow-list vengono negati, quindi una capability aggiunta senza regola cade su deny invece che in un varco. Il `SafetyGate` non restituisce più un `false` muto: ogni rifiuto porta un motivo strutturato (`execution_disabled_in_gate1`, `guard_refused:…`, `capability_not_granted`, `trust_tier_insufficient:…`), perché un gate i cui rifiuti sono indistinguibili non è verificabile. **L'esito non è cambiato**: a Gate 1 nessuna azione è autorizzata, un test lo blocca per ogni `ActionKind`. Il telefono non può far catturare traffico o leggere memoria al PC — ADR-0014 ha allargato i percorsi dati, non chi può usarli. La UI non è policy: chiede, il runtime decide.
- Interruttori di esecuzione sotto controllo dell'operatore: `RuntimeSafetyController` sostituisce la policy fissa catturata alla costruzione. Esecuzione, input diretto e injection pacchetti sono ora **interruttori reali** — l'operatore li accende e li spegne dalla dashboard (`GET`/`POST /api/safety`), e ogni cambio finisce in uno storico con prima/dopo, principal e motivo. Restano **spenti all'avvio**: un runtime che si accendesse già armato agirebbe prima che tu abbia deciso che debba farlo. `executionMode` non è più l'etichetta fissa `disabled_in_gate1` ma è **derivato** dallo stato reale (`enabled_by_operator` / `disabled_by_operator`): un'etichetta che non segue lo stato non è un fatto. A cambiarli può essere solo `SecurityPrincipal.Operator`; il telefono chiede, non decide, e un dispositivo rubato non arma il PC. L'EMERGENCY STOP disarma prima e chiude la sessione dopo, perché l'ordine inverso lascerebbe acceso proprio il pezzo pericoloso. `GatedInputBackend` rilegge la policy **a ogni chiamata**, quindi accensione e spegnimento hanno effetto immediato senza copie stantie da cui re-iniettare.
- Autorità di attuazione legata alla sessione (`SessionActuationAuthority`, P3): prima di offrire al livello decisionale la capacità di agire, il runtime confronta il proprio livello di integrità con quello del client e poi lo **prova** — un movimento di puntatore di pochi pixel dentro la finestra del client mentre questa è in primo piano, riletto e riportato dov'era. Il caso che rende necessaria la prova è che `SendInput` da un processo a integrità media verso un client a integrità alta **fallisce senza dirlo**: né il valore di ritorno né l'ultimo errore lo segnalano, e un ciclo di ritentativi legge quel silenzio come « il gioco non risponde » e gira per sempre. Due rifiuti sono **terminali e non vengono più riprovati** — runtime sotto il client, e puntatore che non arriva dove è stato mandato — perché richiedere di nuovo non li cambia e farebbe sobbalzare il puntatore dell'operatore a ogni ciclo; se ne esce con una sessione nuova o con un `Reset` dell'operatore. Una sessione non attuante **non espone alcuna capacità di attuazione** (`InputActionEffector.UnavailableReason`) invece di esporne una che fallisce all'uso, e l'osservazione continua intatta. Verdetto valido 60 s e ripreso al ritorno in primo piano. Evidenza **locale** (23 test): il giro con il client elevato non è ancora stato fatto, e la superficie CLI/dashboard che lo mostra è la sessione `S1` di `docs/SESSIONI_CURSOR.md`.
- L'arresto d'emergenza dell'operatore ora **abbandona anche l'atto in volo** (`GatedInputBackend.AbortOpenScope`), non solo gli interruttori. Disarmare rifiuta la chiamata successiva e non dice nulla del tasto che il programma in corso ha già premuto: prima di questa modifica un EMERGENCY STOP durante un atto che tiene un tasto disarmava il runtime e **lasciava il tasto premuto**, cioè l'unico stato in cui fermarsi peggiorava le cose. L'ordine è disarmo, poi abort, poi chiusura della sessione.
- Lettura della memoria del processo client (ADR-0014): `ProcessMemoryReader` apre un handle **di sola lettura** e solo dopo aver autorizzato `RuntimeCapability.ReadProcessMemory`; non esiste un percorso di scrittura in questa classe. Un valore è `LIVE` **solo** se supera un controllo di plausibilità fornito dal chiamante, altrimenti è `UNKNOWN` con il motivo: un offset spostato da una patch non fallisce, restituisce quattro byte perfettamente leggibili, e questa è esattamente la ragione per cui il controllo non è opzionale. Lettura parziale, indirizzo nullo, lunghezza assurda e reader chiuso sono fallimenti espliciti, mai mezzi valori. Il rischio per l'account resta reale e resta di chi decide, come registra ADR-0014.
- Lettura degli archivi `.NOS` del client (`NosArchive`): primo passo del database di riferimento. Il formato **non è stato preso da documentazione ma ricavato dai byte e poi verificato**, perché una struttura indovinata produce voci plausibili e sbagliate invece di fallire. Due contenitori riconosciuti: **con nomi** (`count` + voci + un trailer di 12 byte, misurato identico su tutti e 18 gli archivi che lo usano) e **numerato** (banner ASCII, indice di coppie `(id, offset)`; l'invariante che lo stabilisce è che l'indice finisca *esattamente* dove comincia il primo payload). Il riconoscimento è **strutturale**, non per stringa magica, così `NT Data 02…26` e `32GBS V1.0` sono letti dallo stesso codice. Gli `id` sono gli identificativi del gioco, non posizioni — in `NSmpData01.NOS` partono da 1024 perché sono id di mappa — e non vengono mai rinumerati. Sull'installazione reale: **167 archivi, 165 letti, 258 758 voci localizzate**; i 2 restanti (`CCINF V1.20`) sono dichiarati **non ancora supportati con il loro banner**, mai saltati in silenzio. Sola lettura, con condivisione piena del file: il client può restare aperto e nulla scrive nella sua installazione.
- Database di riferimento dai dati del client (`GameReferenceDatabase`, `NosDataTable`, `ReferenceImporter`). La catena di decodifica è stata **ricavata dai byte e verificata**, mai presa da documentazione: `.NOS` → payload → zlib (dove presente, riconosciuto perché le lunghezze dichiarate combaciano) → `XOR 0x33` → testo tabulato. Due punti sono costati un errore ciascuno, ed entrambi sono documentati nel codice: la chiave `0x13` produce lettere leggibili ma trasforma ogni tab in `)`, quindi è sbagliata; e i valori di **tre o più caratteri non sono testo** ma nibble impacchettati (`0x8N` = quanti caratteri, poi `nibble = carattere − 0x2C`, regola fissata sui 2 705 identificativi dei mostri che sono monotoni). Senza il secondo, ogni statistica grande — punti vita, danno — sarebbe semplicemente sparita da un'importazione che sembrava riuscita.
- Un terzo errore ha insegnato più dei primi due: letto quell'alfabeto come sole cifre, 7 555 valori restavano non decodificabili e il conto si fermava al 99,61%. **Non erano numeri rotti: erano tassi e moltiplicatori.** Un nibble 2 è un punto decimale e un nibble 1 è un meno, quindi `83 26 40` è `.20` e `83 2b 40` è `.70` — proprio nei campi che servono a calcolare un danno. Trattarli da interi guasti avrebbe buttato via ogni frazione del gioco riportando un successo del 99,6%. Con l'alfabeto completo la copertura è **100,00% su 1 428 698 valori, zero `UNKNOWN`**, e un test lo blocca a quel valore esatto: una soglia più bassa lascerebbe spazio a una regressione per nascondersi.
- Semantica dal client, non per inferenza (`ImportLanguage`): `NSlangData_IT.NOS` contiene 12 tabelle di lingua e **22 929 voci** che traducono le chiavi in ciò che il gioco mostra — `zts1e` → *Volpe*, e per le battle card → *Attacco Speciale*, *Impedisce l'Attacco Ravvicinato*, cioè il significato del sistema di effetti dichiarato dal client stesso. Una lingua non installata viene **riportata, mai sostituita** con un'altra. Limite dichiarato: oltre le chiavi che non richiedono impacchettamento, fra il numero e la `e` finale c'è un byte in più — costante nelle tabelle dati, variabile in quelle di lingua — e le due numerazioni non si allineano; oggi si risolve il nome di ~100 entità per tabella. Il test fissa ciò che funziona invece di affermare un tasso che sarebbe falso.
- Cosa contiene, misurato sull'installazione reale: **15 279 record** (2 705 mostri, 7 726 oggetti, 1 958 abilità, 2 759 carte, 131 battle card) e **1 428 698 valori, di cui il 99,61% decodificato**. Il resto è `UNKNOWN` **con il motivo**, mai un numero plausibile messo lì per riempire: un totale di punti vita sbagliato è peggio di uno mancante, perché solo uno dei due si annuncia. Ogni riga porta la sua **provenienza** (archivio, tabella, hash del contenuto, quando, da quale installazione); una riga senza provenienza è un difetto che il controllo di integrità segnala. Ciò che i nomi dei campi dichiarano (`VNUM`, `LEVEL`) diventa colonna tipizzata; **quale slot di `ATTRIB` sia l'elemento non è dichiarato da nessuna parte e non viene indovinato** — quell'interpretazione appartiene a uno strato successivo che potrà essere confrontato con ciò che succede davvero in gioco.
- Ciclo di apprendimento dagli errori propri (`PredictionLedger`). Registra **prima** di agire cosa si aspetta, poi confronta con l'osservato e aggiorna la credenza. Riusa `BetaBinomialEvidence` di Gate 4 invece di duplicarla: mancava il registro, non la matematica — Gate 4 impara quale strategia di quest funziona, e nessuno imparava se una previsione sul momento successivo fosse buona. **La regola che lo rende onesto: solo un esito `LIVE` muove una credenza.** Un esito simulato, derivato o sconosciuto viene contato e resta visibile ma non insegna nulla: un sistema che impara dalle proprie previsioni converge, in fretta e con sicurezza, sulla propria fantasia — e dall'interno è indistinguibile dal diventare molto bravi. Una previsione mai risolta viene abbandonata, non contata: non è né giusta né sbagliata. Le calibrazioni si leggono per contesto, ordinate dalla peggiore, perché la domanda utile è dove il modello sbaglia.
- Backend di cattura reale per il canale di percezione (`ScopedLiveCaptureBackend`). Resta l'implementazione di `IRawScopedCaptureBackend` (un segmento TCP in ordine di arrivo, etichetta LIVE). **Non è più il percorso gameplay**: quello è `ReassembledObservationSource.ForNosTaleWorld` in `Gate1BootstrapHost`, perché un payload di segmento letto a offset fissi è esattamente il fallimento che il riassemblatore chiude. Senza driver dichiara il motivo e non osserva nulla: un ripiego sintetico darebbe al world model byte inventati con l'etichetta LIVE.
- World Model, Party, Pet e Partner.
- Coordinated Action Manager.
- Tactical Action Ranking e fondazioni Simulation/Lookahead.
- Contratti Perception, pipeline iniettabile, visione ROI e fondazione tracking.
- Fondazione Game State Evaluator e adapter Perception → WorldState.
- Fondazione Agent Runtime: sessioni, memoria, instradamento provider local-first, risorse, policy e Trust Tier 0–4.
- Ciclo Planner → Guard → Safety → Executor → Verifier multi-step.
- Retry/ripianificazione, checkpoint e watchdog indipendente.
- ToolRegistry, profilazione hardware, contratti LAN e protezione sequenza/replay.
- EventBus bounded, WorldState versionato e Context Slimming.
- Registro eventi **durevole e riproducibile** (M075–M076): ogni evento riceve un `seq` monotono all'inserimento, quindi il log ha un ordine totale e due riletture dello stesso store danno la stessa sequenza — timestamp e `frame_index` si ripetono e da soli non ordinavano niente. `EventLogReader` rilegge in quell'ordine, per sessione o intero. Le **perdite sono registrate**: quando il bus si riempie e scarta eventi, il contatore in memoria diventa una riga di gap nello store, e `EventLogReplay.IsComplete` risponde alla domanda che un consumatore ha davvero. Migrazione dello schema preesistente senza perdere righe.
- Stato di rete del client (`ADR-0014`): il runtime legge dalla tabella TCP di Windows quali socket possiede il processo del client — `networkConnected`, `serverEndpoint`, `connectionState`, `remoteSessionCount`, additivi su `gate1.snapshot.v1`. Verificato sul client reale: `79.110.84.175:4006 Established [LIVE]`. Nessun payload, nessun driver, nessuna elevazione; con più sessioni remote l'endpoint resta `UNKNOWN`, non indovinato.
- Cattura del traffico (`ADR-0014`): motore completo e testato — parser IPv4+TCP, riassemblatore per direzione (fuori ordine, ritrasmissioni sovrapposte, gap che fermano l'output, wrap dei sequence number con aritmetica seriale RFC 1982), e `GameTrafficCaptureEngine` che li compone dietro un'interfaccia `IPacketSource`. La sorgente è astratta: WinDivert dal vivo, un file `.noscap` registrato, o pacchetti sintetici, e il motore non li distingue — così tutto tranne il driver è provato in CI. **Registrazione su file**: una sessione si cattura una volta col driver e poi si rigioca e decodifica offline, senza driver a ogni giro. Sopra sta l'interfaccia di framing: l'unica implementazione dichiara onestamente `UNKNOWN` finché non c'è un decoder NosTale, e un test dimostra che il decoder si innesta senza toccare il motore.
- Analisi della cattura (`ADR-0014`): `CaptureAnalyzer` misura un `.noscap` per direzione — quantità, cadenza, distribuzione delle lunghezze di payload, primo byte più frequente — sul flusso **riassemblato**, così ritrasmissioni e fuori ordine non falsano i numeri, e conta i pacchetti scartati perché rumore di un'altra conversazione non sembri protocollo pulito. Presenta il byte dominante come **candidato opcode/lunghezza, mai una conclusione**: serve a scrivere il decoder da evidenza empirica, non a indovinarlo. Raggiungibile senza driver: `WinDivertProbe.exe --analyze <file>`.
- **Cattura reale già avvenuta (T-04 chiuso)**. Resta da accendere l'osservazione in una sessione in corso (`--observe-game`) e confermare `LIVE` (T-05). Il driver WinDivert va installato in `tools/windivert/` e la console elevata; senza di esso il runtime parte comunque e dichiara il motivo.
- RecoveryController adattivo, circuit breaker e Runtime/HW Watchdog.
- Throttling adattivo del runtime.
- Timeout fail-fast e contratto Protobuf v3.
- Nucleo crittografico X25519 + HKDF-SHA256 + ChaCha20-Poly1305.
- Persistenza SQLite iniziale per sessioni/traiettorie.
- Controller Miniland tramite adapter.
- Framing binario PC↔telefono con `MAGIC/VERSION/TYPE/PAYLOAD_LEN/SEQ`, `SequenceGuard` e delta encoding deterministico del WorldState.
- Canale Guard con **autenticazione mutua** (wire version 2, ADR-0008): transcript legato alla sessione e al ruolo, prova del runtime (`ServerAuthProof`) verificata dal telefono prima che firmi, versione 1 rifiutata. Contratto condiviso C#/Python, vettori pinnati dai test su entrambi i lati. Eseguito su dispositivo Android fisico su USB e su Wi-Fi: evidenza in `docs/GATE1_CHECKLIST.md`, sezione *Evidenza reale — autenticazione mutua, wire v2*.
- Fondazione deployment su storage dedicato e provisioning ADB di Guard AI.
- Suite negativa sul confine del canale Guard: 15 test che guidano il canale reale su socket reale e verificano ciò che deve essere **rifiutato** — header wire v1 e v2, magic di discovery al posto di quello di sessione, firma da chiave non fidata (con la prova che nessuno snapshot esce comunque), frame in chiaro prima dell'autenticazione, `AuthResponse` senza hello, hello senza chiave effimera, punto non sulla curva, sequenza fuori ordine, firma replicata da una sessione precedente, e il rifiuto lato telefono di un runtime non pinnato. Ognuno asserisce il motivo strutturato, non solo che qualcosa è fallito.
- App Guard AI: riconnessione automatica con backoff limitato, che distingue una causa passeggera da una che richiede l'operatore e **non ritenta** la seconda; stato di sicurezza **letto** dallo snapshot invece che affermato dallo schermo; custodia della chiave del dispositivo mostrata (Keystore o file, col motivo). La logica sta in `NosAi.GuardClient` proprio per poter essere testata senza telefono.
- Gate 2 completo a livello di codice: WorldStateSnapshot immutabile con stato iniziale non osservato, riduzione deterministica delle osservazioni (`WorldModelReducer`: upsert/rimozione entità, scadenza staleness, cambio mappa, campi non osservati preservati), BoundedEventBus con priorità e drain garantito alla chiusura, slimming errori/contesto per VRAM (parità con `nosai/runtime/context_slimming.py`), persistenza SQLite WAL reale (policy centralizzata allineata a `nosai/storage/sqlite_policy.py`, `foreign_keys=ON`), sessioni e traiettorie con vincolo di integrità (parità con `nosai/persistence/sqlite_logger.py`), delta encoding con ricostruzione (`ApplyDelta`), codec binario versionato `G2D` v1 con risparmio banda ≥70% misurato, `DeltaSyncTracker` con resync fail-closed e composizione `Gate2IntegratedEngine`.
- Suite automatica `Gate2TestRunner` (22 check nominali) integrata nel runtime principale (`--gate2-test`) e agganciata a `NosAi.Runtime.Tests`.
- Gate 4 integrato a livello di codice: Progression Engine V2, DAG missioni, sblocco SP, Beta-Binomiale, UCB1/MAUT e Knowledge Base.
- Suite automatica `Gate4TestRunner` integrata nel runtime principale.
- Gate 5 completo a livello di codice: Provider Router local-first con escalation cloud fail-closed dietro autorizzazione esplicita, provider di inferenza dichiarati SIMULATED (nessuno stub etichettato come inferenza reale), Hardware Baseline con provenienza per campo (LIVE/UNKNOWN, niente valori inventati), storage discovery che riporta onestamente il fallback quando `NOSAI-SSD` è assente, Eye AI View a 3 strati con provenienza per strato (UNKNOWN senza sorgente reale, mai `IsSafetyAuthorized` senza autorizzazione) e Control Center REST loopback con allowlist comandi e enum in forma wire.
- Suite automatica `Gate5TestRunner` (13 check nominali) integrata nel runtime principale (`--gate5-test`) e agganciata a `NosAi.Runtime.Tests`.
- Gate 6 completo a livello di codice: check di integrazione sui componenti canonici reali (wire format `NOSA` da NosAi.Protocol al posto della copia divergente `NOS1`, `SessionAuth` RSA-2048 monouso del Gate 1, DAG di progressione del Gate 4, router del Gate 5), ciclo chiuso Plan→Safety→Execute→Verify su `SimulatedGameWorld` esplicito con iniezione di discrepanza e recovery certificati, watchdog termico fail-closed su temperatura sconosciuta, messaggistica onesta (solo evidenza locale, nessuna dichiarazione di rilascio).
- Suite automatica `Gate6ReleaseCertifier` (14 check nominali) integrata nel runtime principale (`--gate6-test`) e agganciata a `NosAi.Runtime.Tests`.
- Sottosistema Navigation/Pathfinding nel runtime (`src/NosAi.Runtime/Navigation/Pathfinding`). Il file placeholder `src/NosAi/Navigation/Pathfinding/` è stato rimosso: non era in nessun csproj.
- Sottosistema Economy/Inventory presente nel repository con implementazione dedicata.

Questa sezione indica presenza di codice o integrazione parziale/funzionale nel repository; **non equivale al superamento del gate operativo reale**.

---

## 🟡 Aree Partial da integrare e verificare

- Collegamento reale NosAi ↔ client NosTale: **OS baseline verificato sul client reale**; gameplay HP/mappa/entità ancora UNKNOWN finché l'osservazione non è accesa e il canale non ha letto un `stat`. Il decoder world esiste (`docs/PROTOCOLLO_NOSTALE.md`). Percezione da schermo classificata **DERIVED**, mai LIVE (ADR-0012).
- Lettura affidabile dei dati di base necessari dal client (OS baseline presente; provider gameplay attaccabile con `--observe-game`, non ancora verificato LIVE su sessione in corso).
- Acquisizione dei dati di base del PC nel runtime operativo (RAM processo LIVE; CPU/GPU di sistema UNKNOWN se il probe non riporta valori).
- Custodia delle chiavi di identità (`ADR-0010`): implementata su entrambi i lati. Sul PC l'identità è avvolta con DPAPI in `data/runtime_identity.dpapi` e la migrazione dal PEM in chiaro **conserva la stessa chiave**, quindi nessun telefono già abbinato deve rifare l'abbinamento — verificato sull'identità reale di questa macchina. Sul telefono la chiave è generata **dentro** l'Android Keystore. **Resta Partial**: DPAPI lega la chiave all'account Windows e non a un TPM, e il giro Keystore non è stato eseguito su dispositivo fisico; dove il Keystore manca l'app ripiega su file e **lo dichiara** invece di sembrare protetta.
- Sessioni Guard concorrenti (`ADR-0011`): il canale serve **una sola sessione**, ma ammette fino a quattro candidati che fanno l'handshake in parallelo, e vince chi si autentica per primo. Lo stato dell'handshake è ora per connessione, non più campi condivisi. Uno squatter silenzioso non tiene più fuori il telefono: viene sfrattato per primo e il telefono si autentica accanto a lui. **Resta Partial**: un peer che parla e poi si ferma può ancora occupare i quattro posti.
- Cifratura autenticata (AEAD) del payload di sessione: implementata e verificata in locale (wire v3, ADR-0009), **non ancora provata sul dispositivo fisico**. Il telefono va reinstallato: un APK v2 viene rifiutato all'header, come previsto.
- Dashboard collegata solo al runtime reale e completa per il livello di sviluppo corrente.
- Diagnostica del registro eventi (`EventLogDiagnostics`): un lettore sola-lettura fuori dal runtime che riporta la **salute** dell'audit trail — eventi totali, intervallo di sequenza, e soprattutto `IsComplete` con i gap registrati, così una perdita è visibile e non silenziosamente assente. Serializza in JSON piatto (un pannello lo consuma via HTTP) e ha un comando CLI `--event-log-report [path]` che esce non-zero se il log è incompleto. Verificato su un registro Gate 2 reale. Cursor può mostrarlo chiamandolo dal pannello.
- Resta aperto: il **transport HTTP** degli eventi verso il lato Python. Il lettore e il formato JSON ci sono; agganciare un endpoint richiede una decisione su dove comporlo (tocca Gate1↔Gate2), non forzata qui.
- PredictionEvaluator e metriche produttive.
- Generazione binding Protobuf C++/TypeScript.
- Discovery hardware e benchmark reali.
- Shared Memory nativa e N-API.
- Persistenza analitica completa.
- Sandbox strumenti e capability enforcement.
- Backend produttivi DXGI, Triple Buffer, YOLO, OCR, Kalman e mapping specifico.
- Adapter live del gioco.
- Provider locale `llama.cpp` e provider cloud produttivi.
- Benchmark IPC e Saturazione Controllata.
- Integrazione Miniland con client reale.
- ArrayPool/Memory/Span e caricamento modelli on-demand nel percorso C#/.NET 8.
- Test di integrazione runtime per Navigation/Pathfinding e Economy/Inventory e loro collegamento ai dati reali del client.
- Riallineamento della suite di test tra runtime C# attuale, test Python esistenti e prove end-to-end autorevoli.

---

## 🟢 Gate 1 — superato con evidenza reale

I sette criteri formali sono soddisfatti; la trascrizione delle osservazioni è in
`docs/GATE1_CHECKLIST.md`. Le voci qui sotto marcate **reale** rimandano a quella
evidenza; quelle marcate **locale** sono coperte da test automatici e non da un
giro sul sistema reale.

### Stato di maturità del percorso critico

| Componente critico | Maturità attuale | Nota |
|---|---|---|
| **Bootstrap runtime PC** | **Verified** | Avviato sul PC target con NosTale in esecuzione: `Health: Healthy`, Guard 17471, API operatore 8766. |
| **Protocollo/sessione PC ↔ smartphone** | **Verified** su wire v2; **Integrated** su wire v3 | v2 mutuo validato end-to-end su dispositivo fisico, USB e Wi-Fi. v3 cifra il payload con AES-256-GCM su chiavi effimere (ADR-0009): provato in locale contro il processo runtime reale, **non ancora sul telefono**. |
| **Client connector NosTale** | **Verified** (OS baseline) | Validato contro NosTale reale: processo/PID/titolo/handle/responding/visible `LIVE`. Gameplay resta **Partial**: il provider si attacca con `--observe-game`; manca la conferma `LIVE` su sessione in corso (T-05). |
| **Guard AI smartphone** | **Integrated** | Eseguita su dispositivo fisico: si abbina, si autentica in modo mutuo e riceve lo snapshot reale. Resta **Partial** la custodia della chiave, che non è in Key Store. |
| **Dashboard / Control Center** | **Partial** | Base tecnica presente, ma va resa coerente esclusivamente con segnali reali. |
| **Perception / acquisizione dati di gioco** | **Partial** | Contratti e fondazioni presenti; backend produttivi ancora incompleti. |
| **WorldState reale** | **Integrated** | Struttura presente, ma ancora dipendente dal completamento delle sorgenti reali. |

> Ogni voce segnata qui sotto ha una prova registrata in `docs/GATE1_CHECKLIST.md`.
> **reale** significa osservato sul sistema o sul dispositivo reale; **locale**
> significa coperto da test automatici soltanto. Ciò che non ha prova resta
> aperto.

### NosAi PC

- [x] Avvio affidabile sul PC. *(reale: `Health: Healthy` con NosTale in esecuzione.)*
- [x] Acquisizione dati di base del PC. *(reale: CPU e RAM processo `LIVE`; GPU e RAM di sistema restano `UNKNOWN` quando il probe non riporta valori, e UNKNOWN non viene sostituito da zero.)*
- [x] Collegamento controllato al client NosTale. *(reale: `NostaleClientX` PID 7932, handle `0x8099A`.)*
- [x] Lettura dei dati di base necessari. *(reale per la baseline di sistema operativo; il gameplay HP/mappa/entità resta `UNKNOWN`: il provider esiste ed è collegato, la mappa di protocollo no.)*
- [x] Validazione provenienza, correttezza e freschezza dei dati. *(locale: provenienza per campo nel contratto `gate1.snapshot.v1`.)*
- [x] Gestione client assente, dati incompleti e disconnessione. *(locale: il runtime resta DEGRADED e non inventa gameplay.)*

### Guard AI smartphone

- [x] Avvio affidabile. *(reale: APK installato e avviato sul dispositivo `9125322104AC`.)*
- [x] Connessione a NosAi sul PC. *(reale: USB e Wi-Fi, indirizzo trovato per discovery.)*
- [x] Autenticazione della sessione. *(reale, mutua, wire v2: il telefono verifica il runtime prima di firmare.)*
- [x] Scambio HELLO / CAPABILITIES / HEARTBEAT / STATUS. *(reale: capability lette sullo schermo del telefono, `lastHeartbeatUtc` in avanzamento.)*
- [x] Ricezione dei primi dati di base. *(reale: il telefono ha mostrato `NostaleClientX [LIVE]`, PID e finestra del client vero.)*
- [x] Verifica integrità, provenienza e freschezza. *(reale per la provenienza per campo e per il heartbeat; locale per il rifiuto fail-closed di un `contractVersion` non riconosciuto.)*
- [x] Gestione disconnessione e riconnessione. *(reale: app terminata → sessione caduta fail-closed; riavvio → sessionId nuovo.)*

### Dashboard

- [x] Avvio affidabile. *(locale: operator server Gate 1 su loopback.)*
- [x] Connessione al runtime corretto. *(reale: UI 8765 → runtime 8766 senza variabili impostate a mano.)*
- [x] Visualizzazione dei dati realmente disponibili. *(locale: campi demo rimossi, `UNKNOWN` esplicito.)*
- [x] Stato PC/NosAi/Guard AI coerente. *(locale: snapshot unico.)*
- [x] Controlli disponibili solo se realmente implementati e autorizzati. *(locale: esecuzione, input diretto e injection sono interruttori reali dell'operatore, spenti all'avvio e leggibili dallo snapshot; la dashboard li accende, li spegne e mostra chi li ha cambiati.)*
- [x] Gestione errori e disconnessioni. *(locale: client assente e runtime offline non mascherati.)*
- [ ] Funzionamento al 100% di tutte le funzioni previste per questo livello. *(Aperto di proposito: il Control Panel è in lavorazione e questa voce non ha una definizione verificabile finché non si stabilizza.)*

### Prove obbligatorie del Gate 1

- [x] Test PC. *(reale.)*
- [x] Test smartphone. *(reale, wire v2.)*
- [x] Test NosAi ↔ client NosTale. *(reale.)*
- [x] Test NosAi ↔ Guard AI. *(reale.)*
- [x] Test PC ↔ smartphone. *(reale, via Wi-Fi con il cavo staccato.)*
- [x] Test dashboard. *(reale per la catena runtime → dashboard.)*
- [x] Test errore/disconnessione/riconnessione. *(reale, ciclo completo sul dispositivo.)*
- [x] Nessuna regressione bloccante. *(`pytest` 159, `NosAi.Runtime.Tests` 93, 18 suite del runtime verdi.)*
- [x] Documentazione coerente con il risultato osservato.

**Il Gate 1 non blocca più lo sviluppo.** I tre limiti residui del canale restano
tracciati e vanno chiusi prima di parlare di esercizio continuativo, ma non
sospendono il lavoro sui gate successivi.

---

## Nota sui Gate 2, 4, 5 e 6

Gate 2, Gate 4, Gate 5 e Gate 6 sono presenti nel repository come blocchi software e relative suite di certificazione invocabili, e restano **Present/Integrated**.

Il superamento del Gate 1 **non** li promuove. Restano non **Verified** per una ragione loro, che il Gate 1 non tocca: le loro suite girano su mondo simulato ed evidenza locale, e il percorso decisionale non ha ancora una sorgente reale di gameplay da cui partire. Finché il client espone `UNKNOWN` su HP, mappa ed entità, un ciclo Plan→Safety→Execute→Verify non può essere dichiarato verificato sul sistema reale, per quanto le suite passino.

---

## Validazione successiva

Dopo il Gate 1, ogni nuovo obiettivo significativo deve avere:

1. implementazione completa del blocco interessato;
2. test automatici;
3. test di integrazione;
4. test PC quando il PC è coinvolto;
5. test smartphone quando lo smartphone è coinvolto;
6. test PC ↔ smartphone quando la comunicazione è coinvolta;
7. verifica della dashboard quando il cambiamento la interessa;
8. verifica di assenza di regressioni;
9. aggiornamento della documentazione;
10. approvazione del gate prima del successivo obiettivo.

---

## Nota sui benchmark

Le prestazioni numeriche delle specifiche sono obiettivi di benchmark finché non sono state misurate sul sistema di riferimento.

---

## Storage previsto

```text
<NOSAI-SSD>:\\NosAi\\
├── app\\
├── runtime\\
├── models\\
├── data\\db\\
├── data\\state\\
├── data\\evidence\\
├── data\\exports\\
├── cache\\
├── logs\\
├── temp\\
├── backups\\
├── config\\
└── tools\\
```

---

## Stato di sviluppo corrente

Il progetto possiede una base software ampia, comprendente fondazioni Gate 1, Gate 4, Gate 5 e nuovi sottosistemi come Navigation/Pathfinding ed Economy/Inventory.

Il primo circuito reale `PC ↔ NosTale ↔ smartphone` **è stato chiuso e verificato**, e con esso il Gate 1. Oltre il Gate 1 nessun blocco è **Verified**.

Il vincolo che conta adesso non è più il circuito, ed è cambiato di natura. **Il percorso dei dati di gioco è costruito e attivabile**: pacchetti → riassemblaggio TCP → framing world → `NosTaleWorldProtocolDecoder` → `NetworkWorldFeed` → `NetworkGameplayProvider` → snapshot Gate 1 → `Gate3WorldState`. L'operatore lo accende con `--observe-game <host:porta>`, `NOSAI_OBSERVE_GAME`, o l'impostazione Control Panel. Senza l'opzione il motivo resta `gameplay_provider_not_available`. Senza driver l'host parte e il motivo nominato di `TryOpen` finisce nel log.

Quel che manca per `Verified` non è un decoder: è **la conferma LIVE su una sessione client in corso** (T-05). Il recording di combattimento già legge gli stessi HP/MP di `docs/PROTOCOLLO_NOSTALE.md`. Finché quella sessione non è stata osservata dal runtime acceso, il gameplay resta UNKNOWN e nessun valore viene inventato al suo posto. **Questo è il comportamento corretto, non un difetto residuo.**

La priorità operativa corrente è quindi:

**accendere `--observe-game` su una sessione reale e chiudere T-05** — alle condizioni fissate da `ADR-0012` e `ADR-0014` — **e chiudere quel che resta dei tre limiti del canale Guard.**
