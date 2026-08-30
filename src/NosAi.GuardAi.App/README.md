# NosAi Guard AI — applicazione Android

Client smartphone del canale canonico definito da
[ADR-0006](../../docs/adr/ADR-0006-canonical-phone-channel.md).

L'app è un guscio attorno a `NosAi.GuardClient`. Ogni byte sul filo è deciso da
`NosAi.Protocol`, l'unico assembly condiviso con il runtime PC: il formato non può
divergere fra i due lati senza rompere la compilazione.

## Struttura

| Progetto | Target | Ruolo |
|---|---|---|
| `NosAi.Protocol` | `net8.0` | primitive di wire canoniche (`WireHeader`, `WireMessageType`, `SequenceGuard`), condivise da runtime e telefono |
| `NosAi.GuardClient` | `net8.0` | client del canale: handshake, firma RSA, heartbeat, sequence guard |
| `NosAi.GuardAi.App` | `net8.0-android` | interfaccia operatore |

Il target è **solo Android**. iOS e Mac Catalyst sono stati rimossi dal template:
non sono costruibili né validabili in questo ambiente, e lasciarli avrebbe fatto
sembrare l'app più portabile di quanto sia stato dimostrato.

## Compilazione

Richiede il workload `maui-android`, un JDK 17+ e l'Android SDK:

```bash
dotnet workload install maui-android
dotnet build src/NosAi.GuardAi.App/NosAi.GuardAi.App.csproj \
  -t:InstallAndroidDependencies -f:net8.0-android \
  -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk> \
  -p:AcceptAndroidSDKLicenses=True
```

Poi:

```bash
dotnet build src/NosAi.GuardAi.App/NosAi.GuardAi.App.csproj -c Release -f net8.0-android \
  -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>
```

I percorsi di SDK e JDK non sono fissati nel `.csproj`: dipendono dalla macchina.

## Uso

1. Sul PC, avvia il runtime con la chiave pubblica del telefono registrata:
   `dotnet NosAi.Runtime.dll --guard-public-key-path <file.pem>`.
2. Nell'app, copia la chiave pubblica del dispositivo e salvala in quel file.
3. Inserisci l'indirizzo LAN del PC e la porta (default `17471`), poi connetti.

L'app mostra i campi del client NosTale con la loro provenienza. `UNKNOWN`
significa **non osservato**: non viene mai sostituito con zero, trattino o stringa
vuota, perché su uno schermo sarebbero indistinguibili da una misura reale.

Alla caduta della sessione lo stato passa subito a non connesso e l'ultimo
snapshot valido non resta a schermo: mostrare dati vecchi come correnti è
esattamente ciò che questa app non deve fare.

## Limiti dichiarati

Questi limiti sono reali e vanno chiusi prima di dichiarare `VERIFIED` un punto
smartphone del Gate 1:

- **Ciclo di vita della chiave.** La chiave è persistita nello storage privato
  dell'app, quindi sopravvive ai riavvii, ma **non è nell'Android Key Store** e non
  è hardware-backed: è leggibile con root o da un backup dei dati dell'app.
  `DeviceIdentity` è il punto in cui va sostituita.
- **Il runtime è autenticato verso il telefono.** Wire version 2: il telefono
  verifica `ServerAuthProof` contro la chiave pubblica del runtime, pinnata
  all'abbinamento USB (`python -m nosai.phone.deploy`). Senza pin l'handshake
  è fail-closed. Vedi [ADR-0008](../../docs/adr/ADR-0008-mutual-handshake.md).
- **Una sola sessione per volta.** Su LAN chiunque apra una connessione può
  occupare lo slot ed escludere il telefono legittimo.
- **Trasporto in chiaro.** Il canale autentica il telefono ma non cifra il
  payload. `docs/INTEGRAZIONE_RSA_SESSION_AUTH.md` elenca la cifratura autenticata
  fra i limiti aperti. Usare solo su rete domestica fidata.
- **Nessun comando.** Gate 1 disabilita l'esecuzione: il runtime risponde
  `execution_disabled_in_gate1` a ogni `CommandRequest`. L'app non li invia.

## Deploy su telefono

Con il telefono collegato via USB e il debug ADB autorizzato:

```bash
python -m nosai.phone.deploy
```

Installa l'APK, apre il tunnel USB **e abbina il dispositivo**: la chiave del
telefono viene raccolta e scritta dove il runtime la cerca da solo. Poi:

```bash
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll
```

Nessun flag: il runtime carica `data/guard_public_key.pem` e dichiara nel log
quale chiave sta fidando.

## Come si collega

Nell'app c'è una sola scelta, ricordata fra un avvio e l'altro:

| | |
|---|---|
| **USB** | telefono collegato al cavo; passa dal tunnel `adb reverse` |
| **Wi-Fi** | telefono sulla stessa rete del PC; il runtime è **trovato per discovery** |

In nessuno dei due casi si inserisce un indirizzo, una chiave o un'autorizzazione.
L'abbinamento si fa una volta via USB e vale anche per il Wi-Fi, perché l'identità
del dispositivo è persistita.

> Se l'app mostra `connect_failed (ConnectionRefused)`, controllare
> `adb reverse --list` prima di sospettare il runtime: il tunnel non sopravvive
> alla riconnessione del dispositivo né al riavvio del server ADB, e dall'app la
> sua caduta è indistinguibile da un runtime spento.
