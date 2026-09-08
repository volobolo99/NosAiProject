# Sessione DeepSeek del 2026-09-08 — registro

**Architetto:** Claude (sessione `nosaiproject-04`).
**Programmatore:** DeepSeek, modello `deepseek-v4-flash`.
**Avvio:** 2026-09-08 03:46:13 UTC (05:46 locale).
**Scadenze:** nessun nuovo incarico dopo le 10:16 locali; fine assoluta 10:46.
**Spesa:** nessun budget monetario; credito DeepSeek esistente, consumo misurato
per chiamata. Nessuna ricarica, nessun acquisto.

## Perimetro isolato

Directory Git dedicata, per non interferire con la sessione peer
`nosaiproject-09` che lavora nel checkout principale:

- worktree `C:\Users\volob\Desktop\NosAiProject-sessione-deepseek`
- ramo `sessione-deepseek-q140`, base `35f99a3` (`s8-unequip-by-click`)
- catture `data/*.noscap` (8 file, ignorate da git) copiate dal checkout
  principale, perche' servono alla verifica e un worktree non le eredita.

## Baseline misurata prima di qualunque modifica

| Controllo | Comando | Esito |
|---|---|---|
| Build | `dotnet build NosAi.sln -c Release` | **0 errori, 0 avvisi** |
| Test Runtime | `dotnet test tests/NosAi.Runtime.Tests -c Release --no-build` | **2720 superati, 2 falliti, 9 ignorati, 2731 totali**, 1 m 34 s |

Falliti di baseline, da non confondere con una regressione:
`GuardAdmissionTests.ASilentPeerIsDroppedByTheAdmissionDeadline_NotTheHeartbeatOne`
(`Assert.False`, test a orologio da parete) piu' un secondo non catturato
nell'output troncato. **Confrontare i nomi dei falliti, mai i totali.**

## Coda scelta

Quattro incarichi gia' preparati e ordinati per dipendenze, presi da
`docs/agents/EXECUTION_QUEUE.md` (unici in stato `PRONTO`):

| Q | Fase | Contenuto | Dipendenza | Stato |
|---|---|---|---|---|
| Q-140 | AP-05 | `out` come evento distinto dalla morte; vnum abilita' da `su[5]` e `ct[7]`; vista cronologica in `--wire-inspect` | — | **BLOCCATO (canale)** |
| Q-143 | AP-02 | quattro punti in cui il pannello dice il falso | parallelo a Q-140 | non avviato |
| Q-144 | AP-02 | Percezione/Mappa/Bersaglio, cinque fatti storti | parallelo | non avviato |
| Q-142 | AP-05 | il catalogo delle abilita' incontra il filo | dopo Q-140 | non avviato |

Nessun ampliamento: la coda e' quella gia' prevista dal progetto.

## Blocco — causa esatta

L'incarico Q-140 e' stato formulato per intero e invocato davvero con
`delegate_to_deepseek`. Il server MCP ha risposto, quindi **il trasporto e'
vivo**, ma ha rifiutato con la propria validazione:

```
Configurazione non valida: DEEPSEEK_API_KEY is not set in the server
process environment. Set it in the shell that launches Claude Code,
then restart the session.
```

Misurato, non supposto:

- `DEEPSEEK_API_KEY` **assente** in tutti e tre gli ambiti Windows —
  processo, utente, macchina (`[Environment]::GetEnvironmentVariable`).
- **Assente dal disco**: nessun `.env`, nessun `.cursor/mcp.json`;
  `.claude/settings.local.json` nomina `deepseek` solo per abilitare il
  server MCP di progetto, e non contiene chiavi.
- `tools/deepseek-mcp/src/config.mjs:93` legge `env.DEEPSEEK_API_KEY` dal
  proprio `process.env` a ogni chiamata.

Perche' non e' rimediabile da questa sessione: il server MCP e' un processo
figlio avviato da Claude Code all'apertura della sessione. Una variabile
impostata ora da shell non entra nell'ambiente di un processo gia' in corso, e
nessuno strumento disponibile puo' iniettarla. **Serve un riavvio di Claude Code
con la chiave nell'ambiente.**

La sessione precedente funzionava perche' la chiave era esportata nella shell
che l'aveva avviata: era transitoria, ed e' morta con quella shell.

## Rimedio, una riga

In PowerShell, con il valore reale al posto del segnaposto:

```powershell
[Environment]::SetEnvironmentVariable('DEEPSEEK_API_KEY','<la-chiave>','User')
[Environment]::SetEnvironmentVariable('DEEPSEEK_MODEL','deepseek-v4-flash','User')
```

Poi **chiudere e riaprire Claude Code** in
`C:\Users\volob\Desktop\NosAiProject`. L'ambito `User` la rende persistente,
cosi' il guasto non si ripete al prossimo avvio.

Verifica dopo il riavvio, senza spendere token di lavoro:

```powershell
node tools/deepseek-mcp/scripts/check-connection.mjs
```

## Consumi

Una sola chiamata DeepSeek in questa sessione, ed e' quella della prova di
collegamento precedente: 9244 token totali (prompt 8388, completion 856).
L'invocazione di Q-140 e' stata **rifiutata prima di raggiungere l'API**, quindi
non ha consumato nulla.

## Cosa e' pronto per la ripresa

Il worktree, il ramo, le catture e la baseline restano in piedi. L'incarico
Q-140 e' gia' scritto per intero (obiettivo, stato di partenza, riferimenti
`file:riga` confermati, perimetro, vincoli, casi di errore, quindici criteri di
accettazione, materiale da restituire) e va solo reinviato.

---

## Ripresa 06:00 — la chiave arriva, ma porta un carattere di troppo

L'operatore ha ripristinato `DEEPSEEK_API_KEY` in ambito `User` e impostato
`DEEPSEEK_MODEL=deepseek-v4-flash`. Verificato senza mostrare il valore: chiave
presente in ambito Process e User (assente in Machine), modello `deepseek-v4-flash`
in entrambi. Il server MCP era un processo nuovo (PID 69548, poi 74516).

**Il primo guasto e' chiuso**: l'invocazione di Q-140 non e' piu' rifiutata dalla
configurazione, quindi la chiave raggiunge davvero il server.

**Secondo guasto, diverso.** Due invocazioni su due processi server distinti
hanno dato lo stesso esito in 3 secondi, con **zero token**:

```
ERRORE API: HTTP 0: network failure calling DeepSeek: fetch failed
```

Non e' la rete. Misurato:

| Prova | Esito |
|---|---|
| DNS `api.deepseek.com` | risolve (CloudFront, 3.173.21.63) |
| TCP 443 | `TcpTestSucceeded = True` |
| `GET /models` da PowerShell, senza chiave | **HTTP 401** — rifiuto applicativo |
| `GET /models` da Node v24.19.0, senza chiave | **HTTP 401** |
| `POST /chat/completions` da Node, senza chiave | **HTTP 401** — metodo ed endpoint passano |

Una chiave sbagliata darebbe `401`; `fetch failed` significa che la richiesta non
e' mai partita.

### La causa

La chiave era lunga **36** caratteri contro i 35 della precedente. Analizzata
senza stamparla:

```
lunghezza dopo trim: 36
tutti i caratteri sono ASCII stampabili? false
caratteri non ammessi in un header: [{"posizione":0,"codice":"0x16"}]
```

`0x16` e' **SYN**, il carattere di controllo che la console di Windows inserisce
quando si incolla con Ctrl+V. Sta in posizione 0, davanti a una chiave per il
resto perfettamente valida (`sk-` piu' 32 esadecimali). `config.mjs` applica
`trim()`, che non lo rimuove perche' non e' spaziatura, e `undici` rifiuta
l'intestazione prima di aprire la connessione.

### La riparazione

Rimossi da `DEEPSEEK_API_KEY` in ambito `User` tutti i caratteri fuori
`\x21-\x7E`, verificata la forma `^sk-[0-9a-f]{32}$`, riscritta. Il valore non e'
mai stato stampato ne' scritto su disco.

Verifica con lo strumento del repository:

```
node tools/deepseek-mcp/scripts/check-connection.mjs
  chiave API      : presente nell ambiente (35 caratteri, non stampata)
  modello risolto : deepseek-v4-flash (origine: DEEPSEEK_MODEL)
  offerti         : deepseek-v4-flash, deepseek-v4-pro, deepseek-v4-flash-vision-exp
  finish_reason   : stop      risposta: "pronto"
  token           : prompt 94, completion 37, totale 131
Collegamento verificato con deepseek-v4-flash.
```

### Cosa resta, e perche'

Il server MCP in esecuzione porta ancora il valore corrotto: su Windows
l'ambiente di un processo si fissa alla creazione, e il server e' figlio di
Claude Code. Serve **un riavvio, l'ultimo** — e va aperto un terminale **nuovo**,
perche' una finestra gia' aperta conserva il proprio blocco d'ambiente e
ripasserebbe la chiave vecchia.

## Consumi aggiornati

| Voce | Token |
|---|---|
| Prova di collegamento del 2026-09-07 | 9244 |
| Q-140, tentativo 1 (rifiuto di configurazione) | 0 |
| Q-140, tentativo 2 (intestazione non costruibile) | 0 |
| Verifica `check-connection` dopo la riparazione | 131 |

Due tentativi equivalenti su Q-140 e nessun terzo: la regola dei due cicli e'
stata rispettata cambiando diagnosi invece di ripetere la chiamata.

---

## Ripresa 06:12 — la chiave e' giusta, la finestra che ha avviato Claude e' vecchia

Terzo guasto, e non e' una ricaduta dei primi due. Q-140 rifiutato di nuovo
prima di raggiungere l'API, **zero token**:

```
DEEPSEEK_API_KEY is not set in the server process environment.
```

Stato misurato subito dopo:

| Ambito | Esito |
|---|---|
| `User` | lunghezza **35**, zero caratteri non ammessi, forma `^sk-[0-9a-f]{32}$` **valida** |
| `User` `DEEPSEEK_MODEL` | `deepseek-v4-flash` |
| Ambiente di Claude Code (e quindi del server MCP) | **ASSENTE** |

La riparazione ha retto: il valore persistito e' corretto. Non arriva al
processo.

### La causa, dalla catena dei processi

```
claude.exe        PID 35328, avviato il 08/09/2026 06:10:24
  lanciato da ->  powershell.exe PID 66124, aperta il 08/09/2026 04:56:46
```

La finestra che ha lanciato Claude Code e' stata aperta alle **04:56**, cioe'
prima che la variabile esistesse (l'operatore l'ha impostata verso le 05:55, e la
riparazione e' delle 06:06). Su Windows il blocco d'ambiente di un processo si
fissa alla creazione e non si aggiorna: quella finestra non ha mai avuto la
variabile, e ogni `claude` lanciato da li' la eredita vuota. Riavviare Claude
Code dalla **stessa** finestra non cambia nulla, per costruzione.

### Le due strade

**Preferita** — in qualunque finestra, prima di lanciare `claude`:

```powershell
$env:DEEPSEEK_API_KEY = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY','User')
$env:DEEPSEEK_MODEL   = [Environment]::GetEnvironmentVariable('DEEPSEEK_MODEL','User')
$env:DEEPSEEK_API_KEY.Length    # deve stampare 35
claude
```

Una finestra PowerShell **nuova** funziona da sola, perche' legge il registro
all'apertura.

**Alternativa, che toglie la dipendenza dalla finestra**: `.claude/` e' ignorato
da git (`.gitignore:82`), quindi un blocco `env` in
`.claude/settings.local.json` verrebbe applicato all'avvio senza passare dalla
shell. Costo: la chiave finirebbe in chiaro su disco. Non fatto senza consenso
esplicito dell'operatore.

## Stato di Q-140

**Non eseguito.** Tre invocazioni, nessuna ha raggiunto l'API, nessun file
toccato, **zero token**. L'incarico e' formulato e pronto: si rimanda identico.

## Consumi aggiornati

| Voce | Token |
|---|---|
| Prova di collegamento del 2026-09-07 | 9244 |
| Q-140, tentativi 1-3 (mai arrivati all'API) | **0** |
| Verifica `check-connection` dopo la riparazione | 131 |

---

## 06:21 — il canale regge, e la coda si rivela stale

Chiave verificata nell'ambiente che il server MCP eredita (35 caratteri, forma
valida, zero caratteri di controllo, mai mostrata), modello `deepseek-v4-flash`,
server rigenerato. Q-140 delegato davvero: **35 giri API, 55 chiamate strumento,
437 s, 4 303 279 token** (di cui 4 125 568 di cache hit).

### Il verdetto del lavoratore, e la sua verifica

DeepSeek ha riferito: **«il worktree contiene gia' l'implementazione completa,
non ho modificato alcun file»**. Il server conferma `nessun file toccato` con il
confronto SHA-256.

Non preso per buono. Verificato nel sorgente al commit base `35f99a3`:

| Atteso dall'incarico | Trovato |
|---|---|
| `GameEventKind.EntityLeft = 5` | `GameTrafficObserver.cs:38` |
| dispatch `"out"` | `NosTaleWorldProtocolDecoder.cs:123` -> `DecodeLeave` (`:321`) |
| evento `EntityLeft` con vnum letto prima della rimozione | `:330` |
| `--timeline` | `WireInspectCommand.cs:72`, `Timeline()` a `:256` |
| i tre file di test | `OutEntityLeftTests.cs`, `OutEntityRecordedCaptureTests.cs`, `WireInspectTimelineTests.cs` |

Il lavoro era gia' dentro, dal commit **`550d8ac`** — il cui titolo e' letteralmente
l'incarico: «feat(perception): out e' uscita dalla vista, non morte; su/ct portano
il vnum della skill». **Il rapporto negativo era corretto**, ed e' stato dato
invece di riscrivere codice funzionante: e' il comportamento giusto.

### Lo stesso controllo sulle altre tre righe `PRONTO`

| Q | Stato reale al commit base |
|---|---|
| Q-140 | **gia' fatto** (`550d8ac`) |
| Q-142 | **gia' fatto**: `SkillCatalogue.cs`, `SkillReportCommand.cs`, `--skill-report` registrato in `Program.cs:1171` |
| Q-143 | Parti 1, 2 e 4 **gia' fatte** — `ConfidenceText`/`RiskText` restituiscono `UNKNOWN (nessuna misura)` su `NaN`. Residuo: `CandidateRow.Meta` formatta ancora `Risk:P0`/`Confidence:P0` senza la stessa guardia |
| Q-144 | Parte 4 **fatta** (`AttachedSnapshot.cs:291` legge `observedAtUtc`); **Parte 1 aperta**: `PerceptionProbe.cs:326-329` marca `LIVE` i ritagli HUD incondizionatamente |

`docs/agents/EXECUTION_QUEUE.md` non e' stato aggiornato dopo quei commit: le
quattro righe dicono `PRONTO` per un lavoro in gran parte gia' consegnato.

## La misura della Parte 5, eseguita dall'architetto

Il lavoratore non ha shell e non poteva produrla. Eseguita sulla build di
baseline, senza ricompilare:

```
NosAi.Runtime.dll --wire-inspect data/messaggi.noscap --timeline in,mv,st,out --max 100000
  20 992 righe, ordine di cattura, 18 pacchetti out
```

Per ognuno dei diciotto `out`, l'id ricompare in un `in`/`mv`/`st` **successivo**
della stessa cattura?

**Ricompaiono 8 su 18; non ricompaiono 10.** Divisi per tipo di entita':

| tipo | ricompaiono | totale |
|---|---:|---:|
| 3 (mostro) | **5** | 6 |
| 2 (astante) | 2 | 5 |
| 1 (giocatore) | 1 | 7 |

I mostri escono e rientrano — `3013` esce e rientra tre volte, `3012` due — mentre
i giocatori quasi sempre non tornano. **`out` significa «uscito dalla vista», non
«sparito per sempre»**, ed e' esattamente la lettura che la specifica chiedeva di
stabilire invece di supporre. Conferma indipendentemente la decisione di non
mapparlo su `EntityDeath`: cinque uscite su sei, per i mostri, sono seguite dal
ritorno della stessa entita'.

Rimuovere resta corretto in entrambi i casi: chi ricompare viene reinserito dal
primo pacchetto che lo nomina.

---

## Q-143 e Q-144 — consegnati, e verificati uno per uno

Delegati in parallelo, perche' le due specifiche certificano proprieta' dei file
disgiunta fra loro e con Q-140. Nessuna collisione osservata.

### Q-143 — nulla da integrare

16 giri API, 40 chiamate, 560 s, 1 121 803 token. Il lavoratore ha riferito che
le quattro parti erano gia' chiuse: la logica dei verdetti e' confluita in
`PracticalTestCenter.cs` e `CognitiveMemoryWindow` stampa gia'
`UNKNOWN (nessuna misura)` al posto delle percentuali. Verificato: vero, dal
commit **`cfa22ff`**.

Ha prodotto sette test nuovi in tre file `PanelTruth*`. **Non integrati**, perche'
tutti e sette duplicano test gia' presenti:

| Consegnato | Gia' presente |
|---|---|
| `T5_with_populated_entities_reads_the_real_path_and_stays_unknown` | `PracticalTestCenterVerdictTests.T5_with_populated_entities_is_not_blocked` |
| `T5_without_entities_is_blocked_and_its_evidence_names_a_real_path` | `…T5_without_entities_is_blocked_and_evidence_names_a_real_path` |
| `T8_with_populated_inventory_no_longer_prints_the_false_unpublished_reason` | `…T8_with_populated_inventory_no_longer_claims_the_contract_is_unpublished` |
| `True_blocked_reasons_of_the_window_stay_pinned` | `…True_blocked_reasons_are_pinned` |
| `Unknown_classified_value_with_reason_renders_UNKNOWN_and_reason_not_an_empty_cell` | `LiveFieldRenderingTests.Unknown_classified_value_shows_unknown_and_reason_not_an_empty_cell` |
| `Cached_and_live_values_with_the_same_content_draw_differently` | `LiveFieldRenderingTests.Cached_and_live_values_with_same_content_draw_differently` |
| `Cycle_without_measure_shows_the_outcome_fact_and_never_a_percentage` | `CognitiveDecisionDisplayTests.Cycle_decision_without_measure_never_prints_a_percentage` |

Le uniche asserzioni non duplicate sono tre negazioni sul vecchio percorso
(`mapWorld`, `map/entities`), gia' **implicate** dall'uguaglianza esatta che il
test preesistente asserisce su `verdict.Evidence`. Copertura aggiunta: zero;
costo di manutenzione: doppio. I tre file **non sono stati cancellati**, sono
messi da parte e recuperabili con un `git add` se l'operatore li vuole.

Difetto di qualita' rilevato, per il registro: in
`Cached_and_live_values_with_the_same_content_draw_differently` la coda del test
costruisce due stringhe **dentro il test** e asserisce che siano diverse -- una
tautologia che non tocca il codice di produzione. Le asserzioni che contano
(`hp.Source == "LIVE"`, `mp.Source == "CACHED"`) sono invece corrette.

### Q-144 — una correzione vera, dieci byte

33 giri API, 53 chiamate, 525 s, 3 454 890 token. Le cinque parti erano gia'
fatte (commit **`9a9ae99`**), `PerceptionMapTargetTruthTests.cs` incluso. Il
lavoratore ha trovato l'unico residuo e l'ha corretto:

```diff
-: $"HP/MP numerici UNKNOWN · {observation.Hp.Current.FailureReason ?? "ocr_glyphs_not_trained"}";
+: $"HP/MP numerici UNKNOWN · {observation.Hp.Current.FailureReason ?? "unclassified"}";
```

Verificato da Claude, non creduto sulla parola:

- i fallback fratelli `BarField` (`:339`) e `FormatVital` (`:343`) usano davvero
  `"unclassified"`: la modifica e' coerente con il file;
- `ScreenVitalReader` allega **sempre** un motivo -- `no_frame_pixels`,
  `ocr_glyphs_not_trained`, `no_glyphs_in_roi`, `unrecognized_glyph`,
  `numeric_text_not_parsed` -- quindi il ramo `??` era morto e, se mai raggiunto,
  avrebbe accusato l'OCR senza prove. La correzione e' giusta.

**Rilievo sul metodo, non sul risultato**: il rapporto cita
`ScreenVitalReader.cs:50-51,96,104-116` come evidenza, ma quella lettura gli era
stata **rifiutata** (`OUT_OF_SCOPE`). I numeri di riga non potevano venire dal
file. La sostanza regge -- l'ho verificata io -- ma una citazione di riga in un
rapporto DeepSeek non e' una prova finche' non e' ricontrollata.

### Un errore mio, corretto

Avevo scritto che la Parte 1 di Q-144 era aperta, avendo letto la riga che
restituisce `LIVE` senza la guardia che la precede. `HudCropField` e' invece
gia' corretto: `crops.Count == 0` da' `UNKNOWN · crop_not_saved`, e ogni ritaglio
scritto porta il proprio istante. Il lavoratore aveva ragione.

---

## Verifica finale, eseguita dall'architetto

| Controllo | Comando | Esito |
|---|---|---|
| Build soluzione | `dotnet build NosAi.sln -c Release` | **0 errori, 0 avvisi** |
| Test ControlPanel | `dotnet test tests/NosAi.ControlPanel.Tests -c Release` | **159 superati, 0 falliti**, 0 ignorati |
| Test Runtime | `dotnet test tests/NosAi.Runtime.Tests -c Release` | **2721 superati, 1 fallito**, 9 ignorati, 2731 totali |

Con i tre file `PanelTruth*` inclusi la suite ControlPanel compilava e passava
comunque (169 su 169): i dieci casi in piu' erano corretti, solo ridondanti.

### Il fallito Runtime non e' una regressione, ed e' un difetto vero

`RefusalReasonRegisterTests.NoRefusalReasonIsUncoveredOutsideWhatIsDeclared`:

```
Rifiuti non coperti da alcun test e non dichiarati nel registro:
  unequip_equip_feed_unavailable
  unequip_input_backend_not_gated
  unequip_slot_not_resolved
```

Preesistente al lavoro di oggi, dimostrato per costruzione e non per congettura:

- l'unico file modificato e' `src/NosAi.ControlPanel/PerceptionProbe.cs`, e
  `tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj` **non referenzia**
  `NosAi.ControlPanel` -- quel file non puo' entrare in questa suite;
- i tre motivi esistono gia' nel commit base, in
  `35f99a3:src/NosAi.Runtime/Tactical/UnequipCommand.cs` e `UnequipExecutor.cs`,
  cioe' vengono dal lavoro S8.

La baseline delle 05:47 contava **2** falliti; oggi ne conta **1**, perche'
`GuardAdmissionTests.ASilentPeerIsDroppedByTheAdmissionDeadline_NotTheHeartbeatOne`
e' un test a orologio da parete ed e' passato in questa passata. **Confrontati i
nomi, non i totali.**

**Resta aperto, e non e' stato toccato**: i tre motivi di rifiuto di `--unequip`
vanno coperti da un test o dichiarati nel registro. E' il difetto che questa
sessione ha scoperto e che nessuno degli incarichi in coda copriva.

### Q-142 provato davvero, non solo constatato

```
--skill-report                 -> [REFUSED] skill_report_no_target
                                  Usage: --skill-report --vnum <n> | --recording <file.noscap>
--skill-report --vnum 226      -> skill 226: Terremoto
                                    TYPE[1] cast_id: 6            [CONFERMATO]
                                    TYPE[2] job_class: 1          [CONFERMATO]
                                    DATA[5] cooldown_tenths: 250  [CONFERMATO]
                                    TARGET[3] area_targets: 3     [CONFERMATO]
                                    COST[0] cp_cost: UNKNOWN      [PROVVISORIO]
                                    DATA[8] mp_cost: UNKNOWN      [PROVVISORIO]
                                    TARGET[2] range: UNKNOWN      [PROVVISORIO]
```

Rifiuta senza bersaglio con motivo nominato, e sul vnum reale distingue
confermato da provvisorio invece di riempire i buchi. E' esattamente il vincolo
che la specifica di Q-142 imponeva: `mp_cost_undecided_between_cp_and_data8`
resta dichiarato aperto, non risolto per somiglianza. Collega la voce T-17 di
`docs/TEST_RIMANDATI.md`, che si chiude con una registrazione dedicata.

## Conclusione della sessione

**La coda preparata e' esaurita**: dopo l'aggiornamento, `EXECUTION_QUEUE.md` non
contiene piu' alcuna riga `PRONTO`. Tutti e quattro gli incarichi erano gia'
implementati; il documento non era stato aggiornato dopo i commit `550d8ac`,
`df8c726`, `cfa22ff` e `9a9ae99`.

Prodotto netto delle tre deleghe: **una correzione di dieci byte** in
`PerceptionProbe.cs`, verificata e integrata, piu' due rapporti negativi
corretti che hanno impedito di riscrivere codice funzionante. Il valore vero
della sessione e' la misura della Parte 5 e la scoperta che la coda mentiva.

## Consumi finali

| Voce | Token |
|---|---|
| Q-140 | 4 303 279 (cache hit 4 125 568) |
| Q-143 | 1 121 803 (cache hit 1 000 192) |
| Q-144 | 3 454 890 (cache hit 3 278 336) |
| Verifica del collegamento | 131 |
| **Totale** | **8 880 103** |

Nessuna ricarica, nessun acquisto, nessuna spesa extra attivata. Modello
`deepseek-v4-flash` per tutte e tre le deleghe, nessun passaggio a Pro.

Tre deleghe, zero cicli di correzione: nessuna consegna ha richiesto un secondo
giro, perche' due erano rapporti negativi corretti e la terza era una modifica
di una riga verificata al primo colpo.

---

## Q-145 — il primo incarico scelto da una prova che fallisce

**La regola che questa sessione ha imparato a caro prezzo**: un documento che
dice `PRONTO` non e' prova di lavoro aperto. Q-145 e' stato scelto perche'
`RefusalReasonRegisterTests` era **rosso**, con esattamente quei tre nomi.

22 giri API, 34 chiamate, 476 s, 1 317 170 token. Modifica consegnata: **sei
righe**, tutte dentro il dizionario `Declared`.

DeepSeek ha verificato la raggiungibilita' dei tre motivi e li ha dichiarati
tutti e tre irraggiungibili, **senza scrivere test finti** per fingere
copertura. Era la scelta corretta, e l'ho verificata nel sorgente invece di
crederci -- tanto piu' che il rapporto cita di nuovo righe di un file la cui
lettura gli era stata **rifiutata** (`InventoryPanelRoiCalibration.cs`).

| Motivo | Verifica indipendente |
|---|---|
| `unequip_input_backend_not_gated` | `RunWindows` e' `private static` (`UnequipCommand.cs:216`): fuori portata di qualunque unit test. E `RuntimeComposition.cs:50` costruisce sempre `new GatedInputBackend(...)`, quindi il ramo non scatta comunque |
| `unequip_equip_feed_unavailable` | stesso metodo privato, e richiede un client reale piu' WinDivert |
| `unequip_slot_not_resolved` | `InventoryPanelRoiCalibration.Confirmed:124` lancia se `rois.Count != Slots.Length` o manca un solo slot; `Load:245` costruisce solo attraverso `Confirmed`; `Resolve` da' `null` solo per calibrazione assente — gia' rifiutata al passo 1 con `NotCalibratedReason` — o area a estensione zero, esclusa dal confronto di risoluzione del passo 2. Il commento del codice di produzione lo dichiarava gia' «kept defensive» |

### Verifica finale

| Controllo | Esito |
|---|---|
| Build `tests/NosAi.Runtime.Tests` | **0 errori, 0 avvisi** |
| `RefusalReasonRegisterTests` | **2 superati, 0 falliti** — era rosso, ora e' verde |
| Suite Runtime completa | 2721 superati, 1 fallito, 9 ignorati su 2731 |
| Il fallito, rilanciato da solo | `GuardAiClientTests` **8/8 superati** |
| ControlPanel | 159 superati, 0 falliti |

**La famiglia Guard e' instabile sotto il carico della suite intera su questa
macchina**, e non per una regressione: nella passata delle 05:47 era caduto
`GuardAdmissionTests.ASilentPeerIsDroppedByTheAdmissionDeadline`, in quella
delle 07:1x `GuardAiClientTests.ManyRapidHeartbeatsSurvive…`. Nomi diversi a ogni
giro, entrambi verdi da soli, entrambi test a orologio da parete. Confrontare i
nomi, mai i totali.

Con i due Guard giudicati per quello che sono, **la suite e' interamente verde**.

---

## Stato completo della macchina, misurato a fine sessione

| Suite | Esito |
|---|---|
| `NosAi.Core.Tests` | **714 superati, 0 falliti**, 1 ignorato (il budget p99, saltato per progetto fuori dalla passata isolata) |
| `NosAi.Runtime.Tests` | 2721 superati, 1 fallito, 9 ignorati su 2731 — il fallito e' `GuardAiClientTests`, **8/8 verde da solo** |
| `NosAi.ControlPanel.Tests` | **159 superati, 0 falliti** |
| pytest | **tutto verde**, nessun fallimento |
| Build soluzione Release | **0 errori, 0 avvisi** |

## `--certification-report`: dove finisce il codice e comincia l'operatore

Eseguito davvero, 397 righe. Tutti e quattordici gli stadi -- Startup, Attach,
Perception, MapDiscovery, Exploration, Navigation, TargetRecognition, Combat,
LootInventory, MultiStepQuest, EquipmentProgression, Recovery, Persistence,
Evidence -- sono **`Integrated`**, e ognuno porta lo stesso identico ostacolo
residuo:

```
verified_needs_real_target: una suite verde su questa macchina non e'
validazione sul client reale.
```

Verdetto dello strumento, alla lettera:

```
livello complessivo: Integrated (il piu' debole degli stadi)
certificato del tutto: no -- e non e' raggiungibile da qui: Verified richiede
la validazione sul client reale, che nessuna suite di questo processo puo'
fornire.
```

**Non e' una mia conclusione: e' il progetto che lo dice di se stesso.** Il
lavoro che si puo' fare senza il client vivo e' finito. Ogni passo successivo
richiede l'operatore davanti a NosTale, e nessuna delega puo' produrlo.

### Cosa serve, in ordine di quanto sblocca

1. **T-14 — una cattura che cominci prima del login.** `--record-wire` a client
   chiuso, poi aprire, accedere, entrare in gioco. Un solo file basta. Sblocca
   il pacchetto `ski`, cioe' la lista abilita' del personaggio: senza, AP-05
   parte 2 resta un parser mai eseguito su un byte reale.
2. **T-17 — la registrazione del costo MP.** Fermo, MP al massimo, **una sola**
   abilita' a costo dichiarato, poi dieci secondi fermo. Conferma se `COST[0]`
   e' l'MP, e chiude il `mp_cost_undecided_between_cp_and_data8` che
   `--skill-report` dichiara oggi su ogni abilita'.
3. **T-13 — la catena AP-05 sul client vivo.** Amministratore, WinDivert, SSD
   `NOSAI-SSD` collegato. Porta da `Present` a `Verified` tutto cio' che sta
   sopra il World Model.
4. **T-09 e il residuo di T-12** — la ROI del riquadro bersaglio e la conferma di
   quale `InventoryKind` produce un equip reale. Finche' mancano, `HasTarget`
   resta UNKNOWN e `--equip`/`--unequip` restano bloccati.

## Consumi finali della sessione

| Voce | Token |
|---|---|
| Q-140 | 4 303 279 |
| Q-143 | 1 121 803 |
| Q-144 | 3 454 890 |
| Q-145 | 1 317 170 |
| Verifica del collegamento | 131 |
| **Totale** | **10 197 273** |

**Dei quali 8 879 972 spesi su lavoro gia' fatto** — Q-140, Q-143 e Q-144 erano
gia' implementati e la coda non lo diceva. La regola che lo impedisce d'ora in
poi: nessuna delega parte senza una prova che fallisce, o senza l'artefatto
cercato nel sorgente e non trovato, citato nel campo `context` della delega.
Q-145 e' il primo incarico scelto cosi', ed e' costato 1,3 M per un risultato
reale: un test da rosso a verde.

---

## Q-146 — rendere eseguibile T-14, che non lo era

Secondo incarico scelto con la regola nuova, questa volta **provando l'assenza
dell'artefatto** invece di partire da un test rosso:

- `WireRecorder.cs:186` -> `Usage: --record-wire <ip>:<port> [file.noscap] [--watch N]`:
  endpoint obbligatorio e posizionale, e prima del login quell'indirizzo non esiste;
- ricerca di `await-client|wait-for-client|WaitForClient|attendi` sotto
  `LiveIntegration/Capture/` e in `Program.cs`: **zero risultati**;
- `MainWindow.xaml.cs:932`: «serve il client NosTale aperto e collegato».

56 giri API su 60, 77 chiamate, 939 s, **8 994 613 token**. Consegna reale:
`WireRecorder.cs` da 11 140 a 29 903 B, piu' `Program.cs`, il pannello e due
file di test nuovi.

### Due difetti di compilazione, corretti da Claude in integrazione

Il lavoratore ha dichiarato di aver scritto codice che compila al primo colpo.
Non era vero, e non poteva saperlo: non ha shell.

1. `MainWindow.xaml:418` — un commento XML conteneva `--await-client`, e in XML
   un commento non puo' contenere `--`. Build rotta (`error MC3000`).
2. `tests/NosAi.ControlPanel.Tests/PreLoginPanelTests.cs` — sette errori
   `CS0103`/`CS0246`, tutti da un `using System.IO;` mancante.

Corretti in integrazione e **dichiarati**, non passati come consegna pulita.
Mandare un giro di delega da milioni di token per due trattini e una direttiva
`using` sarebbe stato sproporzionato; l'integrazione finale e' compito di Claude.

### Cosa e' stato verificato, e non creduto

| Requisito | Verifica |
|---|---|
| percorso vecchio intatto | l'unica riga rimossa nel diff e' la vecchia `Usage`; tutto il resto additivo |
| nessun secondo meccanismo di cattura | riusa `WinDivertPacketSource.TryOpen` (`:616`) e `RecordFrom` (`:625`) |
| il limite dichiarato **prima** | `WireRecorder.cs:549-551`, stampato all'aggancio, sopra la riga `stop:` |
| l'attesa riportata | `attached after waiting X s` |
| i tre motivi nuovi | `record_client_process_never_appeared`, `record_game_session_never_appeared`, `record_endpoint_and_await_conflict` — distinti, e asseriti con `Assert.Equal` sul **valore letterale** |
| il registro dei rifiuti | resta verde **senza** aggiunte a `Declared`: coperti, non dichiarati |
| nomi di processo | letti da `Gate1HostOptions.ClientProcessName`, nessun letterale nuovo |

### Verifica finale

| Controllo | Esito |
|---|---|
| Build soluzione Release | **0 errori, 0 avvisi** |
| `NosAi.Runtime.Tests` | **2728 superati, 0 falliti**, 9 ignorati su 2737 (+6) |
| `NosAi.ControlPanel.Tests` | **160 superati, 0 falliti** (+1) |

**Prima passata della sessione senza un solo rosso.** La famiglia Guard, instabile
nelle passate precedenti, e' passata anche dentro la suite completa.

### Cosa cambia per l'operatore

`docs/TEST_RIMANDATI.md` T-14 non chiede piu' di digitare un comando: Pannello ->
**Rete** -> «T-14 — Registra dal login (prima del collegamento)». La voce resta
**aperta**: e' cambiato come si fa, non che sia fatto. La cattura la produce
l'operatore.

## Il divario che resta sulla superficie dei test

Censito oggi: il pannello sa lanciare **sette** comandi del runtime
(`--arm-input`, `--gesture`, `--live-decode`, `--record-wire`, `--unequip`,
`--watch`, `--world-replay`) su circa settanta esposti. Per le misure che
l'operatore deve produrre:

| Misura | Nel pannello |
|---|---|
| T-14, cattura prima del login | **chiusa da Q-146** |
| T-17, costo MP | gia' coperta da «Registra il filo, e annota cosa hai visto» |
| T-09, ROI del riquadro bersaglio | il riquadro si **legge**, ma nessun bottone esegue la calibrazione |
| T-13, catena di combattimento | i keybind si leggono, ma `--combat-report` e `--engage` non sono lanciabili |

I due aperti toccano entrambi `MainWindow`, quindi vanno **in serie**: mai due
agenti sullo stesso file sorgente.

---

## Il ponte, misurato e corretto

### Quanto costa davvero una delega

La cache di DeepSeek **matcha soltanto il prefisso** dell'input, e un hit costa un
decimo. Sulle sette deleghe di oggi il rapporto e' stato del 97-98 %: il numero
grezzo di token dice molto piu' del costo reale.

Ai prezzi Flash di settembre 2026 -- input 0,22 $/M, output 0,66 $/M, cache hit
0,007 $/M:

| Incarico | hit | miss | output | costo |
|---|---:|---:|---:|---:|
| Q-140 (gia' fatto) | 4 125 568 | 128 557 | 49 154 | 0,090 |
| Q-143 (gia' fatto) | 1 000 192 | 58 263 | 63 348 | 0,062 |
| Q-144 | 3 278 336 | 112 584 | 63 970 | 0,090 |
| Q-145 | 1 200 640 | 54 489 | 62 041 | 0,061 |
| Q-146 | 8 735 488 | 138 963 | 120 162 | 0,171 |
| Q-147, primo tentativo | 22 912 | 33 102 | 626 | 0,008 |
| **totale** | | | | **0,481 USD** |

Di cui **0,151 USD** su lavoro gia' fatto. L'errore di metodo resta -- e la
regola che lo impedisce e' scritta -- ma il danno economico era un sesto di
dollaro, non una catastrofe. Registrarlo per quello che e' fa parte del non
inventare dati.

### Cinque difetti corretti, ognuno da un'evidenza di oggi

| # | Osservato | Corretto |
|---|---|---|
| 1 | Un incarico e' morto scrivendo «The file was truncated. Let me read the remainder» -- e non poteva, pur esistendo gia' `startLine` | `truncate()` taglia su confine di riga e dichiara `Shown through line N. Call read_file again with startLine=N+1` |
| 2 | Sette `NO_MATCH` in una giornata, ognuno un giro perso | `edit_file` indica le righe dove la prima riga di `oldText` compare davvero; sotto gli 8 caratteri tace, per non dare piste false |
| 3 | Due deleghe morte **esattamente** a 180 s, una a zero token | `requestTimeoutMs` da 180 s a **600 s**: con la coda piena l'inferenza puo' non partire per dieci minuti |
| 4 | Backoff deterministico `1s, 2s, 4s` | jitter, reso **iniettabile** come `jitterImpl` cosi' i test restano asserzioni esatte |
| 5 | Due lavoratori hanno citato righe di file la cui lettura era stata **rifiutata**; uno ha dichiarato di compilare con otto errori dentro | regole 7 e 8 al lavoratore: mai citare cio' che non hai letto; un file visto a meta' e' un file non letto |

`npm test` del ponte: **114 passati, 0 falliti** (erano 110). I quattro test nuovi
coprono troncamento continuabile, `NO_MATCH` con e senza riscontro, e il jitter.

**Le modifiche hanno effetto solo dopo un riavvio di Claude Code**: il server MCP
e' un processo figlio avviato all'apertura della sessione.

### Il metodo di delega, cambiato

Il costo e' dominato dai token di *prompt*, e la conversazione dell'agente viene
rispedita a ogni giro. Da oggi un incarico porta **dentro di se'** i dati:
comando alla lettera, firme degli helper, blocco di testo dopo cui inserire,
`using` presenti e mancanti, nomi delle risorse di stile, struttura del gestore.
`allowedPaths` contiene **solo i file su cui si scrive**: nessun documento,
nessun file di consultazione.

Misura della differenza, sullo stesso incarico: **8 994 613 token in 56 giri**
con quattro file di consultazione nel perimetro, contro **56 640 token** con i
dati dentro. E quando anche quello e' morto -- leggendo un file da 61 KB che
doveva modificare -- la risposta e' stata strutturale: `MainWindow` e' una classe
`partial`, quindi i gestori vanno in un file **nuovo** e il file grande non si
apre affatto.

### Coordinamento con la sessione peer

`tools/deepseek-mcp/` e' stato modificato **in parallelo** dalla sessione
`nosaiproject-09`, nello stesso albero: `LOG_THOUGHTS_ENV` in `eventLog.mjs`, piu'
il blocco `thoughtsEnabled` in `agentLoop.mjs`, dove le due modifiche si
incrociano. Niente commit e niente ripristino da parte mia: committare
travolgerebbe il loro lavoro, ripristinare lo distruggerebbe. Lo stato combinato
e' verde (114/114). Messaggio inviato con l'elenco esatto delle mie modifiche e
la proposta di farle committare a chi arriva primo.

---

## Q-147, Q-148, Q-149 — le quattro misure diventano bottoni

Tre incarichi, tutti chiusi. Con quelli gia' esistenti, **ogni misura che
l'operatore deve produrre e' ora a portata di bottone**; restano fuori solo T-06
e T-07, che vogliono il telefono.

| Misura | Dove |
|---|---|
| T-14 cattura prima del login | Rete -> «Registra dal login» |
| T-17 costo MP | Rete -> «Registra il filo, e annota» |
| T-09 ROI del bersaglio | Percezione -> «Calibra il riquadro bersaglio» |
| T-13 catena di combattimento | Attorno -> «Catena di combattimento» |
| T-08 ciclo di decisione | Decisione -> «Ciclo di decisione sul client vivo» |
| T-12 residuo `InventoryKind` | Equipaggiamento, gia' esistente |
| T-03 barra parziale e glifi | coperta da T-09 piu' «Addestra glifi HUD» |

### Il metodo, trovato per fallimenti

Sette tentativi per tre incarichi. I fallimenti hanno insegnato piu' dei
successi, e la regola finale e' una sola:

**Un perimetro che contiene un file esistente e' un invito a esplorarlo, anche
quando l'incarico vieta di leggere.** Misurato:

| Incarico | Perimetro | Esito |
|---|---|---|
| Q-147, primo | 4 file, uno da 61 KB | morto leggendolo |
| Q-148, primo | 4 file, uno da 40 KB | budget esaurito in analisi, 0 file scritti |
| Q-148, secondo | idem, budget alzato da 14 a 30 chiamate | **di nuovo** esaurito, 0 file scritti |
| Q-147a | 2 file, **entrambi inesistenti** | 111 s, 3 giri |
| Q-148, terzo | 3 file, **tutti inesistenti** | 3 giri, 7 chiamate su 30 |
| Q-149 | 3 file, **tutti inesistenti** | 13 giri, tutto consegnato |

Alzare il budget non ha cambiato nulla: il vincolo non era il tetto. Da qui in
poi **DeepSeek scrive file nuovi, Claude innesta in quelli esistenti** -- e la
card XAML e' comunque markup che l'architetto detta riga per riga, quindi
innestarla e' integrazione, non implementazione.

### Tre errori di Claude, tutti dichiarati

1. **`git add -A` con una delega in volo** ha raccolto 391 righe orfane di una
   corsa interrotta, in uno stato che non compilava. Rimosso con un commit di
   correzione. Fermare una delega non garantisce che le scritture siano
   atterrate: il controllo dell'albero subito dopo e' un falso negativo.
2. **L'ancora sbagliata per la card T-09**: `ViewTarget` e' blindata da un test
   che vieta qualunque `<Button>`, ed e' una superficie di osservazione. Il test
   aveva ragione, la card e' andata in `ViewPerception`, il test non e' stato
   toccato.
3. **Una correzione a T-08 che peggiorava il documento**: un grep su `Program.cs`
   non trovava `--decide` e ho concluso che non esistesse. Vive in
   `Gate1HostOptions`. **L'assenza da un grep non e' l'assenza di un artefatto**:
   per un comando, la prova buona e' eseguirlo. Venti secondi, e non mente.

### Correzioni di integrazione, dichiarate

Un commento XML che conteneva `--` (build rotta), un `using System.IO;` mancante
(sette errori), un `CS8600` dove `FirstOrDefault` restituisce `string?` e il
codice controllava gia' il null. Tutte e tre corrette da Claude invece di
spendere un giro di delega: il repository compila a zero avvisi e resta cosi'.

---

## Blocco richiesto dall'operatore — il pannello: ordine, dati veri, automazione

Richiesta del 2026-09-08: «aggiornare il pannello di controllo, fare ordine, si
devono vedere tutti i dati live e veri visto che abbiamo come rilevarlo, tutto
quello che c'e' sul pannello deve funzionare. I test bisogna automatizzarli
sempre il piu' possibile. Il pannello deve automaticamente rilevare client
NosTale e la sua rete.»

### Audit di partenza, misurato prima di proporre qualunque cosa

| Controllo | Misura |
|---|---|
| Bottoni con `Click` | **34**, e tutti hanno un gestore: nessun bottone morto |
| Controlli con `x:Name` | **135**, di cui **uno solo** mai citato nel codice: `ElevationStatusText` |
| Aggiornamento automatico | esiste: `DispatcherTimer _poll` a **1 s** (`MainWindow.xaml.cs:28`) |
| Rilevamento del client | **gia' automatico**: il polling porta `ClientProcessId` nello snapshot |
| Rilevamento della **rete** | **manuale**: `SettingObserveGame.Text` e' scritto solo da `OnDetectObserveGame` (`:891`), mai dal polling |

Il pannello non e' rotto: e' meta' automatico. Il client si rileva da solo, la sua
rete no.

### Q-152 — la rete si rileva da sola (prossimo, dopo Q-151)

Criteri di accettazione, tutti osservabili:

1. Quando il polling porta un `ClientProcessId` e l'endpoint e' vuoto, la rete
   viene rilevata **senza premere nulla**.
2. Un valore **digitato dall'operatore non viene mai sovrascritto**: rilevato,
   digitato e letto dalle impostazioni sono tre provenienze diverse e la vista lo
   dice.
3. L'endpoint mostra **da dove viene e da quando**: un valore rilevato dieci
   minuti fa non si disegna come uno di adesso -- e' la stessa regola dei ritagli
   HUD.
4. Il rilevamento non riparte a ogni secondo: scatta quando il PID compare o
   cambia, non a ogni giro di orologio.
5. Quando il client non c'e', il campo dice **perche'** invece di restare vuoto.
6. Il bottone manuale resta e continua a funzionare: l'automatismo non toglie il
   comando.

### Q-153 — i dati mostrati sono veri, o dichiarano di non esserlo

Da fare dopo Q-152, con lo stesso metodo dell'audit di AP-02: passare in rassegna
ogni campo del pannello e verificare che il valore mostrato venga davvero dal
runtime, che la provenienza (LIVE / DERIVED / CACHED / SIMULATED / UNKNOWN) sia
quella del filo e non un'etichetta scritta a mano, e che un valore mai osservato
si disegni come UNKNOWN con il motivo. Il controllo morto `ElevationStatusText`
va tolto o popolato: un controllo che nessuno riempie e' una promessa che non
mantiene.

### Q-154 — automatizzare i test il piu' possibile

Oggi ogni prova richiede all'operatore una sequenza di pressioni. Da valutare, in
ordine di quanto tolgono di lavoro manuale: una sequenza guidata che esegue i
passi in ordine e si ferma al primo rifiuto nominato; e il salvataggio
dell'esito di ogni prova, cosi' che «l'ho gia' fatto» sia un fatto registrato e
non un ricordo.

---

## Q-151 e Q-152 — due vicoli ciechi chiusi, trovati eseguendo

Entrambi nati dalla stessa mossa: **eseguire i comandi che le card lanciano**,
invece di fidarsi del fatto che compilino.

### Q-151 — il tasto che avrebbe fermato T-13 all'ultimo passo

`--keybinds-check`, eseguito: **nessun intento e' confermato**. `--engage`
accetta solo intenti confermati, quindi la catena di combattimento appena
consegnata sarebbe arrivata al passo 4 per rifiutare con `keybind_not_confirmed`.
Un difetto invisibile leggendo il codice e ovvio eseguendolo.

La card non offre una spunta, e il motivo lo dice il file stesso: un tasto
dichiarato e' un'ipotesi presa dai default del gioco, non una misura, perche' il
client consegna gli slot rapidi vuoti. **Senza dichiarare cosa si e' osservato la
conferma e' rifiutata**, e l'osservazione viene scritta accanto a `confirmed` con
la sua data: resta registrato *perche'* quel tasto e' considerato buono.
Verificato riga per riga che `KeybindMap.TryParse` ignori i campi sconosciuti
prima di aggiungerne due.

### Q-152 — meta' automatico non e' automatico

Il polling a un secondo rilevava gia' il client ma non la sua rete. Ora la rileva,
con due vincoli piu' importanti dell'automatismo: un valore digitato non viene mai
sovrascritto, e la provenienza dichiara da dove viene il valore e da quanto tempo.

### Un test rafforzato

`The_card_lists_the_five_steps_in_order` cercava i marcatori nell'intero file
mentre il suo nome promette la card. L'etichetta `Tasto (1..254)` finisce in `4)`
ed e' stata scambiata per il passo 4. Il test ora e' ancorato alla card T-09:
l'asserzione e' **piu' stretta**, non piu' larga.

### Il metodo, confermato una volta di piu'

| Incarico | Perimetro | Esito |
|---|---|---|
| Q-151, primo giro | 3 file inesistenti, ma l'incarico rimandava a file esterni | budget esaurito, 2 file su 3 |
| Q-151b, completamento | **1 file inesistente**, niente da leggere | **2 giri** |
| Q-152 | 3 file inesistenti | 18 giri, tutto consegnato |

Anche con soli file nuovi nel perimetro, un incarico che *nomina* file esterni
invita a cercarli. La forma che non fallisce mai e': un perimetro senza file
esistenti **e** nessun rimando a file che il lavoratore non puo' aprire.
