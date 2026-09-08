# RAPPORTO S5 — cliccare un bersaglio, e sapere dal filo se è andata

Sessione S5 / AP-05 A2+A4. Il runtime clicca già per camminare; qui clicca su
un'entità stabilita e verifica sul filo (`ct`) se il clic l'ha selezionata.

## File creati e modificati

- `src/NosAi.Runtime/Tactical/ClickTargetExecutor.cs` — **nuovo**
- `src/NosAi.Runtime/Tactical/ClickTargetCommand.cs` — **nuovo**
- `src/NosAi.Runtime/Program.cs` — solo la registrazione di `--click-target` (dispatch + `KnownProbeFlags`)
- `tests/NosAi.Runtime.Tests/ClickTargetExecutorTests.cs` — **nuovo**
- `tests/NosAi.Runtime.Tests/ClickTargetCommandTests.cs` — **nuovo**
- `tests/NosAi.Runtime.Tests/RefusalReasonRegisterTests.cs` — 4 voci dichiarate (vedi § Registro)

## Build e test

```
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Debug          → 0 errori, 0 avvisi
dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Debug → 0 errori, 0 avvisi
dotnet test tests/NosAi.Runtime.Tests -c Debug --filter "FullyQualifiedName~ClickTarget|FullyQualifiedName~RefusalReasonRegisterTests"
    → Superati: 25. Non superati: 0.
dotnet test tests/NosAi.Runtime.Tests -c Debug
    → Superati: 2692. Ignorati: 9. Non superati: 1.
```

L'unico non superato è `GuardAiClientTests.ManyRapidHeartbeatsSurviveThePooledReadWriteAdapterWithoutCorruption`
— un test di stress socket/heartbeat estraneo a questa sessione, flaky: rieseguito
in isolamento passa (`Superati: 1. Non superati: 0`). Non tocca alcun file di S5.

## La finestra di verifica, e perché quel numero

`ClickTargetExecutor.DefaultVerificationWindow = 1000 ms`, poll ogni 10 ms.
Il filo consegna ~90 pacchetti/s nelle catture registrate (≈11 ms fra un
pacchetto e l'altro), quindi un `ct` prodotto dal clic è osservabile in pochi
poll; 1000 ms è un multiplo comodo del round-trip clic → server → `ct` (decine o
qualche centinaio di ms) e tiene il comando — che è **one-shot**, non un ciclo —
dentro un secondo di attesa. La finestra è misurata con `Stopwatch` (monotona),
non con l'orologio a muro.

## I tre esiti della verifica, resi distinguibili

`ClickTargetVerification.Outcome` (`TargetSelectionOutcome`):

| Esito | Significato | Distinto da |
|---|---|---|
| `Confirmed` | un `ct` **posteriore al clic** nomina l'id richiesto | — |
| `DifferentTarget` | un `ct` posteriore nomina un id **diverso** | `NotConfirmed` (clic sull'entità sbagliata, non latenza) |
| `NotConfirmed` | nessun `ct` entro la finestra | `DifferentTarget` (latenza o clic inerte) |

«Nessuna conferma» e «il server ha detto un altro id» sono due esiti separati:
solo il secondo è il difetto che il task deve poter rilevare. La verifica conta
solo un `ct` il cui `ObservedAtUtc` è **posteriore** a quello letto subito prima
del clic (baseline): `PlayerTargetSelection` è sticky — nulla sul filo azzera la
selezione — quindi una selezione già presente prima del clic non testimonia nulla
di ciò che il clic ha fatto, e confrontare i due istanti del filo (non l'orologio
dell'executor contro quello del pacchetto) è ciò che tiene la regola robusta.

## Registro dei rifiuti

L'executor e il comando introducono quattro rifiuti non producibili da un unit
test (ramo privato `RunWindows`, richiede un client reale), dichiarati in
`RefusalReasonRegisterTests.cs`:

- `click_target_input_backend_not_gated` — irraggiungibile (CreateSafe restituisce sempre un backend gated)
- `click_target_entity_not_found` — richiede un'entità reale osservata sul filo
- `click_target_vnum_not_observed` — richiede un vnum reale osservato sul filo
- `click_target_player_position_unreadable` — richiede la lettura fallita della posizione dalla memoria

Ho anche rinominato `ClickTargetCommand.NoTargetReason` → `MissingTargetReason`:
il nome `NoTargetReason` collideva con `TargetChainProbe.NoTargetReason`
(`no_target_selected`), e il registro (che copre per *nome della costante* oltre
che per valore) lo avrebbe contato come coperto. Il valore
`click_target_requires_entity_id_or_vnum` è invariato.

## Unknown

Nessun campo decodificato in questa sessione. L'unico «non sapere» che il task
maneggia è il `NotConfirmed` della verifica, che resta distinto da `DifferentTarget`
come sopra — `Unknown` non è `false`, e «nessuna conferma» non è «bersaglio sbagliato».

## Dove mi sono fermato

Da nessuna parte. La parte non testabile (`RunWindows`) è per costruzione fuori
dai test, come `ScoutCommand.RunWindows`: un unit test non deve essere a una
elevazione e un client attaccato dal muovere il mouse vero.

## Livello

**Integrated**. `Verified` richiede l'operatore che guarda il cursore andare sul
mostro e il mostro selezionarsi — è lui a farlo, con `--click-target` in mano.
