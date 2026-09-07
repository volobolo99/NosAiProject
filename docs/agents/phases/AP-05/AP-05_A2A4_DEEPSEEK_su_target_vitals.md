# AP-05 / A2+A4 — DeepSeek — `su` porta la vita del bersaglio, e il decoder si ferma al quarto campo

**Indipendente da AP-09 `--learning-report`.** Tocca
`Perception/Network/NosTaleWorldProtocolDecoder.cs`,
`Observability/WireInspectCommand.cs` e file di test nuovi. **Non tocca
`Program.cs`** (`--wire-inspect` è già agganciato, `Program.cs:828`). I due task
si possono lanciare insieme.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/PROTOCOLLO_NOSTALE.md` § `su` (riga 105) — **contiene già l'analisi**.
3. `NosTaleWorldProtocolDecoder.DecodeOtherVitals` — **il modello da seguire**:
   fa esattamente, per `st`, ciò che qui va fatto per `su`.
4. `CLAUDE.md` § *External reference data*.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**
(non c'è hook: `.git/hooks/` ha solo file `.sample`).

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci la discordanza invece di piegare il codice alla specifica.

---

## Il fatto

`docs/PROTOCOLLO_NOSTALE.md` dice degli ultimi due campi di `su`:

> **confermato con il giocatore come bersaglio** — `7289 7305` è esattamente
> HP/maxHP del personaggio, e segue `stat`

E `DecodeHit` legge `fields[1]`, `fields[2]`, `fields[3]`, `fields[4]` —
attaccante e bersaglio — poi emette un `GameEvent(CombatHit, …)` **senza un
numero dentro**. Dei 17 campi ne legge quattro. L'analisi esiste nel repository
da prima di questo task e **non arriva al World Model**.

Il World Model sa la vita di un mostro solo da `st`. Ogni colpo la ridice, e
nessuno la ascolta.

---

## Le misure, già fatte — da rifare tu, non da credere

Numerazione **come nel decoder**: `fields[0]` è l'opcode, quindi `fields[1]` è
il tipo dell'attaccante. Comando:

```
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --wire-inspect data/nostale_combat.noscap --opcode su --max 0
```

**212 pacchetti `su`** su quattro catture (`nostale_live` 13, `equip_test` 2,
`nostale_combat` 117, `certificazione` 80; `nostale_01` non ne ha). Tutti con 18
token, cioè `fields.Length == 18`.

| Misura | Risultato |
|---|---|
| `fields[16] <= fields[17]` e `fields[17] > 0` | **212 su 212, zero violazioni** |
| `fields[17]` costante per id bersaglio | **40 bersagli distinti, zero con due valori** |
| `fields[11] == 1` ⟺ `fields[16] > 0` | **212 su 212, zero controesempi** |

Due campi presi a caso non sopravvivono a 212 campioni di `hp <= maxHp`, e un
campo qualsiasi non resta costante per bersaglio mentre l'altro scende. La
lettura «HP corrente / HP massimo del bersaglio» è confermata dalla misura, non
supposta.

### Il campo che rende il task quello che è

`fields[11]` è **un flag di presenza**. Quando vale `0`, `fields[16]` vale `0` —
e quello zero significa *non riferito*, non *morto*.

**40 pacchetti su 212 (18,9%)** hanno `fields[11] == 0`. Un decoder che legge
`fields[16]` senza guardare `fields[11]` pubblica «questo bersaglio ha 0 punti
vita», cioè *morto*, quaranta volte su duecentododici — ogni volta sbagliando.
È l'invariante del progetto — *Unknown non è zero, falso o vuoto* — scritta nel
formato del filo. La verifica: nelle stesse catture ci sono **5** pacchetti
`die` in tutto, non 40.

### Il campo da NON decodificare

`fields[12]` sembra una percentuale e non lo è: con `fields[16]=475` e
`fields[17]=888` (53,5%) vale `59`; con `367`/`1066` (34,4%) vale `36`.
Lascialo stare. `DecodeOtherVitals` ha già incontrato la stessa trappola su `st`
e il suo commento la registra — «la percentuale al campo 5 non concorda con quei
valori lungo la cattura ed è ignorata».

---

## Parte 1 — `DecodeHit` pubblica la vita del bersaglio

In `NosTaleWorldProtocolDecoder.cs`, con la stessa forma di
`DecodeOtherVitals`:

1. Leggi `fields[11]`, `fields[16]`, `fields[17]` **solo se
   `fields.Length >= 18`**. Meno campi: nessuna vitalità, e il resto del decode
   di `su` continua a funzionare come adesso.
2. **`fields[11] != 1` ⇒ nessuna vitalità.** Non zero, non un valore di comodo:
   niente. Questo è il punto del task.
3. Rifiuta l'aritmetica implausibile con lo stesso controllo che
   `DecodeOtherVitals` usa già: `maxHp <= 0 || hp < 0 || hp > maxHp` ⇒ nessuna
   vitalità.
4. Aggiorna `_entities[targetId]` come fa `DecodeOtherVitals` (`Vitals`,
   `HpRatio`, `HasHp`, `HpAtUtc`) ed emetti la `Sighting` **solo se la posizione
   è già nota**, esattamente come lì.
5. **Mai il personaggio controllato.** Se `targetId` è l'id proprio, non
   pubblicare vitalità: `stat` è già la fonte su quella vita, e due fonti che
   dicono numeri diversi allo stesso istante sono un conflitto inventato da noi.
   L'id proprio è già noto al decoder — guarda come `DecodeHit` lo usa oggi per
   decidere l'aggressore.
6. L'evento `CombatHit` resta **identico**. Non aggiungere il danno: `GameEvent`
   non ha un campo per un numero, e cambiare quel contratto è di Claude (A1).

Aggiorna il commento XML di `DecodeHit` con ciò che hai **misurato tu**, non con
questo testo.

## Parte 2 — `--wire-inspect --fields`: censire invece di guardare a occhio

Le misure qui sopra sono state fatte incolonnando l'output di `--opcode` in
`awk`. È il terzo task di fila che comincia così. `WireInspectCommand` ha oggi
due sole opzioni (`--opcode`, `--max`, righe 57 e 60).

Aggiungi **`--fields`**: per l'opcode chiesto, su tutte le sue occorrenze nel
file, una riga per indice di campo con almeno

- l'indice **nella numerazione del decoder** (`fields[n]`, opcode = 0);
- quanti valori distinti;
- se sono tutti numerici, minimo e massimo;
- fino a N valori d'esempio (scegli N e scrivi perché);
- la dicitura **costante** quando i valori distinti sono uno solo — un campo
  costante su una cattura non è decodificabile da quella cattura, ed è
  un'informazione, non un buco.

Se le occorrenze hanno lunghezze diverse, dillo nell'intestazione e censisci
ogni lunghezza separatamente: mescolarle inventerebbe varianza che non c'è.

**In più, e vale quanto il resto**: oggi un'opzione sconosciuta viene *ignorata
in silenzio*. `--wire-inspect <file> --opcode su --summary` ha stampato sei
righe come se `--summary` non fosse stato scritto. Rifiutala con una ragione,
come il comando già fa per `--opcode` senza valore
(`OpcodeWithoutValueReason`). Un flag ignorato in silenzio fa credere di aver
misurato una cosa mentre se ne è misurata un'altra — è successo davvero,
scrivendo questa specifica.

---

## Test (file nuovi in `tests/NosAi.Runtime.Tests/`)

Sulle catture reali, con `[RecordedCaptureTheory]` / `[RecordedCaptureFact]`
(esistono già; saltano **visibilmente** dove il file non c'è — **mai**
`if (…) return`):

1. Su tutte e quattro le catture con `su`: ogni pacchetto con `fields[11] == 1`
   soddisfa `0 < fields[16] <= fields[17]`. Il numero di pacchetti visitati va
   **asserito**, non solo contato: un test che non vede pacchetti passa a vuoto.
2. Ogni pacchetto con `fields[11] == 0` **non produce vitalità**. È il test che
   dice il task.
3. `fields[17]` è costante per id bersaglio dentro una cattura.
4. Un `su` verso l'id proprio non pubblica vitalità del bersaglio.
5. Un `su` corto (meno di 18 campi) decodifica ancora attaccante e bersaglio.
6. `--fields`: su una cattura reale riconosce come **costante** un campo che lo
   è (per `stat` il campo 5 vale `0` ovunque nelle cinque catture) e come
   variabile uno che varia (`stat` campo 1).
7. `--wire-inspect` con un'opzione inventata **fallisce con la ragione**.

## Fuori scope

- **Il danno** (`fields[13]`): richiede un campo in più su `GameEvent` — A1.
- **`fields[12]`**, e ogni campo costante nelle catture.
- **`in`**: i suoi campi non letti sono quasi tutti costanti nelle cinque
  catture; usa `--fields` per fotografarli e **riferisci i numeri**, senza
  decodificarli.
- **Nessun aggiornamento ai documenti** — REGOLA #2. `PROTOCOLLO_NOSTALE.md` lo
  aggiorna Claude con i numeri del tuo report.

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests` 0 falliti, totale riportato; `Core` invariato.
- Le tre misure della tabella **rifatte da te**, con i tuoi numeri: se anche una
  sola non torna, fermati e riferisci — significa che la lettura è sbagliata, ed
  è più importante del decoder.
- Quanti pacchetti `su` producono ora una vitalità e quanti no, per cattura.
- L'output di `--fields` per `su` su `nostale_combat`, incollato nel report.
- Livello: **Integrated** (`Verified` vorrebbe un operatore che confronta la
  barra della vita di un mostro sullo schermo con il numero pubblicato).
