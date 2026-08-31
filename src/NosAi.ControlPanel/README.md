# NosAi Control Panel

Console operatore Windows. **Non** sostituisce il runtime né Safety: osserva lo stato classificato e richiede operazioni supportate.

## Avvio

```powershell
dotnet run --project src/NosAi.ControlPanel/NosAi.ControlPanel.csproj -c Release
```

Eseguibile dopo la compilazione (percorso reale, non `NosAi.Runtime.exe` nella stessa cartella):

`C:\Users\volob\Desktop\NosAiProject\src\NosAi.ControlPanel\bin\Release\net8.0-windows\NosAi.ControlPanel.exe`

Relativo alla radice del repo: `src/NosAi.ControlPanel/bin/Release/net8.0-windows/NosAi.ControlPanel.exe`.

Il pulsante **Apri cartella exe** apre quella directory. `NosAi.Runtime.exe` lì accanto è il runtime a console, non questa console.

Oppure:

```powershell
.\scripts\windows\start_control_panel.ps1
```

Lo script compila se l'exe manca. Se la build fallisce, **non** avvia nulla e termina con errore.

All'apertura la console trova la radice del repository, crea `data/` se manca, carica `data/control_panel.json` e:

- **COLLEGATO** — un runtime è già in ascolto sulla porta API: la console osserva. **Scollega** chiude solo questa sessione e **non** spegne l'altro processo.
- **OSPITATO** — nessun runtime in ascolto e auto-avvio attivo: avvia `Gate1BootstrapHost` in-process. **Ferma** spegne quel runtime.
- **OFFLINE** — nessun runtime. Premere Avvia, oppure riattivare l'auto-avvio.

## Cosa fa ogni sezione

| Sezione | Azioni |
|---|---|
| Panoramica | modalità OSPITATO / COLLEGATO / OFFLINE; canale wire di questo build; slot Guard derivato da collegato/autenticato; stato runtime / client / Guard / esecuzione; checklist |
| Client NosTale | baseline OS classificata (LIVE o UNKNOWN) |
| Telefono Guard AI | `python -m nosai.phone.deploy` e enroll. Serve Python nel PATH. Se manca, l'operazione **non** è riuscita. Wire v3: APK v2 rifiutato. Il giro sul telefono resta un promemoria, non Verified. |
| Percezione | probe DXGI in-process (diagnostica). Barra HP/MP: DERIVED solo con firma di barra, altrimenti UNKNOWN. Numeri HP/MP: UNKNOWN senza atlante glifi. Ritagli in `data/perception/crops`. Il probe **non** entra nello snapshot. |
| Rete | porte, ascolto 127.0.0.1, salute dallo snapshot; stream eventi UNKNOWN (endpoint assente) |
| Sicurezza | presenza PEM/DPAPI/pin/chiave telefono; questa UI non avvolge le chiavi |
| Certificazione | tutte le suite `--gateN-test` e compilazione runtime |
| Impostazioni | porte, discovery, loopback, nomi processo; applicate al prossimo avvio |
| Diario | log colorati per livello (INFO / WARN / ERROR) |

STOP richiede l'arresto di emergenza già previsto da `POST /api/command`. Gate 1 continua a rifiutare l'esecuzione.

UNKNOWN resta UNKNOWN: nessun valore finto.
