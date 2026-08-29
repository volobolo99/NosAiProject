# NosAi — Piano operativo

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Scopo

Questo documento traduce la roadmap generale del progetto in un piano operativo eseguibile, con priorità, criteri di maturità, deliverable e ordine di esecuzione.

La regola centrale è semplice:

**prima rendere reale il flusso minimo, poi consolidare, poi espandere.**

---

## Principio di avanzamento

Il progetto non avanza sulla base del numero di file o della quantità di moduli presenti nel repository.

Avanza solo quando un blocco è:

1. **presente** nel codice;
2. **integrato** nel flusso corretto;
3. **verificato** con test credibili;
4. **operativo** nel sistema reale quando pertinente.

Ogni implementazione futura deve essere classificata secondo questi quattro livelli.

---

## Livelli di maturità

| Livello | Significato |
|---|---|
| **Present** | Il codice o il contratto esiste nel repository. |
| **Partial** | Il blocco esiste ma è incompleto, simulato o non sufficientemente collegato. |
| **Integrated** | Il blocco è collegato ad altri componenti rilevanti del runtime. |
| **Verified** | Il blocco è coperto da test credibili e da evidenza di esecuzione pertinente. |
| **Operational** | Il blocco è confermato nel flusso reale previsto dal progetto. |

Questi livelli vanno usati nei documenti di stato, nei gate e nelle revisioni tecniche.

---

## Priorità assoluta

La priorità assoluta del progetto è il **superamento reale del Gate 1**.

Fino a quel momento:

- non si aggiungono nuove espansioni non necessarie;
- non si considera maturo alcun gate successivo sul piano operativo;
- non si confonde la presenza del codice con il completamento del progetto.

Il circuito minimo da chiudere è:

**PC runtime ↔ client NosTale ↔ dati minimi validi ↔ Guard AI smartphone ↔ dashboard coerente**

---

## Fase A — Audit tecnico operativo

### Obiettivo

Stabilire lo stato reale di ogni modulo critico e distinguere chiaramente tra codice presente, integrazione reale, simulazione e verifica.

### Deliverable

- classificazione per maturità dei moduli principali;
- identificazione dei mock, placeholder e dati simulati;
- elenco dei blocchi che partecipano davvero al percorso Gate 1;
- riallineamento tra documentazione e stato osservabile del codice.

### Aree da classificare

- bootstrap runtime
- Gate 1 protocol/runtime
- collegamento client NosTale
- Guard AI smartphone
- dashboard/control center
- world state
- perception
- adapters live
- navigation/pathfinding
- economy/inventory
- safety/trust
- storage/sqlite
- telemetry/observability
- hardware profiling/autoscale
- provider AI locali/cloud
- miniland
- raids/events
- test suite
- documentazione

### Criterio di completamento

La fase è completa quando ogni area ha:

- un livello di maturità assegnato;
- una breve evidenza;
- un blocco principale identificato;
- una priorità dichiarata.

---

## Fase B — Chiusura del Gate 1

### Obiettivo

Rendere reale, testabile e ripetibile il primo percorso operativo minimo del progetto.

### Definizione di done

Il Gate 1 è considerato superato solo quando esistono prove ripetibili di:

1. avvio affidabile del runtime sul PC;
2. collegamento controllato al client NosTale;
3. acquisizione di dati minimi reali e verificabili;
4. acquisizione dei dati di base del PC nel runtime;
5. avvio e connessione reale di Guard AI smartphone;
6. autenticazione reale della sessione PC ↔ smartphone;
7. scambio reale dei messaggi minimi previsti;
8. dashboard coerente con i dati realmente disponibili;
9. gestione corretta di errore, disconnessione e riconnessione.

### Sotto-obiettivi tecnici

#### Runtime PC

- bootstrap senza crash;
- configurazione valida;
- logging utile;
- safety policy attive;
- session state osservabile.

#### Client NosTale

- rilevamento client;
- lettura del dataset minimo;
- validazione provenienza/correttezza/freschezza;
- gestione client assente o dati incompleti.

#### Guard AI smartphone

- connessione reale;
- handshake reale;
- autenticazione reale;
- heartbeat reale;
- riconnessione controllata.

#### Dashboard

- sola visualizzazione di stato reale;
- indicatori coerenti con il runtime;
- nessun dato demo presentato come dato reale;
- error handling chiaro.

---

## Fase C — Consolidamento

### Obiettivo

Dopo la chiusura del Gate 1, trasformare il nucleo del sistema da dimostrabile a stabile.

### Attività principali

- rimozione dei mock non più necessari;
- isolamento dei placeholder ancora utili;
- uniformazione di naming, contratti e modelli dati;
- riduzione delle duplicazioni;
- rafforzamento dell'osservabilità;
- allineamento tra test, documentazione e comportamento reale.

### Regola operativa

Ogni componente oggi simulato deve diventare una di queste tre cose:

- **reale**;
- **esplicitamente mock**;
- **rimosso**.

La zona grigia non è ammessa nei componenti del percorso critico.

---

## Fase D — Integrazione dei moduli avanzati

Questa fase si apre solo dopo il superamento reale del Gate 1.

### Ordine consigliato

1. perception produttiva minima;
2. osservabilità e audit/replay durevoli;
3. persistenza completa e canali IPC maturi;
4. navigation/pathfinding con dati reali;
5. economy/inventory con dati reali;
6. miniland live;
7. raids/events;
8. provider locali/cloud produttivi;
9. validazione operativa dei gate successivi.

---

## Backlog prioritario

## P0 — Bloccanti assoluti

- classificare lo stato reale dei moduli;
- identificare dove il runtime usa dati simulati;
- verificare la catena reale del connettore NosTale;
- verificare il percorso completo di handshake PC ↔ smartphone;
- definire il dataset minimo canonico del Gate 1;
- definire i test autorevoli del Gate 1.

## P1 — Necessari subito dopo

- riallineare documentazione e stato del codice;
- separare test legacy da test correnti;
- rendere osservabile il flusso end-to-end;
- introdurre criteri pass/fail netti per il gate;
- classificare i moduli avanzati con maturità reale.

## P2 — Dopo stabilizzazione

- integrare navigation con dati reali;
- integrare economy con dati reali;
- completare la perception produttiva;
- completare provider AI reali;
- consolidare la dashboard operativa.

---

## Regole di esecuzione del lavoro

Ogni task operativo deve riportare sempre:

1. **scopo**;
2. **input richiesti**;
3. **output atteso**;
4. **criterio di successo**;
5. **rischio se fallisce**.

Ogni modulo critico deve rispondere a quattro domande:

1. da dove arrivano i dati;
2. come vengono validati;
3. chi li consuma;
4. come si prova che funzionano.

Se una di queste quattro risposte manca, il modulo non è maturo.

---

## Cose da non fare prima del Gate 1

Prima della chiusura del Gate 1 vanno evitate:

- nuove espansioni di gameplay non necessarie;
- aumento della superficie della dashboard senza utilità operativa;
- introduzione di nuova logica avanzata sopra dati non reali;
- dichiarazioni di maturità non supportate da prove reali.

---

## Output attesi della fase corrente

L'esecuzione corrente del progetto deve produrre nell'ordine:

1. audit tecnico dei moduli critici;
2. checklist eseguibile del Gate 1;
3. test minimi autorevoli del Gate 1;
4. correzione dei punti che bloccano il primo circuito reale;
5. rivalutazione formale dello stato del progetto.

---

## Sintesi finale

NosAi non ha bisogno prima di diventare più grande.

Ha bisogno prima di diventare **reale, osservabile e verificabile** nel suo primo circuito operativo.

Questo piano operativo è il riferimento da seguire per tutte le decisioni tecniche fino al superamento del Gate 1.
