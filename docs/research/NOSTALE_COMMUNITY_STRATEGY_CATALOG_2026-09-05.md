# NosAiProject — NosTale Community Strategy Catalog

**Date:** 2026-09-05  
**Status:** RESEARCH / CANDIDATE KNOWLEDGE  
**Purpose:** trasformare tattiche pubblicamente condivise dalla community in conoscenza strutturata, senza trattarle come verità fino alla verifica nel ruleset del test server.

## 1. Regola di classificazione

Le informazioni provenienti da forum, Reddit, guide e community sono **candidate knowledge**. Non diventano `Verified` solo perché molte persone le ripetono.

Lifecycle:

`ExternalSource → Candidate → Tested → Validated → Verified → RevalidationRequired → Deprecated`

Ogni record deve mantenere source, data, ruleset/server profile, evidenza osservata, confidence e numero di campioni.

## 2. Strategie e conoscenze candidate

### Hidden Time-Spaces / TSO
- I TSO non sono sempre visibili sulla minimappa.
- La community descrive l'uso delle bacchette/rabdomanti per localizzare il campo energetico e poi creare il TS tramite la pietra appropriata.
- Sono riportati anche problemi/bug storici delle bacchette e relative workaround, che devono essere trattati come version-specific e non come verità universale.
- La conoscenza utile per NosAi è il **pattern di ricerca**: osservare segnali di direzione/distanza, aggiornare la posizione stimata, restringere l'area e verificare il campo prima dell'interazione.
- Fonte: forum ufficiale, FAQ TS/Raid e guide TSO. https://forum.nostale.gameforge.com/forum/thread/447-preguntas-frecuentes-sobre-ts-y-raid/ 

### Lure / mob grouping
- Le guide della community descrivono il `lure` come raggruppamento dei mob per sfruttare attacchi ad area.
- Deve essere modellato come strategia contestuale: densità mob, AoE disponibile, rischio, resistenze, HP/MP, via di fuga e valore XP/min.
- Non deve diventare una macro fissa.
- Fonte: guida community sul leveling. https://forum.nostale.gameforge.com/forum/thread/552-c%C3%B3mo-y-d%C3%B3nde-levear/

### Desert Robber / Ibrahim
- La community segnala ruoli distinti tank/DPS/debuffer/support.
- Per raggiungere il boss: evitare bottoni falsi che possono generare gruppi di ladri; completare i miniboss; nel boss room concentrare l'obiettivo su Ibrahim invece di ingaggiare inutilmente tutta la stanza.
- Strategia candidata: `minimize unnecessary encounters` + tank positioning + role-aware combat.
- Fonte: guida ufficiale-community del forum. https://forum.nostale.gameforge.com/forum/thread/362-desert-robber-band-raid-guide/

### Fafnir
- La guida community documenta preparazione specifica: equipaggiamento/consumabili, gestione del danno percentuale, posizione nel boss room e comportamento rispetto alle meccaniche del drago.
- Riporta inoltre condizioni che possono produrre risultati anomali e interazioni particolari con pet/partner.
- NosAi deve registrare questi casi come `ObservedMechanicCandidate`, non come exploit da usare.
- Fonte: https://forum.nostale.gameforge.com/forum/thread/398-raid-fafnir-el-codicioso/

### SP8 / Caligor preparation
- Guide community riportano materiali necessari, fonti di farm, consumabili, resistenze e tattiche di gestione del Caligor.
- Particolarmente utile il concetto di **preparation gate**: non entrare nel contenuto se il costo/rischio previsto supera la capacità del personaggio.
- Fonte: https://forum.nostale.gameforge.com/forum/thread/506-gu%C3%ADa-para-el-raid-de-la-sp8/

### Diablillas
- La community documenta ruoli, gestione delle calabazas e uso dei potaggi/stun, oltre a specifiche disposizioni del party.
- Sono strategie dipendenti da patch e composizione del gruppo; devono quindi essere versionate.
- Fonte: https://forum.nostale.gameforge.com/forum/thread/517-raid-diablillas/

### Kertos / Valakus / Grenigas
- Guide community descrivono percorsi di lure, ordine degli obiettivi e posizionamento dei boss.
- Pattern generale da memorizzare: `clear corridor → isolate boss → kill in controlled area`, ma solo se verificato nel ruleset corrente.
- Fonte: https://forum.nostale.gameforge.com/forum/thread/524-raids-5.2-actualizaci%C3%B3n-por-mapas-caidos-y-forma-de-hacer-el-raid/

### Glace / Draco resource preparation
- Discussioni della community mostrano una pratica di preparazione delle risorse prima dei raid, inclusa raccolta o acquisto di materiali richiesti.
- Questo è direttamente rilevante per `AcquisitionPlanner`: confrontare farm time, market price e disponibilità prima di partire.
- Fonte: https://forum.nostale.gameforge.com/forum/thread/2809-raid-aiuto/

### Economia / Bazaar
- La community suggerisce di usare il Bazaar come riferimento di prezzo ma di non trattarlo come verità assoluta.
- Alcuni giocatori descrivono acquisto durante cali di prezzo e rivendita successiva; questa è una strategia economica speculativa, non una regola.
- Per NosAi deve diventare un modello di `ExpectedValue`, con rischio, capitale immobilizzato, liquidità e orizzonte temporale.
- Fonte: discussione Reddit su progressione/economia. https://www.reddit.com/r/nostale/comments/1litfsq/

### Damage/statistics discovery
- La community ha discusso del fatto che alcune formule di danno non siano pubblicate ufficialmente e che parte della conoscenza storica sia stata ricostruita empiricamente.
- Questo è un caso esemplare per NosAi: formulare ipotesi, raccogliere campioni controllati, stimare il modello e mantenere l'incertezza.
- Fonte: https://www.reddit.com/r/nostale/comments/1jw6ri6/

### Farming routes / money making
- Discussioni community indicano attività differenti per produrre oro a diversi livelli: Instant Combat, farming di materiali, Time-Spaces, raccolta, raid e combinazioni di equipaggiamento.
- Non salvare la lista come ranking universale: usarla come seed per `AcquisitionPlanner` e rivalutarla per livello, classe, costo opportunità e ruleset.
- Fonte: https://www.reddit.com/r/nostale/comments/1am8coz/

## 3. Meta-knowledge da incorporare nell'AI

### A. Strategie condizionali
Una strategia deve essere indicizzata da:
- classe;
- SP;
- livello;
- equipaggiamento;
- mappa;
- tipo di mob/boss;
- party size/composizione;
- HP/MP;
- cooldown;
- risorse disponibili;
- quest/obiettivo;
- ruleset/versione.

### B. Preparazione prima dell'azione
Prima di un contenuto costoso:

`Goal → Requirements → Inventory → MissingResources → Farm/Buy evaluation → Risk → Execute`

### C. Evitare lavoro inutile
Molte guide community ottimizzano non solo il DPS ma il fatto di **non combattere ciò che non serve**, evitare stanze inutili, isolare boss e preparare in anticipo i consumabili.

### D. Scoperta empirica
Quando una formula o meccanica non è documentata:

`Hypothesis → ControlledObservation → SampleCollection → Model → Confidence → Validation`

## 4. Regole di sicurezza e accesso

Questo catalogo contiene soltanto conoscenza pubblicamente discussa. NosAi può usarla come conoscenza strategica, ma durante il gameplay deve rispettare il confine non privilegiato del progetto: client-visible network, memoria del client legittimamente leggibile, screen/pixel/OCR/CV, telemetria locale e input ordinario.

Non trasformare bug, exploit, packet injection, server manipulation o informazioni amministrative in una strategia di gameplay. Un comportamento osservato che richieda accesso privilegiato deve essere marcato `ForbiddenKnowledge` e non può entrare nel planner.

## 5. Stato della ricerca

Questa prima raccolta non pretende di essere esaustiva. È un seed versionato per il Knowledge Ingestion Engine. Il motore dovrà poter aggiungere nuove fonti e nuove categorie senza modificare lo schema centrale.

## 6. Fonti principali consultate

- Forum ufficiale NosTale — guide community/official team.
- Reddit r/nostale — discussioni community usate come corroborazione e scoperta di conoscenza empirica.
- Patch notes ufficiali 2026 per verificare che le strategie siano soggette a cambiamenti.

**Nota:** le fonti community possono contenere errori, informazioni obsolete o strategie legate a un server specifico. Il loro valore per NosAi è principalmente quello di generare ipotesi e candidati da verificare.
