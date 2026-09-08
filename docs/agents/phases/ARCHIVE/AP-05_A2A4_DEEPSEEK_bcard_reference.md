# AP-05 / A2+A4 — DeepSeek — le abilità dicono quale effetto applicano, e nessuno sa cosa sia

**Indipendente da tutti i task in coda.** Tocca solo `src/NosAi.Runtime/GameData/`,
che nessun altro possiede, e non `Program.cs`. Prendibile subito.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `src/NosAi.Runtime/GameData/SkillReferenceDecoder.cs` — **il modello da
   seguire**, e leggi soprattutto le sue *remarks*: dicono da quale fonte esterna
   viene il layout dei tag, che quella fonte è una pista e non una verità, e come
   è stata incrociata due volte contro un valore reale.
3. `CLAUDE.md` § *External reference data*.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu.**

---

## Il fatto

`ReferenceImporter.Tables` importa cinque tabelle da `NSgtdData.NOS`: `monster`,
`item`, `skill`, **`card`** e **`bcard`**. La descrizione che il codice stesso dà
dell'ultima è:

> `bcard` — «carte di combattimento: **gli effetti a cui le altre tabelle
> rimandano**»

E `SkillReferenceDecoder` decodifica già, per ogni abilità, gli effetti che
applica:

```csharp
public sealed record BCardApplication(
    int BCardVnum, int BCardSub, int EffectVal1, int EffectVal2, int Target);
```

Quindi il runtime sa che l'abilità 201 applica la BCard numero N con due valori —
e **non ha alcun modo di sapere cosa faccia la BCard N**. Il rimando esiste, la
destinazione no:

```
grep -rln "CardReference\|BCardReference" src/ tests/    ->  nessun risultato
```

Sono decodificati item, mostri e abilità. Delle cinque tabelle importate, le due
che spiegano *gli effetti* sono le uniche senza decoder — e sono proprio quelle a
cui le altre puntano.

**I file veri sono su questa macchina**: `C:\Program Files (x86)\Nostale\NostaleData`
esiste con 169 archivi, quindi il lavoro è interamente verificabile offline
contro i dati del gioco, non contro una descrizione.

---

## Cosa fare — `CardReferenceDecoder.cs` (nuovo, in `GameData/`)

Stessa forma di `SkillReferenceDecoder`: un `record` per il contenuto, un
`Decode(NosRecord record)` che restituisce `null` quando il record non è
leggibile, e **niente inventato**.

La disciplina, che qui conta più del risultato:

- **Il layout dei tag viene da una fonte esterna citata**, con URL e pagina, nel
  doc-comment del decoder — come ha fatto `SkillReferenceDecoder` con
  `nt-research.github.io`. Cerca la pagina che documenta `BCard.dat` e `Card.dat`.
- **Ogni campo va incrociato con un valore reale** prima di essere promosso a
  colonna tipizzata. L'incrocio migliore ce l'hai già in casa: prendi una
  `BCardApplication` che `SkillReferenceDecoder` estrae da un'abilità reale del
  client installato, e verifica che il `BCardVnum` a cui punta **esista** nella
  tabella `bcard` e che i suoi campi siano coerenti con l'effetto che quella
  abilità dichiara. Un rimando che risolve è la prova che il layout è giusto.
- **Un tag che la fonte non documenta resta non decodificato.** Non zero, non un
  enum «Unknown» piazzato per completezza: assente dal contratto. È la stessa
  regola per cui `SkillReferenceDecoder` non ha promosso i cinque valori di
  `Target` a enum, e lo scrive.
- **Se la fonte non esiste o non copre BCard**, la risposta completa è dirlo:
  decodifica solo ciò che l'incrocio con i dati reali giustifica da solo, e
  riporta cosa è rimasto fuori e perché. Una consegna che dice «questa metà non è
  confermabile» è completa; una che riempie i buchi per non lasciarli vuoti non
  lo è.

Se dall'incrocio emerge che i cinque valori di `Target` documentati nelle
*remarks* di `BCardApplication` (0 = self pre-attack, 1 = bonus pre-attack,
2 = caster-targeted, 3 = all targets post-attack, 4 = enemy-related) sono
**confermabili** contro i dati reali, dillo nel report: quella nota dice
esplicitamente che nessuno li ha mai incrociati, e chiuderla sarebbe un
risultato a sé. **Non modificare però quel commento** — REGOLA #2, la
documentazione è di Claude.

---

## OWN (file nuovi)

- `src/NosAi.Runtime/GameData/CardReferenceDecoder.cs`
- `tests/NosAi.Runtime.Tests/GameData/CardReferenceDecoderTests.cs`

## MODIFY

**Nessun file.** Il decoder è additivo: nulla lo chiama ancora, e va bene — il
suo consumatore (chi legge un `BCardApplication` e vuole sapere cosa applica) è
lavoro a valle e non fa parte di questo task. Se ti accorgi che serve toccare
`SkillReferenceDecoder` o `ReferenceImporter`, **fermati e riferisci**.

---

## Test — `CardReferenceDecoderTests.cs` (nuovo)

Sintetici, su ogni macchina:

1. Un record ben formato decodifica nei campi attesi.
2. Un record a cui manca un tag obbligatorio restituisce `null`, non un record
   mezzo pieno.
3. Un tag presente ma con un valore non numerico restituisce `null`.
4. I tag non documentati vengono ignorati senza far fallire il resto.

Contro i file reali, con `[NosTaleClientFact]` (esiste già, 13 test lo usano —
salta con la ragione dove il client non è installato, **mai** `if (…) return`):

5. La tabella `bcard` si importa e ha più record di una soglia bassa che
   **misuri tu** e scrivi nel commento.
6. **L'incrocio che vale il task**: presa un'abilità reale, ognuno dei
   `BCardVnum` che applica esiste nella tabella `bcard` e si decodifica. Se un
   rimando non risolve, **fermati e riferisci**: significa che il layout di uno
   dei due decoder è sbagliato, ed è una scoperta più importante del decoder.
7. Lo stesso per `card`, se decidi di coprirla; se decidi di no, scrivi il
   perché nel report.

## Fuori scope

- **Nessun consumatore.** Non collegare il decoder a pianificazione, ranking o
  combattimento: promuovere un effetto a fatto usabile è un'altra decisione.
- **Nessuna modifica ai decoder esistenti**, nemmeno a un commento.
- **Nessun aggiornamento ai documenti** — REGOLA #2.

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests` 0 falliti, numero riportato; `Core` invariato.
- L'URL della fonte esterna usata, e quali tag documenta.
- L'esito dell'incrocio del punto 6, con i numeri veri (quale abilità, quali
  vnum, risolti o no).
- L'elenco dei tag lasciati fuori, con il motivo.
- Livello: **Integrated** (i file sono reali; `Verified` vorrebbe un operatore
  che osserva l'effetto in gioco).
