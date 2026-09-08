# AP-10 / A2+A4 — DeepSeek — gli stadi si certificano su dati inventati

**Indipendente da tutti i task in coda.** Tocca solo
`src/NosAi.Runtime/Testing/`, che nessun altro possiede, e non `Program.cs`
(`--scenario-test` esiste già). Prendibile subito.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `src/NosAi.Runtime/Testing/CertificationReportBuilder.cs` — in particolare il
   perché il tetto è `Integrated` e non può essere superato da questo processo.
3. `CLAUDE.md` § *Real-environment rule*.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu.**

---

## Il fatto

`--certification-report` riporta oggi **14 stadi su 14 a `Integrated`**, e
`IsFullyCertified` resta falso perché `Verified` richiede il client reale. Fin
qui è onesto.

Ma l'evidenza sotto quel `Integrated` è **sintetica**. `ScenarioStageTestRunner`
copre MapDiscovery, Exploration, TargetRecognition e MultiStepQuest con dati
costruiti a mano: provano che la logica regge sulla forma che il catalogo
descrive, e non provano niente su ciò che il filo manda davvero.

Nel frattempo, in `data/`, ci sono **cinque registrazioni reali, 27 726
messaggi**: 167 entità distinte in `certificazione`, 176 in `equip_test`, 158 in
`nostale_live`, mappe attraversate, combattimenti, oggetti raccolti.

Un `Integrated` ottenuto su byte reali non è lo stesso `Integrated` ottenuto su
un fixture, e oggi il rapporto non sa distinguerli.

---

## Cosa fare

### 1. Gli stadi che i byte reali possono davvero coprire

Per **TargetRecognition** ed **Exploration** (e per gli altri due, se l'evidenza
c'è — deciderlo è parte del task), aggiungi a `ScenarioStageTestRunner` dei
controlli che girano sulle registrazioni invece che sui fixture: entità
riconosciute con il loro vnum, entità distinte viste, mappe attraversate — quello
che le catture contengono davvero.

**Non sostituire i controlli sintetici: affiancarli.** I fixture provano la
forma su ogni macchina, anche senza `data/`; i byte reali provano che quella
forma è quella che arriva. Servono entrambi, ed è il motivo per cui questo non è
un rimpiazzo.

### 2. Il rapporto deve dire su cosa si regge

`CertificationReportBuilder` oggi dice livello, evidenza e blocker per stadio.
Aggiungi la natura dell'evidenza: **sintetica**, **registrata**, o entrambe. Uno
stadio coperto solo da fixture e uno coperto anche da una sessione reale non
sono allo stesso punto, e il rapporto deve poterlo dire senza che qualcuno vada
a leggere i test.

### 3. Ciò che non cambia

- **Il tetto resta `Integrated`.** Nessuna registrazione può produrre `Verified`:
  è per costruzione, e il blocker che lo dice resta su ogni stadio. Se dopo la
  tua modifica un solo stadio arriva a `Verified`, la consegna è sbagliata.
- **Uno stadio che nessuna suite copre continua a dichiararlo** invece di
  ereditare il verde di una suite che parla d'altro. `Attach` resta scoperto:
  attaccarsi a un client vivo non è simulabile, e va detto, non aggirato.
- **Su un clone senza `data/`** i controlli registrati saltano con la ragione
  scritta e i sintetici portano il carico. Mai un `if (…) return`.

---

## OWN

- `tests/NosAi.Runtime.Tests/ScenarioOnRecordedBytesTests.cs` (nuovo)

## MODIFY

- `src/NosAi.Runtime/Testing/ScenarioStageTestRunner.cs`
- `src/NosAi.Runtime/Testing/CertificationReportBuilder.cs`

Nient'altro: non `Program.cs`, non `src/NosAi.Core/`, nessun file degli altri
task.

---

## Test

1. Uno stadio coperto solo da fixture è riportato come evidenza **sintetica**.
2. Lo stesso stadio, con i controlli registrati eseguiti, è riportato come
   **entrambe**.
3. **Senza `data/`** il rapporto resta valido, gli stadi restano `Integrated`
   sull'evidenza sintetica, e i controlli registrati risultano saltati — non
   falliti, non silenziosamente passati.
4. Nessuno stadio raggiunge `Verified` in nessuna combinazione. Scrivi questo
   test per primo.
5. `Attach` resta scoperto e porta il proprio blocker.

## Definition of done

- Build 0/0; suite verdi, numeri riportati.
- `--scenario-test` e `--certification-report` eseguiti **davvero**, uscite
  incollate: quanti controlli in più, quali stadi cambiano natura d'evidenza.
- La stessa coppia eseguita con `data/` reso irraggiungibile (basta puntare la
  variabile d'ambiente dei campioni altrove, o rinominare temporaneamente la
  cartella e rimetterla a posto), a dimostrare il punto 3.
- Se decidi che MapDiscovery o MultiStepQuest **non** sono copribili dalle
  registrazioni, dillo con il motivo misurato: è una risposta completa.
- Livello: **Integrated**.
