# Protocollo di consegna per tutti gli agenti NosAi
Estende docs/AGENT_COORDINATION.md con istruzioni di lettura e consegna operative.

## Preparazione del coordinatore
Registrare commit di base, task Q/CID, AP canonica, obiettivo, owner, revisore,
file in scrittura, file in sola lettura, dipendenze, firme e criteri di accettazione.
Usare schemas/agent_message.schema.json senza campi arbitrari.
Il modello deve appartenere ai valori ammessi dallo schema corrente; un futuro
binding dinamico non autorizza modifiche implicite al protocollo di sviluppo.
Non inviare la conversazione completa: contratto, estratti pertinenti e ultimo
errore riproducibile bastano. Allegare hash/riferimenti agli artefatti grandi.

## Contratto prima del codice
Per ogni funzione pubblica nuova o cambiata specificare firma, tipi/nullabilità,
unità, pre/postcondizioni, errori, side effect, autorità, concorrenza, timeout,
cancellazione, compatibilità, limiti risorse e test.
I campi ancora non definiti sono decisioni da risolvere, non libertà di inventare API.
Per C#/Python definire la proprietà dello stato e il formato wire prima del bridge.
Il programmatore conserva i confini contrattuali e implementa solo file assegnati.

## Collaudo per dominio
| Dominio | Evidenza minima |
|---|---|
| Hardware/AutoSet | primo avvio, riuso profilo e cambio dispositivo |
| Capture/Perception | replay noto, input corrotto, backpressure e misura assente |
| World Model | fusione conflittuale, freshness, identità e UNKNOWN |
| Mappe/navigation | aggiornamento persistente, ostacoli, stuck e replan |
| Combat/quest/build | pre/postcondizioni, risorse e fallimento recuperabile |
| Planner/adapter | gate obbligatori, cancellazione, receipt e verifica azione |
| Storage/learning | restart, migrazione, provenienza e revoca |
| Pannello/GuardAi | dati reali, perdita canale, riconnessione e assenza dipendenze live |
| MCP/Lab | evidenze autentiche, processi distinti, offline e rollback |
| Release | build/test del commit finale e scenario Windows |

Ricavare comandi esatti da build/CI e contratto del checkout.
Non attribuire PASS a test non eseguiti o a smoke test che non coprono il requisito.
Un mock non prova il funzionamento su hardware o provider reale.
Non ripetere tutta la suite per modifiche solo documentali.

## Handoff e integrazione
Report per task con commit base/finale, file e motivi, CID, comandi/exit code,
artefatti, difetti residui, migrazione/rollback e successore.
Aggiornare summary nel messaggio con il riferimento al report; confidence non
sostituisce verifica. Il revisore controlla diff e prove dal candidato reale.
Un solo integratore modifica file condivisi, coda e ledger.
Parallelizzare solo file disgiunti e dipendenze soddisfatte.
Dopo merge ripetere i controlli interessati sul commit integrato.
Registrare documentazione consegnata separatamente da runtime verificato.
Non riscrivere report storici per farli apparire riferiti al nuovo commit.
