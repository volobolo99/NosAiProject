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
| Guard AI smartphone | Avvio affidabile | [x] **reale** | APK installato e avviato su dispositivo Android `9125322104AC`; UI operativa |
| Guard AI smartphone | Connessione reale | [x] **reale via Wi-Fi** | sessione autenticata su LAN, cavo USB staccato e tunnel rimosso; runtime trovato per discovery, nessun indirizzo inserito |
| Guard AI smartphone | Autenticazione reale | [x] **reale** | chiave del telefono registrata via `enroll`; `authenticated=True [LIVE]` sul runtime |
| Guard AI smartphone | Heartbeat reale | [x] **reale** | `lastHeartbeatUtc` aggiornato dal dispositivo fisico; silenzio > 2s → sessione chiusa |
| Guard AI smartphone | Riconnessione controllata | [x] **reale** | app terminata → sessione caduta fail-closed; riavvio → **nuovo** sessionId `2730cc13…` (era `eb78f421…`) |
| Dashboard | Avvio affidabile | [x] locale | operator server Gate 1 su loopback |
| Dashboard | Connessione al runtime corretto | [x] **reale** | UI 8765 → runtime 8766 con default `NOSAI_RUNTIME_URL`, nessuna variabile impostata a mano; porta occupata → runtime vivo e `dashboard_port_in_use` esplicito |
| Dashboard | Dati reali soltanto | [x] locale | demo gold/mostri/GPU rimossi; UNKNOWN esplicito |
| Dashboard | Coerenza degli stati | [x] locale | snapshot unico PC/client/guard/safety |
| Dashboard | Error handling | [x] locale | client assente e runtime offline non mascherati |
| End-to-end | PC ↔ client | [x] **reale** | runtime `Healthy` contro NosTale in esecuzione; `attached_os_session`, campi client `LIVE`, gameplay `UNKNOWN` |
| End-to-end | PC ↔ smartphone | [x] **reale via Wi-Fi** | NosTale reale → runtime → telefono su LAN senza USB: `authenticated=True`, heartbeat in avanzamento, `NostaleClientX [LIVE]` |
| End-to-end | Runtime ↔ dashboard | [x] **reale** | catena verificata con client reale: runtime 8766 → dashboard 8765 `connected=true`, `telemetry_source=LIVE` |
| End-to-end | Errore/disconnessione/riconnessione | [x] **reale** | ciclo completo su dispositivo fisico: connesso → ucciso → fail-closed → riconnesso con nuova sessione |
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

## Evidenza reale — telefono Android (2026-08-30)

**Dispositivo:** Android `9125322104AC`, collegato via USB.
**Trasporto:** `adb reverse tcp:17471 tcp:17471`. Il telefono chiama `127.0.0.1:17471`,
il tunnel porta la connessione al runtime sul PC. **Non è la rete Wi-Fi/LAN**, che
resta non provata.

Procedura eseguita:

```bash
python -m nosai.phone.deploy --reinstall          # installa APK + apre il tunnel
python -m nosai.phone.enroll --out data/guard_public_key.pem
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll \
    --guard-public-key-path data/guard_public_key.pem
```

Sessione autenticata, letta dal runtime su `/api/gate1`:

```
connected            True                                LIVE
authenticated        True                                LIVE
sessionId            'eb78f4213e9b477e993db153f0161e6a'  LIVE
lastHeartbeatUtc     '2026-08-30T17:41:12.9348027Z'      LIVE
runtimeStatus        Healthy
client.status        attached_os_session
gameplayBaseline     None                                UNKNOWN
liveInput False · packetInjection False
```

Sul telefono l'app mostrava `CONNESSO`, le capability
`gate1;auth=rsa2048-sha256;heartbeat=2000;execution=disabled` e i campi del client
reale: `NostaleClientX [LIVE]`, PID `7932 [LIVE]`, finestra `Nostale [LIVE]`.

Disconnessione e riconnessione:

```
app terminata  -> connected False, authenticated False, sessionId UNKNOWN
app riavviata  -> sessionId '2730cc13f7794ba5b31c7d34ede04b15'   (nuovo)
```

Cosa prova: il circuito completo `NosTale reale → runtime PC → canale NOSA
autenticato RSA-2048 → telefono Android fisico`, con caduta fail-closed e
riconnessione con sessione nuova.

Cosa **non** prova: nessuna sessione su Wi-Fi/LAN; nessuna lettura di memoria di
gioco (il gameplay resta `UNKNOWN`); nessun input o injection.

> **Trappola operativa osservata.** Il reverse tunnel non sopravvive alla
> riconnessione del dispositivo né al riavvio del server ADB, e dall'app la sua
> caduta è indistinguibile da un runtime spento: si vede solo
> `connect_failed (ConnectionRefused)`. Se l'app non si collega, verificare
> `adb reverse --list` prima di cercare il problema nel runtime.

## Evidenza reale — Wi-Fi (2026-08-30)

Verifica con **cavo USB staccato** e tunnel `adb reverse` rimosso, così la LAN
era l'unico percorso possibile.

```
adb devices                  -> (nessun dispositivo)
telefono -> 127.0.0.1:17471  -> rifiutato          (nessun tunnel)
telefono -> 192.168.0.4:17471 -> aperta            (LAN)
```

PC `192.168.0.4`, telefono `192.168.0.2/24`. Nell'app è stato scelto Wi-Fi;
**nessun indirizzo è stato inserito**: il runtime è stato trovato per discovery
su UDP/17472.

Sessione letta dal runtime, con `adb devices` vuoto:

```
connected        True                                LIVE
authenticated    True                                LIVE
sessionId        '648fbd94d9eb4085b3f80072085f386a'  LIVE
client.status    attached_os_session
proc             NostaleClientX

lastHeartbeatUtc 18:07:12 -> 18:07:15 -> 18:07:17    (in avanzamento)
```

Il runtime era stato avviato **senza alcun flag**: ha caricato da solo la chiave
del dispositivo da `data/guard_public_key.pem`, scritta dall'abbinamento via USB
fatto in precedenza.

```
[INFO] Trusting one Guard device key. source=data/guard_public_key.pem
[INFO] Gate 1 runtime is listening. guardPort=17471 discovery=udp/17472
```

Cosa prova: il circuito completo su rete reale, senza cavo e senza configurazione
da parte dell'operatore.

Cosa **non** prova: nessuna autenticazione del runtime verso il telefono (ADR-0007,
limite 1); nessuna cifratura del payload; nessuna lettura di memoria di gioco.

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

**Stato: tutti e sette i criteri sono soddisfatti con evidenza reale.**

Il criterio 3 è chiuso **su rete Wi-Fi**, non solo su USB: la sessione è stata
completata con il cavo staccato e il tunnel `adb reverse` rimosso, con il runtime
trovato per discovery e nessun indirizzo inserito a mano (vedi *Evidenza reale —
Wi-Fi*).

Restano tre limiti dichiarati. **Nessuno blocca il Gate 1**, ma vanno chiusi prima
di parlare di esercizio continuativo, e il primo prima di usare il Wi-Fi su una
rete non controllata:

1. **Il runtime non è autenticato verso il telefono.** Il canale prova il telefono
   al PC, non il contrario: su una rete ostile un host può rispondere per primo
   alla discovery e presentarsi come runtime, alimentando il telefono con stato
   inventato o raccogliendo firme su challenge scelte da lui. Vedi ADR-0007.
2. **Il canale serve una sola sessione per volta**, quindi su LAN chiunque apra
   una connessione può occupare lo slot ed escludere il telefono legittimo.
3. **Il payload non è cifrato**, e la chiave del dispositivo è in storage privato
   dell'app, non nell'Android Key Store.

Il punto 1 richiede autenticazione mutua nell'handshake, che modifica il contratto
di wire e supera in parte ADR-0006. Non è stato fatto qui.

---

## Regola di disciplina

Fino al superamento formale del Gate 1:

- le nuove implementazioni devono essere giustificate dal suo completamento;
- i moduli successivi non vanno considerati maturi sul piano operativo;
- le espansioni non essenziali hanno priorità inferiore ai blocchi reali del primo circuito.
