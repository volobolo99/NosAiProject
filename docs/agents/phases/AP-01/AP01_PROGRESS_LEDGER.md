# AP-01 — Registro di Avanzamento Multi-Agente

**Rilevazione eseguita:** 5 settembre 2026, 11:49 UTC (13:49 CEST), letta in diretta dal repo montato (`$HOME/mnt/NosAiProject`) tramite device bridge. Nessun dato qui sotto è ricostruito da conversazioni precedenti senza verifica: ogni numero è stato ricontrollato in questa sessione.

**Ramo corrente del repo condiviso:** `codex/cursor-perception-contract` (upstream: `origin/codex/cursor-perception-contract`, **non** `origin/main`). Situazione nota e per ora deliberata, non un errore da correggere.

Ultimi 5 commit sul ramo corrente:
```
23747c8 feat(dashboard): inspect every classified runtime field
7f0237b fix(tests): declare core dependency for direct engine tests
e62bb97 feat(direct-engine): every legacy capability gets a name before any of them gets a call
4e4486e feat(perception): add classified visual observation contracts
8787752 feat(c3-1b): a key is confirmed by what the wire says happened, not by pressing it
```

## 1. Stato per ruolo (A1-A6)

| Ruolo | Ambito / file | Stato attuale | Note di rischio |
|---|---|---|---|
| **A1** - World Model contracts | `src/NosAi.Runtime/WorldModel/ClassifiedWorldModel.cs` (65 righe), `ObservationSnapshotFusionFeed.cs` (217 righe) | **Non committato** (`??` in `git status`) | Nessuno |
| **A2** - Sensor Fusion | `src/NosAi.Runtime/Perception/Fusion/DeterministicSensorFusion.cs` (704), `SensorFusionContracts.cs` (180), `SensorFusionWorldAdapter.cs` (127), `SensorNormalizer.cs` (158) | **Non committato** (intera cartella `??`) | Nessuno |
| **A3** - Temporal Prediction | atteso in `src/NosAi.Runtime/Prediction/` + `docs/agents/phases/AP-01/A3_HANDOFF_TEMPORAL_PREDICTION.md` | **Non ancora atterrato.** `find src/NosAi.Runtime/Prediction` e `find docs/agents/phases/AP-01` (prima della scrittura di questo documento) non hanno restituito nulla. Agente separato che lavora in scratch clone locale (`$HOME/nosai-scratch-a3`), fuori dalla cartella montata | In corso in parallelo, da ricontrollare in un check successivo |
| **A4** - Runtime Wiring | modifica `src/NosAi.Runtime/Gate1/Gate1BootstrapHost.cs` (diff: +10/-7 righe su 17 toccate), nuovo `src/NosAi.Runtime/Gate3/WorldModelWorldStateSource.cs` (183 righe) | **Non committato** | Vedi rischio branch-base in sezione 2: `Gate1BootstrapHost.cs` e' anche uno dei file che `origin/main` ha modificato - sovrapposizione diretta |
| **A5** - Tests & Docs | `tests/NosAi.Runtime.Tests/SensorFusionTests.cs` (613 righe), `WorldModelRuntimeWiringTests.cs` (289 righe) | **Non committato** | Nessuno |
| **A6** - Integrazione | - | **Non iniziato** | Deve eseguire la riconciliazione di branch (sezione 2) come parte del proprio mandato, non come opzionale |

Nessun ruolo A1-A5 ha ancora effettuato commit: tutto il lavoro sopra elencato risiede solo come modifiche non tracciate/non committate nella cartella condivisa montata.

**Nota collaterale (non richiesta ma osservata):** nella cartella montata sono presenti anche una sottocartella `Claude outputs/` con due file `.patch` (`0001-feat-world-model-add-AP-01-versioned-World-Model-con.patch`, `0002-test-world-model-pin-replay-digest-add-budget-benchm.patch`, sessione `session_01T1AkiKxzo8vw9wrHfGC8uS`) e una `_to_delete/` con bundle git (`ap01-a1-a3.bundle`, `ap01-handoff.bundle`, `handoff-ap01/ap01-world-model.bundle`) e sottocartelle residue (`src/`, `tests/`, `tools/`, `worktrees/`, `NosAi_vecchio_prototipo/`). I due file patch descrivono un **secondo disegno di World Model, distinto e non sovrapponibile** a quello di A1 sopra: contratti `WorldFact<T>` con provenienza LIVE/DERIVED/CACHED/SIMULATED/UNKNOWN in `src/NosAi.Core/WorldModel/` (non `src/NosAi.Runtime/WorldModel/`), non applicati al repo. Questo e' un segnale che **piu' tentativi paralleli e indipendenti di World Model contract** sono in corso o sono stati tentati sullo stesso repo in tempi ravvicinati; A6 dovrebbe accertare quale disegno e' quello autorevole prima di integrare, per evitare di dover conciliare due modelli concettualmente diversi. Non e' stato toccato ne' spostato nulla di questo materiale in questa sessione.

## 2. Rischio base di branch - dati aggiornati (AGGRAVATO rispetto all'ultima verifica)

Verifica precedente (nota storica): `codex/cursor-perception-contract` 6 commit avanti, **22** dietro `origin/main`, con zero sovrapposizione di file coi percorsi toccati da A1-A5.

**Verifica di oggi (5 settembre, 11:49 UTC):**
- `git rev-list --left-right --count codex/cursor-perception-contract...origin/main` -> **6 avanti, 209 dietro**. `origin/main` si e' allontanato di molto in due giorni (209 commit contro i 22 precedenti - quasi 10x in piu').
- Merge-base: `17814d5b` del 3 settembre 2026, 12:08 CEST. Ultimo commit su `origin/main`: 5 settembre 2026, 12:07 CEST (ramo main attivissimo, aggiornato pochi minuti prima di questa verifica).
- Diff totale `codex/cursor-perception-contract` -> `origin/main`: **254 file toccati**.
- **Sovrapposizione con i percorsi di lavoro AP-01 - ORA PRESENTE** (prima era nulla):
  - `src/NosAi.Core/Perception/PerceptionContracts.cs`
  - `src/NosAi.Runtime/Gate1/Gate1BootstrapHost.cs` - **lo stesso file che A4 sta modificando adesso, senza commit, nella cartella montata**
  - `src/NosAi.Runtime/Gate1/Gate1NavigationView.cs`
  - `src/NosAi.Runtime/Gate3/Gate3DecisionLoop.cs`
  - `src/NosAi.Runtime/Perception/Network/SkillCooldownTracker.cs`

**Conclusione aggiornata:** la valutazione "rischio basso, nessuna sovrapposizione" **non e' piu' valida**. C'e' sovrapposizione reale su 5 file, e uno di questi (`Gate1BootstrapHost.cs`) ha in questo momento modifiche locali non committate di A4. La riconciliazione (rebase o merge con `origin/main`) non e' piu' un passo a basso rischio da eseguire con calma a fine fase: **A6 deve trattarla come priorita' alta e verificare concretamente il file `Gate1BootstrapHost.cs`** (confronto a tre vie tra la versione di merge-base, la versione committata su `codex/cursor-perception-contract` e la versione su `origin/main`) prima di dichiarare l'integrazione chiusa. Nessuna azione di riconciliazione e' stata eseguita in questa sessione (documentazione-only, nessun comando distruttivo o di merge lanciato).

## 3. File di comando AP-01 per A3-A6: risultano ancora perduti

Confermato in questa sessione: nel repo (su nessun ramo controllato, incluso `origin/main` e `work/main-sync`, che coincidono esattamente allo stesso commit `3cbb49e`) esistono **solo**:
- `docs/agents/phases/AP-01/A1_CLAUDE_world_model_contracts.md`
- `docs/agents/phases/AP-01/A2_CURSOR_sensor_fusion.md`

I file di comando per A3, A4, A5, A6 (`A3_CLAUDE_temporal_prediction.md`, `A4_CURSOR_runtime_wiring.md`, `A5_CLAUDE_tests_and_docs.md`, `A6_INTEGRATION.md`) **non sono presenti in nessun ramo** al momento di questa verifica. Lo scope dettagliato per questi quattro ruoli esiste oggi solo come cronologia di conversazione tra l'owner del progetto e una sessione Claude precedente - non e' recuperabile dal repo.

**Raccomandazione:** se l'owner del progetto vuole mantenere questo processo multi-agente riproducibile in futuro (ad esempio per una fase AP-02), vale la pena ri-autorare questi quattro file di comando come documenti durevoli nel repo, sul modello di `A1_CLAUDE_world_model_contracts.md`/`A2_CURSOR_sensor_fusion.md`. Non e' stato fatto in questa sessione: e' documentazione-only e la ricostruzione dello scope dettagliato richiede input umano che questa sessione non ha.

## 4. Nota d'ambiente permanente - build/test tramite device bridge

La cartella condivisa `$HOME/mnt/NosAiProject` (montata tramite bridge sul dispositivo locale) **blocca la cancellazione/sovrascrittura di alcuni file gia' esistenti** (comportamento osservato in precedenza soprattutto sotto `obj/` e `bin/` durante il restore NuGet). Questo impedisce l'esecuzione diretta di `dotnet build`/`dotnet restore` dentro questa cartella, perche' NuGet deve poter sovrascrivere file sotto `obj/` durante il restore.

**Soluzione gia' validata (di una sessione precedente, riconfermata qui come valida per ogni agente futuro che lavori tramite questo stesso bridge):**
1. Clonare la cartella montata in una directory di scratch **fuori** dal mount (es. `git clone $HOME/mnt/NosAiProject $HOME/nosai-scratch-<ruolo>`).
2. Buildare/testare li', con l'SDK .NET 8 gia' installato in `$HOME/.dotnet8`.
3. Copiare indietro nella cartella montata **solo i file nuovi** prodotti (evitare di sovrascrivere file di build esistenti sotto `obj/`/`bin/`, che il mount tende a bloccare).

Osservazione aggiuntiva emersa in questa sessione: comandi Git anche di sola lettura come `git status` possono lasciare un `.git/index.lock` residuo che un tentativo di `rm` diretto non riesce a rimuovere (`rm: cannot remove '.git/index.lock': Operation not permitted`), perche' Git crea il lock per aggiornare la cache di stat dell'index e poi tenta di cancellarlo a fine operazione. Questo non ha impedito, in questa sessione, ulteriori comandi Git di sola lettura ne' la scrittura/commit di questo stesso documento (creazione di file nuovi e modifica in-place con `sed -i`/redirezione si sono dimostrate possibili). Se in futuro un comando Git dovesse rifiutarsi con "Unable to create '.git/index.lock': File exists", la causa e' questa, non una repo danneggiata - la soluzione resta comunque lavorare nel clone di scratch fuori dal mount per qualunque operazione pesante (build/test/restore).

---
*Documento generato in sessione di sola documentazione (nessun file `.cs`/`.csproj`/`.sln` toccato, nessuna build eseguita, nessun comando Git distruttivo lanciato).*
