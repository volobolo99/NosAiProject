# AP-10 / A1 — Contratti di certificazione + report onesto

## Perché questa fase è diversa da AP-00→AP-09

`docs/ROADMAP_ESECUTIVA.md` S:AP-10: "Scenario completo: startup →
attach → perception → map discovery → exploration → navigation → target
recognition → combat → loot/inventory → multi-step quest →
equipment/progression → recovery → persistence → evidence. DoD:
segmento autonomo senza gameplay commands umani, dati privilegiati o
hardware di automazione esterno; funzionamento entro i budget misurati
del laptop."

AP-10 non è una fase di costruzione, è la fase di **certificazione**.
Non produce contratti/algoritmi di dominio come AP-04→AP-09: certifica
se l'intera pipeline, end-to-end, funziona davvero. Questo ambiente
(container Linux, nessun client NosTale reale, nessun hardware ASUS
Nitro V16/RTX 5060, nessun telefono Guard AI) **non può eseguire quello
scenario**, per la stessa "Real-environment rule" di `CLAUDE.md` già
citata in ogni fase precedente: "Client integration, perception and
actuation require real target validation before Verified." Dichiarare
qui una certificazione superata sarebbe esattamente la violazione che
questo progetto vieta in modo più esplicito di ogni altra regola.

Quello che **è** onesto e utile fare in questo ambiente: dare per la
prima volta una forma tipizzata reale alla convenzione
`Present`/`Integrated`/`Done`/`Verified` (usata solo in prosa dalla fase
AP-00 in poi) e compilare, con citazioni esatte, il report reale di dove
sta oggi ciascuno dei 14 stadi della DoD — senza inventare un livello
più alto di quello che le fonti già scritte in questo repository
supportano.

## Consegnato

`src/NosAi.Core/WorldModel/Certification/CertificationContracts.cs`:

- `VerificationLevel` — `Present`/`Integrated`/`Done`/`Verified`, ora un
  tipo reale invece di sola prosa.
- `CertificationStage` — i 14 stadi esatti della DoD, nello stesso
  ordine.
- `CertificationStageResult` — livello + `Evidence` (citazione
  obbligatoria, mai vuota) + `Blockers`. Un vincolo reale nel
  costruttore: uno stadio `Verified` non può avere blocker (se qualcosa
  blocca ancora, non è Verified per definizione).
- `CertificationReport` — `OverallLevel` è il livello **più debole** tra
  tutti gli stadi (il principio "anello debole": uno scenario end-to-end
  vale quanto il suo stadio peggiore), `null` se non c'è nessuno stadio
  valutato (mai `Present` di default). `IsFullyCertified` richiede
  esattamente 14 stadi tutti `Verified`.

Test: `tests/NosAi.Core.Tests/WorldModel/Certification/CertificationContractsTests.cs`,
9 test, tutti verdi. `dotnet build NosAi.sln -c Release`: 0 errori (1
warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **564/564**,
0 falliti (555 precedenti + 9 nuovi, zero regressioni).

## Il report reale, oggi — con citazione per ogni riga

Non un `CertificationReport` costruito in codice (una certificazione non
è telemetria a runtime da serializzare, è una valutazione con fonti
citabili) — la tabella stessa, qui, è il contenuto reale che quel tipo
esiste per rappresentare.

| # | Stadio | Livello | Evidenza citata | Blocco principale |
|---|---|---|---|---|
| 1 | Startup | `Verified`* | `docs/STATO_IMPLEMENTAZIONE.md`: "Il primo circuito reale PC↔NosTale↔smartphone è stato chiuso e verificato, e con esso il Gate 1." — bootstrap `Gate1BootstrapHost` fa parte di quel circuito. | — |
| 2 | Attach | `Verified`* | Stessa fonte — l'attach al processo client è precondizione del circuito Gate 1 verificato. | — |
| 3 | Perception | `Integrated` | `AP-02_STATUS.md` §9: "Integrated... ambito vitali." | Mob/NPC/oggetti da visione bloccati da OCR/ONNX non addestrato (stesso §10). |
| 4 | Map discovery | `Integrated` | `AP-03_STATUS.md` §8: "Integrated". | Portali multi-mappa: nessuna fonte dati reale (`AP-04_A1_STATUS.md`). |
| 5 | Exploration | `Present` | `AP-04_A1_STATUS.md`: A1+A3 `Present`. `EXECUTION_QUEUE.md` Q-028/Q-029: `ScoutCommand`/`MovementVerificationProjector` consegnati da DeepSeek il 2026-09-06, build/test verdi. | Consegnato ma non ancora eseguito contro un client reale — resta `Present`, non `Integrated` (nessun ciclo runtime lo richiama ancora automaticamente) né `Verified`. |
| 6 | Navigation | `Present` | Il cammino cella-per-cella (`PathWalkController`/`WalkCommand`) è reale e Gate-1-verificato come **meccanismo**. La catena "il World Model sceglie dove andare → si cammina davvero" ora esiste (`ScoutCommand`, DeepSeek, Q-028/Q-029) e passa build/test in questo ambiente Linux. | Non ancora eseguita contro un client NosTale reale/Windows — stesso limite di ogni altro stadio, non una lacuna di codice. |
| 7 | Target recognition | `Present` | Entità da rete (pacchetti reali) fluiscono in `Mob`/`Player` (AP-01 `Integrated`); riconoscimento **visivo** bloccato da OCR/ONNX come sopra. | Nessuna classificazione visiva reale di mob/NPC. |
| 8 | Combat | `Present` | `AP-05_A1_STATUS.md`: A1+A3 parziale `Present`. | Nessun dato reale di danno/costo skill; A2+A4 non specificato, in attesa di una decisione (verifica solo-vitali-player vs chiudere il gap HP-mob). |
| 9 | Loot/inventory | `Present` | `AP-07_A1_STATUS.md`: A1+A3 parziale `Present`. | Generazione candidati Equip bloccata (nessun dato categoria item). |
| 10 | Multi-step quest | `Present` | `AP-06_A1_STATUS.md`: A1+A3 `Present`. | Semantic extraction OCR/UI bloccata (stesso gap ML). |
| 11 | Equipment/progression | `Present` | `AP-07_A1_STATUS.md` (equip) + `AP-09_A1_STATUS.md`/`NosAi.Core.Progression` (progressione, preesistente). | Statistiche reali per item mancanti. |
| 12 | Recovery | `Present`, parziale | `AP-08_A1_STATUS.md`: nessun segnale strategico "Recovery" valutato (serve un fatto reale "sono in combattimento adesso"). Il recovery di **sicurezza/esecuzione** (`RecoveryController`, sistema Gate 1-6 preesistente) è invece reale e testato, ma è un concetto diverso (fault recovery, non "riposare per curarsi"). | Nessun segnale di recovery gameplay-level; non riverificato in questa sessione lato Gate 1-6. |
| 13 | Persistence | `Integrated`, parziale | `MapModelStore` (AP-03) reale, SQLite WAL, `Integrated`. Il ledger di AP-09 (`ActionOutcomeLedgerEntry`) è invece solo `Present`: nessuno store lo persiste ancora. | Ledger AP-09 non cablato a una persistenza reale. |
| 14 | Evidence | non riverificato in questa sessione | Il sistema di audit/eventi (`RuntimeEvent`, `SqliteEventJournal`, hash-chain) è preesistente, reale, parte del sistema Gate 1-6 — non riaudita da questa sessione con evidenza fresca. | Da confermare con una verifica dedicata, non assunto per memoria. |

\* **Nota importante sugli stadi 1-2 e sul recovery di sicurezza**: la
loro classificazione `Verified` viene da documentazione preesistente
(`docs/STATO_IMPLEMENTAZIONE.md`), **non da una riverifica hardware
eseguita in questa sessione** (questo ambiente non ha un client NosTale
reale né l'hardware target). Citata come fonte secondaria dichiarata,
non come osservazione diretta di questa fase — la distinzione stessa che
CLAUDE.md richiede.

## `OverallLevel` risultante

Il livello più debole tra i 14 stadi è `Present` (stadi 5-11). **Il
progetto non è certificabile end-to-end in questo momento, in questo
ambiente, e questa fase non lo dichiara.** Nessuno stadio raggiunge
`Verified` per lavoro di questa sessione; gli unici due (`Startup`/
`Attach`) marcati `Verified` lo sono per eredità documentata del sistema
Gate 1-6 preesistente, esplicitamente segnalata come non riverificata
qui.

## Deliberatamente non affrontato qui

- **Esecuzione reale dello scenario end-to-end**: richiede Windows, un
  client NosTale reale, l'hardware target (ASUS Nitro V16/RTX 5060) e un
  telefono Guard AI — nessuno disponibile in questo ambiente di sviluppo.
  Non simulabile senza violare la Real-environment rule.
- **Chiusura dei gap già segnalati** (OCR/ONNX, dati skill/item reali,
  fonte portali, riconciliazione `KnowledgeScope`/`DataSourceKind`):
  elencati per riferimento, non richiusi da questa fase — sono il lavoro
  reale che separa il progetto da una certificazione vera, non un
  dettaglio di reportistica.

**Livello di verifica per il contenuto di questa fase (i contratti
stessi):** `Present` — scritti, testati, compilano puliti. **Il report
compilato sopra non è una certificazione**: è la fotografia onesta,
citata, di quanto lontano il progetto sia da poterne ricevere una.
