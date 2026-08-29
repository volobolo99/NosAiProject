# NosAi — Checklist esecutiva Gate 1

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Scopo

Questa checklist definisce i requisiti eseguibili del primo gate operativo reale del progetto.

Il Gate 1 è superato solo quando tutti i punti pertinenti risultano completati con evidenza coerente.

---

## Stato di avanzamento

| Area | Punto | Stato | Evidenza attesa |
|---|---|---|---|
| Runtime PC | Avvio affidabile | [ ] | Il runtime parte senza crash e produce stato osservabile coerente |
| Runtime PC | Configurazione valida | [ ] | Configurazione caricata e validata senza fallback opachi |
| Runtime PC | Logging utile | [ ] | Log sufficienti per capire bootstrap, errori e sessione |
| Runtime PC | Safety policy attive | [ ] | Le policy rilevanti sono esposte e coerenti con lo stato reale |
| Runtime PC | Stato sessione osservabile | [ ] | Stato della sessione disponibile in modo leggibile |
| Client NosTale | Rilevamento client | [ ] | Il client viene rilevato in modo controllato |
| Client NosTale | Lettura dati minimi | [ ] | Almeno il dataset minimo canonico è letto dal client reale |
| Client NosTale | Validazione dati | [ ] | Provenienza, correttezza e freschezza dei dati sono verificabili |
| Client NosTale | Gestione client assente | [ ] | Il runtime non degrada in modo opaco quando il client manca |
| Guard AI smartphone | Avvio affidabile | [ ] | Guard AI è avviabile senza stato ambiguo |
| Guard AI smartphone | Connessione reale | [ ] | Il telefono raggiunge il runtime corretto |
| Guard AI smartphone | Autenticazione reale | [ ] | La sessione autenticata avviene con esito osservabile |
| Guard AI smartphone | Heartbeat reale | [ ] | Il watchdog di heartbeat entra nel flusso reale |
| Guard AI smartphone | Riconnessione controllata | [ ] | Il sistema gestisce disconnessione e ritorno del peer |
| Dashboard | Avvio affidabile | [ ] | La dashboard si apre in modo stabile |
| Dashboard | Connessione al runtime corretto | [ ] | Lo stato mostrato proviene dal runtime attivo |
| Dashboard | Dati reali soltanto | [ ] | Nessun dato demo viene esposto come dato reale |
| Dashboard | Coerenza degli stati | [ ] | PC, runtime, client e guard risultano coerenti |
| Dashboard | Error handling | [ ] | Gli stati di errore/disconnessione sono chiari |
| End-to-end | PC ↔ client | [ ] | Il flusso reale PC ↔ client è verificato |
| End-to-end | PC ↔ smartphone | [ ] | Il flusso reale PC ↔ smartphone è verificato |
| End-to-end | Runtime ↔ dashboard | [ ] | Il flusso reale runtime ↔ dashboard è verificato |
| End-to-end | Errore/disconnessione/riconnessione | [ ] | I casi negativi sono stati provati e documentati |
| Governance | Nessuna regressione bloccante | [ ] | I test pertinenti non mostrano regressioni critiche |
| Governance | Documentazione coerente | [ ] | La documentazione riflette il comportamento osservato |

---

## Dataset minimo canonico da acquisire

Il Gate 1 deve definire e rendere disponibile almeno un dataset minimo reale e verificabile.

### Dataset minimo richiesto

- stato del runtime;
- stato della sessione con Guard AI;
- stato del collegamento al client NosTale;
- primi dati di base del client ritenuti indispensabili dal progetto;
- primi dati di base del PC ritenuti indispensabili dal progetto;
- stato di sicurezza/autorizzazione rilevante per il livello corrente.

Se uno di questi elementi manca, il Gate 1 non è completo.

---

## Evidenze richieste

Ogni punto completato deve avere almeno una delle seguenti evidenze:

- test automatico pertinente;
- test di integrazione pertinente;
- log osservabile e ripetibile;
- output dashboard coerente;
- nota di validazione manuale chiaramente descritta.

Le dichiarazioni prive di evidenza non sono considerate completamento.

---

## Criterio formale di superamento

Il Gate 1 è superato solo se:

1. tutti i punti critici del runtime PC sono completati;
2. tutti i punti critici del collegamento client sono completati;
3. tutti i punti critici del collegamento smartphone sono completati;
4. la dashboard riflette solo stato reale e coerente;
5. i test end-to-end minimi hanno esito positivo;
6. i casi di errore e disconnessione hanno esito positivo;
7. la documentazione finale è coerente con le prove osservate.

Se anche uno solo dei blocchi critici fallisce, il Gate 1 rimane non superato.

---

## Regola di disciplina

Fino al superamento formale del Gate 1:

- le nuove implementazioni devono essere giustificate dal suo completamento;
- i moduli successivi non vanno considerati maturi sul piano operativo;
- le espansioni non essenziali hanno priorità inferiore ai blocchi reali del primo circuito.
