# NosAiProject — Regola Prodotto: Dashboard e Control Panel EXE

**Versione:** 1.0  
**Data:** 2026-09-05  
**Stato:** CANONICA  
**Priorità:** vincolo di prodotto

## Regola obbligatoria

NosAiProject deve prevedere un **file `.exe` Windows** che costituisce la **Dashboard + Control Panel ufficiale dell'AI**.

Il progetto può avere servizi, runtime, librerie, processi worker e componenti locali separati, ma l'esperienza operativa dell'utente deve essere concentrata in un'applicazione Windows avviabile tramite `.exe`.

La Dashboard/Control Panel sarà progettata e implementata in una fase successiva. Non è necessario anticiparne ora la UI.

## Responsabilità future

L'EXE dovrà diventare il punto operativo per:

- avvio/arresto controllato del runtime;
- stato del client e della sessione;
- stato dell'autonomous player;
- monitoraggio CPU/GPU/VRAM/RAM/temperature/SSD;
- stato della perception pipeline;
- World Model e qualità/provenance delle osservazioni;
- mappa e navigazione;
- combat/quest/character intelligence;
- memoria, learning e simulation status;
- Guard/Trust/Safety state;
- recovery/watchdog events;
- log, replay ed evidence;
- configurazione consentita all'operatore;
- diagnostica e health status.

## Vincoli architetturali

1. La Dashboard è una **superficie di controllo/operatività**, non può diventare un bypass della Safety Gate.
2. Nessun comando della Dashboard può concedere privilegi gameplay non disponibili al normale client.
3. Le azioni sensibili devono attraversare lo stesso percorso autorizzato del runtime:

   `Dashboard → Runtime Control API → Guard → Trust → Safety → Execute → Verify`

4. La Dashboard non deve diventare una fonte di gameplay truth: mostra dati osservati/derivati con provenance e stato di freshness.
5. Un dato `UNKNOWN` deve essere visualizzato come `UNKNOWN`, mai trasformato in un valore implicito.
6. Il Control Panel deve poter funzionare anche in modalità diagnostica quando il client non è collegato.
7. Il packaging finale deve produrre un artefatto `.exe` riproducibile per Windows.
8. La UI deve essere separata dalla logica core: nessuna business logic critica deve vivere esclusivamente nella Dashboard.
9. La chiusura/crash della Dashboard non deve automaticamente autorizzare o mantenere azioni gameplay non consentite; il runtime deve rimanere fail-closed secondo le proprie policy.
10. La Dashboard deve poter collegarsi al runtime tramite un contratto versionato e autenticato localmente.

## Packaging target

Il target finale è un'applicazione Windows distribuibile, con un `.exe` principale, supportata dai componenti runtime necessari.

La tecnologia UI sarà scelta più avanti sulla base di:

- compatibilità Windows;
- consumo RAM/VRAM;
- avvio rapido;
- accessibilità e qualità UX;
- facilità di packaging;
- integrazione con telemetry e runtime;
- manutenibilità a lungo termine.

Non assumere ora una tecnologia UI specifica come decisione architetturale definitiva.

## Relazione con la roadmap

Questa regola non modifica l'ordine delle fasi autonome. La Dashboard viene trattata come **Control Plane/UI futura**, mentre l'autonomous player deve continuare a essere sviluppato indipendentemente dalla UI.

La certificazione finale dovrà includere la presenza di un `.exe` Windows funzionante e la verifica che la Dashboard non possa bypassare Guard/Trust/Safety.
