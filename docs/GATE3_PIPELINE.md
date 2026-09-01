# NosAi — Gate 3: Pipeline Decisionale e di Sicurezza a Ciclo Chiuso

**Versione:** 1.0 Beta
**Aggiornato:** 2026-08-30
**Codice:** `src/NosAi.Runtime/Gate3/`

## Scopo

Gate 3 implementa il ciclo canonico del progetto:

`Observe → World Model → Decision/Policy → Safety → Execute → Verify → Re-observe`

L'ordine non è negoziabile e ogni passo è fail-closed. Il gate non decide *cosa*
sia giusto fare in gioco: decide se un'azione proposta possa essere autorizzata,
eseguita e poi **confermata**.

## Componenti

| Componente | File | Ruolo |
|---|---|---|
| `ActionPlanner` | `Gate3Runtime.cs` | genera i candidati d'azione dallo stato |
| `SimulationEngine` | `Gate3Runtime.cs` | previsione deterministica e senza effetti collaterali |
| `TacticalRankingEngine` | `Gate3Runtime.cs` | ordinamento MAUT per utilità attesa |
| `GuardPolicyEngine` | `Gate3Runtime.cs` | policy operative (modo runtime, soglia di rischio) |
| `TrustBoundary` | `Gate3Runtime.cs` | livello di fiducia corrente; **scende soltanto** |
| `SafetyGate` | `Gate3Runtime.cs` | autorizzazione ed emissione del `SafetyToken` HMAC monouso |
| `AuthorizedActionExecutor` | `Gate3Runtime.cs` | valida il token e delega all'effector |
| `IActionEffector` | `Gate3Effector.cs` | **ciò che tocca davvero il mondo** |
| `IWorldStateObserver` | `Gate3Observation.cs` | **rilegge il mondo dopo l'azione** |
| `ActionExecutionVerifier` | `Gate3Runtime.cs` | confronta previsione e osservazione |
| `RecoveryController` | `Gate3Runtime.cs` | strategia di degrado dopo un fallimento |
| `Gate3ExecutionOrchestrator` | `Gate3Runtime.cs` | esegue il ciclo completo |

## I due difetti corretti

Il codice esisteva già e i test passavano. Non erano difetti di stile: rendevano
il ciclo chiuso **incapace di dire la verità**.

### 1. L'executor dichiarava successo senza eseguire nulla

```csharp
await Task.Delay(50, cancellationToken);
return new ExecutionResult(candidate.CandidateId, true, true, ..., null);
```

Cinquanta millisecondi di attesa e poi `ExecutionCompleted: true`. Nessun byte
raggiungeva il client. La pipeline riportava azioni portate a termine mentre non
toccava niente — esattamente il caso "simulato spacciato per reale" che il
progetto vieta, e un mock sul percorso critico proibito da `.cursorrules`.

**Correzione.** L'esecuzione passa da `IActionEffector`. Il default è
`DisabledActionEffector`, che rifiuta e lo dichiara, coerente con
`RuntimeSafetyPolicy.SafeDefault` (live input disattivato). Non eseguire non è un
limite da aggirare: è la postura di sicurezza del gate. Ciò che non è ammesso è
fingere.

### 2. La verifica confrontava la simulazione con sé stessa

L'orchestratore calcolava lo stato post-azione applicando i delta **della stessa
previsione** che stava per verificare, e lo passava al verifier:

```csharp
int simulatedNewHp = playerHp + predictedOutcome.ExpectedHpDelta;   // dalla previsione
verifier.Verify(..., predictedOutcome, ..., simulatedNewHp, ...);   // contro la previsione
```

La verifica riusciva **per costruzione**. Il passo che deve accorgersi quando la
realtà diverge dal modello non poteva fallire: la rete di sicurezza del ciclo
chiuso era una tautologia.

**Correzione.** Il verifier confronta la previsione con un `ObservedState` letto
da `IWorldStateObserver`. Dove non c'è osservazione non c'è conferma: l'esito è
`Unverified`, classificato `UNKNOWN`.

### 3. Il pianificatore accettava numeri senza provenienza

`ExecuteCycleAsync(800, 1000, 100, …)` prendeva interi nudi. Chiunque poteva
passare valori inventati e ottenere un piano sicuro di sé, senza nulla che li
marcasse come finzione.

È lo **stesso difetto del verifier, ma sull'ingresso**: un valore privo di
provenienza trattato come un'osservazione.

**Correzione.** Il ciclo accetta `Gate3WorldState`, in cui ogni campo è
classificato. Da qui una regola precisa:

> **Si può pianificare su dati simulati. Non si può agire su dati simulati.**

- stato `UNKNOWN` → `NoWorldState`: nessuna pianificazione, perché costruirla
  significherebbe inventare gli ingressi;
- stato `SIMULATED` con effector reale collegato → `RefusedSimulatedInput`:
  l'effector non viene mai raggiunto;
- stato `SIMULATED` senza effector → pianificazione consentita, è una prova a
  vuoto legittima;
- stato `LIVE` → il ciclo procede.

L'overload con gli interi resta per prove a vuoto e test, ma costruisce uno stato
esplicitamente `SIMULATED`: i chiamanti esistenti continuano a funzionare e la
loro provenienza diventa onesta. Applicando la regola, quattro test già scritti
sono diventati rossi — passavano numeri simulati a un effector reale. Erano loro
a sbagliare.

### L'aggancio al runtime reale

`Gate1SnapshotWorldStateSource` legge lo stato di pianificazione dallo snapshot
canonico di Gate 1. **Oggi restituisce sempre `UNKNOWN`**, perché Gate 1
classifica il gameplay come `gameplay_provider_not_available`: il runtime osserva
processo, finestra e titolo del client, non i suoi HP.

Non è uno stub: è il risultato corretto. Gate 3 non può pianificare sul gioco
finché qualcosa non sa leggere il gioco, e collegare l'adapter ora rende la
dipendenza esplicita e verificata invece di lasciarla scoprire a chi passa numeri
a mano. Nel momento in cui esisterà un provider gameplay, l'adapter comincerà a
restituire `LIVE` e nient'altro dovrà cambiare.

## Esiti del ciclo

`Gate3CycleResult.Outcome` distingue casi che prima collassavano su un `bool`:

| Esito | Significato | Recovery |
|---|---|---|
| `Confirmed` | eseguito e confermato su osservazione reale | azzera i fallimenti |
| `NoCandidate` | nulla da pianificare o nulla sopravvive al ranking | no |
| `NoWorldState` | stato del mondo non leggibile: nessuna pianificazione | no |
| `RefusedSimulatedInput` | piano su stato simulato con effector reale collegato | no |
| `Blocked` | Safety Gate ha negato l'autorizzazione | no |
| `ExecutionDisabled` | policy vieta l'input live, nulla è stato tentato | **no** |
| `Unverified` | eseguito ma non osservabile | **no**, e non azzera i fallimenti |
| `Failed` | discrepanza osservata, o esecuzione fallita | sì |

Due distinzioni contano più delle altre:

- **`ExecutionDisabled` non è un fallimento.** Nulla è stato tentato, quindi non
  c'è nulla da cui recuperare. Trattarlo come fallimento farebbe degradare il
  livello di fiducia per una configurazione che sta funzionando come previsto.
- **`Unverified` non è né successo né fallimento.** L'azione potrebbe benissimo
  aver funzionato, quindi non fa scattare il recovery; ma non azzera nemmeno il
  contatore dei fallimenti, perché non è stata confermata.

`IsConfirmed` è vero solo per `Confirmed`. Un chiamante che interpretasse "non
fallito" come "funzionato" ricadrebbe nel difetto appena corretto.

## Invarianti garantite dai test

`--gate3-test` (21 controlli) e `NosAi.Runtime.Tests/Gate3Tests.cs`:

- la simulazione è deterministica e priva di effetti collaterali;
- a HP critico il ranking mette la sopravvivenza davanti al danno;
- il Safety Gate nega un'azione oltre il livello di fiducia;
- un token contraffatto, scaduto, riusato o legato a un altro candidato non
  autorizza nulla — e un tentativo di riuso **non brucia** il token del legittimo
  titolare;
- in modo `Stopped` tutto è inibito; in `Cooling` il combattimento è inibito ma il
  recupero resta possibile, altrimenti il throttling termico impedirebbe al
  personaggio di salvarsi;
- la soglia di rischio blocca le azioni pericolose ma **non** la fuga, che è
  l'azione presa proprio perché la situazione è pericolosa;
- la fiducia scende e non risale mai, e `RecoveryController` non espone alcun
  metodo di escalation;
- la scala di recovery è `Retry → Retry → DegradedReplan → HaltAndAlert`;
- un ciclo bloccato **non raggiunge mai l'effector**;
- un'esecuzione inibita non è un successo;
- un'esecuzione non osservata è `Unverified`, non un successo;
- un osservatore che solleva un'eccezione lascia il ciclo non verificato invece di
  abbattere la pipeline;
- una lettura non osservata non viene mai letta come zero;
- pianificare su stato `UNKNOWN` è rifiutato;
- uno stato simulato non raggiunge mai un effector reale, mentre resta pianificabile
  a vuoto.

## Cosa Gate 3 **non** fa ancora

- **L'effector reale è collegato, ma può solo premere tasti.**
  `InputActionEffector` (F3-1) traduce un `ActionCandidate` in un gesto e passa
  sempre da `GatedInputBackend`, mai da `Win32InputBackend` diretto. È collegato
  nella composizione di `Gate1BootstrapHost` e resta spento finché l'operatore
  non accende `LiveInputEnabled`: l'orchestratore riceve la policy **viva**, non
  una copia letta all'avvio, quindi accendere e spegnere l'interruttore vale
  dall'azione successiva.

  Ciò che sa fare oggi: `UseConsumable` e `UseSkill`, premendo il tasto che
  l'operatore ha configurato in `data/keybinds.json` (C3/F2-4).

  Ciò che rifiuta, per nome, finché non arriva F2-3: `UseBasicAttack`,
  `TargetEntity`, `MoveToPosition` ed `EmergencyFlee` terminano `Refused` con
  motivo `screen_projection_not_calibrated`. Non esiste una trasformazione da coordinata
  di mappa a pixel, e una di ripiego cliccherebbe in un punto qualsiasi della
  finestra: il ciclo lo scoprirebbe solo alla verifica, dopo aver già agito.
  `CollectGroundItem` e `RestAndRecover` non hanno un gesto e sono rifiuti
  nominati.

  `Completed` significa che l'input è stato accettato: `SendInput` riporta quanti
  eventi ha accodato, il backend restituisce `false` quando non è quello atteso, e
  l'esito è `Failed` con motivo. È il difetto qui sopra, e non rientra da questo lato.
- **Nessun osservatore reale è collegato.** Serve il backend di percezione. Finché
  non c'è, un'eventuale esecuzione termina `Unverified`.
- **L'aggancio a Gate 1 esiste ma non ha dati.** `Gate1SnapshotWorldStateSource`
  legge lo snapshot reale, che però classifica il gameplay come non disponibile.
  Finché non c'è un provider, ogni ciclo termina `NoWorldState`.

Questi punti sono la vera distanza dall'operatività, e sono limiti dichiarati,
non difetti nascosti. Il primo si è ristretto a metà: la tastiera arriva al
client, il mouse no, e manca la trasformazione coordinata → pixel (F2-3) perché
è la sola che non si può dedurre — va calibrata su un client reale.

## Debito noto: tipi duplicati

`TrustTier` è definito in `Contracts`, `Gate3`, `Gate6` e `Host`. `SafetyGate`
esiste in `Gate3`, `Gate6` e `Safety`; `TrustBoundary`, `RuntimeMode` e
`RecoveryController` in `Gate3` e `Gate6`.

Compilano perché stanno in namespace diversi, ma qualunque file che ne importi due
diventa ambiguo, e nulla impedisce alle definizioni di divergere. È una violazione
di DRY su un confine di sicurezza, quindi non è cosmetica.

Non è stata risolta qui: unificarli tocca i contratti condivisi e altri gate, e va
coordinata invece che fatta a metà.

## Esecuzione

```bash
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --gate3-test
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

Prima di questa modifica la suite Gate 3 **non era eseguibile**: il file conteneva
un proprio `Program.Main`, reso irraggiungibile dallo `StartupObject` fissato nel
`.csproj`, e nessun flag la invocava. Era lo stesso difetto già corretto per
`--host-test`.
