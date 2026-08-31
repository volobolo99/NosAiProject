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
| Client NosTale | Stato di rete del client | [x] **reale** | letto dalla tabella TCP di Windows sul PID del client: `networkConnected=True [LIVE]`, `serverEndpoint=79.110.84.175:4006 [LIVE]`, `connectionState=Established [LIVE]`. Nessun payload, nessun driver, nessuna elevazione. Con più sessioni remote l'endpoint resta `UNKNOWN`, non indovinato |
| Client NosTale | Validazione dati | [x] locale | provenance `LIVE`/`UNKNOWN` nel snapshot |
| Client NosTale | Gestione client assente | [x] locale | runtime resta DEGRADED, non inventa gameplay |
| Guard AI smartphone | Avvio affidabile | [x] **reale** | APK installato e avviato su dispositivo Android `9125322104AC`; UI operativa |
| Guard AI smartphone | Connessione reale | [x] **reale via Wi-Fi** | sessione autenticata su LAN, cavo USB staccato e tunnel rimosso; runtime trovato per discovery, nessun indirizzo inserito |
| Guard AI smartphone | Autenticazione reale | [x] **reale, mutua** | wire v2: il telefono verifica la prova del runtime prima di firmare; `authenticated=True [LIVE]` su USB e su Wi-Fi |
| Guard AI smartphone | Riservatezza del payload | [x] locale | wire v3 (ADR-0009): AES-256-GCM su chiavi effimere P-256 legate alle firme dell'handshake. Verificato contro il processo runtime reale, **non ancora sul telefono** |
| Guard AI smartphone | Heartbeat reale | [x] **reale** | `lastHeartbeatUtc` aggiornato dal dispositivo fisico; silenzio > 2s → sessione chiusa |
| Guard AI smartphone | Riconnessione controllata | [x] **reale** | app terminata → sessione caduta fail-closed; riavvio → **nuovo** sessionId `2730cc13…` (era `eb78f421…`) |
| Guard AI smartphone | Riconnessione automatica | [x] locale | l'app ritenta da sola con backoff 1→15 s quando la causa può passare (runtime spento, tunnel caduto, Wi-Fi); **non ritenta** ciò che richiede l'operatore (dispositivo non riconosciuto, versione wire diversa, frame che non si apre). Provato dai test, **non sul telefono** |
| Guard AI smartphone | Stato di sicurezza mostrato | [x] locale | l'app **legge** `safety.executionMode` dallo snapshot invece di affermarlo: prima lo schermo dichiarava da sé «nessun input, nessuna injection». Senza sessione mostra `Sconosciuto`, mai «disabilitato» |
| Dashboard | Avvio affidabile | [x] locale | operator server Gate 1 su loopback |
| Dashboard | Connessione al runtime corretto | [x] **reale** | UI 8765 → runtime 8766 con default `NOSAI_RUNTIME_URL`, nessuna variabile impostata a mano; porta occupata → runtime vivo e `dashboard_port_in_use` esplicito |
| Dashboard | Dati reali soltanto | [x] locale | demo gold/mostri/GPU rimossi; UNKNOWN esplicito |
| Dashboard | Coerenza degli stati | [x] locale | snapshot unico PC/client/guard/safety |
| Dashboard | Error handling | [x] locale | client assente e runtime offline non mascherati |
| End-to-end | PC ↔ client | [x] **reale** | runtime `Healthy` contro NosTale in esecuzione; `attached_os_session`, campi client `LIVE`, gameplay `UNKNOWN` |
| End-to-end | PC ↔ smartphone | [x] **reale via Wi-Fi** | NosTale reale → runtime → telefono su LAN senza USB: `authenticated=True`, heartbeat in avanzamento, `NostaleClientX [LIVE]` |
| End-to-end | Runtime ↔ dashboard | [x] **reale** | catena verificata con client reale: runtime 8766 → dashboard 8765 `connected=true`, `telemetry_source=LIVE` |
| End-to-end | Errore/disconnessione/riconnessione | [x] **reale** | ciclo completo su dispositivo fisico: connesso → ucciso → fail-closed → riconnesso con nuova sessione |
| Governance | Nessuna regressione bloccante | [x] locale | `pytest` 181; `NosAi.Runtime.Tests` 175; 18 suite del runtime verdi. Fra questi 15 test negativi sul confine del canale (peer v1/v2, magic estraneo, chiave non fidata, frame in chiaro, replay, punto fuori curva), ognuno con il motivo atteso. Nota: su questa macchina l'apphost `.exe` è bloccato da Application Control (`0x800711C7`), quindi le suite vanno lanciate come `dotnet <percorso>.dll` |
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

Cosa **non** prova: nessuna cifratura del payload; nessuna lettura di memoria di gioco.
Il circuito Wi-Fi documentato sopra è stato chiuso su wire version 1, quando il
runtime non era ancora autenticato verso il telefono. È stato ripetuto dopo il
re-pair sul protocollo versione 2: vedi la sezione seguente, che è la prova
autorevole e sostituisce questa quanto ad autenticazione.

## Evidenza reale — autenticazione mutua, wire v2 (2026-08-30)

Verifica sul dispositivo Android `9125322104AC` (NX809J) contro NosTale in
esecuzione, con il protocollo versione 2.

Abbinamento, che prima falliva su build release:

```
Pairing: device key written to data\guard_public_key.pem
Pairing: runtime pin pushed (data\runtime_public.pem)
deploy exit=0
```

Runtime avviato **senza alcun flag**: carica da solo entrambe le chiavi.

```
[INFO] Trusting one Guard device key. source=data/guard_public_key.pem
[INFO] Gate 1 runtime is listening. guardPort=17471 discovery=udp/17472
Client process: NostaleClientX [LIVE] pid=7932 [LIVE]
Client window:  Nostale [LIVE] 0x8099A [LIVE]
```

**Sessione USB** (tunnel `adb reverse`):

```
connected      True   authenticated True
sessionId      '7f73c07ed84c42f880be53d46fbce329'
```

**Sessione Wi-Fi**, con il tunnel rimosso — `adb reverse --list` vuoto e il
loopback dal telefono **rifiutato**, quindi la LAN era l'unico percorso possibile:

```
loopback dal telefono -> CHIUSA
connected      True   authenticated True
sessionId      'dbb75562afea436194b348625c27388d'    (nuova sessione)
lastHeartbeat  20:45:02 -> 20:45:06 -> 20:45:09      (in avanzamento)
```

Sullo schermo del telefono: Wi-Fi selezionato, `CONNESSO`, endpoint
`192.168.0.4:17471` — l'indirizzo LAN del PC, non il loopback — e i campi del
client reale. PC `192.168.0.4`, telefono `192.168.0.2`.
Screenshot e trascritto grezzo restano in `data/`, che è gitignorata perché
contiene materiale di chiave: quanto sopra è la trascrizione, ed è il record
durevole. Un clone non troverà quei file, ed è voluto.

Cosa prova: l'handshake mutuo su hardware reale, su USB e su rete. Il telefono ha
verificato la prova del runtime prima di firmare, e il runtime ha verificato il
telefono — nessuno dei due ha creduto all'altro sulla parola.

Cosa **non** prova: nessuna cifratura del payload; nessuna lettura di memoria di
gioco; nessun input o injection. La cifratura è arrivata dopo, con wire version 3,
ed è documentata nella sezione seguente: **quel giro non è stato ripetuto sul
telefono**, e questa evidenza non lo copre.

## Verifica locale — cifratura del payload, wire v3 (2026-08-30)

Non è evidenza su dispositivo reale, ed è segnata come locale apposta. È però
più di un test unitario: il client di riferimento Python parla con il **processo
runtime reale** su un socket reale, e la prova guarda i byte sul filo invece di
fidarsi di quello che il client dichiara.

```
tests/test_guard_client_conformance.py::test_the_snapshot_is_not_readable_on_the_wire
  frame TelemetrySnapshot catturato prima dell'apertura:
    "contractVersion"  assente dal ciphertext
    "gate1.snapshot"   assente
    "UNKNOWN"          assente
    nonce iniziale     000000000000000000000000  (contatore a zero)
  lo snapshot aperto:  contractVersion = gate1.snapshot.v1
```

Parità fra i due linguaggi, pinnata da vettori identici sui due lati:

```
transcript client  C21C431996795F1008869B2F2F404788065FEBB2B4D540EBA6E10586EB81DCCB
transcript server  4FA15241CCA7785A61BA9ADA88CD5C6C6C3330BDA4B9C7160D6F50E8F6E59047
binding chiavi     EEA2EFAC25055CB73768C2C38E4150E682441F83A2D9EDF8056FEC37078DD397
frame golden       sigillato in Python, aperto in C# byte per byte
```

Cosa prova: il payload è cifrato davvero, l'header resta leggibile ma
autenticato (riscriverne il tipo fa fallire il tag), le due direzioni non
condividono chiave, un nonce fuori ordine è rifiutato, e C# e Python producono
gli stessi byte.

Cosa **non** prova: niente sul dispositivo fisico. L'APK va reinstallato — la
copia v2 sul telefono viene rifiutata all'header, che è il comportamento voluto.

## Evidenza reale — wire v3 · **NON ESEGUITO**

> **Modello da riempire.** Nessun campo qui sotto è stato osservato. Finché
> restano `non eseguito`, wire v3 sul dispositivo è **Integrated/locale**, mai
> `Verified`. Non compilare questa sezione senza output reale davanti.

Procedura, con il telefono collegato via USB e il debug ADB autorizzato:

```bash
# 1. costruisce l'APK se il protocollo si è mosso, installa, abbina e spinge il pin
python -m nosai.phone.deploy --reinstall

# 2. avvia il runtime senza flag: carica da solo la chiave del telefono e la propria identità
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll

# 3. sul telefono: premere Connetti (USB). Poi leggere lo stato dal PC:
curl -s http://127.0.0.1:8766/api/gate1

# 4. per il giro Wi-Fi: rimuovere il tunnel, così la LAN resta l'unico percorso
adb reverse --remove-all && adb reverse --list      # deve essere vuoto
# sul telefono: selezionare Wi-Fi, poi Connetti. Ripetere il punto 3.
```

`deploy` stampa la versione di wire che il PC si aspetta ed è ora **fail-closed
su un APK vecchio**: se l'APK precede l'ultima modifica al protocollo lo
ricostruisce, e con `--no-build` si rifiuta di installarlo invece di consegnare
un telefono che non potrà collegarsi.

| Campo | Come si ottiene | Valore osservato |
|---|---|---|
| Device id | `adb devices` | *non eseguito* |
| Versione wire negoziata | header accettato dal runtime, nessun `unsupported_version` nel log | *non eseguito* |
| `authenticated` (USB) | `/api/gate1` → `guard.authenticated` | *non eseguito* |
| `sessionId` (USB) | `/api/gate1` → `guard.sessionId` | *non eseguito* |
| `authenticated` (Wi-Fi) | come sopra, con il tunnel rimosso | *non eseguito* |
| `sessionId` (Wi-Fi) | deve essere **diverso** da quello USB | *non eseguito* |
| Endpoint mostrato dall'app | schermata dell'app: deve essere l'IP LAN del PC, non `127.0.0.1` | *non eseguito* |
| Loopback dal telefono | `adb shell 'nc -w 2 -z 127.0.0.1 17471'` → deve essere **rifiutato** | *non eseguito* |
| Heartbeat in avanzamento | tre letture successive di `guard.lastHeartbeatUtc` | *non eseguito* |
| Payload non in chiaro | catturare un frame `TelemetrySnapshot`: non deve contenere `contractVersion` né `gate1.snapshot` | *non eseguito* |
| Rifiuto di un APK v2 | installare la build precedente → il runtime logga `unsupported_version` e la sessione non si apre | *non eseguito* |

L'ultima riga è la più informativa delle due direzioni: dimostra che il divieto
di downgrade morde davvero, invece di essere solo dichiarato.

Quando i campi sono compilati con output reale, questa sezione va rinominata
togliendo **NON ESEGUITO**, e la riga *Riservatezza del payload* nella tabella di
avanzamento passa da `[x] locale` a `[x] reale`.

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

Il criterio 3 è chiuso **su rete Wi-Fi e con autenticazione mutua**, non solo su
USB: la sessione è stata completata con il tunnel `adb reverse` rimosso e il
loopback dal telefono rifiutato, con il runtime trovato per discovery e nessun
indirizzo inserito a mano (vedi *Evidenza reale — Wi-Fi* per il circuito e
*Evidenza reale — autenticazione mutua, wire v2* per la ripetizione su wire v2,
che è la prova autorevole).

Quello che era il primo limite di questa lista — **il runtime non autenticato
verso il telefono** — è chiuso. L'handshake wire version 2 (ADR-0008) è mutuo: il
telefono verifica la prova del runtime prima di firmare, la versione 1 è
rifiutata, e il circuito è stato verificato su dispositivo reale sia su USB sia su
Wi-Fi (vedi *Evidenza reale — autenticazione mutua, wire v2*). Cade con esso la
riserva di ADR-0007 sull'uso del Wi-Fi solo su rete controllata.

Restano tre limiti dichiarati. **Nessuno blocca il Gate 1**, ma vanno chiusi prima
di parlare di esercizio continuativo:

1. **Il payload è cifrato nel codice, ma non ancora provato sul telefono.**
   Wire version 3 ([ADR-0009](adr/ADR-0009-session-payload-encryption.md)) sigilla
   ogni frame dopo l'handshake con AES-256-GCM, sotto chiavi effimere P-256 che le
   firme dell'handshake autenticano. È verificato in locale contro il processo
   runtime reale — la telemetria sul filo non contiene più il testo dello snapshot
   — ma il circuito **non è stato ripetuto sul dispositivo fisico**, che va
   reinstallato: un APK v2 viene rifiutato all'header, come previsto.
   Restano visibili dimensione e cadenza dei frame.
2. **Il canale serve una sola sessione per volta**, ma non è più una connessione
   sola: fino a quattro candidati fanno l'handshake in parallelo e vince chi si
   autentica per primo ([ADR-0011](adr/ADR-0011-single-guard-session.md)). Uno
   squatter silenzioso non tiene più fuori il telefono — viene sfrattato per
   primo, e il telefono si autentica accanto a lui in 82 ms misurati. Una
   sessione autenticata non è spodestabile. **Resta aperto**: chi *parla* e poi
   si ferma può ancora occupare i quattro posti. Ridotto, non chiuso.
3. **Le chiavi di identità non sono più in chiaro sul PC**, e sul telefono lo
   sono solo se il dispositivo non offre di meglio
   ([ADR-0010](adr/ADR-0010-key-custody.md)). L'identità del runtime è avvolta
   con DPAPI in `data/runtime_identity.dpapi`; quella del dispositivo è generata
   **dentro** l'Android Keystore, e dove il Keystore non c'è l'app ripiega su
   file **dichiarandolo**, invece di sembrare protetta. Le chiavi **di sessione**
   restano effimere. **Resta aperto**: DPAPI lega la chiave all'account Windows,
   non a un TPM, e il giro Keystore **non è stato provato su dispositivo**.

---

## Regola di disciplina

Fino al superamento formale del Gate 1:

- le nuove implementazioni devono essere giustificate dal suo completamento;
- i moduli successivi non vanno considerati maturi sul piano operativo;
- le espansioni non essenziali hanno priorità inferiore ai blocchi reali del primo circuito.
