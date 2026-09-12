# NosAiProject — Guida al completamento al 100%

**Scopo di questo file, ed esclusivamente questo:** una sequenza operativa unica, in ordine
stretto, che fonde in un solo percorso camminabile tutto il lavoro che separa il progetto
da un agente verificato al 100% sul design originale. Ogni passo dichiara il **ruolo
responsabile** (`employee_id` esatto da `nosai/mcp/roles.py`), le **dipendenze**, e
**l'evidenza di chiusura richiesta**.

Questo file **non duplica** ciò che esiste già — lo cita e basta:

| Cosa serve | Dove sta già, non qui |
|---|---|
| Processo, ciclo di vita DISCOVER→MERGE, schema incarico, comandi di ricerca rapida | `docs/AGENT_COORDINATION.md` |
| Tabella completa ruolo→modello→responsabilità | `AGENTS.md`, `nosai/mcp/roles.py:41-165` |
| Priorità P0/P1/P2 e criteri di chiusura generali | `docs/REMAINING_WORK.md` |
| Dettaglio dei singoli test rimandati (T-xx) | `docs/TEST_RIMANDATI.md` |
| Pacchetti del laboratorio di ricerca (LAB-xx) | `docs/mcp/RESEARCH_LAB_SPEC.md` |
| Stato dei contratti prodotto/infrastruttura | `contracts/ledger.json`, `docs/MASTER_ROADMAP.md` |
| Stato per fase di prodotto (AP-00..AP-10) | `docs/agents/phases/AP-*/**/*STATUS.md` |

Se uno di questi documenti e questa guida si contraddicono, vince l'ordine di precedenza
di `docs/AGENT_COORDINATION.md` §1: `CLAUDE.md` → `SOURCE_OF_TRUTH.md` → ADR → ledger →
`EXECUTION_QUEUE.md` → documenti di fase. Questa guida è sempre l'ultima in quell'ordine:
riorganizza, non sostituisce.

## Come cercare in questo file

Ogni passo ha un ID univoco (`P<n>`) e, quando esiste, l'ID originale del contratto/test/voce
(`C-xxx`, `T-xx`, `R-xxx`, `LAB-xx`). Cerca con:

```bash
rg -n "^### P" docs/GUIDA_COMPLETAMENTO_100.md          # elenco di tutti i passi
rg -n "P7\b|T-13\b" docs/GUIDA_COMPLETAMENTO_100.md      # un passo specifico
```

Ogni modello che riceve un incarico deve prima leggere qui il proprio `employee_id` fra i
passi con stato aperto, poi seguire il ciclo di vita di `docs/AGENT_COORDINATION.md` §4 per
quel singolo passo. Nessun modello salta un passo la cui dipendenza non è chiusa.

## Legenda stato

- 🟢 **APERTO — nessuna dipendenza bloccante**: si può iniziare subito.
- 🟡 **APERTO — in corso**: qualcuno ci sta già lavorando in questa sessione.
- 🔴 **BLOCCATO**: la dipendenza dichiarata non è ancora chiusa.
- ⚪ **RICHIEDE L'OPERATORE**: nessun modello può chiuderlo da solo; serve una sessione umana
  al client di gioco, hardware specifico, o un asset che questo ambiente non produce.
- ⚫ **ESCLUSO**: decisione architetturale registrata in un ADR; si riapre solo con un caso
  nominato, non per iniziativa di un modello.

---

## FASE A — Manutenzione della catena di produzione (nessun operatore richiesto)

Questi passi sbloccano gli strumenti che tutti gli altri passi useranno. Vanno chiusi per
primi perché C-315 in particolare ha già dimostrato di bloccare qualunque modifica parziale
futura a `scripts/code_agent.py`.

### P1 — C-315: riconoscere i metodi di classe nell'innesto parziale Python
🟡 APERTO — IN CORSO IN QUESTA SESSIONE

- **Cosa:** `_intervalli_funzioni()` in `scripts/code_agent.py:270` oggi vede solo le
  funzioni di primo livello; deve camminare anche dentro le classi (metodi), con dedent dei
  frammenti rientrati e rifiuto in caso di ambiguità fra classi diverse.
- **Ruolo:** `employee.coding` — tier `complex` (Qwen3 Coder 30B): i tier gratuiti sono stati
  provati ed esauriti su questo file (vedi nota sotto).
- **Dipendenze:** nessuna.
- **Contratto:** `contracts/python-partial-graft-methods-021.json`.
- **Evidenza di chiusura:** `python -m pytest tests/test_code_agent_parziale_metodi.py tests/test_code_agent_parziale.py -q` verde; `git diff scripts/code_agent.py` mostra solo il corpo di `_intervalli_funzioni` cambiato, nessun'altra firma toccata.
- **Nota per chi riprende questo passo:** due difetti reali dello strumento sono stati
  trovati e corretti in questa sessione, non nel contratto — restano validi per qualunque
  tentativo futuro: (1) il tetto di `budget_token()` (8192 token, `scripts/code_agent.py`
  linea vicina a `def budget_token`) è insufficiente per un file di 812 righe quando il
  modello ignora "restituisci solo questa funzione" e riscrive il file intero — vedi P3;
  (2) due commenti che nominavano il segnaposto "TODO" in prosa (non in una stringa) facevano
  fallire il controllo automatico di `validate_implementation` su qualunque rigenerazione di
  questo file — già corretti nei commenti vicino a `STRINGHE = re.compile(...)` e nella
  docstring di `_senza_stringhe`.

### P2 — C-314: potatura automatica dei binding orfani
🔴 BLOCCATO da P1

- **Cosa:** `McpStateStore.prune_unknown_bindings()` in `nosai/mcp/state.py:179` (scheletro
  già presente, corpo `NotImplementedError`) e l'integrazione in
  `RoleBindingRegistry.__init__` (`nosai/mcp/bindings.py`) che lo chiama dopo
  `ensure_default_bindings` e riallinea l'export JSON se trova orfani.
- **Perché dipende da P1:** entrambi i bersagli (`prune_unknown_bindings`, `__init__`) sono
  metodi dentro una classe; l'innesto parziale li trova solo dopo che P1 ha insegnato a
  `_intervalli_funzioni` a vedere i metodi.
- **Ruolo:** `employee.coding`.
- **Contratto:** `contracts/binding-registry-prune-020.json`.
- **Evidenza di chiusura:** `python -m pytest tests/test_role_binding_prune.py -q` verde;
  numero di righe della tabella `bindings` coincide con `len(DEFAULT_EMPLOYEE_ROLES)` dopo la
  costruzione del registro con l'elenco completo dei ruoli.

### P3 — Alzare il tetto di `budget_token()` per i file grandi
🟢 APERTO — nessuna dipendenza, ma non urgente finché P1/P2 restano chiusi con l'aggiramento
manuale già verificato in sessione (budget esteso a 20000 solo per quella chiamata)

- **Cosa:** `budget_token()` in `scripts/code_agent.py` restituisce 8192 per ogni modello non
  Groq/DeepSeek, indipendentemente dalla dimensione del file bersaglio. Misurato in sessione:
  su un file di 812 righe il modello (anche il tier `complex`) tende a riscrivere l'intero
  file invece di rispettare "restituisci solo questa funzione", e con 8192 token viene
  troncato a metà, producendo `SyntaxError`. Con 20000 token la stessa chiamata completa
  correttamente.
- **Ruolo:** `employee.coding` per l'implementazione, `employee.reviewer` per la revisione
  (è una modifica allo strumento che tutta la catena usa).
- **Dipendenze:** nessuna, ma richiede prima un contratto (Fase 1) — non ha ancora un CID.
- **Evidenza di chiusura:** un test che verifica `budget_token()` restituire un valore
  proporzionale alla dimensione dello scheletro per i tier non-Groq/DeepSeek, più la riprova
  che P1/P2 (o un incarico equivalente su un file grande) passano senza aggiramenti manuali.

### P4 — Registrare C-314 e C-315 (e l'eventuale P3) nel ledger
🔴 BLOCCATO da P1, P2

- **Cosa:** `update_contract_state` per ciascun CID, cosicché `docs/MASTER_ROADMAP.md` smetta
  di ignorarli.
- **Ruolo:** `employee.orchestrator_cto` (Claude) — `update_contract_state` non è assegnato a
  nessun ruolo delegabile nello schema attuale.
- **Evidenza di chiusura:** i CID compaiono in `contracts/ledger.json` con stato
  `TEST_VERIFIED`; `docs/MASTER_ROADMAP.md` rigenerato.

### P5 — Colmare il buco di bookkeeping AP-03..AP-09 nel ledger
🟢 APERTO — indipendente da P1-P4, ma logicamente viene dopo per non aggiungere rumore mentre
la catena è instabile

- **Cosa:** il ledger e `docs/MASTER_ROADMAP.md` oggi coprono solo i Gate 0-4 e 9
  (infrastruttura, tutti al 100%). Le fasi di prodotto AP-03 (Map Reconstruction), AP-04
  (Exploration/Navigation), AP-05 (Combat), AP-06 (Quest), AP-07 (Equipment) non hanno un
  proprio Gate né propri CID nel ledger: il loro stato vive solo nei singoli
  `docs/agents/phases/AP-0X/*_STATUS.md`, mai riportato al roadmap generale. Chi legge solo
  `MASTER_ROADMAP.md` non sa che queste fasi esistono ancora da verificare.
- **Ruolo:** `employee.product_architect` decide la struttura dei nuovi Gate/CID;
  `employee.documentation` li scrive nel ledger via `local_update_documentation`.
- **Evidenza di chiusura:** ogni fase AP-03..AP-09 ha un Gate o dei CID propri nel ledger,
  con stato coerente con quanto dichiarato nei rispettivi `*_STATUS.md`.

---

## FASE B — Sblocco degli asset strutturalmente mancanti

⚪ Questi due passi non li chiude nessun modello da solo: servono dati o decisioni che questo
ambiente di sviluppo non produce autonomamente. Sono il prerequisito reale di gran parte del
"cervello" decisionale (combattimento, equipaggiamento, valore degli obiettivi).

### P6 — R-101: asset/back-end ML per OCR, object detection, tracking
⚪ RICHIEDE L'OPERATORE (dataset/modello non producibile in questo ambiente)

- **Cosa:** nessun modello OCR addestrato, nessun dataset per classificare Mob vs NPC esiste
  nel repo oggi (`docs/agents/phases/AP-02/*_STATUS.md` §10).
- **Ruolo:** `employee.perception` integra l'asset quando arriva; l'operatore lo procura o
  lo produce fuori da questa catena.
- **Riferimento:** `docs/REMAINING_WORK.md` R-101.
- **Evidenza di chiusura:** cattura reale con confidence/provenance, test su replay.

### P7 — R-102: dati reali di skill, danno, costo, cooldown
⚪ RICHIEDE L'OPERATORE

- **Cosa:** `GameReferenceDatabase` non decodifica semanticamente le statistiche reali di
  oggetti/skill; blocca la simulazione di combattimento e 8 delle 9 dimensioni di
  `LoadoutEvaluation`.
- **Ruolo:** `employee.world_model` integra i dati una volta forniti.
- **Riferimento:** `docs/REMAINING_WORK.md` R-102.
- **Evidenza di chiusura:** simulazione e ranking confrontati con catture reali.

---

## FASE C — Catena di verifica dal vivo (client aperto, operatore presente)

⚪ Ogni passo qui richiede una sessione con il client NosTale aperto e l'operatore al
computer. **Sono in ordine di dipendenza reale**, non alfabetico: T-13 (P10) è lo snodo che
sblocca tutta la catena di combattimento a monte, quindi tutto ciò che lo precede è lì perché
lo abilita direttamente o indirettamente.

### P8 — T-09: calibrare il ROI del riquadro bersaglio
⚪ RICHIEDE L'OPERATORE — 🟢 nessuna dipendenza interna

- **Ruolo:** `employee.perception` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-09.
- **Evidenza:** `--calibrate-target` eseguito sul client reale; il rifiuto
  `target_roi_not_calibrated` scompare.

### P9 — T-03: barra HP/MP su riempimento parziale + addestramento `HudGlyphAtlas`
⚪ RICHIEDE L'OPERATORE — 🟢 nessuna dipendenza interna

- **Ruolo:** `employee.perception` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-03.
- **Evidenza:** `--hud-probe` con barra parzialmente scarica confrontato col numero HUD letto
  via OCR addestrato.

### P10 — T-13: osservare un Mob reale nello snapshot World Model fuso
⚪ RICHIEDE L'OPERATORE — 🔴 beneficia di P8 (bersaglio calibrato) ma può partire anche prima

- **Cosa:** **il passo più critico dell'intera guida.** Nessun operatore ha mai visto un
  `Mob` reale comparire nello snapshot World Model fuso. Finché non succede, tutta la catena
  di combattimento sopra (planner, ranking, esecuzione) resta codice verde ma mai osservato
  funzionare dal vivo, indipendentemente da quanti test unitari passano.
- **Ruolo:** `employee.world_model` + `employee.perception` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-13 (5 passi manuali documentati lì).
- **Prerequisiti hardware:** client NosTale acceso, WinDivert installato, terminale
  amministratore (già soddisfatti per l'SSD `NOSAI-SSD` dal 2026-09-07).
- **Evidenza:** uno snapshot `WorldModelSnapshot.Mobs` non vuoto durante una sessione live.

### P11 — T-05: confermare i vitali LIVE (non solo CACHED) su sessione in corso
✅ CHIUSO il 2026-09-12

- **Ruolo:** `employee.perception` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-05.
- **Evidenza:** `--decide --observe-game 79.110.84.175:4005 --decide-interval-ms 5000` su
  sessione reale in corso registra `Game observation attached. endpoint=... source=LIVE`
  (consenso esplicito dell'operatore per l'automazione sul client vivo).

### P12 — T-06/T-07: latenza Gate 1 PC↔telefono su hardware reale
⚪ RICHIEDE L'OPERATORE + telefono Guard reale in rete locale — 🟢 indipendente dagli altri
passi di questa fase

- **Ruolo:** `employee.action` (canale di attuazione) + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-06/T-07.
- **Evidenza:** budget p99 < 25 ms misurato sul canale fisico PC↔telefono, non sulla contesa
  di CI (102 ms osservati su GitHub Actions non sono questa misura).

### P13 — T-08: `--decide --observe-game` su sessione viva
🟡 EVIDENZA PARZIALE il 2026-09-12 — resta aperto un solo punto

- **Ruolo:** `employee.decision` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-08.
- **Evidenza attesa:** `ExecutionDisabled`-equivalente è l'esito corretto (l'effettore resta
  `DisabledActionEffector` per policy di sicurezza), non un guasto — non aspettarsi
  un'azione eseguita. **Confermato:** ciclo avviato (`Gate 3 decision loop started`),
  `acting=False`, `authority_live_input_not_armed`. **Non confermato:** la transizione da
  `NoWorldState` a una decisione — nessun mostro nei paraggi durante la finestra di prova;
  richiede rieseguire vicino a un'entità reale (si intreccia con P10/T-13).

### P14 — T-16: collegare gli id `sayi`/`msgi` sul filo al testo visto a schermo
⚪ RICHIEDE L'OPERATORE — 🟢 indipendente

- **Ruolo:** `employee.perception` (cattura) + `employee.memory` (persistenza della coppia
  osservata) + operatore (annotazione di cosa vedeva).
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-16. Pannello già pronto: "Registra il filo, e
  annota cosa hai visto".
- **Evidenza:** almeno una coppia id↔testo osservata e registrata.

### P15 — T-17: misurare il costo MP reale di una skill
⚪ RICHIEDE L'OPERATORE — 🟢 indipendente

- **Ruolo:** `employee.testing` + operatore.
- **Riferimento:** `docs/TEST_RIMANDATI.md` T-17.
- **Evidenza:** sessione con MP al massimo, uso di una sola skill, lettura `stat`
  prima/dopo, confronto con `COST[0]` dichiarato dal catalogo.

---

## FASE D — Colmare i meccanismi di esecuzione mancanti

🔴 Questi passi hanno senso solo dopo che la Fase C ha dato visibilità reale (in particolare
P10); prima di allora non c'è nulla di osservabile su cui costruire l'esecuzione.

### P16 — R-104/AP-07: bridging Safety Gate per equip/unequip/upgrade + conferma di `--optimization-gesture`
🔴 BLOCCATO da P10 (serve un bersaglio/contesto osservabile per validare l'esecuzione)

- **Ruolo:** `employee.action` implementa, `employee.security` rivede (tocca
  `CommitPointValidator`/`ActuationScope`, superficie di sicurezza).
- **Riferimento:** `docs/REMAINING_WORK.md` R-104; `docs/agents/phases/AP-07/*_STATUS.md`.
- **Evidenza:** primitive di azione, verifica prima/dopo, test negativi. Calibrazione
  `bag-panel-roi` **prodotta il 2026-09-12** su client vivo (`data/perception/bag-panel-roi.calibration`,
  24 slot, area client 1024x768) — resta da fare la parte Safety Gate/`--optimization-gesture`.

### P17 — R-103/AP-05: bridging `EngageCommand` → `CommitPointValidator`/`ActuationScope`
🔴 BLOCCATO da P10

- **Cosa:** oggi `EngageCommand` non ha alcun ponte verso il Safety Gate reale — limite
  dichiarato esplicitamente, non nascosto (`docs/agents/phases/AP-05/*_STATUS.md` §8).
- **Ruolo:** `employee.action` implementa, `employee.security` rivede.
- **Riferimento:** `docs/REMAINING_WORK.md` R-103.
- **Evidenza:** esecuzione autorizzata, receipt e post-condition su Windows con un bersaglio
  reale.

### P18 — R-105/AP-06: feed di occupazione live + fonti per dialogue/interact/deliver
🔴 BLOCCATO da P10 (occupazione) — la parte dialogue/interact/deliver è 🟢 indipendente

- **Cosa:** `OccupancyView` è sempre `null` oggi, quindi `--collect`/`--walk`/`--scout` si
  rifiutano al primo passo su un client reale. `Dialogue`/`Interact`/`Deliver` non hanno
  ancora un intento tastiera né un opcode di rete decodificato.
- **Ruolo:** `employee.world_model` (occupazione) + `employee.planning` (integrazione nella
  navigazione).
- **Riferimento:** `docs/REMAINING_WORK.md` R-105.
- **Evidenza:** obiettivi quest grounded e completamento multi-step osservato.

---

## FASE E — Certificazione finale

### P19 — R-106/AP-10: certificazione di autonomia end-to-end su hardware reale
🔴 BLOCCATO da P16, P17, P18 (tutti i meccanismi di esecuzione devono esistere prima di
certificare la catena che li usa)

- **Cosa:** oggi `OverallLevel` = `Present`, non `Verified`, su tutti i 14 stadi della
  Definition of Done. Richiede Windows + client NosTale reale + l'hardware dichiarato
  (Nitro V16/RTX 5060) + telefono Guard, esercitati end-to-end nella stessa sessione.
- **Ruolo:** `employee.orchestrator_cto` coordina; tutti i ruoli partecipano nel proprio
  dominio; operatore indispensabile per l'hardware.
- **Riferimento:** `docs/REMAINING_WORK.md` R-106; `docs/agents/phases/AP-10/*_STATUS.md`.
- **Evidenza:** report con evidenza per ciascuno dei 14 stadi, non solo codice presente.

---

## FASE F — Indipendenti, parallelizzabili in qualsiasi momento

Questi due passi non dipendono da nessuno degli step sopra e nessuno degli step sopra
dipende da loro. Possono partire quando un `employee.mcp_chief` è libero.

### P20 — R-205: completare la cascata free-first del MCP
✅ **CHIUSO** — verificato in questa sessione (2026-09-12): `scripts/free_first.py:1-281` è
implementato integralmente (nessun `NotImplementedError`), `tests/test_free_first.py` ha 21
test verdi che coprono fallback, quota 429/pause, budget esaurito e giudizio di qualità.
`docs/REMAINING_WORK.md` riga 33 non è ancora stata aggiornata a chiuso: farlo nel prossimo
passaggio di manutenzione documentale.

- **Riferimento:** `docs/REMAINING_WORK.md` R-205; `scripts/free_first.py`,
  `tests/test_free_first.py`.

### P21 — R-208: watchdog periodico del MCP Chief in ambiente operativo
🟢 APERTO

- **Ruolo:** `employee.mcp_chief`.
- **Riferimento:** `docs/REMAINING_WORK.md` R-208.
- **Evidenza:** report di health tick e raccomandazioni su replay reali, senza
  auto-mutazioni non autorizzate.

---

## Traccia opzionale, fuori dal percorso critico

### LAB-03..LAB-07 — Research Lab: supervisione end-to-end, worker pool, esperimenti, rilasci a mandato, certificazione pannello
⚪ Non attivabile prima che P19 sia chiuso — `docs/mcp/RESEARCH_LAB_SPEC.md` dichiara
esplicitamente: "Il laboratorio non è attivo finché prove e integrazione end-to-end non sono
completate." LAB-01 (evidenze firmate) e LAB-02 (stato SQLite transazionale) sono già su
`main`. Non è un prerequisito del 100% funzionale: è una capacità di governance aggiuntiva
per quando il resto è certificato.

- **Riferimento:** `docs/mcp/RESEARCH_LAB_SPEC.md`, `contracts/mcp-research-lab-001.json`.

---

## Esclusi per decisione architetturale — non sono lavoro mancante

Questi non compaiono come passi perché **sono già chiusi**, per scelta e non per incapacità.
Un modello non li riapre di propria iniziativa: serve un caso nominato e, per un cambio di
questa portata, l'operatore.

| CID | Cosa | ADR |
|---|---|---|
| C-105 | Hook di memoria/DLL diretto nel client | `docs/adr/ADR-0032-no-memory-hook-yet.md` — il canale rete + `DllImport` basta oggi |
| C-304 | FSM esplicita al posto di Planner/Orchestrator | `docs/adr/ADR-0031-no-explicit-fsm.md` — Planner/Orchestrator (HTN/GOAP) restano fuori dal percorso di esecuzione |
| C-003/004/005 | ASan, gatekeeper Python dedicato, bridge ctypes | `docs/adr/ADR-0029-phase5-dotnet-pytest-no-native-branch.md` — nessun codice nativo di prima mano nel progetto |

---

## Riepilogo a colpo d'occhio

```
FASE A  (automatica)         P1 → P2 → P4 → P5   (P3 in parallelo, non urgente)
FASE B  (serve l'operatore)  P6, P7                                  [asset]
FASE C  (client + operatore) P8, P9 → P10 → P11, P12, P13, P14, P15  [T-13 e' lo snodo]
FASE D  (esecuzione)         P10 → P16, P17, P18
FASE E  (certificazione)     P16, P17, P18 → P19
FASE F  (indipendente)       P20, P21   -- in qualsiasi momento
OPZIONALE                    LAB-03..07 -- solo dopo P19
ESCLUSI                      C-105, C-304, C-003/004/005 -- chiusi da ADR
```

La frase che riassume tutto: **la parte automatizzabile da soli modelli è quasi finita
(Fase A); il resto non è "altro codice da scrivere", è "sedersi al client vero" (Fasi B-E),
più due passi di manutenzione MCP indipendenti (Fase F) che non aspettano nessuno.**
