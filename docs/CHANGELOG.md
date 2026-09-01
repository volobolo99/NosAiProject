# NosAi — Registro delle modifiche

## 1.0 Beta

### Runtime

- Integrati i blocchi Gate 4 per Progression Engine V2: DAG missioni, sblocco SP1/SP2, evidenza Beta-Binomiale, selezione UCB1/MAUT e Knowledge Base con ciclo di vita Mastery.
- Aggiunto `Gate4TestRunner` con prove automatiche su prerequisiti DAG, convergenza bayesiana, UCB1, Mastery, pipeline SP e determinismo.
- Integrati i blocchi Gate 5 per Provider Router local-first, policy StrictLocalOnly, provider euristico/locale/cloud, hardware baseline, discovery `NOSAI-SSD` e Eye AI View.
- Completato il Control Center REST loopback su `127.0.0.1`, con endpoint di stato/Eye View e coda esplicita dei comandi verso il sink del runtime.
- Aggiunto `Gate5IntegratedEngine` e `Gate5TestRunner` con prove automatiche su isolamento cloud, non-esecuzione dei provider, soglia termica, storage, stratificazione Eye AI e REST.
- Il `Program` principale espone `--gate4-test` e `--gate5-test` senza introdurre entry point multipli nel progetto.

### Documentazione

- Aggiunta la documentazione del controllo del personaggio: `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` (confine di dominio, canale di attuazione, invarianti `DOMAIN-xx`), `CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` (commit point, occlusione, precedenza dell'operatore, griglia di mappa, finestre di verifica) e `CONTROLLO_PERSONAGGIO_ROADMAP.md` (ordine dei lavori e ripartizione Claude/Cursor).
- Aggiunto `ADR-0019`: l'attuazione del controllo del personaggio avviene per input del sistema operativo. Scopre una scelta che `ADR-0014` aveva lasciato aperta e non revoca alcun permesso; l'evasione delle rilevazioni resta fuori come da `ADR-0014`.
- Corretto `SOURCE_OF_TRUTH.md`: l'elenco degli ADR accettati si fermava a `ADR-0014` e ora copre fino a `ADR-0019`.
- Registrato che la calpestabilità statica delle mappe è un dato del client e non un'inferenza percettiva, con la semantica dei bit di cella e la regola di invalidazione sulla build.
- Documentazione principale resa italiana.
- Aggiunti metadati, requisiti, strategia di test, sicurezza, contributi e glossario.
- Consolidata l'architettura di sistema.
- Documentato il modello RecoveryController + Watchdog adattivo.
- Stabilita la regola linguistica: italiano per la documentazione, inglese solo dove tecnicamente necessario.
- Aggiunta e consolidata la specifica di deployment su SSD esterno dedicato.
- Integrati nel modello architetturale il Crucial X6 CT2000X6SSD9, il volume `NOSAI-SSD`, il layout canonico, la policy SQLite e il provisioning PC-Phone.
- Integrate nella documentazione le ottimizzazioni di memoria, throttling adattivo, delta encoding e requisiti di benchmark della specifica definitiva di performance.

### Test e validazione

- I test Gate 4 e Gate 5 sono ora invocabili dal runtime principale, ma la loro presenza **non equivale al superamento operativo dei gate**.
- Il progetto resta vincolato alla regola di avanzamento del Gate 1: PC ↔ client NosTale ↔ rete ↔ Guard AI smartphone deve essere verificato end-to-end prima di dichiarare avanzamento operativo.
- Il deployment SSD e il percorso PC-Phone restano soggetti a validazione fisica sul PC e sullo smartphone reali.
- Nessuna prestazione dichiarata del Crucial X6 viene assunta come garantita: throughput e latenza devono essere misurati.
- Le ottimizzazioni C#/.NET 8 basate su `ArrayPool`, `Memory`, `Span` e caricamento modelli on-demand restano da integrare e benchmarkare nel percorso nativo.

### Build e dipendenze

- `System.Management` è passato dalla versione `8.0.1` alla `8.0.0` nel commit `d5c6731`. La modifica **non è dichiarata nel messaggio di quel commit**, che parla solo del pin di `StartupObject`: viene registrata qui perché una modifica di dipendenza non resti invisibile in review.
- Motivo della correzione: la versione `8.0.1` **non esiste su NuGet**. Il restore la risolveva silenziosamente alla `9.0.0` emettendo `NU1603`, quindi il progetto compilava contro una major diversa da quella dichiarata. La `8.0.0` è la versione realmente pubblicata della linea 8.
- Il pin di `StartupObject` nello stesso commit ha reso irraggiungibili tutti i `Main` diversi da `Program.Main`, orfanando le self-test di `MasterHostTestRunner`. Sono state riattivate tramite il flag `--host-test`.
