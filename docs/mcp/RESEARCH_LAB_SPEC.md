# MCP Chief e NosAi Research Lab
Stato: specifica approvata dall'operatore; implementazione estesa da realizzare.
Versione specifica: 1.0.0. Versione prodotto invariata.
Contratto: contracts/mcp-research-lab-001.json.
Questa specifica governa l'evoluzione futura. Non certifica il runtime esistente.

## Obiettivo
Migliorare il motore principale e tutte le funzioni NosAi: percezione, world model,
pianificazione, simulazione, memoria, adattatori, GuardAi, pannello e MCP.
Misurare successo, correttezza, latenza, risorse e autonomia offline separatamente.
Nessuna promessa di perfezione o percentuale senza esperimenti.

## Organizzazione
Separare ruolo (mandato versionato), dipendente (identità e memoria), binding
(modello/provider/configurazione qualificati) e worker (istanza temporanea).
Il MCP Chief decide priorità e modifiche entro il mandato. Il laboratorio produce
ipotesi e candidati. Un valutatore indipendente giudica. Un servizio di rilascio
esterno applica autorizzazioni, revisioni e rollback.
Il Chief non può riscrivere valutatore, policy, autorizzazioni o test protetti.

## Autorità
| Operazione | Condizioni |
|---|---|
| Aggiungere/modificare funzioni | isolamento, dipendenze note, contratti, prove indipendenti e rilascio progressivo |
| Ritirare funzioni | deprecazione, disattivazione reversibile, drenaggio incarichi e periodo di osservazione |
| Eliminare codice | nessun consumatore attivo, migrazione validata, backup e autorizzazione prevista dal mandato |
| Cambiare binding | qualificazione della configurazione esatta, confini fra incarichi, rollback |
| Creare/unire/ritirare ruoli | copertura responsabilità e migrazione memoria/ownership senza perdita |
| Cambiare caratteristiche | versione nuova di istruzioni, competenze e strumenti; nessun aumento implicito dei permessi |
| Attivare multiagente | vantaggio misurabile, quote condivise, contesti separati |
| Rollback | automatico verso checkpoint verificato quando il mandato lo consente |
| Cambiare autorità, rete o budget | operatore; MCP Rete resta spento all'avvio |

Le operazioni reversibili nel mandato preapprovato non richiedono conferme ripetute.
Il runtime attuale continua a richiedere confirmation=operator: la nuova autonomia
non è attiva finché servizio di rilascio e prove indipendenti non sono implementati.

## Laboratorio: responsabilità attivabili
- Research Scout: fonti, data, versione, licenza, pertinenza e dipendenze.
- Diagnostic Analyst: incidenti, colli di bottiglia, funzioni inutilizzate.
- Experiment Designer: baseline, ipotesi falsificabile, metriche e dataset separati.
- Experiment Builder: implementazione isolata e riproducibile.
- Independent Evaluator: test nascosti al candidato, regressioni e confronto.
- Offline Curator: procedure/dati/skill validate con provenienza e possibilità di revoca.
Sono incarichi logici, non sei modelli obbligatoriamente residenti.

## Ciclo persistente
OBSERVED -> HYPOTHESIS -> PLANNED -> RUNNING -> EVALUATED -> AUDITED ->
CANARY -> PROMOTED oppure REJECTED / ROLLED_BACK.
PAUSED e CANCELLED sono stati espliciti. Ogni transizione è registrata.
Una ricerca fallita resta consultabile per evitare ripetizioni.
La ricerca parte da incidenti, regressioni, nuove versioni o scadenze pianificate.
Si ferma per assenza di beneficio, risorse insufficienti o limite del mandato.

## Stato e concorrenza
Usare un servizio locale unico con database transazionale SQLite per incarichi,
revisioni, evidenze e release. Dashboard e MCP interrogano la stessa autorità.
Migrare il JSON dei binding conservando backup, versioni e riferimenti rollback.
Ogni worker acquisisce lease con scadenza e fencing token; le operazioni esterne
usano chiavi di idempotenza. Un crash non deve duplicare promozioni.
Controllo ottimistico expected_revision contro aggiornamenti concorrenti.
Non basta os.replace di un file o un lock fra thread di un solo processo.

## Qualificazione e prove
Un risultato è collegato a candidate_digest, versione test, dataset_digest,
configurazione modello, ambiente, executor_id e artefatti con hash.
Il servizio verifica identità e autorizzazioni dell'esecutore; un campo
audit_approved=true inviato dal Chief o browser non è evidenza.
L'audit non è autoapprovazione. Due worker dello stesso modello non garantiscono
indipendenza degli errori. Controlli deterministici restano obbligatori.
Separare dataset di ottimizzazione e valutazione. Usare replay ripetuti e intervalli
di incertezza; evitare di selezionare sullo stesso campione usato per dichiarare successo.
Nessun punteggio aggregato può compensare violazioni dei contratti obbligatori.

## Modelli e multiagente
Un provider può servire molti dipendenti con memoria e contesti separati.
Riutilizzare il server di inferenza senza duplicare necessariamente i pesi.
Applicare limiti di concorrenza per provider e macchina, priorità gameplay,
timeout, cancellazione, backpressure e prevenzione cicli di delega.
Confrontare sempre con baseline singolo agente. Nessuna squadra permanente inutile.
Nuovi incarichi usano il binding promosso; gli incarichi in corso restano legati
alla revisione iniziale oppure vengono cancellati e ripianificati esplicitamente.

## Salute reale e ripristino
Distinguere configurato, raggiungibile, autenticato, qualificato e operativo.
Ogni misura ha timestamp, scadenza e UNKNOWN quando non disponibile.
Le prove online richiedono MCP Rete attivo e registrano eventuali costi.
Heartbeat del Chief controllato da supervisore deterministico esterno.
Riavvii con backoff e circuit breaker; il Chief non ripara il processo che lo ospita
senza un supervisore separato. Riprendere gli incarichi da checkpoint.
Nessun health check del solo catalogo prova che il server risponda.

## Rilasci e strumenti MCP
Versionare contratti e catalogo. Drenare chiamate in corso prima del ritiro.
Notificare cambiamenti con tools/list_changed quando supportato e rinegoziare
compatibilità; la notifica non risolve da sola migrazioni o dipendenze.
Canary del supporto asincrono, mai esperimenti sul percorso urgente di gioco.
Ripristinare insieme codice/configurazione/schema compatibili.

## Offline e risorse
Rete spenta: niente ricerca esterna né provider remoti; replay e ottimizzazione
locale continuano. Ricerca online autorizzata: fonti in quarantena, mai istruzioni
eseguibili implicitamente. Consolidare solo risultati validati.
Memoria e procedure non equivalgono a modifica dei pesi del modello.
Fine-tuning eventuale è esperimento separato con dati/licenze e regressioni.
Laboratorio a priorità inferiore al gameplay; quote CPU/RAM/VRAM adattate tramite
AutoSet, nessuna soglia hardware inventata. Pausa automatica su pressione risorse.

## Pannello
Sezioni: salute reale, mandato/autonomia, ruoli e binding, worker e code,
ricerche e fonti, esperimenti, evidenze, release e rollback, risultati offline.
Mostrare baseline/candidato, differenze, benefici misurati e non misurati,
timestamp, costi, errori e motivi di rifiuto. Nessun dato simulato presentato come reale.
Comandi pausa/riprendi/annulla e arresto del laboratorio indipendente dal gioco.
Le credenziali restano nel gestore dedicato; non entrano nel contesto del laboratorio.

## Riferimenti da valutare, non dipendenze importate
- https://github.com/microsoft/RD-Agent : ciclo R&D e valutazione.
- https://github.com/microsoft/agent-framework : workflow Python/.NET.
- https://github.com/langchain-ai/langgraph : checkpoint e ripresa.
- https://github.com/gepa-ai/gepa : evoluzione guidata da feedback.
- https://github.com/optuna/optuna : ottimizzazione/pruning.
- https://microsoft.github.io/autogen/dev/user-guide/agentchat-user-guide/tutorial/teams.html : costo/beneficio dei team.
- https://modelcontextprotocol.io/specification/2025-06-18/server/tools : catalogo dinamico.
Prima di riutilizzare codice fissare commit, verificare licenza e registrare provenienza.
Non installare tutti i framework: scegliere con uno spike compatibile col runtime.

## Mappa di implementazione
Seguire contracts/mcp-research-lab-001.json in ordine LAB-01..LAB-07.
File esistenti da integrare: nosai/mcp/chief.py, bindings.py, router.py, server.py,
dashboard.py, roles.py, learning.py e static/index.html.
Il contratto mcp-hub-001 resta quello del runtime; questo contratto estende la
progettazione senza dichiarare implementate API future.
