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

### `R2` — assegnazione

Decisa il 2 settembre 2026 leggendo i sedici tipi e ognuno dei loro consumatori, con
`R1` già propagato: `Contracts.TrustTier` non esiste più, `Autonomy.SafetyGate` è
`ActionTokenIssuer` e `Safety.SafetyGate` è `Safety/CapabilityAuthorizationGate.cs`.
Questa sezione usa i nomi nuovi.

I tipi pubblici in `AutonomyPipeline.cs` sono **diciassette**, non sedici. Il
diciassettesimo è `AutonomyPipelineNotes`: una classe statica con dentro una `const
string` che ripete il nome del proprio namespace, e sopra il commento che racconta la
storia del file. Nessuno la costruisce, nessuno la legge, nessun documento la cita —
misurato, zero riferimenti in `src/`, `tests/` e `docs/`. È il caso di prova migliore
che il criterio potesse avere, ed è trattata qui come gli altri sedici.

#### Il criterio

> **Un tipo sta nel file del componente che può cambiarlo da solo. Se cambiarlo
> richiede l'accordo di più componenti, non appartiene a nessuno di loro: è il
> vocabolario su cui devono mettersi d'accordo, e sta in `Contracts/`.**

La domanda da porre al diciottesimo tipo è una sola, e si risponde contando:

**chi deve essere d'accordo per aggiungergli un valore, o un campo?**

- **Uno solo** → il tipo sta nel file di quel componente. Non ha vita propria: è ciò
  che quel componente dice.
- **Più d'uno** → il tipo è un accordo, e sta in `Contracts/`, un file per accordo.
- **Nessuno** → non serve a nessuno, e va cancellato. È la risposta per
  `AutonomyPipelineNotes`.

Due corollari, che decidono la granularità e chiudono le ambiguità che restano.

**Un file, un soggetto.** Il file porta il nome del suo soggetto e contiene lui e
nient'altro che i tipi di cui è l'unico produttore. Un file il cui nome non nomina un
soggetto — `AutonomyPipeline`, `RuntimeContracts` — è un magazzino: il nome non
respinge nulla, quindi tutto ci entra. È il difetto che `R2` sta togliendo, e il
criterio lo previene per costruzione, perché un tipo nuovo o ha un produttore unico, e
allora ha già un file, o è un accordo, e allora l'accordo ha già un nome.

**La cartella dice a quale domanda il file risponde.**

| Cartella | Domanda | Comportamento |
|---|---|---|
| `Contracts/` | su che cosa i componenti devono essere d'accordo | nessuno, o solo la regola che rende un valore inesprimibile |
| `Safety/` | che cosa è permesso adesso | rifiuta, e riduce ciò che è permesso |
| `Autonomy/` | che cosa fare | propone, e non permette mai nulla a se stesso |

**Perché questo criterio e non gli altri due che si presentano per primi.**

*« Dati di là, comportamento di qua »* si rompe sul primo tipo che si guarda.
`ActionCandidate` è un `record` il cui costruttore **rifiuta** « attacca il nulla »;
`SafetyToken` è un oggetto valore che fa un `CompareExchange` per essere spendibile una
volta sola. Sono dati il cui comportamento è la loro ragione d'essere, e una regola che
li separasse metterebbe la regola lontano dalla cosa su cui vale.

*« Per strato del flusso canonico »* non risponde per `RuntimeMode` né per `TrustTier`,
che attraversano tutti gli strati per costruzione: il primo è letto dal guard, scritto
dal breaker e scritto anche dal throttling termico di Gate 6; il secondo è confrontato
dalla policy di `Security`, da `Guard`, dagli adattatori e da `TrustBoundary`. Uno
strato solo non li contiene, quindi la domanda « quale strato » non ha risposta, e un
criterio senza risposta si risolve a maggioranza in una riunione — che è esattamente
ciò che questa sezione esiste per evitare.

#### Che cosa risponde il criterio, prima dell'elenco

**Nessuno dei diciassette resta in `Autonomy/`.**

Non è un effetto collaterale: è la diagnosi. `AutonomyPipeline.cs` non conteneva niente
che rispondesse a « che cosa fare ». Conteneva il vocabolario con cui si dice un atto, e
le tre macchine che decidono se quell'atto è permesso. Il file portava il nome della
cartella invece del nome di un soggetto, e una cartella non respinge niente: è per
questo che diciassette tipi ci sono finiti dentro e nessuno se n'è accorto.

`Autonomy/` resta con ciò che sceglie che cosa fare — `GoalStack` (175 righe),
`TargetEstablishment` (152), `TargetSelector` (279) — e diventa per la prima volta una
cartella che dice il vero.

#### L'assegnazione

**`Contracts/` — sette tipi, cinque file.** Vocabolario: nessuno di questi ha un
produttore unico, e nessuno può cambiare senza che qualcuno fuori dalla propria cartella
se ne accorga.

| Tipo | File | Chi deve essere d'accordo per cambiarlo |
|---|---|---|
| `TrustTier` | `Contracts/TrustTier.cs` | `Security/RuntimeAuthorization` (la policy confronta richiesto e concesso), `Safety/TrustBoundary`, `Guard/GuardAi`, `Adapters/NosTaleGameAdapter`, `Orchestration`, `LowLevel`, `LiveIntegration`, Gate 1/3/6 — **misurato: 17 file**. Nessuno lo possiede |
| `RuntimeMode` | `Contracts/RuntimeMode.cs` | `RecoveryController` lo scrive per `ref`; `Gate6Runtime` lo scrive da sé sul throttling termico (`_currentMode = RuntimeMode.Cooling`, riga 230); `GuardPolicyEngine` lo legge per rifiutare. **Tre scrittori, e uno non è nella catena di recovery** |
| `MapPoint` | `Contracts/MapPoint.cs` | `Perception` (proiezione schermo), `LiveIntegration` (i provider di posizione), `Navigation`, Gate 1/2/3/6, `Autonomy`, e `NosAi.ControlPanel` — **un secondo assembly**. È l'unità di misura del mondo, e appartiene a chi lo misura tanto quanto a chi ci cammina |
| `ActionType` | `Contracts/ActionCandidate.cs` | i pianificatori (`Gate3DecisionLoop`, `GoalStack`, `LocalAiInferenceEngine`, `Dodekatheon`), l'effettore (`InputActionEffector`), le postcondizioni (`PostConditions`) |
| `ActionTarget` | `Contracts/ActionCandidate.cs` | gli stessi, più `TargetSelector` che li risolve |
| `ActionCandidate` | `Contracts/ActionCandidate.cs` | gli stessi |
| `PredictedOutcome` | `Contracts/PredictedOutcome.cs` | lo costruiscono `Gate3Runtime` e `Gate6Runtime`; lo leggono `GuardPolicyEngine` (soglia di rischio) e `PostConditions` (`StateSignatureAfter`). Due produttori, due lettori, nessun proprietario |

`ActionType`, `ActionTarget` e `ActionCandidate` **stanno in un file solo**, ed è il
corollario « un file, un soggetto » che si guadagna il posto: la regola che lega i primi
due — `ActionCandidate.RequireTarget`, che rende inesprimibile « attacca il nulla » e
« cammina verso un'entità » — è scritta dentro il terzo. In tre file la regola starebbe
lontana da entrambe le metà che vincola, e la prossima persona che aggiunge un
`ActionType` non la vedrebbe. Sono un accordo solo: *la forma di un atto proposto*.

`PredictedOutcome` no, ed è la stessa regola applicata al contrario: è un accordo
**diverso** — non che cosa si propone, ma che cosa ci si aspetta che accada — fra chi
predice e chi valuta il rischio. Un file di diciassette righe è il prezzo giusto per non
far credere che la previsione sia un campo del candidato.

**`Safety/` — nove tipi, quattro file.** Le tre macchine che decidono se un atto è
permesso, ognuna col vocabolario che soltanto lei produce.

| Tipo | File | Perché |
|---|---|---|
| `TrustBoundary` | `Safety/TrustBoundary.cs` | È lo stato « quanta autonomia è in vigore adesso », e il suo `DowngradeTrust` è a senso unico per costruzione. Un componente che può solo ridurre ciò che è permesso è un componente di sicurezza, qualunque cosa dica la cartella in cui è nato |
| `GuardPolicyEngine` | `Safety/GuardPolicyEngine.cs` | Applica la politica operativa: rifiuta a runtime `Stopped`, rifiuta il combattimento in `Cooling`, rifiuta sopra il 75 % di rischio. Rifiuta soltanto: non autorizza mai niente che fosse vietato |
| `GuardEvaluationResult` | `Safety/GuardPolicyEngine.cs` | **Produttore unico, misurato**: quattro `new`, tutti dentro `GuardPolicyEngine.Evaluate`. Non ha vita propria — è la frase con cui quel motore risponde |
| `ActionTokenIssuer` | `Safety/ActionTokenIssuer.cs` | Emette e valida la credenziale di un singolo atto. La chiave di firma è generata per istanza e non esce mai, quindi un token vale solo al cancello che l'ha emesso |
| `SafetyToken` | `Safety/ActionTokenIssuer.cs` | **Produttore unico**: un solo `new SafetyToken` in codice di produzione, dentro `TryAuthorize`. Gli altri sei `new` sono prove che fabbricano un token contraffatto o scaduto per verificare che venga rifiutato — costruiscono ciò che il cancello deve respingere, non ciò che emette. Firma ed emittente vivono e cambiano insieme, e devono: `R3` sposta il digest dal solo `CandidateId` all'intento, e in questa disposizione è una modifica a un file |
| `RecoveryController` | `Safety/RecoveryController.cs` | Il potere che ha è rifiutare (`TryBeginAction`) e togliere fiducia (`DowngradeTrust` fino a `Tier0_ReadOnly`). Non ne ha altri: le sue osservazioni di classe dicono per esteso che non rialza mai la fiducia, e che chiudere il breaker restituisce il `RuntimeMode` e nient'altro. Un componente che può solo restringere sta in `Safety/` |
| `RecoveryState` | `Safety/RecoveryController.cs` | **Produttore unico**: è lo stato interno di quel breaker, esposto in lettura |
| `RecoveryStrategy` | `Safety/RecoveryController.cs` | **Produttore unico**: lo restituisce solo `HandleFailure`. Lo leggono Gate 3 e `PostConditions`, ma leggere non è essere d'accordo — nessuno dei due può aggiungerci un valore senza passare dal metodo che lo produce |
| `RecoveryHaltTransition` | `Safety/RecoveryController.cs` | **Produttore unico, misurato**: un solo `new`, dentro `Halt`. È la fotografia che quel metodo scatta |

`Safety/ActionTokenIssuer.cs` finisce accanto a `Safety/CapabilityAuthorizationGate.cs`,
ed è voluto. `R1` ha stabilito che sono due stadi distinti di un percorso di
autorizzazione — la politica e la credenziale — e li ha separati per nome; metterli
nella stessa cartella li separa **senza nasconderli l'uno all'altro**, che è la
condizione perché `R3` possa guardarli insieme e dire quale ordine hanno.

**Cancellato — un tipo.**

| Tipo | Dove va | Perché |
|---|---|---|
| `AutonomyPipelineNotes` | via | Zero riferimenti, misurati. La `const string Namespace = "NosAi.Runtime.Autonomy"` ripete un fatto che il compilatore già conosce e che questa stessa sezione rende falso. Un tipo che nessuno può dover cambiare, perché nessuno lo usa, non ha un posto: ha una data di scadenza |

Delle osservazioni che `AutonomyPipelineNotes` portava, **una sola va salvata** e non è
ancora scritta altrove: la copia di Gate 6 di `ValidateToken` controllava la firma e non
la scadenza, quindi su quel percorso i 1500 ms erano un commento e non un limite. Va
lasciata nelle osservazioni di `ActionTokenIssuer.ValidateToken`, dove è la ragione per
cui quel confronto sulla scadenza non va tolto. Il resto — quali file contenevano che
cosa prima di `R1` — è archeologia che `git log` conserva meglio.

#### I tre casi di `R1`

##### `Position2D` × 4 — due concetti, non uno

Le quattro definizioni non sono quattro copie:

| Definizione | Forma | Che cosa misura |
|---|---|---|
| `Events.InstantBattle` | `(int X, int Y)`, `DistanceTo` su `int` | caselle dell'arena del CI |
| `Gate2` | `(int X, int Y)`, `DistanceTo` su `long` | la posizione di `WorldEntity` nel modello del mondo |
| `Gate6` | `(int X, int Y)`, `DistanceTo` su `int` | caselle del mondo simulato |
| `Raids.Dodekatheon` | **`(double X, double Y)`**, `DistanceTo`, `Zero` | il centro di un telegrafo celeste, con raggio in caselle e **angolo in gradi** |

**Le prime tre sono `MapPoint`, e `MapPoint` è già la stessa cosa**: `readonly record
struct (int X, int Y)`. Vanno su `Contracts/MapPoint.cs`, che guadagna `DistanceTo`. Un
punto sulla mappa non è un concetto di Gate 2, di Gate 6 o degli eventi: è l'unità in cui
il filo riporta le posizioni e in cui la proiezione schermo le consuma, e quattro nomi
per essa sono quattro occasioni perché due moduli intendano cose diverse dicendo la
stessa parola.

**La quarta no, e non va unita.** `CelestialSafeSpotResolver.TryResolveSafePosition`
calcola un punto sicuro dividendo per la lunghezza di un vettore e moltiplicando per un
margine; `ProjectedCelestialTelegraph.ContainsPoint` fa un `Atan2` e confronta gradi. È
geometria continua. Chiamarla `MapPoint` direbbe che una schivata di raid mira a una
casella — che è falso — e portarla a `int` quantizzerebbe esattamente il calcolo per cui
quel risolutore esiste. **Resta, e cambia nome: `Raids.Dodekatheon.TelegraphPoint`**,
perché il nome condiviso è ciò che ha reso questo un duplicato a quattro vie: due
concetti diversi che non portano lo stesso nome non possono essere confusi da nessuno.

Conseguenza sulla prova: la voce `["Position2D"]` in `DuplicateTypeNameTests.Declared` va
tolta, e `EveryDeclaredDuplicateStillExists` lo dirà da solo se qualcuno se ne dimentica.

##### `IAgentVerifier` e `AutonomousAgentRuntime` — si cancella

Il misurato, oggi, con `R1` propagato:

- `IAgentPlanner`, `IAgentExecutor`, `IAgentVerifier`: **zero implementazioni**, in `src/`
  e in `tests/`;
- `AutonomousAgentRuntime`: **mai costruito**, nessun `new` da nessuna parte;
- e una cosa che `R1` non aveva enumerato: `Orchestrator` **non è mai costruito**, e
  `AutonomousOrchestratorLoop` non è mai costruito né mai nominato fuori dal proprio
  file. `Orchestration/` è morto per intero **tranne `RuntimeComposition.cs`**, che è il
  composition root vivo e provato.

**Si cancella**, e la ragione non è che è inutilizzata — è che è **un secondo percorso di
autorizzazione all'atto**. `ADR-0020` apre dichiarando che ne esistono due, uno progettato
e mai costruito e uno costruito e provato, e che nulla dice quale vince.
`AutonomousAgentRuntime.Run` è un terzo: pianifica, autorizza con `IGuardAi` e
`ISafetyGate`, esegue, verifica e ripianifica. `AutonomousOrchestratorLoop` è un quarto,
con `Func<CandidateAction, bool> execute` e `Func<CandidateAction, bool> verify` passati
dal chiamante — cioè con l'esecuzione e la verifica lasciate a chi costruisce l'oggetto.
`R3` si chiama « un solo percorso di autorizzazione »; tenere in vita due percorsi che
nessuno ha mai eseguito significa che `R3` dovrà argomentare contro di essi invece di
limitarsi a costruire quello vero. Un percorso senza implementazioni non è un progetto
conservato: è una risposta alternativa alla domanda su cui il progetto ha deciso di
averne una sola.

**Che cosa se ne va con lei**, esattamente:

| Da | Che cosa |
|---|---|
| `Orchestration/AutonomousAgentRuntime.cs` | il file intero: `AutonomousAgentRuntime`, `AutonomousStepTrace`, `AutonomousRunResult` |
| `Orchestration/AutonomousOrchestratorLoop.cs` | il file intero |
| `Orchestration/Orchestrator.cs` | il file intero: `Orchestrator`, `OrchestratorTickResult` |
| `Contracts/RuntimeContracts.cs` | `IAgentPlanner`, `IAgentExecutor`, `IAgentVerifier`, `AgentPlan`, `AgentStep`, `AutonomousRuntimeOptions`, `VerificationResult` |

**Che cosa non se ne va, e va detto perché il taglio non sbordi.** `ActionKind`,
`CandidateAction`, `GuardDecision`, `IGuardAi` e `ISafetyGate` **restano**: sono vivi e
provati fuori da questa pipeline — `RuntimeComposition.CreateSafe()` li compone,
`Safety/CapabilityAuthorizationGate` implementa `ISafetyGate`, `Guard/GuardAi` implementa
`IGuardAi`, `PlayAi/UtilityAi` e i tre file di `Tactical/` li usano, e dieci prove in
`RuntimeAuthorizationTests` li coprono. Dopo il taglio `RuntimeContracts.cs` resta con
esattamente questi cinque, che sono un accordo solo — *il vocabolario dell'autorizzazione
per capability* — e per il corollario « un file, un soggetto » il file va rinominato
`Contracts/CandidateAction.cs`. Non è uno dei sedici: è la conseguenza che il criterio
produce sul file rimasto, e va fatta nello stesso passo, o il magazzino resta aperto col
nome che invita a riempirlo.

**Il conto delle prove.** Nessuna prova costruisce nessuno dei tipi cancellati —
verificato sull'intero `tests/`. Il numero di prove C# non deve scendere; se scende, la
cancellazione ha preso qualcosa che non era morto e va fermata lì. Le prove Python
`tests/test_agent_runtime.py` e `test_agent_runtime_expansion.py` portano un nome simile e
**non** verificano questo codice: sono di `R4`, corsia B, e non si toccano qui.

##### `Contracts.VerificationResult` — se ne va con la pipeline

`R1` aveva deciso che non era un duplicato dei due `VerificationResult` di gate, che era
l'esito di `IAgentVerifier.Verify`, e che non andava unificata ma « rinominata insieme al
resto di quella pipeline, o cancellata con essa ». La pipeline si cancella, quindi si
cancella con essa. Non c'è una terza opzione: un tipo `(bool Passed, string Reason)` senza
il verificatore che lo produce sarebbe un terzo modo di dire « com'è andata la verifica »,
e il primo che ne avesse bisogno lo userebbe al posto della canonica di Gate 3 proprio
perché è più semplice — perdendo `Unverified` e la provenienza, che è la perdita che
`VER-05` vieta e che `R1` ha argomentato per esteso.

Il vincolo di sicurezza da rispettare nell'esecuzione: **`Gate3.VerificationResult` non va
toccato**, e dopo il taglio resta l'unica definizione del nome. La voce
`["VerificationResult"]` in `DuplicateTypeNameTests.Declared` diventa allora obsoleta e va
tolta; `EveryDeclaredDuplicateStillExists` fallisce se non lo si fa, ed è la prova che
accorcia l'elenco da sé — esattamente il comportamento per cui è stata scritta.

#### Meccanico, o serve una scelta

`Cursor` sposta ciò che è meccanico. Le tre righe segnate **scelta** sono lavoro di questa
sessione: le decisioni sono prese qui sotto, così il passo resta uno spostamento.

| Spostamento | Meccanico? |
|---|---|
| I nove tipi di `Safety/` (`TrustBoundary`; `GuardPolicyEngine` + `GuardEvaluationResult`; `ActionTokenIssuer` + `SafetyToken`; `RecoveryController` + `RecoveryState` + `RecoveryStrategy` + `RecoveryHaltTransition`) | **Sì.** Cambia solo il namespace: i consumatori in Gate 1/3/6, `LowLevel`, `Navigation`, `Observability` e `Host` sostituiscono `using NosAi.Runtime.Autonomy` con `using NosAi.Runtime.Safety`. Nessuna firma cambia |
| `RuntimeMode`, `ActionType`, `ActionTarget`, `ActionCandidate`, `PredictedOutcome` → `Contracts/` | **Sì.** Stessa sostituzione di `using`, verso `NosAi.Runtime.Contracts` |
| `TrustTier` → `Contracts/TrustTier.cs` | **Sì**, e va detto perché non riapre `R1`. `R1` ha deciso **quale definizione sopravvive**: la scala `Tier0_ReadOnly … Tier4_FullAutonomous`, ed è quella che si sposta, byte per byte, con gli stessi nomi di membro che `R1` ha appena propagato. Cambia il file. Costo misurato: dei 17 file che la nominano, **11 hanno già `using NosAi.Runtime.Contracts`** e devono solo togliere quello di `Autonomy`. Il guadagno è di strato: oggi `Security/RuntimeAuthorization.cs` — la policy che decide se un principal può esercitare una capability — importa `NosAi.Runtime.Autonomy` per sapere che cos'è un grado di fiducia. La sicurezza che dipende dall'autonomia è il verso sbagliato, e `CLAUDE.md` dice che il runtime è l'autorità sulla sicurezza |
| `AutonomyPipelineNotes` → cancellato | **Sì.** Zero riferimenti |
| `MapPoint` → `Contracts/`, e assorbe i tre `Position2D` interi | **Scelta.** Due, decise qui sotto |
| `Position2D` di `Dodekatheon` → `TelegraphPoint` | **Sì**, una volta scelto di non unirlo: il tipo è usato in un file solo, `Zero` compreso |
| Cancellazione della pipeline agente | **Scelta.** Una, decisa qui sotto |

**Scelta 1 — `MapPoint.DistanceTo` sottrae in `long`.** `MapPoint` non ha `DistanceTo`; i
tre `Position2D` che assorbe sì, e non sono d'accordo: Gate 2 sottrae in `long`, gli eventi
e Gate 6 in `int`. È una differenza silenziosa dello stesso tipo che `R1` ha passato la
giornata a togliere, e va chiusa nel verso sicuro. **`long`**: su coordinate di casella non
cambia mai risultato, e su coordinate che un giorno non fossero di casella è la differenza
fra una distanza sbagliata e una giusta. Chi propaga non deve decidere: la firma è
`public double DistanceTo(MapPoint other)` con le sottrazioni in `long`.

**Scelta 2 — il formato su filo di Gate 2 non cambia, ed è verificato.** `Gate2DeltaSync`
serializza una posizione come due `ReadInt32` consecutivi, campo per campo: il nome del
tipo non compare sul filo, quindi rinominare `Position2D` in `MapPoint` non è un cambio di
contratto ai sensi di `ADR-0005` e non richiede una versione. Se la propagazione trova un
percorso che serializza per nome, si ferma e lo segnala invece di adattare il formato.

**Scelta 3 — che cosa fa il consumatore di `AutonomousRunResult`.** Non ce n'è nessuno: è
la scelta che non c'è da fare, ed è la ragione per cui la cancellazione è un taglio e non
una migrazione. Se la propagazione trova anche un solo chiamante che questa sezione non ha
visto, **la cancellazione si ferma** e torna qui: un consumatore vivo cambierebbe la misura
su cui la decisione è presa.

#### Che cosa cambia nel « Fatto quando » di `R2`

Il criterio d'accettazione scritto sopra — « nessun file supera le 400 righe in
`Autonomy/` » — è stato scritto prima che qualcuno contasse i soggetti, e le 400 righe
erano un modo indiretto di dire « nessun file è un magazzino ». Va letto per la proprietà
che sostituiva, perché la misura letterale ora è soddisfatta per il motivo sbagliato:
`Autonomy/` resta con tre file da 175, 152 e 279 righe **perché è stato svuotato**, non
perché sia stato equilibrato.

La proprietà vera è il corollario: **un file, un soggetto**. Un file la viola quando il suo
nome non nomina il soggetto — ed è quello, non la lunghezza, che fa entrare il
diciottesimo tipo per inerzia.

Con l'assegnazione qui sopra c'è **un file solo sopra le 400 righe**, ed è
`Safety/RecoveryController.cs`, circa 560. Non si divide: `TryBeginAction`,
`HandleFailure`, `HandleSuccess` e `ResetFailures` prendono lo stesso `_lock` e mutano la
stessa finestra, lo stesso `_state` e lo stesso contatore di halt. Separarli
significherebbe separare il lock, cioè cambiare il comportamento di un componente di
sicurezza sotto concorrenza — esattamente ciò che `R2` vieta al passo di propagazione. È
lungo perché ha molto da dichiarare, non perché contenga più di una cosa: più di metà delle
sue righe sono le osservazioni che dicono perché il contatore di fallimenti consecutivi non
bastava.

**`R2` è fatta quando** — nessun file in `Contracts/`, `Safety/` e `Autonomy/` contiene più
di un soggetto; `AutonomyPipeline.cs` non esiste più; il numero di prove C# non è sceso; e
il `git diff` non mostra cambi di logica al di fuori delle tre scelte enumerate sopra.

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
