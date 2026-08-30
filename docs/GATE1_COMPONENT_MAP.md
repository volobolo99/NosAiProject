# NosAi — Mappa componenti Gate 1

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Scopo

Questo documento mappa i componenti principali del percorso critico Gate 1, i file coinvolti, il loro livello di maturità, l'uso di dati reali, lo stato dei test e il blocco principale da risolvere.

Serve come ponte operativo tra documentazione, audit tecnico e refactoring del primo circuito reale del progetto.

---

## Percorso critico Gate 1

Il percorso minimo da chiudere e verificare è il seguente:

**bootstrap runtime PC → collegamento client NosTale → acquisizione dati minimi → collegamento Guard AI smartphone → dashboard coerente → gestione errore/disconnessione**

---

## Mappa componenti

| Componente | File principali | Maturità | Usa dati reali? | Test esiste? | Blocco principale |
|---|---|---|---|---|---|
| **Bootstrap runtime** | `src/NosAi.Runtime/Program.cs`, `Gate1/Gate1BootstrapHost.cs` | **Integrated** | **Parziale** | **Sì** | avvio locale coperto; manca evidenza completa sul PC di produzione |
| **Protocollo Gate 1 PC ↔ smartphone** | `src/NosAi.Runtime/Gate1/Gate1Runtime.cs` | **Integrated** | **Parziale** | **Sì** | auth/heartbeat/riconnessione locali coperti; manca sessione su dispositivo reale |
| **Test runner Gate 1** | `src/NosAi.Runtime/Gate1/Gate1TestRunner.cs`, `tests/NosAi.Runtime.Tests` | **Integrated** | **No** | **Sì** | copre invarianti locali, non il client NosTale reale |
| **Runtime snapshot provider** | `src/NosAi.Runtime/Gate1/Gate1CanonicalSnapshot.cs` | **Integrated** | **Parziale** | **Sì** | contratto `gate1.snapshot.v1` con classificazione LIVE/UNKNOWN |
| **Client connector NosTale** | `src/NosAi.Runtime/LiveIntegration/RealClientConnector.cs` | **Partial** | **Parziale** | **Sì** | attachment processo/finestra; gameplay ancora UNKNOWN |
| **Dashboard embedded** | `src/NosAi.Runtime/Gate1/Gate1BootstrapHost.cs`, `Host/NosAiMasterRuntimeHost.cs` | **Integrated** | **Parziale** | **Sì** | campi demo rimossi; dashboard mostra UNKNOWN invece di gold/mostri finti |
| **Hardware profiling / autoset** | `Hardware/LiveHardwareTelemetry.cs` | **Integrated** | **Parziale** | **Sì** | RAM processo LIVE; RAM sistema/GPU UNKNOWN se il probe fallisce |
| **Perception contracts** | `src/NosAi.Runtime/Perception/PerceptionContracts.cs` | **Present** | **No** | **Parziale** | il contratto esiste ma non basta senza backend produttivo |
| **Perception null provider** | `src/NosAi.Runtime/Perception/NullPerceptionProvider.cs` | **Partial** | **No** | **Parziale** | è utile come fallback tecnico ma non contribuisce al Gate 1 reale |
| **World state / world model** | area `src/NosAi.Runtime/WorldModel` | **Integrated** | **Parziale** | **Parziale** | dipende dal completamento delle sorgenti reali di input |
| **Guard AI smartphone** | area `src/NosAi.Runtime/Guard` + `Gate1Runtime.cs` | **Partial** | **Parziale** | **Parziale** | handshake locale coperto; manca prova sul dispositivo reale |
| **Control center HTTP** | `Gate1OperatorServer`, `Host/NosAiMasterRuntimeHost.cs` | **Integrated** | **Parziale** | **Sì** | `/api/gate1` classificato; Host legacy non inventa più gold/mostri |
| **Gateway eventi dashboard** | `src/NosAi.Runtime/Network/Gateway/ControlPanelGatewayEngine.cs` | **Integrated** | **No / misto** | **Sì** | l'infrastruttura esiste ma non dimostra ancora stream di dati reali del runtime |
| **Safety / trust boundary** | `src/NosAi.Runtime/Host/NosAiMasterRuntimeHost.cs`, `src/NosAi.Runtime/Contracts/RuntimeContracts.cs` | **Integrated** | **Parziale** | **Parziale** | necessita verifica con segnali reali e casi negativi reali |
| **Storage / session persistence** | area `src/NosAi.Runtime/Storage` | **Integrated** | **Parziale** | **Parziale** | va dimostrato l'uso reale lungo il percorso critico |
| **Logging / telemetria** | Host, Gateway, Gate1 snapshot | **Integrated** | **Parziale** | **Sì** | snapshot Gate 1 classificato; debug end-to-end reale ancora incompleto |
| **Dashboard error/disconnect handling** | Gate1 operator dashboard + Python dashboard | **Integrated** | **Parziale** | **Sì** | runtime offline e client assente restano UNKNOWN |
| **Suite test Python legacy/ibrida** | `tests/` | **Partial** | **No / misto** | **Sì** | aggiunti test di classificazione Gate 1; e2e reale ancora assente |

---

## Zone con dati simulati o da chiarire subito

Queste aree devono essere verificate prima di qualsiasi dichiarazione di avanzamento operativo:

| Area | Segnale di ambiguità | Azione richiesta |
|---|---|---|
| **Dashboard embedded** | metriche e stati che sembrano esemplificativi o statici | sostituire o marcare esplicitamente come non reali |
| **Host telemetry** | parte dei valori osservabili sembra non derivare da provider live completi | collegare ogni campo a una sorgente reale o rimuoverlo dal percorso critico |
| **Client connector NosTale** | attualmente verifica processo/finestra e produce uno snapshot baseline strutturato, ma non espone ancora dati gameplay reali | estendere il baseline dataset senza oltrepassare i vincoli del progetto |
| **Perception** | contratti presenti ma backend reali non ancora chiusi | delimitare cosa è davvero usato nel Gate 1 e cosa è ancora fondazione |
| **Test suite** | copertura eterogenea tra Python e C# | distinguere test di contratto, test di integrazione e test autorevoli Gate 1 |

---

## Ordine di intervento consigliato

1. verificare il **bootstrap reale** del runtime sul PC;
2. tracciare il **dataset minimo reale** proveniente da client e PC;
3. verificare il **flusso reale Gate 1** per sessione, auth e heartbeat smartphone;
4. rimuovere o isolare i **campi demo/simulati** dalla dashboard;
5. definire i **test autorevoli** del percorso end-to-end;
6. aggiornare di conseguenza stato implementazione e checklist Gate 1.

---

## Definizione pratica di completamento del documento

Questa mappa è utile solo se viene mantenuta viva.

Per ogni modifica critica del Gate 1 devono essere aggiornati:

- file principali coinvolti;
- livello di maturità;
- presenza o assenza di dati reali;
- esistenza di test autorevoli;
- blocco principale residuo.

---

## Sintesi finale

Il Gate 1 non è oggi bloccato dall'assenza totale di codice.

È bloccato soprattutto dalla distanza tra:

- fondazioni tecniche già presenti;
- integrazione reale delle sorgenti dati;
- prova end-to-end osservabile e ripetibile.

Questa mappa esiste per chiudere quella distanza in modo disciplinato.
