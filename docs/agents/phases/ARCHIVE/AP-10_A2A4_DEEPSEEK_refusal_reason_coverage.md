# AP-10 / A2+A4 — DeepSeek — i rifiuti che nessuno ha mai provato

**Indipendente da tutti i task in coda.** Non tocca un solo file di `src/`:
tutto il lavoro sta in `tests/`. Prendibile in qualunque ordine, anche in
parallelo con qualsiasi altro.

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `tests/NosAi.Runtime.Tests/DuplicateTypeNameTests.cs` e
   `src/NosAi.Runtime/Observability/ModuleReachability.cs` — **il modello da
   seguire**: elenco dichiarato, un motivo scritto per voce, e la prova che
   fallisce sia su una voce nuova sia su una voce diventata stantia.
3. `CLAUDE.md` § *Do not* — «bypass authentication, authorization or Safety».

**REGOLA #3**: i numeri qui sotto sono stati misurati il 2026-09-07 sul
sorgente. Ricontrollali come primo passo: se sono cambiati, i tuoi vincono e lo
scrivi nel report.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull` prima, **`git
push` tu alla fine.**

---

## Il fatto misurato

`src/NosAi.Runtime/` dichiara **190 costanti** `public const string …Reason`.
Sono il vocabolario dei rifiuti: il progetto rifiuta per nome invece che con un
booleano, e quei nomi finiscono sotto gli occhi dell'operatore.

Confrontandole con tutto `tests/`, per **nome della costante e per valore della
stringa**:

- **48 sono prodotte dalla produzione e nessun test le verifica.**
- **4 non sono prodotte da nulla**: compaiono una volta sola in tutto il
  repository, nella loro stessa dichiarazione.

Il secondo gruppo, per intero:

| File | Costante | Valore |
|---|---|---|
| `Autonomy/GoalStack.cs:97` | `NoActiveGoalReason` | `no_active_goal` |
| `Autonomy/GoalStack.cs:100` | `NotNamedByGoalReason` | `not_named_by_active_goal` |
| `Navigation/StepGuardChain.cs:176` | `AuthorityUnknownReason` | `step_session_authority_unknown` |
| `Navigation/TargetChainProbe.cs:49` | `NoTargetReason` | `no_target_selected` |

I primi tre hanno **una sola occorrenza anche del valore letterale**: quel
rifiuto non può accadere. Il quarto ne ha due — la costante non è usata ma la
stringa sì, quindi lì il difetto è più piccolo e diverso: un letterale
duplicato accanto alla costante che esisteva per evitarlo. **Verifica ognuno dei
quattro tu stesso** invece di fidarti di questa tabella.

Perché conta: un rifiuto senza prova è una promessa. `CLAUDE.md` vieta di
aggirare autorizzazione e Safety, e nel gruppo mai verificato ci sono proprio i
rifiuti del confine di attuazione — `authority_not_verified`,
`authority_client_area_too_small`, `token_integrity_unreadable`,
`actuation_scope_aborted`, i quattro `*_input_backend_not_gated`. Sono i
percorsi che tengono chiusa la porta, e nessuno ha mai controllato che si
chiudano davvero.

---

## Cosa fare

### 1. Il registro — `RefusalReasonRegisterTests.cs` (nuovo)

Nello stile esatto di `DuplicateTypeNameTests`:

- scandisce il sorgente di `src/NosAi.Runtime/` (non la reflection: leggi il
  sorgente, come fanno gli altri due registri, così i due insiemi di file
  coincidono) e raccoglie ogni `public const string \w*Reason = "…"`;
- per ognuna cerca in `tests/` il nome della costante **o** il valore della
  stringa;
- fallisce elencando ogni motivo non coperto **che non sia dichiarato** in una
  lista di eccezioni con, per ciascuna, il motivo scritto per cui non è coperto;
- fallisce anche su una voce **dichiarata che nel frattempo è coperta** — così
  l'elenco si accorcia da solo invece di sopravvivere al debito che descriveva.
  Questa seconda prova è la metà che conta: senza, la lista diventa un posto
  dove nascondere.

La lista dichiarata parte da quello che **non** copri al punto 2. Ogni voce
porta il perché, in una riga: «richiede un client reale», «richiede
privilegi di amministratore», «ramo raggiungibile solo fuori da Windows», e così
via. Una voce senza motivo scritto non è dichiarata: è nascosta.

### 2. La copertura vera — `ActuationRefusalCoverageTests.cs` (nuovo)

Copri con test veri, non con dichiarazioni, il gruppo del confine di attuazione.
È il gruppo che va fatto per primo e per intero:

- `LowLevel/SessionActuationAuthority.cs` — `NoVerdictReason`,
  `ClientAreaTooSmallReason`, `CursorUnreadableReason`, `MoveRefusedReason`,
  `TokenNotOpenedReason`, `LabelUnreadableReason`, `LabelMalformedReason`;
- `LowLevel/ActuationScope.cs` — `AlreadyAbortedReason`;
- `LowLevel/GatedInputBackend.cs` — `ReleaseUnsupportedReason`;
- i quattro `UngatedBackendReason` di `WalkCommand`, `ScoutCommand`,
  `CollectCommand`, `AutoplayCommand` (più `SingleStepCommand`, se il tuo
  conteggio ne trova un quinto);
- `Navigation/SingleStepExecutor.cs` — `CursorMoveRefusedReason`,
  `GeometryUnknownReason`.

Ogni test costruisce la condizione che **provoca** quel rifiuto e verifica due
cose: che il rifiuto avvenga, e che porti quel nome. Non basta asserire che
qualcosa è stato rifiutato — il nome è il contratto verso l'operatore, ed è la
parte che si rompe in silenzio.

Dove la condizione richiede Windows o l'amministratore, usa gli attributi che
esistono già (`WindowsOnlyFact`, `NonWindowsFact`, `NosTaleClientFact`) e
**mai** un `if (…) return`, che xUnit conta come superato. Se un rifiuto non è
raggiungibile in nessun modo da un test, quella è la voce che va nella lista
dichiarata del punto 1, con il perché.

### 3. I quattro mai prodotti — riferire, non cancellare

Verifica ognuno dei quattro e **scrivi nel report** cosa hai trovato: mai
prodotto, oppure prodotto per letterale accanto alla costante. **Non
cancellarli e non cablarli.** Togliere una costante `public` è un cambio di API,
e decidere se `GoalStack` debba emettere quei due rifiuti è una decisione di
progetto: è di Claude e dell'utente. Il tuo compito è metterli in tavola con la
misura accanto.

---

## OWN (file nuovi)

- `tests/NosAi.Runtime.Tests/RefusalReasonRegisterTests.cs`
- `tests/NosAi.Runtime.Tests/ActuationRefusalCoverageTests.cs`

## MODIFY

**Nessun file.** Se ti accorgi che per coprire un rifiuto serve una modifica a
`src/`, **fermati e riferisci**: significa che quel rifiuto non è raggiungibile
dal suo stesso confine pubblico, e quella è una scoperta più importante del
test. Non aprirti la strada modificando la produzione.

---

## Fuori scope

- **Cancellare o cablare le quattro costanti mai prodotte.**
- **Cambiare un solo messaggio di rifiuto.** Se un valore ti sembra sbagliato,
  va nel report, non nel diff.
- **Toccare i file degli altri task in coda** — non ce n'è motivo: qui non si
  tocca `src/`.
- **Nessun aggiornamento ai documenti** — REGOLA #2.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errori, 0 avvisi**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` — 0 falliti; il numero di
  test **sale** di quanti ne hai scritti, e lo riporti.
- `tests/NosAi.Core.Tests` — invariato.
- Nel report, tre numeri misurati da te: quante costanti `…Reason` esistono
  oggi, quante erano scoperte prima, quante lo sono dopo.
- L'elenco delle voci dichiarate come non copribili, ognuna con il suo perché.
- L'esito dei quattro mai prodotti.
- **La prova che il registro morde**: aggiungi temporaneamente una costante
  `…Reason` finta in un file di produzione, mostra che il test diventa rosso,
  togli la costante. Incolla le due esecuzioni. Un registro che nessuno ha visto
  fallire non è un registro — è un commento che compila.
- Livello: **Integrated**.
