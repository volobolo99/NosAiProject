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
| Runtime PC | Avvio affidabile | [x] locale | `Gate1BootstrapHost` + `--gate1-test`; evidenza PC reale ancora richiesta per VERIFIED |
| Runtime PC | Configurazione valida | [x] locale | `Gate1HostOptionsLoader` rifiuta timeout/porte invalidi |
| Runtime PC | Logging utile | [x] locale | `ConsoleRuntimeLogger` con correlation id nel bootstrap |
| Runtime PC | Safety policy attive | [x] locale | snapshot Gate 1 espone live input/packet injection disabilitati |
| Runtime PC | Stato sessione osservabile | [x] locale | `gate1.snapshot.v1` include sessione Guard classificata |
| Client NosTale | Rilevamento client | [x] locale | processo/finestra LIVE; assenza → `client_unavailable` |
| Client NosTale | Lettura dati minimi | [ ] | attachment LIVE; gameplay baseline ancora UNKNOWN |
| Client NosTale | Validazione dati | [x] locale | provenance `LIVE`/`UNKNOWN` nel snapshot |
| Client NosTale | Gestione client assente | [x] locale | runtime resta DEGRADED, non inventa gameplay |
| Guard AI smartphone | Avvio affidabile | [ ] | richiede dispositivo reale |
| Guard AI smartphone | Connessione reale | [ ] | loopback autenticato coperto da test; rete reale no |
| Guard AI smartphone | Autenticazione reale | [x] locale | RSA-2048 challenge/response + fail-closed |
| Guard AI smartphone | Heartbeat reale | [x] locale | timeout 2s fail-closed + riconnessione |
| Guard AI smartphone | Riconnessione controllata | [x] locale | nuova sessione accettata dopo timeout |
| Dashboard | Avvio affidabile | [x] locale | operator server Gate 1 su loopback |
| Dashboard | Connessione al runtime corretto | [x] locale | `/api/gate1` dal runtime; Python dashboard resta UNKNOWN se `NOSAI_RUNTIME_URL` manca |
| Dashboard | Dati reali soltanto | [x] locale | demo gold/mostri/GPU rimossi; UNKNOWN esplicito |
| Dashboard | Coerenza degli stati | [x] locale | snapshot unico PC/client/guard/safety |
| Dashboard | Error handling | [x] locale | client assente e runtime offline non mascherati |
| End-to-end | PC ↔ client | [ ] | richiede NosTale reale |
| End-to-end | PC ↔ smartphone | [ ] | richiede Guard AI reale |
| End-to-end | Runtime ↔ dashboard | [x] locale | `/api/gate1` + dashboard classificata |
| End-to-end | Errore/disconnessione/riconnessione | [x] locale | heartbeat fail-closed; dispositivo reale ancora richiesto |
| Governance | Nessuna regressione bloccante | [ ] | `pytest` e `--gate1-test` verdi; `NosAi.Runtime.Tests` **non eseguito** su questa macchina (Application Control blocca l'assembly, `0x800711C7`), quindi la copertura xunit non è ancora evidenza |
| Governance | Documentazione coerente | [x] locale | source of truth, checklist, stato |

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

Il Gate 1 **non è superato** finché restano aperti i punti end-to-end reali (NosTale, smartphone, evidenza sul PC di produzione). Le spunte `locale` coprono implementazione e test automatici, non la promozione a `VERIFIED`.

---

## Regola di disciplina

Fino al superamento formale del Gate 1:

- le nuove implementazioni devono essere giustificate dal suo completamento;
- i moduli successivi non vanno considerati maturi sul piano operativo;
- le espansioni non essenziali hanno priorità inferiore ai blocchi reali del primo circuito.
