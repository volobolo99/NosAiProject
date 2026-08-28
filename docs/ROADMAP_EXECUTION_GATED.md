# NosAiProject — Roadmap esecutiva a fasi con gate obbligatori

**Versione progetto:** 1.0 Beta  
**Regola assoluta:** una fase non è completata e la successiva non può iniziare finché tutti i gate applicabili sono PASS.

## Gate globale

Per ogni fase che modifica runtime, UI, protocolli o integrazioni:

- [ ] test automatici locali/CI PASS;
- [ ] build/compile PC PASS;
- [ ] test funzionale su PC PASS;
- [ ] test funzionale su smartphone PASS per dashboard/UI/API accessibili da smartphone;
- [ ] nessun `NOT_RUN`, `PARTIAL` o `FAIL` su un test richiesto dalla fase;
- [ ] evidenza con commit e timestamp registrata;
- [ ] regressione delle funzionalità precedenti PASS.

**Regola di blocco:** se PC o smartphone fallisce, la fase resta BLOCCATA. Non si implementano funzionalità della fase successiva per aggirare il problema.

## Fase 1 — Fondazione verificabile + importazione selettiva

### Lavori eseguiti insieme

- analisi comparativa `NosAi` vs `NosAiProject`;
- importazione solo di componenti legacy non conflittuali;
- consolidamento del modello replay/evidence;
- definizione dei gate PC/smartphone;
- verifica e pulizia CI;
- test automatici del nuovo codice;
- documentazione della provenienza e delle decisioni architetturali.

### Gate di uscita

Tutto quanto sopra PASS, più verifica PC e smartphone.

**Stato:** IN CORSO / BLOCCATA fino alla validazione reale PC + smartphone.

## Fase 2 — Runtime cognitivo integrato + percezione + dashboard + osservabilità

### Lavori eseguiti insieme

- WorldState canonico;
- percezione e normalizzazione;
- brain/decision provider;
- planner e tactical ranking;
- memory/replay/evidence;
- EventBus e trace;
- REST/WebSocket;
- dashboard Control Center, Brain, Memory, Runtime e Test Center;
- diagnostica e metriche;
- suite unit/contract/integration/regression.

Nessuna azione live viene abilitata in questa fase.

### Gate di uscita

CI + PC + smartphone + regressione completa PASS.

## Fase 3 — Validazione reale PC/Smartphone e integrazione client observation-only

### Lavori eseguiti insieme

- bring-up Windows/PC;
- acquisizione osservativa del client;
- normalizzazione dello stato reale;
- screenshot/perception regression corpus;
- diagnostica hardware/runtime;
- dashboard con dati reali;
- stress test e stabilità;
- recovery/watchdog sotto condizioni controllate.

Il client rimane observation-only.

### Gate di uscita

- PC reale PASS;
- smartphone PASS;
- osservazione client PASS;
- stato normalizzato PASS;
- recovery/watchdog PASS;
- nessuna regressione.

## Fase 4 — Azione live separatamente autorizzata + hardening finale

Questa è l'unica fase in cui si valuta il trasporto di azioni reali.

### Lavori eseguiti insieme

- action contract;
- dry-run;
- Safety/Guard/Trust;
- autorizzazione esplicita;
- executor tramite adapter;
- verifica risultato -> nuova osservazione;
- recovery/replan;
- audit trail;
- security/SBOM;
- benchmark e performance;
- release checklist.

### Gate assoluto

Non parte se **anche uno solo** dei gate precedenti non è PASS. In particolare, PC e smartphone devono essere già stati validati positivamente nelle fasi precedenti.

## Politica di avanzamento

L'ordine operativo è quindi:

```text
FASE 1
  ↓ PASS automatici + PC + smartphone
FASE 2
  ↓ PASS automatici + PC + smartphone
FASE 3
  ↓ PASS automatici + PC + smartphone + observation-only
FASE 4
  ↓ PASS completo + autorizzazione esplicita
RELEASE
```

Un errore riporta il progetto al gate minimo necessario; non si considera completata una fase solo perché il codice è stato scritto.
