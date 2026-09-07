# AP-02 / A2+A4 — DeepSeek — il regime su ogni riga dei campioni

**Prendibile quando i task `refusal_reason_coverage` e `unobserved_breakdown`
sono consegnati.** Nessun file in comune con loro, ma questo task **aggiunge una
costante `…Reason`** in `src/NosAi.Runtime`, e il registro dei rifiuti misura
proprio quell'insieme: partire insieme sposterebbe la sua linea di partenza a
metà lavoro.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/TEST_RIMANDATI.md` **T-15** — la misura da cui nasce questo task.
3. `src/NosAi.Runtime/Perception/GeometryEpoch.cs`, il tipo `GeometryShape` e il
   suo commento: **esiste già** e dice esattamente a cosa serve — «the part of a
   window's geometry that survives being written to a file: what decides the
   *shape* of a measured transform». Non inventarne un altro.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu.**

---

## Il fatto, e la decisione presa

`data/perception/screen-samples.txt` contiene dodici campioni che **non si
adattano a una sola trasformazione**: residuo peggiore 73,6 px contro una soglia
di 1,5 caselle (≈ 38 px). Sottoinsiemi contigui passano, con trasformazioni
diverse:

```
tutti e 12   73,6 px = 2,9 caselle    scala 32,9 / 14,7 px per casella
primi 5      16,1 px = 0,63 caselle   scala 36,6 / 22,8
ultimi 6     31,8 px = 1,25 caselle   scala 30,3 / 12,7
```

Il file è una **miscela di due sessioni con geometria diversa**, e oggi nulla
nel formato lo impedisce: la riga porta `mx my sx sy width height` — larghezza e
altezza sì, **DPI no**, e nulla che distingua due momenti in cui il client
disegnava a scale diverse.

**Decisione dell'operatore (2026-09-07): il regime sta su ogni riga.** Non una
procedura da ricordare (svuotare il file a ogni cambio), ma un dato che il file
porta, così che due regimi diversi non possano mescolarsi nemmeno per
distrazione.

### Il fatto scomodo, che va scritto nel codice e non nascosto

Le due sessioni hanno **la stessa larghezza e la stessa altezza** (1024×768 su
tutte e dodici le righe). Se il regime fosse la sola `GeometryShape`, la
distinzione dipenderebbe interamente dal DPI — che è plausibile (il rapporto di
scala misurato, ~0,83, è vicino a 96/120 = 0,8) ma **non è provato**, perché il
DPI di quelle righe non è stato registrato e non è più recuperabile.

Quindi: portare il DPI sulla riga rende la domanda **rispondibile la prossima
volta**, e non risponde a questa. Il codice deve dirlo, non lasciar credere che
il controllo copra un caso che nessuno ha verificato.

---

## Cosa fare

### 1. La riga porta il regime

Formato nuovo, con una **versione in testa al file** — `ScreenProjectionCalibration`
ha già l'idea (`Magic` + `Version`, e rifiuta per versione: leggi come lo fa,
riga 180 e 571):

```
nosai-screen-samples 1
<mx> <my> <sx> <sy> <width> <height> <dpi>
```

Sette campi invece di sei, e il settimo è il DPI, cioè il terzo componente di
`GeometryShape`. Riusa quel tipo per rappresentare il regime della riga.

**Il file attuale, a sei campi, va rifiutato per versione** — non letto a metà,
non "adattato" mettendo DPI zero. `GeometryShape.IsKnown` è già `false` quando il
DPI è zero, e il commento dice perché: *unknown is not a shape*. Un file senza
versione è il vecchio formato, e il vecchio formato non sa dire il regime.

### 2. Il solutore rifiuta la miscela **prima** di adattare

Oggi la miscela si manifesta come un residuo alto alla fine, cioè nel modo meno
diagnostico possibile. Con il regime sulla riga:

- se i campioni portano più di un regime, il solutore **rifiuta e li conta per
  regime**, invece di adattare tutto insieme;
- il rifiuto è nominato — una costante `…Reason` nuova — e nomina i regimi
  trovati, così chi legge sa che deve svuotare e non che il client è cambiato.

### 3. Chi scrive i campioni scrive anche il regime

`ScreenProjectionAutoCalibrator` (riga ~281) e `ScreenProjectionWatcher`
(riga ~145) sono i due che appendono. Entrambi conoscono già la finestra da cui
leggono: prendono la `GeometryShape` da lì. Se il DPI non è leggibile, la riga
**non si scrive**: un campione di regime ignoto è peggio di un campione in meno,
perché entra nel fit e nessuno sa di che sessione sia.

### 4. Ciò che non fa, ed è scritto

- **Non indovina il regime delle dodici righe esistenti.** Non hanno il DPI, e
  ricostruirlo dai residui sarebbe scegliere i dati che confermano. Quelle righe
  vanno svuotate dall'operatore (`--screen-samples-clear`) e ricampionate: è il
  passo T-15, e non è tuo.
- **Non tocca `screen-projection.calibration`** né il suo formato: la
  calibrazione salvata già porta larghezza, altezza, DPI e modalità.
- **Non affronta lo zoom.** Se il client può cambiare scala a DPI e dimensioni
  costanti, il regime della riga non lo vede, e questo resta un buco noto. Va
  scritto nel doc-comment del formato: un controllo che non copre un caso deve
  dire quale, altrimenti la prossima persona crede che lo copra.

---

## OWN (file nuovi)

- `tests/NosAi.Runtime.Tests/ScreenSampleRegimeTests.cs`

## MODIFY

- `src/NosAi.Runtime/Perception/ScreenProjectionProbe.cs` — formato, versione, lettura
- `src/NosAi.Runtime/Perception/ScreenProjectionAutoCalibrator.cs` — scrive il regime
- `src/NosAi.Runtime/Perception/ScreenProjectionWatcher.cs` — idem
- `src/NosAi.Runtime/Perception/ScreenProjectionCalibration.cs` — il rifiuto della miscela

Nient'altro. Non `GeometryEpoch.cs`: il tipo si usa, non si cambia.

---

## Test

1. Un file senza intestazione di versione è rifiutato per versione, con il motivo
   nominato, e **nessun campione** ne esce.
2. Una riga a sei campi dentro un file dichiarato versione 1 è scartata, e lo
   scarto è visibile — non silenzioso.
3. Campioni di un solo regime si risolvono come prima: il comportamento buono non
   cambia.
4. Campioni di **due** regimi sono rifiutati prima del fit, e il rifiuto conta
   quanti campioni per regime.
5. **Il caso che conta**: due regimi che differiscono **solo** per il DPI, con
   larghezza e altezza identiche, sono riconosciuti come due. È esattamente la
   forma della miscela reale, ed è ciò che il formato vecchio non poteva vedere.
6. Un campione il cui DPI non è leggibile non viene scritto, e chi ha provato a
   scriverlo lo dice.

## Definition of done

- Build 0/0; le tre suite verdi, numeri riportati.
- Il numero di costanti `…Reason` in `src/NosAi.Runtime` **prima e dopo**: ne
  aggiungi una, e il registro dei rifiuti (task `refusal_reason_coverage`) deve
  vederla coperta da un test, non dichiarata come eccezione.
- Livello: **Integrated**. `Verified` vuole l'operatore che ricampiona (T-15).
