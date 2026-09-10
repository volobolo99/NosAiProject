# Contratti di integrazione per gli agenti
Stato: contratti parzialmente implementati; EvidenceAuthority e McpStateStore sono presenti, mentre i pacchetti LAB-03..LAB-07 restano da implementare e verificare.
Fonte: contracts/mcp-research-lab-001.json e RESEARCH_LAB_SPEC.md.

## Comunicazioni
| Produttore → Consumatore | Dato | Autorità e comportamento |
|---|---|---|
| Pannello/Chief → servizio cambiamenti | ChangeRequest | verifica mandato/revisione; restituisce identificatore |
| Scheduler → worker | incarico + WorkerLease | worker vincolato a revisione e fencing token |
| Runner → archivio evidenze | EvidenceRecord | identità dal canale e artefatti verificati |
| Valutatore → servizio rilascio | verdetto riferito alle evidenze | nessuna autoapprovazione |
| Servizio rilascio → router | binding/ruolo revisionati | nuovi task leggono la revisione confermata |
| Ricerca → esperimenti | fonti e ipotesi | dati esterni in quarantena |
| Valutatore → learning | candidato validato | provenienza, revoca e modalità offline |
| Supervisore → pannello | misura salute | timestamp/freshness, UNKNOWN se mancante |

## Tipi comuni
Identificatori: stringhe non vuote, generate dal servizio; riferimenti sempre risolti.
Revisioni: interi non negativi monotoni per risorsa, aggiornamento compare-and-swap.
Digest: SHA-256 esadecimale lowercase dei byte dell'artefatto immutabile;
per JSON usare serializzazione canonica scelta/versionata nel contratto.
Timestamp: UTC RFC3339; unità delle durate esplicita in millisecondi.
Un dato assente non equivale a zero o falso.
Enum sconosciuti e campi inattesi rifiutati nelle richieste di mutazione.
Secret reference ammesso solo verso vault; nessun valore nelle evidenze.

## Semantica dei record
ChangeRequest.kind: add_feature, modify_feature, retire_feature, change_binding,
change_role, rollback. target_id riferisce una risorsa esistente salvo creazione.
EvidenceRecord.result: `ok|pass|passed|success|verified` per un esito accettabile;
`failed|inconclusive` non promuovono. observed_at non prova da solo freshness:
applicare versione suite, digest, scadenza e identità dell'esecutore.
Experiment.state usa il ciclo definito nella specifica; transizioni illegali rifiutate.
WorkerLease.fencing_token cresce a ogni riassegnazione; lease scaduta non scrive.
ReleaseDecision.evidence_ids riferisce evidenze archiviate, non payload del caller.
Il contratto corrente elenca campi minimi: non dichiararlo schema JSON completo.

## Errori di dominio proposti
INVALID_REQUEST: input non valido, nessuna mutazione.
NOT_FOUND: riferimento assente.
REVISION_CONFLICT: aggiornare snapshot prima di nuova decisione.
EVIDENCE_REJECTED: prove mancanti, incongruenti o non autorizzate.
POLICY_DENIED: fuori mandato o rete non autorizzata.
LEASE_EXPIRED: rifiutare effetti del vecchio worker.
DEPENDENCY_IN_USE: ritiro bloccato da consumatori.
UNAVAILABLE: operazione non eseguita oppure esito UNKNOWN da riconciliare.
Gli adattatori HTTP/MCP devono mantenere codice, operation_id e retryability,
senza esporre segreti o tracce interne. Mappatura HTTP definita nel contratto API.

## Idempotenza, crash e rollback
Ogni mutazione ha operation_id persistito e digest della richiesta.
Stessa chiave/stesso payload restituisce l'esito precedente; payload diverso conflitto.
Persistenza dell'esito e revisione nella stessa transazione quando possibile.
Effetti esterni richiedono outbox e riconciliazione: niente promessa exactly-once
da un semplice retry. Un timeout non certifica che l'effetto non sia avvenuto.
Rollback crea nuova revisione, non riscrive audit/storia. Migrazioni distruttive
richiedono piano di recupero collaudato.

## Compatibilità
Versionare schemi e API; registrare consumatori Python/C#/pannello interessati.
Non cambiare firme pubbliche in silenzio né introdurre una seconda autorità di stato.
Le identità dei dipendenti persistono anche cambiando modello.
Lo schema agent_message esistente resta valido; la sua futura estensione richiede
test di compatibilità e aggiornamento dei produttori/consumatori.

## Evidenza di accettazione
Per ogni requisito produrre test, comando, ambiente, commit e risultato.
Usare test di integrazione per confini di processo, storage, canali e UI.
Mock consentiti per prove unitarie; non usarli come prova di disponibilità del provider.
Registrare espressamente ciò che richiede rete autorizzata, GPU o Windows.
