# NosAi — Regole del progetto

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## 1. Governance

La versione corrente è **1.0 Beta**. Nessuna implementazione, rifattorizzazione, documentazione o automazione deve modificare la versione senza richiesta esplicita del creatore.

## 2. Lingua ufficiale della documentazione

Tutti i documenti del progetto devono essere scritti in **italiano**.

Sono ammesse parole, sigle e denominazioni in altre lingue esclusivamente quando almeno una delle seguenti condizioni è vera:

- sono identificatori di codice, nomi di classi, funzioni, variabili, moduli o file;
- sono nomi ufficiali di API, protocolli, librerie, standard o tecnologie;
- sono nomi propri o denominazioni ufficiali che non devono essere tradotti;
- la traduzione altererebbe il significato tecnico richiesto;
- il termine straniero è necessario per garantire interoperabilità o corrispondenza con un'interfaccia esterna.

Quando un termine tecnico può essere tradotto senza perdere precisione, deve essere preferita la forma italiana. Se un termine inglese è indispensabile, alla prima occorrenza deve essere spiegato in italiano quando questo migliora la comprensione.

## 3. Organizzazione della documentazione

Ogni documento deve avere una responsabilità chiara. Non devono esistere documenti duplicati o sostanzialmente sovrapposti.

`docs/ARCHITETTURA.md` è l'unico riferimento canonico per l'architettura del sistema.

La documentazione deve distinguere sempre tra:

- **implementato** — presente nel codice e verificabile;
- **fondazione** — presente parzialmente o predisposto, ma non completo per l'uso produttivo;
- **pianificato** — definito ma non ancora implementato.

## 4. Separazione delle responsabilità

- Percezione osserva e produce dati semantici.
- World Model possiede lo stato semantico canonico.
- Party, Pet e Partner sono modellati separatamente.
- Coordinated Action Manager propone azioni coordinate.
- Tactical Ranking ordina i candidati.
- Orchestrator coordina i moduli.
- Guard valuta il contesto operativo.
- Safety costituisce il confine finale per le azioni protette.
- I/O del client di gioco è isolato dietro adapter espliciti.
- I provider LLM forniscono dati decisionali e non eseguono direttamente il sistema.

## 5. Percorso deterministico

Il percorso critico deve poter essere testato senza client di gioco reale. La simulazione e il lookahead sono strumenti di validazione prima dell'esecuzione reale.

Percorso autorevole:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Planner → Guard → Trust → Safety → Execute → Verify → Re-observe`.

## 6. Recovery e Watchdog

RecoveryController e Watchdog sono componenti attivi del controllo runtime.

Recovery può cambiare strategia, ripianificare, effettuare retry, usare modalità degradate, attivare Cooling e riprendere l'esecuzione secondo policy e condizioni osservate.

Watchdog può adattare modalità runtime e budget operativi sulla base delle condizioni del runtime e dell'hardware.

La precedente regola che limitava Recovery e Watchdog esclusivamente a riduzione o blocco dell'esecuzione non fa più parte delle regole del progetto.

Recovery e Watchdog non acquisiscono automaticamente autorità di esecuzione né possono aumentare il livello Trust.

## 7. Sicurezza

Le azioni protette devono rispettare i confini Guard, Trust e Safety configurati.

Un risultato non verificato non deve essere considerato automaticamente riuscito. Una conoscenza non supportata da evidenza non deve essere promossa automaticamente a conoscenza verificata.

## 8. Percezione e integrazioni esterne

Le pipeline produttive di acquisizione, visione, OCR e tracciamento devono essere validate prima dell'uso reale.

Le integrazioni con client, smartphone, rete, provider AI, hardware e sistemi nativi devono essere isolate dietro contratti e adapter espliciti.

## 9. Persistenza

La persistenza analitica non sostituisce il WorldState canonico. EventBus, audit, replay e Knowledge Base devono mantenere separazione di responsabilità e provenienza dei dati.

## 10. Repository legacy

`volobolo99/NosAi` è esclusivamente un riferimento. Il codice deve essere analizzato e reimplementato selettivamente.

## 11. Test

Non si procede al traguardo successivo quando i test richiesti per il traguardo corrente non hanno dato esito positivo.

Le prestazioni dichiarate nelle specifiche sono obiettivi finché non sono state misurate e validate.

## 12. Integrità del repository

Non devono essere eliminati componenti tecnici solo perché non possono essere completati nell'ambiente corrente. Se una componente richiede sviluppo o validazione esterna, deve rimanere nel progetto, essere documentata e avere un punto di integrazione chiaro.
