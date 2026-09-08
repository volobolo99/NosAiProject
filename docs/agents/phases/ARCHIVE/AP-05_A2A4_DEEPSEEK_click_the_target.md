# S5 / AP-05 — DeepSeek — cliccare un bersaglio, e sapere dal filo se è andata

**Si prende dopo S3**, che è l'unica altra sessione che tocca `Program.cs`.
Nessun altro file in comune con S1, S2, S4.

## File tuoi (5)

- `src/NosAi.Runtime/Tactical/ClickTargetExecutor.cs` — **nuovo**
- `src/NosAi.Runtime/Tactical/ClickTargetCommand.cs` — **nuovo**
- `src/NosAi.Runtime/Program.cs` — **solo** la registrazione dell'opzione
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**

**Da leggere e non toccare**: `Navigation/SingleStepExecutor.cs` è **il modello
esatto** — apre uno scope di attuazione, proietta, clicca e verifica. Copiane la
forma, non il file. Non toccare `LowLevel/`, `Perception/`, il decoder, né
`Autonomy/TargetEstablishment.cs`.

**Compila solo** `src/NosAi.Runtime` e `tests/NosAi.Runtime.Tests`, mai la
soluzione. Altri agenti compilano in parallelo: `MSB3027 file bloccato` è
contesa, aspetta e riprova.

`git pull` prima, `git push` tu, e **committa tutto**. Rapporto finale in
`docs/agents/reports/RAPPORTO_S5.md` — l'unico documento che scrivi.

---

## Il fatto

Il runtime **clicca già**, ma solo per camminare: `SingleStepExecutor` proietta
una casella in un pixel con `CalibratedScreenProjection`, apre uno scope, muove il
cursore e preme. La calibrazione prospettica è stata chiusa il 2026-09-07 —
passo 38,14 × 16,83 px, residuo 1,23 caselle — quindi i pixel sono buoni.

**Nessuno clicca su un'entità.** `--engage` agisce con le scorciatoie da
tastiera, e per farlo dà per scontato che il bersaglio sia già selezionato: chi lo
selezioni, oggi, non è il runtime.

E c'è la cosa che rende questo task diverso da «emetti un clic»: **il filo dice se
è andata**. Il decoder pubblica `PlayerTargetSelection` da `ct`, con l'id
dell'entità bersagliata. Un clic su un mostro produce quindi un'osservazione che
lo conferma, dal server, in millisecondi.

È il ciclo canonico che `CLAUDE.md` chiede — *Execute → Verify → Re-observe* — e
qui si chiude per intero senza inventare niente.

---

## Cosa fare

### Parte 1 — `ClickTargetExecutor`

Un esecutore puro che, data un'entità e la calibrazione:

1. **rifiuta prima di agire** se l'entità non è un bersaglio stabilito. Usa
   `TargetEstablishment.Assess`, che esiste: rifiuta gli astanti con
   `target_is_a_bystander` — un varco, due NPC e un pet fra le entità osservate —
   e lo fa **dopo** le due prove per evidenza, quindi un'entità che ci ha colpito
   resta attaccabile. **Non riscrivere quella logica**: chiamala;
2. proietta la posizione dell'entità in un pixel con `CalibratedScreenProjection`.
   Se la calibrazione manca o rifiuta (`screen_projection_dpi_regime_changed`,
   `screen_projection_client_size_changed`), **il clic non parte** e il motivo è
   quello che la proiezione ha dato — mai un motivo tuo che lo copre;
3. **verifica che il pixel sia dentro la finestra del client** prima di muovere il
   cursore. Un pixel fuori è un clic sul desktop, cioè su qualunque cosa ci sia
   sotto;
4. apre uno scope di attuazione con commit point e clicca, **esattamente come
   `SingleStepExecutor`**. Gli undici rifiuti del `CommitPointValidator` restano
   quelli e non si aggirano: finestra non in primo piano, input umano recente,
   geometria cambiata, DPI cambiato, e via;
5. **verifica sul filo**: entro una finestra di tempo che scegli tu e **motivi nel
   codice**, arriva un `PlayerTargetSelection` che nomina **quell'**id? Riporta
   confermato / non confermato, e nel secondo caso quanto ha aspettato.

**`Unknown` non è `false`.** «Nessuna conferma entro la finestra» e «il server ha
detto un altro id» sono due esiti diversi e devono restare distinguibili: il
primo può essere latenza, il secondo è un clic finito sull'entità sbagliata — ed è
il difetto che questo task deve poter rilevare.

### Parte 2 — `--click-target`

Il comando dell'operatore, sul modello di `--engage` e `--scout`. Prende l'id
dell'entità, o il vnum del mostro più vicino. Stampa in ordine: il bersaglio
scelto e perché, il pixel calcolato, l'esito dello scope, l'esito della verifica
sul filo.

**Fallisce chiuso.** Senza `--arm-input`, senza calibrazione, con la finestra non
in primo piano: `[REFUSED]` col motivo vero. Un'opzione sconosciuta è rifiutata
col proprio nome, come fanno già gli altri comandi.

**Un solo clic per invocazione.** Nessun ciclo, nessuna ripetizione automatica:
questo comando serve a stabilire che il clic funziona e si verifica, e un ciclo lo
si costruisce dopo, sopra qualcosa di provato.

---

## Test

In `tests/NosAi.Runtime.Tests/`, due file nuovi. Nessun test può muovere il mouse
vero: l'esecutore prende il backend di input come dipendenza, quindi si prova con
un backend finto che registra cosa gli è stato chiesto — è come sono provati
`SingleStepExecutor` e `--engage`, **guardali**.

1. Un astante non viene cliccato, e il motivo è `target_is_a_bystander`. **Il
   backend non riceve nessun movimento**: asserisci che il finto sia rimasto
   fermo, non solo che l'esito sia un rifiuto.
2. Un mostro stabilito viene cliccato al pixel che la calibrazione dà — asserisci
   le coordinate, calcolate a mano nel test dalla calibrazione che gli passi.
3. Calibrazione assente: nessun clic, e il motivo è quello della proiezione.
4. Pixel fuori dalla finestra: nessun clic, motivo proprio.
5. Scope rifiutato dal commit point: nessun clic, e il motivo del validatore
   arriva fino all'operatore senza essere riscritto.
6. Verifica sul filo: `ct` con l'id giusto → confermato; `ct` con un id **diverso**
   → un esito distinto da «nessuna conferma»; nessun `ct` → non confermato con il
   tempo atteso. **Tre esiti, tre asserzioni.**
7. `--click-target` senza argomenti, con un'opzione sconosciuta, e senza input
   armato: tre `[REFUSED]` con tre motivi diversi.

**Mai un `Assert.All` o un `foreach` su una raccolta che potrebbe essere vuota**
senza prima asserire che non lo sia.

---

## Fuori scope

- **Nessun ciclo automatico**, nessun collegamento a `--autoplay`, nessuna scelta
  del bersaglio fatta dal comando oltre «il più vicino fra quelli stabiliti».
- **Nessuna modifica a `LowLevel/`.** Se il cancello ti rifiuta, è il cancello che
  fa il suo lavoro: riporta il motivo, non allargarlo.
- **Nessun attacco.** Questo comando seleziona. Colpire è `--engage`, che esiste.
- **Nessun aggiornamento ai documenti** oltre il rapporto.

## Definition of done

- Build 0/0 sui due progetti; `NosAi.Runtime.Tests` 0 falliti, totale riportato.
- Nel rapporto: la finestra di attesa che hai scelto per la verifica sul filo, e
  **perché quel numero**.
- I tre esiti della verifica, e come li hai resi distinguibili.
- Livello: **Integrated**. `Verified` vuole l'operatore che guarda il cursore
  andare sul mostro e il mostro selezionarsi — quello lo fa lui, con questo
  comando in mano.
