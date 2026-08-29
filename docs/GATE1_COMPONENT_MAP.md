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
| **Bootstrap runtime** | `src/NosAi.Runtime/Program.cs` | **Integrated** | **Parziale** | **Sì** | manca evidenza completa di esecuzione affidabile sul PC reale |
| **Protocollo Gate 1 PC ↔ smartphone** | `src/NosAi.Runtime/Gate1/Gate1Runtime.cs` | **Integrated** | **Parziale** | **Sì** | manca validazione end-to-end reale del trasporto e della sessione |
| **Test runner Gate 1** | `src/NosAi.Runtime/Gate1/Gate1TestRunner.cs` | **Present** | **No** | **Sì** | va verificato quanto copra il flusso reale e non solo invarianti locali |
| **Runtime snapshot provider** | `src/NosAi.Runtime/Gate1/Gate1Runtime.cs` | **Integrated** | **Parziale** | **Parziale** | espone stato utile ma include campi non ancora alimentati da provider reali completi |
| **Client connector NosTale** | `src/NosAi.Runtime/LiveIntegration/RealClientConnector.cs` | **Partial** | **Parziale** | **Non provato** | ora espone uno snapshot baseline strutturato, ma non legge ancora dati gameplay reali dal client |
| **Perception contracts** | `src/NosAi.Runtime/Perception/PerceptionContracts.cs` | **Present** | **No** | **Parziale** | il contratto esiste ma non basta senza backend produttivo |
| **Perception null provider** | `src/NosAi.Runtime/Perception/NullPerceptionProvider.cs` | **Partial** | **No** | **Parziale** | è utile come fallback tecnico ma non contribuisce al Gate 1 reale |
| **World state / world model** | area `src/NosAi.Runtime/WorldModel` | **Integrated** | **Parziale** | **Parziale** | dipende dal completamento delle sorgenti reali di input |
| **Guard AI smartphone** | area `src/NosAi.Runtime/Guard` + `Gate1Runtime.cs` | **Partial** | **Parziale** | **Parziale** | manca prova reale di handshake, auth, heartbeat e riconnessione sul dispositivo |
| **Dashboard embedded** | `src/NosAi.Runtime/Host/NosAiMasterRuntimeHost.cs` | **Partial** | **No / misto** | **Parziale** | parte della telemetria appare dimostrativa o simulata |
| **Control center HTTP** | `src/NosAi.Runtime/Host/NosAiMasterRuntimeHost.cs` | **Integrated** | **Parziale** | **Parziale** | va separata meglio la struttura reale dai dati dimostrativi |
| **Gateway eventi dashboard** | `src/NosAi.Runtime/Network/Gateway/ControlPanelGatewayEngine.cs` | **Integrated** | **No / misto** | **Sì** | l'infrastruttura esiste ma non dimostra ancora stream di dati reali del runtime |
| **Safety / trust boundary** | `src/NosAi.Runtime/Host/NosAiMasterRuntimeHost.cs`, `src/NosAi.Runtime/Contracts/RuntimeContracts.cs` | **Integrated** | **Parziale** | **Parziale** | necessita verifica con segnali reali e casi negativi reali |
| **Hardware profiling / autoset** | `src/NosAi.Runtime/Program.cs` + area `src/NosAi.Runtime/Hardware` | **Integrated** | **Parziale** | **Parziale** | manca prova documentata di acquisizione reale e benchmark utili nel Gate 1 |
| **Storage / session persistence** | area `src/NosAi.Runtime/Storage` | **Integrated** | **Parziale** | **Parziale** | va dimostrato l'uso reale lungo il percorso critico |
| **Logging / telemetria** | Host, Gateway, Gate1 snapshot | **Partial** | **Parziale** | **Parziale** | segnali presenti ma non ancora chiaramente autorevoli per il debug end-to-end |
| **Dashboard error/disconnect handling** | Host + Gateway + componenti UI embedded | **Partial** | **No / misto** | **Non provato** | mancano casi negativi provati e visualizzati in modo coerente |
| **Suite test Python legacy/ibrida** | `tests/` | **Partial** | **No / misto** | **Sì** | copertura abbondante ma non ancora chiaramente allineata al runtime C# reale |

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
