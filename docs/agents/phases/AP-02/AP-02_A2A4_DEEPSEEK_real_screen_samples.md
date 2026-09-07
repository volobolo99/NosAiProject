# AP-02 / A2+A4 — DeepSeek — dodici misure reali che nessun test guarda

**Indipendente da tutti.** Non tocca un solo file di `src/`. Prendibile subito,
anche in parallelo — l'unica cautela è la contesa sulla build se un altro task
gira nello stesso momento nella stessa cartella.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `src/NosAi.Runtime/Perception/ScreenProjectionProbe.cs` — il commento in testa
   spiega **perché** il campione è un offset dal personaggio e non una coordinata
   assoluta. Leggilo: è la ragione per cui il vecchio formato è rifiutato.

**REGOLA #3**: i fatti qui sotto sono stati letti dal repository il 2026-09-07.
Se il codice dice altro, vince il codice e lo scrivi nel report.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull` prima, **`git
push` tu alla fine.**

---

## Il fatto

`data/perception/screen-samples.txt` contiene **dodici campioni reali**, presi
sul client vero a 1024×768: offset dal personaggio in caselle, e il pixel a cui
il client li ha risolti.

```
  8   2  843 466 1024 768        -10  -8  181 302 1024 768
  6   7  730 555 1024 768         -8 -16  294 213 1024 768
  2   9  559 597 1024 768         -3 -20  465 171 1024 768
 -3   8  375 583 1024 768          5 -18  649 185 1024 768
 -8   6  228 515 1024 768         10 -13  796 253 1024 768
-10   0  157 412 1024 768         11  -6  867 356 1024 768
```

Sono un anello attorno al personaggio: l'offset gira da (8,2) fino a (11,−6)
passando per il lato opposto. È una misura fatta contro il client vivo, cioè la
cosa che questo progetto tiene per più preziosa del codice
(`PIANO_DI_RIORDINO.md` § 0).

**Nessun test li legge.**

```
grep -rn "screen-samples" tests/     ->  nessun risultato
```

La proiezione schermo è provata solo su dati sintetici. Il che vuol dire: se il
solutore smettesse di adattarsi a una misura reale — e restasse coerente con sé
stesso — nessuna prova se ne accorgerebbe.

---

## Cosa fare — `ScreenProjectionRealSamplesTests.cs` (nuovo, in `tests/`)

Con `RecordedCaptureFactAttribute` come modello per lo skip: `data/` è
gitignored, quindi su un clone i campioni non ci sono e il test **salta con la
ragione scritta**, mai `if (…) return`, che xUnit conta come superato. Se
l'attributo esistente non calza (cerca un `.noscap`), scrivine uno gemello per i
campioni — stesso idioma, stessa disciplina.

1. **I dodici campioni si leggono tutti.** Il parser di `ReadSamples` accetta
   sei campi per riga: verifica che nessuno dei dodici venga scartato in
   silenzio, e che larghezza e altezza siano coerenti su tutte le righe.
   Un campione scartato per un campo malformato è invisibile oggi.
2. **Il solutore li risolve, e il residuo è quello che è.** Fai risolvere i
   dodici campioni e **riporta il residuo nel report**: non lo fisso io qui,
   perché è la misura che nessuno ha ancora fatto e scriverla a priori la
   trasformerebbe in un'aspettativa. Il test asserisce che il residuo è finito e
   sotto una soglia che **scegli tu dopo averlo misurato**, motivandola in un
   commento.
3. **La calibrazione salvata e quella ricalcolata dicono la stessa cosa.**
   `data/perception/screen-projection.calibration` è il risultato già salvato. Se
   ricalcolando dai dodici campioni escono coefficienti diversi, **fermati e
   riferisci**: significa che il file salvato non è più quello che quei campioni
   producono, ed è un fatto che vale più del test.
4. **Un campione tolto sposta il modello.** Togline uno, risolvi, e verifica che
   il risultato cambi. Se non cambia, il campione non stava portando
   informazione, e va detto.
5. **Il formato vecchio resta rifiutato.** Una riga in coordinate assolute (il
   formato che il probe dichiara di rifiutare) non deve entrare nel modello. È
   la protezione descritta nel commento di `ScreenProjectionProbe`, e oggi nulla
   la prova.

---

## Fuori scope

- **Nessuna modifica a `src/`.** Se per scrivere un test ti servisse rendere
  pubblico qualcosa, **fermati e riferisci**: significa che quella logica non è
  verificabile dal suo confine, e la scoperta vale più del test.
- **Nessuna nuova calibrazione salvata su disco.** I test non scrivono in
  `data/`.
- **Nessun aggiornamento ai documenti** — REGOLA #2.

## Definition of done

- Build `-c Release` 0/0; `NosAi.Runtime.Tests` 0 falliti, numero riportato;
  `NosAi.Core.Tests` invariato.
- Il residuo misurato sui dodici campioni, nel report.
- L'esito del confronto col file di calibrazione salvato.
- Livello: **Integrated** (i campioni sono reali ma registrati; `Verified`
  vorrebbe un operatore che riprende la misura).
