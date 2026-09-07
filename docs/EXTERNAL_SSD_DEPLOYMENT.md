# NosAi — Distribuzione su SSD esterno dedicato

**Versione progetto:** 1.0 Beta  
**Obiettivo:** PC Windows 11 / Play AI  
**Supporto di riferimento:** Crucial X6 CT2000X6SSD9, 2 TB, USB-C / USB 3.2, fino a 800 MB/s

## 1. Decisione architetturale

Il Crucial X6 viene trattato come **volume dati e runtime dedicato a NosAi**, non come semplice disco di backup. Windows resta sul disco interno del PC; runtime NosAi, codice operativo, ambiente Python, modelli, SQLite, cache, log, artefatti e configurazioni persistenti vengono collocati sul volume esterno.

Questa scelta rende NosAi portabile tra PC compatibili, separa il progetto dai dati personali del sistema operativo e permette una struttura di archiviazione prevedibile.

Il dispositivo non deve essere considerato disco di sistema o di avvio. La velocità dichiarata di 800 MB/s è un riferimento del produttore e non una garanzia del rendimento applicativo.

## 1-bis. Cosa smette di funzionare senza il volume — misurato il 2026-09-07

Questo documento descrive il volume come una scelta di distribuzione. È anche una
**dipendenza di funzionamento**, e la macchina di sviluppo attuale non la
soddisfa: `Get-Volume` riporta il solo `C:` etichettato `Acer`, nessun volume
`NOSAI-SSD` collegato.

`VolumeLocator.TryResolve` cerca per **etichetta** e non ha ripiego. La sua stessa
doc-comment spiega perché, ed è una decisione giusta: «un journal atterrato in
silenzio su un disco diverso da quello che l'operatore gli ha dedicato è un
journal che l'operatore non sa più trovare, salvare, né su cui ragionare in
termini di durabilità».

Quattro cose ne dipendono, e senza il volume nessuna delle quattro conserva nulla:

| Cosa | Chi la apre | Conseguenza senza volume |
|---|---|---|
| Journal degli eventi con catena di hash | `SqliteEventJournal`, da `NosAiHost` | Gate 1 non ha storia verificabile |
| Mappe persistite | `MapModelStore`, da `MapReconstructionSource` | ogni mappa scoperta si riscopre da capo |
| Registro degli esiti d'azione | `ActionOutcomeLedgerStore`, da `--scout`, `--engage`, `--autoplay`, `--recover` | **ogni atto mai eseguito ha registrato zero righe** |
| Catalogo di riferimento del gioco | `GameReferenceLocator` | il catalogo non stabilisce alcun vnum come mostro |

L'ultima riga è quella che si vede a occhio: `--world-replay` stampa
`reference catalog: nosai_ssd_not_found` in testa a ogni esecuzione.

**Non è un difetto del codice.** I comandi avvisano e proseguono
(`[WARN] action_outcome_ledger_unavailable:…`), con il commento accanto che dice
esplicitamente perché la mancanza del registro non deve mai diventare un motivo
di rifiuto. È il comportamento voluto. Ma la conseguenza va detta: **finché
nessun volume `NOSAI-SSD` è collegato, questo progetto non ricorda niente fra
un'esecuzione e l'altra**, e la capacità che `CLAUDE.md` chiama *learn from
failures* non ha dove scrivere.

**Cosa serve, in concreto.** Non un SSD da 2 TB: serve un volume la cui etichetta
sia esattamente `NOSAI-SSD`. Una chiavetta, una partizione, un disco esterno
qualsiasi — il codice cerca l'etichetta, non il modello. Il resto della struttura
in § 3 lo creano gli strumenti.

Ai bersagli questo toglie meno di quanto sembri: `TargetEstablishment` stabilisce
l'ostilità anche dal fatto osservato di un colpo subito, che non passa dal
catalogo. Senza catalogo resta però l'unica via, quindi un mostro che non ha
ancora attaccato non è autorizzabile.

---

## 2. File system e volume

Per Windows viene raccomandato **NTFS** come file system primario. Il volume deve essere inizializzato una sola volta e non deve essere riformattato automaticamente dagli script di avvio.

Etichetta consigliata: `NOSAI-SSD`.

## 3. Struttura

```text
<NOSAI-SSD>:\NosAi\
├── app\                  # repository/runtime applicativo
├── runtime\              # runtime Python e strumenti
├── models\               # modelli AI e manifest/hash
├── data\
│   ├── db\               # SQLite e WAL/SHM
│   ├── state\            # stato persistente
│   ├── evidence\         # evidenze verificate append-only
│   └── exports\          # esportazioni utente
├── cache\                # cache ricostruibili
├── logs\                 # log e trace
├── temp\                 # temporanei NosAi
├── backups\              # backup locali
├── config\               # configurazione persistente
└── tools\                # diagnostica e utilità
```

Il percorso radice deve essere risolto all'avvio dal volume NosAi. Non devono essere utilizzati percorsi applicativi relativi dipendenti dalla directory corrente del processo.

## 4. Portabilità

NosAi deve poter essere avviato con una lettera di unità diversa. Il launcher individua il volume tramite etichetta `NOSAI-SSD`, verifica `NosAi\config` e costruisce tutti i percorsi dalla radice individuata.

Non deve essere codificata una lettera come `D:\` o `E:\`.

L'avvio deve essere rifiutato se il volume non è presente, l'etichetta non corrisponde, il percorso non è leggibile/scrivibile, lo spazio libero è insufficiente o è presente un'operazione concorrente incompatibile.

## 5. Contenuto del volume

Devono risiedere sul volume dedicato codice, runtime locale, dipendenze compatibili, modelli, SQLite, memoria e stato persistenti, archivio delle evidenze, cache, log, trace, configurazioni, risultati dei test e artefatti locali.

Driver Windows, driver NVIDIA, driver USB e componenti di sistema che richiedono installazione globale rimangono gestiti dal sistema operativo.

## 6. SQLite

Il repository utilizza SQLite con WAL. Il volume deve essere un file system locale montato dal PC e non una condivisione di rete.

Il profilo operativo raccomandato deve rendere espliciti modalità WAL, sincronizzazione per i dati critici, timeout di occupazione, checkpoint e procedure di backup coerenti.

L'impostazione attuale `synchronous=NORMAL` deve essere trattata come profilo prestazionale da rivalutare per la persistenza critica, non come garanzia definitiva di durabilità.

## 7. Scollegamento accidentale

Il runtime deve esporre lo stato `STORAGE_SAFE / STORAGE_BUSY / STORAGE_ERROR` al Watchdog.

Se il volume scompare:

1. bloccare nuove scritture;
2. sospendere le attività non essenziali;
3. generare un evento critico;
4. tentare il riconoscimento quando il volume ritorna disponibile;
5. se la persistenza non è garantibile, entrare in modalità degradata e arrestare in sicurezza le funzioni dipendenti dallo storage.

Non deve essere promessa immunità alla rimozione improvvisa.

## 8. Prestazioni

Il rendimento del Crucial X6 deve essere misurato sul sistema reale. Il runtime deve rilevare almeno throughput sequenziale, IOPS quando rilevanti, latenza I/O, apertura del database, checkpoint SQLite, spazio libero, temperatura disponibile, errori I/O e tempi di caricamento dei modelli.

Le impostazioni automatiche devono usare queste misure insieme al profilo hardware invece di assumere sempre il valore nominale di 800 MB/s.

## 9. Modelli e cache

`models\` contiene artefatti identificati da manifest con versione, dimensione e hash. `cache\` deve essere ricostruibile: cancellarla non deve distruggere conoscenza verificata o configurazione.

## 10. Avvio e arresto

Avvio:

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

Arresto:

```text
stop new work
 → finish/abort safely
 → flush critical events
 → checkpoint/close SQLite
 → persist runtime state
 → stop Watchdog
 → exit
```

Il volume deve essere espulso da Windows solo dopo la terminazione di NosAi e quando il sistema operativo indica che il dispositivo può essere rimosso.

## 11. Backup

Un SSD dedicato non costituisce ridondanza. Una copia realmente protettiva deve essere conservata su un secondo supporto o destinazione distinta.

Priorità: configurazioni, database/stato/evidenze, manifest e metadati dei modelli, modelli non facilmente recuperabili e log diagnostici.

## 12. Integrazione con NosAi

Lo strato di archiviazione si inserisce sotto Memoria, EventBus/Trace, Recovery, Watchdog e Control Center senza creare un canale alternativo per Guard, Trust, Safety o Executor.

## 13. Criteri di accettazione PC

Il deployment è riuscito solo quando risultano positivi: avvio con lettere diverse, gestione volume assente, volume in sola lettura, lettura/scrittura, stress SQLite WAL, chiusura e riapertura, simulazione perdita volume, recovery, benchmark I/O, verifica delle destinazioni dei dati e test completo sul PC di riferimento.

Quando è coinvolta l'applicazione smartphone, devono essere positivi anche i test richiesti sullo smartphone.

**Regola:** non si procede alla fase successiva finché i test richiesti non hanno dato esito positivo.

## 14. Implementazione prevista

Componenti previsti:

- `nosai/storage/volume.py` — individuazione e validazione del volume;
- `nosai/storage/paths.py` — radice e struttura canonica;
- `nosai/storage/health.py` — spazio, accessibilità e stato I/O;
- `nosai/storage/sqlite_policy.py` — policy SQLite centralizzata;
- `scripts/windows/nosai_bootstrap.ps1` — provisioning non distruttivo e avvio;
- test dedicati in `tests/storage/`.

Questi componenti devono essere integrati senza cancellare o sostituire moduli esistenti.
