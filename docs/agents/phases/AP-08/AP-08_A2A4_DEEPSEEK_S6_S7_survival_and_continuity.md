# S6 e S7 — DeepSeek — il personaggio muore, lo zaino si riempie, la sessione cade

Due sessioni. **S7 si prende dopo S6**, perché condividono un file. Nessuna delle
due tocca file di S1, S2, S3, S4 o S5.

Regole generali: `docs/agents/DEEPSEEK_HANDOFF.md`. `git pull` prima, `git push`
tu, **committa tutto**, niente impalcature lasciate indietro, e l'unico documento
che scrivi è il rapporto.

**Compila solo** `src/NosAi.Runtime` e `tests/NosAi.Runtime.Tests`, mai la
soluzione: altri agenti compilano in parallelo e `MSB3027 file bloccato` è
contesa.

---

# S6 — il personaggio muore e nessuno se ne accorge

## File tuoi (4)

- `src/NosAi.Runtime/Autonomy/CharacterLifecycle.cs` — **nuovo**
- `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**

Rapporto: `docs/agents/reports/RAPPORTO_S6.md`.

## Il fatto

**Non esiste una riga in tutto `src/` che tratti la morte del proprio
personaggio.** `TargetSelector.cs:221` gestisce la morte di un'*entità* — «un'entità
nota come morta non è un bersaglio» — ma se muore il personaggio controllato, il
runtime continua a decidere come se niente fosse: sceglie bersagli, calcola
percorsi, e clicca su una schermata di morte.

Per un giocatore autonomo è il primo buco da chiudere: è ciò che trasforma «gioca
da solo per un'ora» in «gioca finché non muore, poi si agita a vuoto».

## Le due osservazioni che lo dicono, entrambe già disponibili

1. **`die` non ha guardia sul proprio id.** `DecodeDeath` emette
   `GameEventKind.EntityDeath` per qualunque id, compreso quello del personaggio
   controllato — che il decoder conosce già (`_playerEntityId`, usato da
   `DecodeMove` per non pubblicare sé stesso come entità esterna). Un
   `EntityDeath` con quell'id **è** la nostra morte.
2. **`stat` porta la vita propria.** HP a zero è la stessa notizia da una seconda
   fonte indipendente.

**Usale entrambe e tienile distinte.** Due fonti che concordano sono una conferma;
una sola è un'osservazione. E se una dice morto e l'altra viva, **quello è un
fatto da riportare**, non da appianare scegliendone una.

## Cosa fare

### Parte 1 — `CharacterLifecycle`

Un tipo puro che riceve le osservazioni e dice in che stato è il personaggio.
Almeno: vivo, morto, e **non so** — che non è nessuno dei due e non va appiattito
su «vivo».

Vincoli:

- **`Unknown` è lo stato iniziale**, non «vivo». Prima che una fonte parli, il
  runtime non sa se il personaggio è vivo, e comportarsi come se lo fosse è
  precisamente l'errore che questo task esiste per evitare;
- lo stato porta **da quale fonte** viene e **quando** è stato osservato. Il
  repository ha `ClassifiedValue<T>` per questo: usalo, non inventare un
  meccanismo nuovo;
- **la risurrezione va osservata, non dedotta.** Tornare «vivo» richiede
  un'osservazione positiva — HP maggiore di zero da un `stat` nuovo — mai il
  semplice passare del tempo.

### Parte 2 — pubblicarlo

`GameplayObservation.ToWire()` (`GameplayProvider.cs:279-340`) pubblica quattordici
campi. Aggiungi lo stato del personaggio, **additivamente**: nessun campo
esistente cambia nome o significato.

### Parte 3 — fallire chiuso da morti

**Questo è il punto del task.** Ogni comando che agisce sul mondo —
`--engage`, `--walk`, `--scout`, `--collect`, `--recover`, `--autoplay` — deve
**rifiutare** con un motivo proprio quando lo stato è «morto».

E deve rifiutare **anche quando lo stato è `Unknown`**: agire senza sapere se sei
vivo è la stessa scommessa che `commit_human_input_unknown` già rifiuta di fare
altrove nel progetto. È la regola «fail closed where safety requires it» di
`CLAUDE.md`.

**Attenzione all'ownership**: se questi comandi vivono in file che non sono tuoi,
**non toccarli**. Consegna il tipo, il campo sul filo e il punto di aggancio, e
scrivi nel rapporto **quali file servirebbero e di chi sono**. Un rifiuto
cablato a metà è peggio di nessun rifiuto.

## Il limite da dichiarare, e non è un difetto

**Nessuna delle otto registrazioni contiene una morte del giocatore.** La logica
si prova quindi su righe di filo sintetiche, e la verifica contro una morte vera
resta da fare con l'operatore. **Scrivilo nel rapporto**: è la differenza fra
`Integrated` e `Verified`, e dichiararla è parte della consegna.

## Test

Due file nuovi. Righe di filo costruite a mano (è come sono provati
`EntityLevelFromStTests` e gli altri: **guardali**).

1. Stato iniziale: `Unknown`, e **non** vivo.
2. `die` con l'id del personaggio → morto, con la fonte e l'istante.
3. `die` con l'id di un'altra entità → lo stato del personaggio **non cambia**.
4. `stat` con HP a zero → morto, da una fonte diversa dalla precedente.
5. Le due fonti in disaccordo → l'esito lo dichiara, e non ne sceglie una in
   silenzio.
6. Morto, poi `stat` con HP maggiore di zero → vivo. Morto e basta passare il
   tempo → **ancora morto**.
7. Il campo nuovo compare in `ToWire()` e nessuno dei quattordici esistenti
   cambia.

---

# S7 — lo zaino si riempie, la sessione cade

**Si prende dopo S6**: condividete `GameplayProvider.cs`.

## File tuoi (5)

- `src/NosAi.Runtime/Autonomy/InventoryCapacity.cs` — **nuovo**
- `src/NosAi.Runtime/Autonomy/SessionContinuity.cs` — **nuovo**
- `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**

Rapporto: `docs/agents/reports/RAPPORTO_S7.md`.

## Primo fatto — lo zaino

Zero occorrenze di `inventory_full` o equivalenti in tutto `src/`. `--collect` va
a raccogliere, il gioco rifiuta perché non c'è posto, e nulla lo nota: il ciclo
riprova per sempre sullo stesso oggetto.

**Il dato per accorgersene arriva già.** `ivn` porta una riga per slot:

```
ivn 0 4.13.0.0.0.0.0      slot 4, vnum 13
ivn 0 0.0.0.0.0.0.0       slot 0, vnum 0 -> vuoto
ivn 0 0.8.0.0.0.0.0       slot 0, vnum 8
```

Un vnum `0` è uno slot **vuoto**. Contarli dà i posti liberi **fra quelli che il
server ci ha nominato** — e la distinzione è tutto:

- **non conosciamo la capienza totale dello zaino.** Nessun pacchetto la dice, e
  il numero di slot dipende da cose che non osserviamo. Quindi
  `InventoryCapacity` pubblica **«slot liberi noti»**, non «slot liberi», e
  quando nessun `ivn` è mai arrivato la risposta è `Unknown`, non zero;
- **zero slot liberi noti non è «zaino pieno»** se conosciamo solo tre slot.
  Tienili distinti: «so che è pieno» e «non ho visto posti liberi» sono due
  affermazioni diverse, e solo la prima può fermare la raccolta.

**Cosa fare**: il conteggio, pubblicato in `ToWire()` additivamente, con la sua
provenienza e il suo istante. E il motivo pronto per chi vorrà fermare la
raccolta — **senza cablarlo tu** in `--collect`, che non è tuo: riporta nel
rapporto dov'è il punto di aggancio.

## Secondo fatto — il rilogin

La gestione della disconnessione esiste solo a livello di trasporto
(`MessageFramer`, `NosTaleWorldFramer`): niente a livello di gioco. Dopo un
rilogin **gli id delle entità cambiano tutti**, e il World Model resterebbe pieno
di mostri che non esistono più — con la scadenza a sessanta secondi
(`MaxEntityRetention`) come unica difesa, che nel frattempo lascia scegliere
bersagli fantasma.

**Il segnale c'è ed è misurabile**: l'id del personaggio controllato. Vale
`3548294` in `data/messaggi.noscap` e `3443217` in `data/nostale_combat.noscap` —
due sessioni, due id. **Un id proprio che cambia è una sessione nuova.**

**Cosa fare**: `SessionContinuity` osserva l'id proprio e dichiara quando è
cambiato, con il vecchio e il nuovo. Pubblicalo, e dai a chi tiene stato un
motivo esplicito per buttarlo — **senza buttarlo tu**: la decisione di svuotare il
World Model non è in questi file, e cablarla di nascosto sarebbe peggio del
problema.

**Due trappole, entrambe da evitare per costruzione**:

- **il primo id osservato non è un cambio.** Passare da «non so» a `3548294` è
  l'inizio, non un rilogin;
- **un id assente non è un id nuovo.** Se per un po' nessun pacchetto lo nomina,
  la risposta è `Unknown`, non «è cambiato».

## Test

1. Nessun `ivn` mai visto → slot liberi `Unknown`, **non** zero.
2. Tre `ivn`, uno vuoto → uno slot libero noto, e la risposta dice **noto**.
3. Tutti gli slot conosciuti pieni → «nessun posto libero fra quelli noti», e il
   testo lo distingue da «zaino pieno».
4. Un `ivn` che rioccupa uno slot prima vuoto → il conteggio cala.
5. Primo id osservato → **nessun** cambio di sessione.
6. Id che passa da `3548294` a `3443217` → cambio dichiarato, con entrambi.
7. Id che smette di arrivare → `Unknown`, **non** un cambio.
8. Lo stesso id ripetuto cento volte → nessun cambio.
9. I campi nuovi compaiono in `ToWire()` e nessuno degli esistenti cambia.

---

## Per entrambe

**Mai un `Assert.All` o un `foreach` su una raccolta che potrebbe essere vuota**
senza prima asserire che non lo sia.

Nel rapporto: cosa hai pubblicato, quali comandi andrebbero agganciati e in quali
file (senza toccarli se non sono tuoi), e cosa resta non verificato contro il
gioco reale.

**Il criterio di giudizio**: non quanti stati hai aggiunto, ma quanti restano
onestamente `Unknown` invece di essere indovinati. Un runtime che sa di non sapere
se è vivo vale più di uno che lo dà per scontato.
