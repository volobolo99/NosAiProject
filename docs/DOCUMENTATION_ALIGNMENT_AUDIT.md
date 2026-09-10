# Audit documentale e firme contrattuali
Snapshot storico: 3e4a8c42d75a73ccf5c6b739705e6293b059827e. Riconciliazione aggiornata: 2026-09-10; non è un audit di esecuzione runtime.

## Disallineamenti e stato della correzione
1. **Risolto:** ROADMAP_ESECUTIVA usa AP-01 World Model, AP-02 Perception, AP-03 mappe;
   CONTRACT_MAP associa vecchi gate AP-01 cattura, AP-02 dispatcher, AP-03 decisione.
   CONTRACT_MAP e ledger ora usano `product_phases`; gli agenti devono identificare i contratti tramite CID.
2. **Risolto:** CONTRACT_MAP ha colonne Contratto/Stato ma le righe mettono MERGED/DRAFT nella
   colonna Contratto e il titolo nella colonna Stato. Il ledger JSON è leggibile.
3. **Risolto nella documentazione di ingresso:** EXECUTION_QUEUE include istruzioni storiche per Claude che implementa A1/A3,
   mentre .claude/CLAUDE.md descrive Claude come coordinatore che non implementa.
   Risolvere precedenza delle istruzioni applicabili prima di assegnare lavoro.
4. **Risolto:** ROADMAP_ESECUTIVA e profilo hardware indicano Acer Nitro V16 AI e SSD esterno a capacità rilevata; Ryzen 7 260 è registrato come baseline confermata. La specifica hardware
   fornita dall'operatore in conversazione è Acer Nitro V16 AI, Ryzen 7 260, RTX 5060,
   RAM 16 GB e SSD 1024 GB. Non convertire il profilo documentale in valori rilevati:
   riconciliare hardware tramite AutoSet e decisione canonica.
5. **Risolto:** SYSTEM_MAP non contiene più sequenze letterali backslash-n nelle voci Python.
6. **Risolto nella documentazione:** le descrizioni di watchdog permanente, audit indipendente e rollback
   non sono certificate dai soli cataloghi, flag booleani o endpoint su richiesta.
   RESEARCH_LAB_SPEC e LAB-01..07 distinguono correttamente progettato da verificato.
7. **Risolto per lo snapshot corrente:** l’inventario ora riporta la revisione Git e 1.533 file; la rigenerazione automatica resta consigliata.
   Usare un tree Git fissato, come FUNCTION_INDEX.json, per contare i sorgenti.

## Sedici contratti senza firma precisa nel ledger
| CID | Firma corrente | Bersaglio |
|---|---|---|
| C-103 | vedi il file: superficie pubblica gia' in uso dai test | src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs |
| C-104 | da confermare sul file prima di modificarlo | src/NosAi.Protocol/WireProtocol.cs |
| C-105 | da definire | da definire |
| C-106 | da confermare sul file | src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs |
| C-201 | da confermare | da confermare fra i 16 sorgenti che citano dispatch |
| C-202 | da confermare | src/NosAi.Core/WorldModel/ |
| C-203 | da confermare sul file | src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs |
| C-204 | da definire | da definire |
| C-301 | da confermare | da confermare fra i 4 sorgenti che citano HTN |
| C-302 | da confermare | da confermare fra i 5 sorgenti che citano GOAP |
| C-303 | da confermare | da confermare fra i 30 sorgenti che citano Orchestrator |
| C-304 | da definire | da definire |
| C-305 | da confermare | da confermare fra i 44 sorgenti che citano recovery o reconnect |
| C-401 | da confermare sul file | src/NosAi.Protocol/SessionCipher.cs |
| C-402 | da definire | da definire |
| C-403 | da confermare | src/NosAi.Security/ |

C-103/104/106/203/401 hanno un file specifico: consultare l'indice per estrarre
la superficie pertinente, poi scegliere quali firme fanno parte del contratto.
C-202/403 indicano directory: serve definire il perimetro pubblico.
C-201/301/302/303/305 devono prima identificare l'implementazione canonica.
C-105/204/304/402 richiedono decisioni o nuovi contratti prima del codice.
C-003/004/005 hanno già firme proposte, ma non equivalgono a implementazioni verificate.
C-404 è una procedura di rilascio con firma n/a, non una funzione mancante.
Anche le firme già compilate vanno confrontate col codice prima di modificarle.

## Limiti dell'indice
FUNCTION_INDEX.json contiene copertura per 1.098 sorgenti e 12.599 voci sintattiche
ripartite in 29 shard. Quattro file hanno errori parser: non certificarne completezza.
- scripts/windows/nosai_bootstrap.ps1
- src/NosAi.Runtime/Perception/DxgiInterop.cs
- third_party/sources/opennos/reference/LoginPacketHandler.cs
- tools/find-vitals.ps1

Sono incluse definizioni private, test, sorgenti esterni, lambda e accessori.
Non è un call graph né include funzioni generate a runtime o varianti del preprocessore.
La copertura completa dei file non garantisce riconoscimento di ogni costrutto linguistico.
Nessun contratto è promosso a VERIFIED con questo audit.

## Esito

La documentazione è stata riallineata e il profilo Acer è stato registrato.
Le firme contrattuali non sono state inventate: restano task espliciti in
CONTRACT_SIGNATURE_TASKS.md. L’indice delle funzioni è stato rigenerato sullo snapshot
Git e include la copertura dichiarata; non certifica compilazione o comportamento.
