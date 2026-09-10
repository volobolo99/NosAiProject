# Guida di programmazione per agenti — Chief e Research Lab

## Entrata unica
Questa guida accompagna docs/AGENT_COORDINATION.md per il sottosistema MCP.
Leggere prima AGENTS.md e .claude/CLAUDE.md del checkout corrente.
Specifiche: [RESEARCH_LAB_SPEC.md](RESEARCH_LAB_SPEC.md).
Lavori: [AI_WORK_PACKAGES.md](AI_WORK_PACKAGES.md).
Integrazione: [AI_INTEGRATION_CONTRACTS.md](AI_INTEGRATION_CONTRACTS.md).
Contratto DRAFT: ../../contracts/mcp-research-lab-001.json.
Messaggi pronti: [AI_TASK_PACKETS.json](AI_TASK_PACKETS.json).

## Avvio di un incarico
1. Registrare commit di base e stato del checkout. Non sovrascrivere lavoro altrui.
2. Scegliere il primo LAB con dipendenze verificate; leggere solo la sua sezione.
3. Verificare file reali e test; distinguere esistente, proposto e mancante.
4. Registrare ownership dei file. Il campo files dei messaggi indica il perimetro
   applicativo; nuovi test/file richiedono assegnazione esplicita del coordinatore.
5. Completare contratto locale con firme, tipi, errori, timeout e migrazione prima del codice.
6. Riprodurre il difetto o scrivere il test di accettazione, poi implementare.
7. Eseguire verifiche mirate, `python scripts/verify_mcp_contracts.py --strict` e
   l'indice funzioni strict quando si tocca tooling/parser.
8. Consegnare artefatti e record di evidenza al revisore; i booleani nel payload non
   valgono come prova.
9. Integrare solo dopo controllo delle dipendenze e test sul commit risultante.

## Ricerca breve
Usare simboli e percorsi, non numeri di riga che diventano obsoleti:
```bash
rg -n "class McpChief|class RoleBindingRegistry|def promote|def rollback" nosai/mcp
rg -n "mcp_role_promote_binding|promote_role_binding|def choose" nosai/mcp
rg -n "LAB-01|EvidenceRecord" contracts/mcp-research-lab-001.json docs/mcp
rg --files tests | rg "mcp|binding|chief"
python scripts/verify_mcp_contracts.py --strict
python scripts/build_function_index.py CHECKOUT COMMIT OUTPUT_DIRECTORY --strict
```
Non inviare l'intero repository al worker. Pacchetto minimo: commit, LAB, contratto,
sezione della specifica, file posseduti, test e precedente errore verificato.
Una confidence elevata non sostituisce risultati di esecuzione.

## Uso dei messaggi JSON
AI_TASK_PACKETS.json contiene messaggi pending conformi allo schema attuale
schemas/agent_message.schema.json. I modelli sono assegnazioni iniziali di sviluppo,
non certificazioni né configurazioni attive dei dipendenti.
Il coordinatore completa il commit e le firme concordate nel campo input prima
dell'invio. I pacchetti con dipendenze incomplete non sono eseguibili. Le promozioni
dei binding usano `evidence_ids.tests`, `evidence_ids.shadow` e
`evidence_ids.audit`; i record devono essere firmati, freschi, digest-bound e
prodotti da executor distinti. Lo stato condiviso è `data/mcp/state.sqlite3`.
Lo schema attuale limita model a un enum: estenderlo con migrazione versionata
prima di inserire altri identificatori. Non aggirare la validazione.

## Consegna
Restituire il messaggio originale aggiornando status, summary e confidence.
In summary mettere riferimento al report contenente:
- commit iniziale/finale e task;
- file modificati, motivo e contratto interessato;
- comandi eseguiti, exit code e artefatti;
- prove positive e negative;
- cambiamenti di schema/configurazione, migrazione e rollback;
- limiti residui, dipendenze e prossimo incarico.
Non aggiungere campi arbitrari al messaggio: additionalProperties è false.
Risultati non eseguiti: dichiarare blocked o non verificato, mai PASS.

## Coordinatore e revisore
Il coordinatore possiede i file di integrazione condivisi e la coda.
Il revisore verifica le prove dal commit candidato, non dal riepilogo del worker.
Autore e valutatore devono essere identità separate applicate dal servizio.
LAB-03 e LAB-04 possono procedere dopo LAB-02 solo con ownership disgiunta.
Server, dashboard, contratti e migrazioni condivisi si integrano in sequenza.
Le autorizzazioni runtime del Chief non cambiano le regole di sviluppo del repository.

## Chiusura
Per ogni LAB: contratto completo, implementazione, test, revisione e documentazione.
Prima della consegna il verifier deve risultare `ok=true`; un fallimento CI o un
ambiente non disponibile resta esplicitamente `blocked`, non `PASS`.
La presenza di questi documenti non rende il Research Lab operativo.
Mantenere versione prodotto invariata. Aggiornare la coda e il registro lavori
senza modificare retroattivamente i report storici.
