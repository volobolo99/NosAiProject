# NosAi Control Panel

Console operatore Windows. **Non** sostituisce il runtime né Safety: osserva lo stato classificato e richiede operazioni supportate.

## Avvio

```powershell
dotnet run --project src/NosAi.ControlPanel/NosAi.ControlPanel.csproj -c Release
```

Eseguibile dopo la compilazione:

`src/NosAi.ControlPanel/bin/Release/net8.0-windows/NosAi.ControlPanel.exe`

Oppure:

```powershell
.\scripts\windows\start_control_panel.ps1
```

All'apertura la console trova la radice del repository, crea `data/` se manca, carica `data/control_panel.json` e:

- se un runtime è già in ascolto sulla porta API, si collega in sola osservazione;
- altrimenti, se l'auto-avvio è attivo, avvia `Gate1BootstrapHost` in-process con le stesse opzioni del runtime a riga di comando.

## Cosa fa ogni sezione

| Sezione | Azioni |
|---|---|
| Panoramica | stato runtime / client / Guard / esecuzione; checklist di auto-configurazione |
| Client NosTale | baseline OS classificata (LIVE o UNKNOWN) |
| Telefono Guard AI | `python -m nosai.phone.deploy` e enroll, senza digitare comandi |
| Percezione | probe DXGI; hardware osservato |
| Certificazione | tutte le suite `--gateN-test` e compilazione runtime |
| Impostazioni | porte, discovery, loopback, nomi processo; applicate al prossimo avvio |
| Diario | log del runtime e delle operazioni |

STOP richiede l'arresto di emergenza già previsto da `POST /api/command`. Gate 1 continua a rifiutare l'esecuzione.

UNKNOWN resta UNKNOWN: nessun valore finto.
