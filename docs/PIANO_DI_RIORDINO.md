# Piano di riordino — quattro voci, nessuna riscrittura

**Versione:** 1.0
**Data:** 2 settembre 2026
**Ruolo:** operativo, subordinato a `PIANO_CAPACITA.md`. Quello dice *quale capacità*
si costruisce; questo dice *quale disordine* si toglie, e non aggiunge capacità.
**Regola che vale su tutte le voci:** ogni passo tiene i test verdi. Se toglierne uno
li rompe, quel pezzo non era ridondante — e la scoperta vale il passo.

---

## 0. Perché non si riscrive

Il valore di questo progetto non è il codice: sono le **misure**.
`MapIdModuleOffset = 0x38D1BC` è il superstite di un oracolo su quattro mappe e un
riavvio. Che la telecamera segua il personaggio — quindi che la proiezione assoluta non
esista — è costato cinque tentativi sul client vivo. Che `ct` non azzeri mai il bersaglio
è misurato su 16 selezioni in 90 secondi.

E c'è la ragione che chiude la questione: `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` § 0
elenca **quattro affermazioni su sette** dei documenti di progetto che il confronto col
codice ha smentito. Quei documenti contengono ancora quegli errori. Ripartire da lì li
reimporterebbe tutti.

Il disordine, invece, è enumerabile. Sono le quattro voci qui sotto.

---

## 1. Stato misurato, non ricordato

Rilevato il 2 settembre 2026 leggendo il codice. **Linea di partenza da difendere:
1602 test verdi, 0 falliti** (1449 runtime, 66 core, 87 control panel), build senza
errori.

| Tipo | Dove è definito | Danno |
|---|---|---|
| `VerificationResult` | `Contracts/RuntimeContracts.cs`, `Gate3/Gate3Runtime.cs`, `Gate6/Gate6Runtime.cs` | **tre** definizioni di « com'è andata la verifica » |
| `TrustTier` | `Contracts/RuntimeContracts.cs`, `Autonomy/AutonomyPipeline.cs` | ha rotto un build oggi: riferimento ambiguo |
| `SafetyGate` | `Safety/SafetyGate.cs`, `Autonomy/AutonomyPipeline.cs` | **due** risposte a « quest'atto è autorizzato » |

`Autonomy/AutonomyPipeline.cs` è **1053 righe** e contiene sedici tipi pubblici:
`TrustTier`, `RuntimeMode`, `ActionType`, `RecoveryStrategy`, `MapPoint`,
`ActionTarget`, `ActionCandidate`, `PredictedOutcome`, `GuardEvaluationResult`,
`SafetyToken`, `TrustBoundary`, `GuardPolicyEngine`, `SafetyGate`, `RecoveryState`,
`RecoveryHaltTransition`, `RecoveryController`. È il centro del disordine: metà di
questi appartengono a `Contracts`, `Safety` o `Autonomy`, e stanno insieme per storia,
non per progetto.

`nosai/` ha **18 pacchetti Python**. Cercando nelle CI, negli script e nella
documentazione operativa, l'unico modulo citato è **`nosai.dashboard.server`**. Ci sono
40 test Python.

`ADR-0020` è **proposto e non implementato**: il token firma ancora il solo
`CandidateId`, quindi esistono due contratti di attuazione in parallelo.

---

## 2. Le quattro voci, in ordine di danno

### `R1` — Un tipo, un posto

Il duplicato non è untidiness: è **due risposte alla stessa domanda su un confine di
sicurezza**. Due `SafetyGate` sono due verdetti su « quest'atto è autorizzato », e nulla
impedisce loro di divergere.

| Passo | Chi |
|---|---|
| Decidere quale definizione è canonica per `VerificationResult`, `TrustTier`, `SafetyGate`, e **perché** — la scelta è di sicurezza, non di gusto | Claude |
| Propagare: ogni consumatore usa la canonica, le altre spariscono | Cursor |
| Un test che fallisce se un tipo torna a essere definito due volte | Cursor |

**Fatto quando** — `grep` di ciascun nome trova una sola definizione, e i test sono verdi.

### `R1` — scelte

Decise il 2 settembre 2026 leggendo ogni definizione e ogni consumatore. Il criterio
non è stato « la più vecchia » né « la più usata », ma **quale definizione esprime il
concetto al livello giusto** — e per due nomi su tre la risposta è stata che non c'era
nessun concetto da unificare.

#### `VerificationResult` — canonica: `NosAi.Runtime.Gate3.VerificationResult`

Tre definizioni, e **non erano tre copie della stessa cosa**.

| Definizione | Che cosa dice |
|---|---|
| `Gate3` | `(Guid, VerificationOutcome, float DiscrepancyScore, string, DataSourceKind)` — quattro esiti, una divergenza misurata, la provenienza |
| `Gate6` | `(Guid, bool IsSuccess, float, string)` — due esiti, nessuna provenienza |
| `Contracts` | `(bool Passed, string Reason)` — l'esito di `IAgentVerifier`, un'altra pipeline |

**Gate 3 assorbe Gate 6.** Il `bool` di Gate 6 non sa esprimere `Unverified`, e
collassarlo in « non riuscito » è esattamente ciò che `VER-05` vieta: non osservabile
non è né riuscito né fallito. Ogni esito che Gate 6 sapeva produrre esiste nella
canonica; il contrario è falso.

`AutonomyPipeline.cs` argomentava che unirli « etichetterebbe dati simulati come live ».
**L'argomento non regge**, ed è il motivo per cui va scritto qui invece di essere
ereditato: la canonica di Gate 3 ha il campo `Source`, e Gate 6 lo riempie con
`DataSourceKind.Simulated` — che è ciò che quel gate già dichiara di sé in ogni
`ExecutionResult` che emette. Unificare non ha tolto la dichiarazione di simulazione:
l'ha resa esplicita anche sul verdetto, dove prima era sottintesa.

**Campi che solo una definizione aveva.** Gate 6 non ne aveva nessuno che Gate 3 non
abbia. Nel verso opposto Gate 6 **guadagna** `Source`, e `Outcome` al posto di un
booleano. Nessuna decisione è andata persa; due sono state acquisite.

**`Contracts.VerificationResult` non è un duplicato dei due precedenti.** È l'esito di
`IAgentVerifier.Verify(CandidateAction, object)`, cioè della pipeline
`Orchestration`/`Guard`/`PlayAi`, che ha un proprio vocabolario (`CandidateAction`,
`GuardDecision`, `ISafetyGate`) e verifica un'altra cosa. Misurato: quell'interfaccia
**non ha implementazioni**, e `AutonomousAgentRuntime` — il suo unico consumatore — non
è **mai costruito**, né in `src/` né nei test. Non va unificata: va rinominata insieme
al resto di quella pipeline, o cancellata con essa. È fuori dal mandato di `R1`.

**Applicato.** La copia di Gate 6 è sparita; da tre definizioni a due.

#### `TrustTier` — canonica: `NosAi.Runtime.Autonomy.TrustTier`

| Definizione | Scala |
|---|---|
| `Autonomy` | `Tier0_ReadOnly` … `Tier4_FullAutonomous` (byte) |
| `Contracts` | `Tier1` … `Tier4` (int), **nessun gradino di sola lettura** |

`AutonomyPipeline.cs` sostiene che siano « due domande diverse »: una gradua la
sensibilità di una `RuntimeCapability` richiesta, l'altra l'autonomia del runtime.
**Il codice smentisce il commento.** `IRuntimeAuthorizationPolicy.Evaluate` prende
`requiredTier` **e** `grantedTier` dello stesso tipo e confronta `grantedTier <
requiredTier`; `TrustBoundary.IsAuthorized` confronta `_currentTrust >= requiredTier`.
È lo stesso ordinamento sulla stessa idea di fiducia, usato in entrambi i sensi da
entrambe le pipeline. Un solo concetto.

**Canonica quella di `Autonomy` perché è la sola che sa dire « nessuna autonomia ».**
Con la scala di `Contracts`, `TrustBoundary` non può rappresentare un runtime in sola
lettura: il gradino più basso esprimibile è già un permesso. Una scala di sicurezza a
cui manca lo zero costringe a inventare un valore per « niente », ed è il difetto che
questo progetto rifiuta ovunque altrove.

**Campi persi: nessuno.** I valori `1..4` coincidono numericamente fra le due scale,
quindi ogni confronto e ogni serializzazione per numero resta identica. Si guadagna lo
zero. Cambiano invece i **nomi** dei membri (`Tier1` → `Tier1_Assisted`), quindi la
propagazione non è meccanica: ogni sito d'uso va riscritto a mano.

**Non applicato, e il motivo è misurato.** Togliere `Contracts.TrustTier` produce
**30 errori di compilazione in 8 file**, di cui 6 fuori dal perimetro di questa
sessione: `Adapters/NosTaleGameAdapter.cs`, `Guard/GuardAi.cs`,
`LowLevel/InputControlTestRunner.cs`, `Orchestration/AutonomousAgentRuntime.cs`,
`Orchestration/AutonomousOrchestratorLoop.cs`, `Orchestration/Orchestrator.cs`,
`Security/RuntimeAuthorization.cs`, `Safety/SafetyGate.cs`. La rimozione appartiene
alla propagazione, non alla decisione.

#### `SafetyGate` — **non un duplicato: due concetti sotto un nome**

| Definizione | Domanda a cui risponde | Firma |
|---|---|---|
| `Safety.SafetyGate : ISafetyGate` | « questo *chiamante* ha il diritto di esercitare questa capability? » | `Authorize(CandidateAction, GuardDecision) → bool`, con `SecurityPrincipal` e `IRuntimeAuthorizationPolicy` |
| `Autonomy.SafetyGate` | « emetti una credenziale a uso singolo, firmata e con scadenza, per *questa* azione » | `TryAuthorize(ActionCandidate, PredictedOutcome, RuntimeMode, out SafetyToken)`, `ValidateToken` |

**Zero membri in comune, zero firme in comune, tipi d'ingresso diversi**
(`CandidateAction` contro `ActionCandidate`), uscite diverse (una decisione contro un
token HMAC). Non sono due copie che hanno divergito: sono due stadi distinti di un
percorso di autorizzazione — la **politica** e la **credenziale** — che hanno pescato
la stessa parola.

Entrambi sono vivi e provati: il primo lo costruisce `RuntimeComposition.CreateSafe()`
e lo coprono dieci prove in `RuntimeAuthorizationTests`; il secondo firma ogni token
del ciclo Gate 3.

**Quindi la mossa non è unificare, è rinominare.** Unificarli richiederebbe di
decidere quale delle due domande è quella vera, e nessuna delle due è di troppo. I
nomi proposti dicono il livello:

- `Safety.SafetyGate` → **`CapabilityAuthorizationGate`** — decide su principal e
  capability, non su una singola azione;
- `Autonomy.SafetyGate` → **`ActionTokenIssuer`** — emette e valida la credenziale di
  un atto.

**Campi persi: nessuno**, perché nulla viene fuso.

**Non applicato.** Un rinomina è propagazione per definizione: tocca 28 siti in 9 file,
fra cui `Orchestration/RuntimeComposition.cs` e quattro file di prova.

#### Che cosa ha trovato la prova, e nessuno aveva enumerato

`DuplicateTypeNameTests` guarda l'assembly con la reflection invece di fidarsi
dell'elenco. Oltre ai tre nomi del mandato ne ha trovati **sette**:
`ActionExecutionVerifier`, `AuthorizedActionExecutor` ed `ExecutionResult` (Gate 3 e
Gate 6, tenuti separati per decisione già scritta), `Position2D` — **quattro**
definizioni di un punto sul piano, in `Events.InstantBattle`, `Raids.Dodekatheon`,
`Gate2` e `Gate6` — `CaptureFrame`, `ScreenPoint`, e `Program` (un `Main` per
eseguibile, atteso).

Il documento diceva tre; la misura dice dieci. Ognuno è dichiarato nella prova con il
proprio motivo, e la prova fallisce sia su un duplicato **nuovo** non dichiarato, sia
su una voce dichiarata che **non serve più** — così l'elenco si accorcia da solo
invece di sopravvivere al debito che descriveva.

---

### `R2` — Sciogliere `AutonomyPipeline.cs`

`R1` non regge finché il file che duplica resta un contenitore unico: il prossimo tipo
ci finisce dentro per inerzia.

| Passo | Chi |
|---|---|
| Assegnare ognuno dei sedici tipi al suo posto — contratto, sicurezza, autonomia — e scrivere il criterio, così il diciassettesimo si colloca da solo | Claude |
| Spostare i tipi, un file per famiglia, **senza cambiare una riga di comportamento** | Cursor |

**Fatto quando** — nessun file supera le 400 righe in `Autonomy/`, i test sono verdi, e
il `git diff` non mostra cambi di logica.

### `R3` — Un solo percorso di autorizzazione

`ADR-0020` è deciso e non applicato. Finché non lo è, il token firma un identificativo e
non un atto: `candidate with { Target = ... }` produce un candidato diverso con lo stesso
id, e il token lo valida.

| Passo | Chi |
|---|---|
| Il digest dell'intento: quali campi entrano nell'HMAC, e la regola che rende impossibile firmare un'azione diversa | Claude |
| Il token raggiunge il confine che emette; propagazione e test negativi — contraffatto, bersaglio ricambiato, scaduto, già consumato | Cursor |

**Fatto quando** — un candidato col bersaglio sostituito non valida più, e c'è un test
che lo dimostra.

### `R4` — Tagliare la linea Python

18 pacchetti, **uno solo** citato da script, CI o documentazione operativa. La
cancellazione è la forma più economica di ordine, ma non si fa alla cieca.

| Passo | Chi |
|---|---|
| Classificare i 18 pacchetti: **coperto** da C# testato, **vivo** (sul percorso dell'operatore), **morto**. La classificazione è un giudizio e va scritta | Claude |
| Cancellare i morti, aggiornare CI e `BUILD_TEST_RELEASE.md`, verificare che i 40 test Python restanti passino ancora | Cursor |

**Fatto quando** — `nosai/` contiene solo ciò che è vivo o dichiarato tenuto, con il
motivo scritto accanto.

### Non in questo piano

Rinominare le cartelle `Gate1`…`Gate6` secondo cosa fanno invece che secondo quando sono
nate. È cosmetico, tocca decine di file e non fa male oggi: si fa quando le quattro voci
sono chiuse, o mai.

---

## 3. Cosa può girare in parallelo

`R1`, `R2` e `R3` toccano gli stessi file — `Contracts`, `Safety`, `Autonomy`, `Gate3`,
`Gate6` — quindi **vanno in fila**, in quest'ordine, e non si sovrappongono.

`R4` sta in `nosai/`, `.github/` e `scripts/`: **non incrocia nulla** e può girare
insieme a qualunque altra.

Quindi due corsie, non quattro:

```
corsia A (sequenziale):   R1 -> R2 -> R3
corsia B (parallela):     R4
```

---

## 4. Come si riporta

Come `PIANO_CAPACITA.md` § 7: id della voce, file, build e test con l'esito **reale**,
livello raggiunto, e le domande su cui ci si è fermati invece di decidere.

In più, per ogni voce di questo piano: **il numero di test prima e dopo**. Un riordino
che li fa scendere ha cancellato una decisione insieme al codice, e va guardato.
