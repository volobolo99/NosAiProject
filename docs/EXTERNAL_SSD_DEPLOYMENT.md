# NosAi — Deployment su SSD esterno dedicato

**Versione progetto:** 1.0 Beta  
**Target:** PC Windows 11 / PlayAi  
**Supporto di riferimento:** Crucial X6 CT2000X6SSD9, 2 TB, USB-C / USB 3.2, fino a 800 MB/s  

## 1. Decisione architetturale

Il Crucial X6 viene trattato come **volume dati e runtime dedicato a NosAi**, non come semplice disco per backup. Il sistema operativo Windows resta sul disco interno del PC; il runtime NosAi, il codice operativo, l'ambiente Python, i modelli, SQLite, cache, log, artefatti e configurazioni persistenti vengono collocati sul volume esterno.

Questa scelta rende NosAi portabile tra PC compatibili, separa il progetto dai dati personali del sistema operativo e permette di mantenere una struttura di storage prevedibile.

Il dispositivo non deve essere considerato un disco di sistema/boot. La velocità dichiarata di 800 MB/s è una condizione di riferimento del produttore e non una garanzia del throughput applicativo: controller USB, porta, cavo, temperatura, carico e filesystem possono ridurre le prestazioni.

## 2. Filesystem e volume

Per il deployment Windows viene raccomandato **NTFS** come filesystem primario del volume NosAi. La ragione è la compatibilità con permessi, ACL e semantica dei file richiesta da un runtime Windows completo. Il volume deve essere inizializzato una sola volta e non deve essere riformattato automaticamente dagli script di avvio.

Etichetta consigliata del volume: `NOSAI-SSD`.

Lo spazio non deve essere usato da software estraneo al progetto. Il sistema può comunque riservare una piccola area tecnica per file temporanei necessari al funzionamento del sistema operativo; nessun dato applicativo NosAi deve essere scritto sul disco interno se può essere mantenuto sul volume dedicato.

## 3. Struttura fisica/logica

```text
<NOSAI-SSD>:\NosAi\
├── app\                  # repository/runtime applicativo
├── runtime\              # Python/runtime locale e toolchain necessaria
├── models\               # modelli AI e relativi manifest/hash
├── data\
│   ├── db\               # SQLite e WAL/SHM
│   ├── state\            # stato persistente
│   ├── evidence\         # evidenze verificate append-only
│   └── exports\          # esportazioni utente
├── cache\                # cache ricostruibili
├── logs\                 # log e trace
├── temp\                 # temporanei NosAi
├── backups\              # backup locali del volume
├── config\               # configurazione persistente
└── tools\                # diagnostica e utility del deployment
```

La configurazione runtime deve usare un **root path assoluto risolto all'avvio** dal volume NosAi. Sono vietati path applicativi relativi che dipendano dalla directory corrente del processo.

## 4. Principio di portabilità

NosAi deve poter essere avviato con una lettera di unità diversa. Il launcher individua il volume tramite etichetta `NOSAI-SSD`, verifica che esista `NosAi\config` e costruisce tutti i percorsi da quella radice.

Non deve essere hard-codificato `D:\`, `E:\` o altra lettera.

Il launcher deve rifiutare l'avvio se:

1. il volume dedicato non è presente;
2. l'etichetta non corrisponde;
3. il percorso NosAi non è leggibile/scrivibile;
4. lo spazio libero è sotto la soglia configurata;
5. è rilevata un'operazione concorrente di manutenzione/backup incompatibile.

## 5. Cosa va sul SSD

Devono risiedere sul volume dedicato:

- codice NosAi;
- ambiente runtime locale;
- dipendenze Python/C++ necessarie e compatibili con la piattaforma;
- modelli AI;
- SQLite e relativi file `-wal` / `-shm` quando WAL è attivo;
- memoria persistente e stato;
- evidence store;
- cache;
- log e trace;
- configurazioni utente;
- risultati benchmark;
- pacchetti/artefatti locali di build e test.

Le dipendenze di sistema realmente necessarie (driver NVIDIA, driver USB, componenti Windows e runtime che richiedono installazione globale) restano gestite dal sistema operativo. Non vengono duplicati inutilmente sul SSD.

## 6. SQLite sul SSD

Il repository utilizza SQLite in WAL. La documentazione SQLite conferma che WAL mantiene file `-wal` e `-shm` accanto al database e che il WAL non è progettato per filesystem di rete. Il volume NosAi deve quindi essere un filesystem locale montato dal PC, non una condivisione SMB/NFS. citeturn0search3turn0search10

Per massima robustezza del deployment esterno, la configurazione SQLite deve essere resa esplicita e centralizzata. Il profilo operativo raccomandato è:

- `journal_mode=WAL`;
- `synchronous=FULL` per la persistenza critica;
- timeout di busy configurabile;
- checkpoint controllati;
- backup SQLite eseguiti con API SQLite o procedura consistente, mai copiando arbitrariamente un DB WAL mentre è in uso.

SQLite documenta `FULL` come modalità che assicura la durabilità ACID in WAL, mentre `NORMAL` offre maggiore velocità ma può perdere l'ultima transazione dopo perdita di alimentazione. citeturn0search1

Il progetto attuale usa `synchronous=NORMAL`; questa impostazione deve quindi essere trattata come **profilo prestazionale da rivedere** per lo storage esterno, non come valore di sicurezza definitivo. fileciteturn5file0L2-L2

## 7. Protezione da scollegamento accidentale

Un SSD USB non deve essere scollegato durante scritture. Il runtime deve avere uno stato `STORAGE_SAFE / STORAGE_BUSY / STORAGE_ERROR` osservabile dal Watchdog.

Se il volume scompare:

1. bloccare nuove operazioni di scrittura;
2. sospendere le attività non essenziali;
3. generare un evento critico;
4. tentare il riconoscimento/reconnect solo quando il volume ritorna disponibile;
5. se la persistenza non può essere garantita, entrare in modalità degradata e arrestare in sicurezza le funzioni che richiedono storage persistente.

Il progetto non deve promettere immunità alla rimozione a caldo. SQLite stesso documenta che guasti di storage, flush non affidabili e rimozione improvvisa possono causare perdita o corruzione; WAL riduce alcuni rischi ma non li elimina. citeturn0search2

## 8. Prestazioni

Il Crucial X6 è sufficiente come storage dedicato per codice, modelli, database, log e cache, ma non deve essere assunto come equivalente a un NVMe interno.

Il runtime deve misurare almeno:

- throughput sequenziale lettura/scrittura;
- IOPS random quando rilevanti;
- latenza I/O;
- tempo di apertura DB;
- latenza checkpoint SQLite;
- spazio libero;
- temperatura/telemetria disponibile;
- errori I/O;
- durata delle operazioni di caricamento modello.

Le impostazioni Auto-Setting devono usare queste misure insieme al profilo hardware già previsto dal progetto, invece di assumere sempre gli 800 MB/s nominali. Il repository già richiede un profilo hardware normalizzato e Auto-Setting deterministico. fileciteturn3file0L2-L2

## 9. Modelli AI e cache

`models\` contiene solo artefatti identificati da manifest con versione, dimensione e hash. `cache\` deve essere sempre ricostruibile: la cancellazione della cache non deve distruggere conoscenza verificata o configurazione.

I modelli grandi non devono essere copiati sul disco interno come comportamento normale. Se un backend AI richiede esplicitamente una cache di sistema non relocabile, deve essere registrata come eccezione osservabile nella diagnostica.

## 10. Avvio e arresto

Il launcher deve eseguire, nell'ordine:

```text
detect volume
 → validate filesystem/path
 → validate free space
 → load configuration
 → validate runtime
 → validate models/manifests
 → initialize storage
 → hardware Auto-Setting
 → start Watchdog
 → start NosAi
```

L'arresto deve essere inverso:

```text
stop new work
 → finish/abort safely
 → flush critical events
 → checkpoint/close SQLite
 → persist runtime state
 → stop Watchdog
 → exit
```

Il volume deve essere espulso da Windows solo dopo che NosAi è terminato e il sistema operativo indica che il dispositivo può essere rimosso.

## 11. Backup

Il fatto che il SSD sia dedicato non equivale a ridondanza. `backups\` contiene backup locali temporanei; una copia realmente protettiva deve essere conservata su un secondo supporto o destinazione distinta.

La procedura di backup deve privilegiare:

1. configurazioni;
2. database/stato/evidence;
3. manifest e metadati modelli;
4. modelli eventualmente non facilmente riscaricabili;
5. log utili alla diagnostica.

## 12. Integrazione con l'architettura NosAi

Il nuovo storage layer si inserisce sotto `Memoria`, `EventBus/Trace`, `Recovery`, `Watchdog` e `Control Center`, senza creare un canale alternativo per Guard/Trust/Safety/Executor.

La separazione delle responsabilità già definita dall'architettura rimane invariata. fileciteturn2file0L2-L2

## 13. Criteri di accettazione PC

Il deployment su SSD è considerato riuscito solo quando tutti i test passano:

- avvio con lettera di unità diversa;
- volume assente → avvio rifiutato in modo sicuro;
- volume read-only → avvio rifiutato o modalità esplicitamente read-only;
- test lettura/scrittura;
- stress SQLite WAL;
- chiusura normale e riapertura;
- simulazione perdita del volume durante una fase non critica;
- verifica recovery dopo riconnessione;
- benchmark I/O;
- verifica che modelli, log, cache e DB non finiscano sul disco interno senza una ragione documentata;
- test completo NosAi sul PC di riferimento.

**Regola di progetto:** non si procede alla fase successiva finché i test PC e, quando coinvolta l'app, Smartphone non hanno dato esito positivo.

## 14. Implementazione prevista

Componenti da aggiungere:

- `nosai/storage/volume.py` — discovery e validazione del volume dedicato;
- `nosai/storage/paths.py` — root path e layout canonico;
- `nosai/storage/health.py` — spazio, accessibilità e stato I/O;
- `nosai/storage/sqlite_policy.py` — policy SQLite centralizzata;
- `scripts/windows/nosai_bootstrap.ps1` — provisioning non distruttivo e launcher;
- test dedicati in `tests/storage/`.

Questi componenti devono essere implementati senza cancellare o sostituire i moduli esistenti. L'integrazione deve essere incrementale e coperta da test.
