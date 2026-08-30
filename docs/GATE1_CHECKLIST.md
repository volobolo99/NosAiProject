# NosAi — Checklist esecutiva Gate 1

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Scopo

Questa checklist definisce i requisiti eseguibili del primo gate operativo reale del progetto.

Il Gate 1 è superato solo quando tutti i punti pertinenti risultano completati con evidenza coerente.

---

## Stato di avanzamento

| Area | Punto | Stato | Evidenza attesa |
|---|---|---|---|
| Runtime PC | Avvio affidabile | [x] **reale** | avviato sul PC target con NosTale in esecuzione: `Health: Healthy`, Guard 17471, API operatore 8766 |
| Runtime PC | Configurazione valida | [x] locale | `Gate1HostOptionsLoader` rifiuta timeout/porte invalidi |
| Runtime PC | Logging utile | [x] locale | `ConsoleRuntimeLogger` con correlation id nel bootstrap |
| Runtime PC | Safety policy attive | [x] locale | snapshot Gate 1 espone live input/packet injection disabilitati |
| Runtime PC | Stato sessione osservabile | [x] locale | `gate1.snapshot.v1` include sessione Guard classificata |
| Client NosTale | Rilevamento client | [x] **reale** | NosTale reale agganciato: `NostaleClientX` PID 7932, handle `0x8099A`; assenza → `client_unavailable` |
| Client NosTale | Lettura dati minimi | [x] **reale** | dal client reale: processName/processId/windowTitle/windowHandle/processResponding/windowVisible tutti `LIVE`; gameplay HP/mappa/entità ancora `UNKNOWN` |
| Client NosTale | Validazione dati | [x] locale | provenance `LIVE`/`UNKNOWN` nel snapshot |
| Client NosTale | Gestione client assente | [x] locale | runtime resta DEGRADED, non inventa gameplay |
| Guard AI smartphone | Avvio affidabile | [ ] | app Android presente e compilata (`src/NosAi.GuardAi.App`); manca l'esecuzione su dispositivo fisico |
| Guard AI smartphone | Connessione reale | [ ] | client canonico verificato contro il runtime reale in C# e Python; **rete LAN e dispositivo fisico ancora no** |
| Guard AI smartphone | Autenticazione reale | [x] locale | RSA-2048 challenge/response + fail-closed |
| Guard AI smartphone | Heartbeat reale | [x] locale | timeout 2s fail-closed + riconnessione |
| Guard AI smartphone | Riconnessione controllata | [x] locale | nuova sessione accettata dopo timeout |
| Dashboard | Avvio affidabile | [x] locale | operator server Gate 1 su loopback |
| Dashboard | Connessione al runtime corretto | [x] **reale** | UI 8765 → runtime 8766 con default `NOSAI_RUNTIME_URL`, nessuna variabile impostata a mano; porta occupata → runtime vivo e `dashboard_port_in_use` esplicito |
| Dashboard | Dati reali soltanto | [x] locale | demo gold/mostri/GPU rimossi; UNKNOWN esplicito |
| Dashboard | Coerenza degli stati | [x] locale | snapshot unico PC/client/guard/safety |
| Dashboard | Error handling | [x] locale | client assente e runtime offline non mascherati |
| End-to-end | PC ↔ client | [x] **reale** | runtime `Healthy` contro NosTale in esecuzione; `attached_os_session`, campi client `LIVE`, gameplay `UNKNOWN` |
| End-to-end | PC ↔ smartphone | [ ] | entrambi i lati esistono e si parlano in test; manca la prova su telefono reale via LAN |
| End-to-end | Runtime ↔ dashboard | [x] **reale** | catena verificata con client reale: runtime 8766 → dashboard 8765 `connected=true`, `telemetry_source=LIVE` |
| End-to-end | Errore/disconnessione/riconnessione | [x] locale | heartbeat fail-closed; dispositivo reale ancora richiesto |
| Governance | Nessuna regressione bloccante | [x] locale | `pytest` 87; `--gate1-test` 19/19; `--host-test` 7/7; `NosAi.Runtime.Tests` 5/5. Nota: su questa macchina l'apphost `.exe` è bloccato da Application Control (`0x800711C7`), quindi le suite vanno lanciate come `dotnet <percorso>.dll` |
| Governance | Documentazione coerente | [x] locale | source of truth, checklist, stato |

---

## Evidenza reale registrata

**Data:** 2026-08-30 · **Ambiente:** PC target Windows 11, NosTale installato in
`C:\Program Files (x86)\Nostale`, client in esecuzione.

Comandi:

```bash
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll
python -m nosai.dashboard.server
```

Osservato dal runtime:

```
Health: Healthy
Guard port: 17471
Runtime operator API: http://127.0.0.1:8766/
Client: attached_os_session (LIVE)
Client process: NostaleClientX [LIVE] pid=7932 [LIVE]
Client window: Nostale [LIVE] 0x8099A [LIVE]
Gameplay baseline: UNKNOWN (gameplay_provider_not_available)
Hardware CPU: AMD Ryzen 7 260 w/ Radeon 780M Graphics [LIVE]
```

Osservato dalla dashboard operatore su `http://127.0.0.1:8765/api/state`, senza
impostare `NOSAI_RUNTIME_URL` a mano:

```
connected        : True
telemetry_source : LIVE
gate1_failure    : None
client.status    : attached_os_session
processName      : NostaleClientX / LIVE
windowTitle      : Nostale / LIVE
gameplayBaseline : None / UNKNOWN
```

Cosa prova: rilevamento e lettura del client reale, catena runtime → dashboard su
porte separate, e classificazione onesta (il gameplay resta `UNKNOWN`, non viene
inventato).

Cosa **non** prova: nessuna sessione smartphone, nessuna lettura di memoria di
gioco, nessun input o injection (restano disabilitati).

---

## Dataset minimo canonico da acquisire

Il Gate 1 deve definire e rendere disponibile almeno un dataset minimo reale e verificabile.

### Dataset minimo richiesto

- stato del runtime;
- stato della sessione con Guard AI;
- stato del collegamento al client NosTale;
- primi dati di base del client ritenuti indispensabili dal progetto;
- primi dati di base del PC ritenuti indispensabili dal progetto;
- stato di sicurezza/autorizzazione rilevante per il livello corrente.

Se uno di questi elementi manca, il Gate 1 non è completo.

---

## Evidenze richieste

Ogni punto completato deve avere almeno una delle seguenti evidenze:

- test automatico pertinente;
- test di integrazione pertinente;
- log osservabile e ripetibile;
- output dashboard coerente;
- nota di validazione manuale chiaramente descritta.

Le dichiarazioni prive di evidenza non sono considerate completamento.

---

## Criterio formale di superamento

Il Gate 1 è superato solo se:

1. tutti i punti critici del runtime PC sono completati;
2. tutti i punti critici del collegamento client sono completati;
3. tutti i punti critici del collegamento smartphone sono completati;
4. la dashboard riflette solo stato reale e coerente;
5. i test end-to-end minimi hanno esito positivo;
6. i casi di errore e disconnessione hanno esito positivo;
7. la documentazione finale è coerente con le prove osservate.

Il Gate 1 **non è superato**. Il ramo PC ↔ NosTale è ora chiuso con evidenza reale
(vedi *Evidenza reale registrata*), ma il ramo smartphone resta aperto e il criterio 3
non è soddisfatto.

Il blocco residuo **non è la disponibilità di un telefono**: l'applicazione Guard AI
non esiste nel repository (nessun progetto Android/iOS). Finché non viene scritta
contro il canale canonico di ADR-0006, i tre punti smartphone non possono chiudersi.

Le spunte `locale` coprono implementazione e test automatici, non la promozione a
`VERIFIED`.

---

## Regola di disciplina

Fino al superamento formale del Gate 1:

- le nuove implementazioni devono essere giustificate dal suo completamento;
- i moduli successivi non vanno considerati maturi sul piano operativo;
- le espansioni non essenziali hanno priorità inferiore ai blocchi reali del primo circuito.
