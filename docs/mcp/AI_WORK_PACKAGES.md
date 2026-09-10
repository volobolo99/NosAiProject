# Pacchetti di programmazione
Stato iniziale di tutti i pacchetti: PLANNED.
Fonte delle dipendenze: contracts/mcp-research-lab-001.json.
I nomi nuovi di servizi qui sotto sono responsabilità proposte, non API esistenti.

## LAB-01 — Evidenze affidabili
Leggere bindings.py:RoleBindingRegistry.promote; chief.py:McpChief.promote_binding;
server.py:mcp_role_promote_binding; dashboard.py:promote_role_binding.
Separare ricevute di esecuzione da richieste del browser. Conservare evidenze in
archivio controllato dal valutatore; legarle al digest del candidato e alla suite.
Determinare identità autenticata dal canale, mai da executor_id dichiarato.
Bloccare promozioni senza evidenza verificabile; definire migrazione dell'API.
Prove: flag tutti true senza ricevuta rifiutati; ricevuta altra revisione/scaduta/
altro autore rifiutata; evidenza valida promuove una sola volta.
Consegna: firma del verificatore, confine di autorizzazione, adattatori API e test.

## LAB-02 — Stato condiviso
Dipendenza LAB-01. Leggere bindings.py e router.py:ModelRouter.choose.
Introdurre repository transazionale locale, migrazione JSON con backup e rollback.
Preservare proposal_id e cronologia dopo restart; expected_revision obbligatoria
per mutazioni. In caso di errore disco non lasciare stato RAM promosso.
Prove con processi distinti: revisione visibile, scrittura obsoleta rifiutata,
crash durante transazione, doppia promozione e rollback dopo riavvio.
Consegna: schema storage versionato, migrazione idempotente, test multiprocesso.

## LAB-03 — Scheduler e salute
Dipendenza LAB-02. Leggere chief.py, config.py, policy.py e entrypoint MCP.
Persistenza incarichi con lease/fencing; supervisore esterno e restart con backoff.
Distinguere configurazione, raggiungibilità e inferenza valida. UNKNOWN senza misura.
Clock iniettabile per test; scadenze persistite con semantica esplicita.
Prove: crash/ripresa senza doppio effetto, lease vecchia rifiutata, pausa/cancellazione,
rete spenta senza chiamate remote, stop su pressione risorse.
Consegna: lifecycle servizio, configurazione, API stato e runbook avvio/arresto.

## LAB-04 — Ruoli e worker
Dipendenza LAB-02. Leggere roles.py, router.py, inference.py e schema agent_message.
Versionare separatamente ruolo/binding. Worker acquisisce snapshot all'inizio.
Un pool per provider/modello serve contesti separati, senza condividere conversazioni.
Ritiro ruolo conserva memoria e assegna responsabilità pendenti; niente ruoli orfani.
Prove: binding cambiato durante task, due ruoli stesso provider, quote comuni,
timeout, ciclo di delega e migrazione memoria. Nessuna qualifica dal solo model_id.
Consegna: contratti di assegnazione, pool, versionamento e matrice responsabilità.

## LAB-05 — Esperimenti
Dipendenze LAB-01/03/04. Leggere learning.py e simulation.py.
Pipeline ricerca con fonti/versioni/licenze, ipotesi, baseline, esperimento,
valutazione separata e conservazione anche dei fallimenti.
Dataset di ottimizzazione e valutazione distinti. Metriche con unità/direzione,
campioni e incertezza. Nessun miglioramento universale da una singola prova.
Prove: fonte non verificata in quarantena, dataset contaminato rifiutato,
candidato peggiore respinto, offline skill revocabile e carico ceduto al gameplay.
Consegna: adattatori ricerca, esperimenti riproducibili e report comparativo.

## LAB-06 — Rilasci
Dipendenza LAB-05. Leggere chief.py, director.py, auditor.py e server.py.
Mandato preapprovato verificato da servizio esterno; checkpoint compatibile.
Deprecazione prima di rimozione; dipendenze/consumatori e chiamate in corso censiti.
Canary e rollback su regressioni; notifiche catalogo quando supportate.
Prove: fuori mandato rifiutato, permessi non autoespandibili, strumenti usati non
rimossi, riavvio durante rilascio, rollback coerente con schema dati.
Consegna: protocollo rilascio, migrazione/ritiro, evidenze end-to-end.

## LAB-07 — Pannello e certificazione
Dipendenza LAB-06. Leggere dashboard.py, static/index.html e schema stato.
Mostrare mandato, salute osservata, ruoli, worker, ricerche, esperimenti, release,
benefici misurati e rollback. Tutti i comandi usano servizi canonici.
Niente evidenza di audit costruita nel browser; errori visibili, nessun falso successo.
Prove HTTP/UI su processo reale: disconnessione, conflitto revisione, pausa/ripresa,
restart, UNKNOWN, flussi offline e online autorizzati. Verifica sul PC Windows.
Consegna: pannello collegato, CI/report Windows e limiti espliciti.

## Test comuni
Eseguire prima i test mirati già esistenti:
python -m pytest -q tests/test_mcp_chief.py tests/test_mcp_role_bindings.py
Aggiungere i test mancanti nel perimetro assegnato dal coordinatore.
Eseguire compileall sui moduli modificati. Dopo integrazione ripetere suite MCP
e controlli configurati in CI; registrare comando esatto e commit.
Un test sul solo dizionario di flag non prova indipendenza, disponibilità o rollback.
