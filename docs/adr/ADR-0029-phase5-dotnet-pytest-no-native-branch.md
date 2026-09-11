# ADR-0029 — La FASE 5 si misura con dotnet test e pytest, non con ASan

**Status:** Accepted — deciso dall'orchestratore il 2026-09-11
**Date:** 2026-09-11

## Context

`.claude/CLAUDE.md` sezione 7 definisce la FASE 5 come «Build ed esecuzione dei
controlli (AddressSanitizer dove previsto, test .NET e Python)». La roadmap la
segnala come domanda aperta:

> La FASE 5 presuppone codice nativo e ASan che il progetto non ha: o si apre un
> ramo nativo, o la FASE 5 va riscritta su `dotnet test` e `pytest`.

Due contratti restano bloccati su quella presupposizione:

- **C-003** Harness AddressSanitizer — *«`scripts/run_asan_pipeline.py` non
  esiste; la FASE 5 lo invoca»*
- **C-004** Gatekeeper dei test Python — *«`scripts/gatekeeper.py` non esiste; la
  FASE 5 lo invoca»*

e un terzo ne dipende: **C-005** Bridge ctypes verso il modulo nativo —
*«nessun modulo nativo da caricare»*.

### Misurato il 2026-09-11

Il progetto non ha codice nativo di prima mano. I soli file `.cpp` e `.h` sono
materiale di riferimento di terze parti:

| File | Righe | Natura |
|---|---|---|
| `third_party/sources/robmikh/Win32CaptureSample/reference/SimpleCapture.cpp` | 233 | esempio Microsoft, non compilato dal progetto |
| `third_party/.../CaptureSnapshot.cpp` | 66 | idem |
| `third_party/.../SimpleCapture.h` | 78 | idem |
| `third_party/.../CaptureSnapshot.h` | 13 | idem |
| `tools/windivert/WinDivert-2.2.2-A/include/windivert.h` | 630 | header di una libreria esterna |

Dimensioni del codice di prima mano: **188.762 righe di C#** e **24.214 di
Python**, zero righe native.

L'accesso alla memoria del client, che e' la ragione per cui un ramo nativo
sembrava necessario, il progetto **lo fa gia' in C# via P/Invoke**:
`src/NosAi.Adapter/Win32ProcessAdapter.cs`,
`src/NosAi.Runtime/LiveIntegration/Capture/WinDivertPacketSource.cs`,
`ClientNetworkObserver.cs` e `BroadWirePrelude.cs` dichiarano `DllImport`.

Il 2026-09-11 `scripts/code_agent.py` ha acquisito `build_check`, che compila il
`.csproj` contenente il file modificato. Quello **e'** il cancello di FASE 5 per il
C#, e con una proprieta' che ASan non ha su questo progetto: se una firma cambia,
i chiamanti non compilano. Il compilatore e' un controllo di firme piu' severo di
qualunque confronto di alberi.

## Decision

La FASE 5 si misura con **`dotnet build` piu' `dotnet test` per il C#** e
**`pytest` per il Python**. Non si apre un ramo nativo.

ASan resta nominato nella sezione 7 come applicabile *dove previsto*, e oggi non
e' previsto da nessuna parte: non e' un'esenzione, e' l'assenza del soggetto.

Se un domani nascera' un modulo nativo di prima mano, questo ADR si riapre: ASan
tornera' obbligatorio **per quel modulo**, non retroattivamente per la catena.

## Consequences

- **C-003** si chiude come *superato*: l'harness ASan non serve a un progetto
  senza codice nativo. Non si chiude come «fatto»: si chiude perche' il suo
  oggetto non esiste.
- **C-004** si chiude come *superato*: il gatekeeper dei test Python e' il
  `tests_command` dell'incarico, che `code_agent.py` esegue e il cui fallimento
  ripristina lo scheletro. Un secondo cancello duplicherebbe l'autorita'.
- **C-005** si riduce: non serve un bridge ctypes verso un modulo che non
  esiste. Se servira' leggere memoria da Python, la strada e' chiedere al runtime
  C# che la legge gia', non aprire un secondo accesso allo stesso processo. Due
  lettori della memoria di un client sono due modi di sbagliare.
- La sezione 7 di `.claude/CLAUDE.md` va allineata a questa decisione. Finche' non
  lo e', ogni modulo risulta formalmente incompleto per un cancello che non puo'
  scattare.
- **Rischio accettato**: senza ASan non si intercettano corruzioni di memoria nel
  codice di terze parti che il C# richiama via P/Invoke. E' un rischio reale e
  resta aperto; il mitigante e' che quel codice non e' nostro e non lo
  modifichiamo.
