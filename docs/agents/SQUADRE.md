# Squadre — ruoli, forma degli incarichi, e cosa costa

Due squadre. Claude decide, verifica e integra; DeepSeek scrive codice. Le regole
qui sotto non sono preferenze: ognuna viene da un fallimento misurato l'8
settembre 2026 e registrato in `logact.md`.

## Squadra Claude — `.claude/agents/`

| Ruolo | Modello | Che cosa fa | Quando chiamarlo |
|---|---|---|---|
| `esploratore` | haiku | estrae fatti come `file:riga`, o dimostra un'assenza con la ricerca che l'ha esclusa | **prima** di scrivere un incarico |
| `verificatore` | haiku | compila, esegue le suite, riporta i **nomi** dei falliti e li rilancia in isolamento | dopo ogni consegna, prima del commit |
| `revisore` | sonnet | cerca difetti nel diff consegnato, non nel rapporto di chi l'ha scritto | prima di integrare |

Il modello più economico dove il compito è meccanico: cercare e compilare non
richiedono giudizio. Il revisore è l'unico che giudica, ed è l'unico su sonnet.

**Orchestrazione e integrazione restano alla sessione principale.** Nessun agente
committa, nessuno modifica file esistenti.

## Squadra DeepSeek — ruoli come forma dell'incarico

DeepSeek non ha agenti persistenti: ha incarichi. Il «ruolo» è la forma
dell'incarico, e la forma decide se costa 40 mila token o 9 milioni.

| Ruolo | Perimetro | Consegna |
|---|---|---|
| `logica pura` | 1 file nuovo + 1 di test | classe `internal static`, nessun WPF, nessun disco, nessuna rete |
| `gestori` | 1 file nuovo `partial` + 1 di test | i gestori dell'interfaccia, che chiamano la logica pura |
| `test` | 1 solo file nuovo | i test di una logica già consegnata |
| `sonda` | `readOnly: true` | ispeziona e riferisce, non scrive |

## La regola che decide il costo

**Nel perimetro di un incarico non deve esserci alcun file esistente, e
l'incarico non deve nominare file che il lavoratore non può aprire.**

Misurato sugli stessi lavori:

| Perimetro | Esito |
|---|---|
| 4 file, uno esistente da 61 KB | morto leggendolo |
| 4 file, uno esistente da 40 KB, budget 14 chiamate | budget esaurito in analisi, **0 file** |
| idem, budget alzato a 30 chiamate | **di nuovo** esaurito, **0 file** |
| 3 file, **tutti da creare** | **3 giri, 7 chiamate su 30** |

Alzare il budget non ha cambiato nulla: il vincolo non era il tetto. Un file
esistente nel perimetro è un invito a esplorarlo anche quando l'incarico vieta di
leggere — e persino *nominare* un file esterno lo fa cercare (Q-151, primo giro,
esaurito inseguendo quattro percorsi fuori perimetro).

**Corollario operativo**: le modifiche ai file grandi le fa Claude. `MainWindow`
è una classe `partial`: i gestori nuovi vanno in un file nuovo, e il file da
61 KB non si apre mai.

## Forma di un incarico: breve, e senza nulla da cercare

L'incarico porta **dentro di sé** i dati, perché ogni file che il lavoratore apre
viene rispedito a ogni giro successivo. Un file da 30 KB letto al quinto giro di
cinquanta si paga cinquanta volte.

Dentro l'incarico, sempre:

1. **la sequenza esatta delle chiamate** — `write_file`, `write_file`,
   `edit_file`, `report_done` — e che la prima chiamata è la numero uno;
2. **i comandi alla lettera**, con i loro argomenti;
3. **le firme** degli helper da riusare, copiate;
4. **il testo esatto** dopo cui inserire, quando serve una modifica cieca;
5. **i `using`** già presenti e quelli da aggiungere;
6. **i nomi dei controlli** che l'incarico non crea ma userà;
7. **le regole di rifiuto**, ognuna con il campo che deve nominare;
8. **i test richiesti**, uno per riga, con cosa asserisce ciascuno.

E le tre trappole che hanno rotto la build, da ripetere ogni volta che valgono:
un commento XAML non può contenere due trattini di fila; un file di test che usa
`Path` o `File` ha bisogno di `using System.IO;`; `FirstOrDefault` restituisce
`string?`.

## Divieti permanenti al lavoratore

Sono nel prompt di sistema del ponte, e valgono senza ripeterli:

- mai citare un file, una riga o un simbolo non letto in quella sessione;
- mai dichiarare di aver compilato o eseguito test: non ha shell;
- quando una lettura è troncata, continuare dalla riga che il troncamento nomina
  prima di scrivere.

## Quello che nessun agente può scrivere

`logact.md` lo scrive l'orchestratore. Un agente **non conosce i propri token**:
il consumo è noto al server MCP solo dopo la chiamata. Farlo dichiarare
all'esecutore produrrebbe una stima, cioè un dato inventato.
