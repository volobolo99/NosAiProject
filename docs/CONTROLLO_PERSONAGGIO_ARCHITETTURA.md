# Controllo del personaggio — architettura, confini e invarianti

**Versione:** 3.1
**Data:** 1 settembre 2026
**Ruolo:** normativo su **principi, confini e invarianti** del controllo del personaggio.
Il « come » operativo sta in `CONTROLLO_PERSONAGGIO_ATTUAZIONE.md`; l'ordine dei lavori in
`CONTROLLO_PERSONAGGIO_ROADMAP.md`.
**Subordinato a:** `NOSAI_ARCHITECTURE_BASELINE.md` e `docs/adr/*` (cfr. `SOURCE_OF_TRUTH.md`).
**Origine:** consolida i documenti « Architettura Controllo NosTale v3.0 » e « Specifica
Hardened V2.0 », riconciliati con il repository il 1 settembre 2026.

---

## 0. Nota di riconciliazione

I due documenti d'origine sono stati scritti guardando le note di progetto e non il codice.
Il confronto con il repository ha mostrato che una parte del loro contenuto **descriveva
come nuovo ciò che è già implementato**, e che in un punto contraddiceva un ADR accettato.
Questa versione tiene solo ciò che sopravvive al confronto.

| Contenuto d'origine | Esito |
|---|---|
| « L'AI non deve avere accesso a primitive OS » | **Tenuto.** È `GatedInputBackend`: la barriera sta al confine, non dentro l'adapter |
| « SendInput HWND », con `PostMessage` come alternativa | **Corretto.** `SendInput` non indirizza una finestra. `Win32InputBackend` usa già solo `SendInput` con coordinate assolute normalizzate; `PostMessage` va vietato per iscritto perché non ci ricada nessuno |
| Probe di autorità d'input | **Già esistente**, `InputEnvironmentProbe`. Resta da legarne l'esito allo stato della sessione |
| « Proiettore isometrico » | **Ritirato.** `ScreenProjectionCalibration` misura già `screen = A·Δmap + anchor` e documenta perché la forma assoluta e quella isometrica non si assumono |
| Griglia di calpestabilità dedotta dallo schermo | **Sostituita.** La griglia è un dato del client (§ 5) |
| « Nessuna scrittura sul filo, nessuna injection » come divieto di progetto | **Corretto.** Contraddiceva ADR-0014. Vedi ADR-0019: la scelta è di canale, non un divieto |
| Commit point, precedenza umana, controllo di occlusione | **Tenuti.** Sono i tre buchi reali |

---

## 1. Regola di dominio

> Pieno potere operativo su NosTale; nessun potere arbitrario fuori da NosTale.

Dentro il dominio: movimento, combattimento, skill, interazioni, uso completo di mouse e
tastiera verso il client, lifecycle della sessione inclusa la chiusura.

Fuori dal dominio: aprire o controllare altre applicazioni, inviare input a una finestra
diversa da quella di sessione, controllare un processo non appartenente alla sessione, usare
primitive OS generiche per aggirare i contratti, creare un percorso che scavalchi Safety.

La sicurezza non dice « non puoi farlo ». Dice « puoi farlo solo se appartiene alla sessione
autorizzata e rispetta le invarianti tecniche ».

---

## 2. Autorità semantica

Il livello decisionale ragiona in capacità del mondo di gioco, non in primitive hardware.

```
"Usa Fireball su Monster_17"      non  KeyDown(F5)
"Interagisci con NPC_42"          non  Click(1432, 876)
"Termina la sessione"             non  CloseProcess(1234)
```

`CloseNosTaleSession(sessionId)` è autorizzata dal dominio perché la Safety può verificare
che la sessione e il processo coincidano. `CloseProcess(pid)` non è rappresentabile nel tipo.

---

## 3. Canale di attuazione

Deciso in **ADR-0019** e qui riassunto, perché è il vincolo da cui discende tutto il resto.

| Canale | Uso nel controllo del personaggio |
|---|---|
| Cattura del traffico | **Osservazione.** Permesso da ADR-0014, in uso |
| Lettura della memoria del client | **Osservazione.** Permesso da ADR-0014, in uso (l'auto-calibratore legge il bersaglio di cammino risolto dal client) |
| Input del sistema operativo verso la finestra di sessione | **Attuazione. È il canale scelto** |
| Scrittura sul filo, injection nel processo | Permessi da ADR-0014, **non scelti** per l'attuazione. Le ragioni sono in ADR-0019 e non sono il rischio di ban |
| Evasione delle rilevazioni | **Fuori**, come da ADR-0014 |

La ragione che decide: un atto emesso come input passa dal codice del client, quindi ogni
rifiuto che il client già implementa — cella non calpestabile, skill in cooldown, bersaglio
fuori portata — resta in vigore gratis. Un atto sul filo scavalca il client, e ogni errore
del nostro modello del mondo diventa un atto.

Ne discende un fatto scomodo: `SendInput` non indirizza una finestra, va a chi ha il focus.
**Il confinamento non è una proprietà dell'API: è una proprietà che la pipeline costruisce**
verificando primo piano, geometria e appartenenza del punto nell'istante dell'atto. È questa
la ragione del commit point, non un eccesso di prudenza.

---

## 4. Il percorso obbligatorio

```
  Decision Engine            valuta proposte, non inventa accessi
        ↓
  ActionEnvelope             versionato, con scadenza e prerequisiti
        ↓
  Guard / Trust / Safety     ADR-0003
        ↓
  COMMIT POINT               rivalidazione atomica       ← nuovo
        ↓
  GatedInputBackend          il confine, non aggirabile
        ↓
  Win32InputBackend          SendInput, coordinate assolute normalizzate
        ↓
  NosTale
        ↓
  Osservazione               filo · memoria · schermo, classificate
        ↓
  Verifier                   input inviato ≠ azione riuscita
        ↓
  WorldState v(N+1) → Recovery / Replan
```

---

## 5. La calpestabilità statica è un dato del client

L'unica miglioria sostanziale sopravvissuta al confronto con il codice.

`NavigationPathfinding` oggi popola la griglia per osservazione: `TileType.Unobserved = 255`,
correttamente non calpestabile. Ma la calpestabilità statica non ha bisogno di essere
osservata: **è un file del client**, e `NosArchive` sa già leggerne gli archivi.

L'archivio è **`NStcData`**. Vale la pena scriverlo, perché il primo tentativo di estrazione
ha letto `NSmpData` — il nome sembra « map » e non lo è: contiene sprite. I payload sgonfiati
non avevano due `uint16` plausibili come dimensioni e sono stati rifiutati con
`grid_rectangle_implausible`, che è il contratto che funziona al primo contatto con dati veri
invece di produrre una griglia da byte di sprite.

Il formato è una griglia rettangolare: due interi a 16 bit little-endian (larghezza,
altezza), poi larghezza × altezza byte, uno per cella, con significato a bit.

| Bit | Significato |
|---|---|
| `0x01` | Camminata vietata |
| `0x02` | Attraversamento degli attacchi bloccato — **è il dato di linea di vista** |
| `0x04` | Vincolo legato ai raid |
| `0x08` | Aggro dei mostri disabilitato |
| `0x10` | PvP disabilitato |

Quattro conseguenze:

1. La guardia di calpestabilità diventa un accesso indicizzato a un array di byte:
   deterministica, costante, **testabile offline senza il gioco in esecuzione**.
2. Il bit `0x02` dà la linea di vista senza euristiche visive.
3. `Unobserved` smette di coprire la geometria statica e resta agli ostacoli dinamici —
   mostri e altri giocatori — che è un dominio molto più piccolo e onesto.
4. L'hash delle griglie entra nell'identità della build del client: una patch le invalida
   automaticamente, senza codice dedicato.

La griglia è `CACHED` con provenienza « file del client », non `LIVE`: è vera finché la
build non cambia, ed è per questo che l'invalidazione è parte del contratto e non un extra.

---

## 6. Invarianti

Da citare per identificatore nei test, nei commenti e nei documenti operativi. `DOMAIN-01`
… `DOMAIN-14` riprendono la formulazione d'origine; da `DOMAIN-15` in poi sono nuovi e
nascono dai buchi trovati.

| ID | Invariante |
|---|---|
| `DOMAIN-01` | Il progetto è specializzato nel dominio NosTale |
| `DOMAIN-02` | Dentro il dominio esercita pieno controllo sulle capacità previste |
| `DOMAIN-03` | L'autorità non si estende al resto del sistema operativo |
| `DOMAIN-04` | Il livello decisionale ragiona per proposte e capacità semantiche |
| `DOMAIN-05` | Le primitive OS sono dettagli interni agli adapter |
| `DOMAIN-06` | Ogni esecuzione passa da Guard / Trust / Safety |
| `DOMAIN-07` | Ogni operazione è vincolata alla sessione autorizzata |
| `DOMAIN-08` | Mouse e coordinate valgono solo rispetto alla geometria corrente |
| `DOMAIN-09` | Movimento e interazioni spaziali rispettano mappa e stato live |
| `DOMAIN-10` | `UNKNOWN` non autorizza un'azione protetta |
| `DOMAIN-11` | Input inviato non equivale a successo: serve verifica |
| `DOMAIN-12` | Nessun aggiornamento può creare un bypass della Safety |
| `DOMAIN-13` | Le capacità di lifecycle valgono se appartengono alla sessione |
| `DOMAIN-14` | Ogni nuova capacità è classificata come di dominio o rifiutata dal contratto |
| `DOMAIN-15` | L'autorità di emettere input è **dimostrata** all'apertura della sessione con un atto osservabile, non presunta. Non dimostrata ⇒ sessione non attuante |
| `DOMAIN-16` | L'input umano ha precedenza assoluta: il sistema e l'operatore condividono un solo mouse |
| `DOMAIN-17` | Un programma d'input ha **al massimo un passo irreversibile, ed è l'ultimo**; subito prima si rivalidano geometria, primo piano e appartenenza del punto |
| `DOMAIN-18` | La calpestabilità statica è un dato del client, versionato con la build. Nessun percorso la deriva dallo schermo |
| `DOMAIN-19` | Nessuna trasformazione mondo→schermo è cablata: è misurata, e scade |
| `DOMAIN-20` | Ogni rifiuto cita un codice del catalogo dei guasti. Nessun motivo come stringa letterale |
| `DOMAIN-21` | L'attuazione avviene solo per input del sistema operativo (ADR-0019). I messaggi postati alla finestra non sono un'implementazione alternativa e non sono ammessi |

---

## 7. Test di regressione critici

Quelli marcati **nuovo** non hanno oggi una copertura corrispondente.

- **Domain** — ogni capacità esposta al livello decisionale appartiene al dominio.
- **Session** — un HWND o un processo di un'altra applicazione è sempre rifiutato.
- **Zero-outside-client** — nessun campione di mouse esce dal client verificato.
- **Geometry stale** — spostamento, ridimensionamento o cambio DPI invalidano le coordinate.
- **Occlusion** *(nuovo)* — una finestra interposta sul punto di click nega l'atto.
- **Authority** *(nuovo)* — autorità non dimostrata ⇒ sessione non attuante, non « azione fallita ».
- **Human takeover** *(nuovo)* — un evento di input umano aborta l'atto in corso.
- **Commit atomicity** *(nuovo)* — geometria mutata fra autorizzazione e commit ⇒ nessun pixel emesso.
- **Map** *(nuovo)* — destinazione bloccata ⇒ rifiuto; sconosciuta ⇒ sconosciuto; nessun input.
- **Projection decay** — calibrazione scaduta ⇒ nessun click, mai un click approssimativo.
- **Lifecycle** — la chiusura non agisce su un processo non associato alla sessione.
- **Bypass** — nessun modulo raggiunge il backend di input senza passare da `GatedInputBackend`.
- **Verifier** — input inviato ma risultato non osservato ⇒ mai successo.

---

## 8. Osservabilità

| Evento | Dati minimi |
|---|---|
| `ActionRequested` | id azione, proposta, bersaglio, versione del mondo |
| `SessionValidated` | id sessione, identità di processo, hwnd, esito del probe di autorità |
| `WindowObserved` | rect, modo, epoca di geometria, dpi |
| `SafetyEvaluated` | esito, guardia decisiva, codice di guasto |
| `CommitPointChecked` | esito della rivalidazione, ritardo misurato |
| `NavigationPlanned` | mappa, hash della griglia, percorso, nodi espansi |
| `InputStarted` / `InputCompleted` | id azione, backend, ricevuta |
| `InputAborted` | codice di guasto, ultimo punto valido |
| `VerificationCompleted` | atteso, osservato, esito, ritardo misurato |

Tutte le durate con orologio monotono, mai con l'orologio di parete.
