# NosAi — Registro delle modifiche

## 1.0 Beta

### Documentazione

- Documentazione principale resa italiana.
- Aggiunti metadati, requisiti, strategia di test, sicurezza, contributi e glossario.
- Consolidata l'architettura di sistema.
- Documentato il modello RecoveryController + Watchdog adattivo.
- Stabilita la regola linguistica: italiano per la documentazione, inglese solo dove tecnicamente necessario.
- Aggiunta e consolidata la specifica di deployment su SSD esterno dedicato.
- Integrati nel modello architetturale il Crucial X6 CT2000X6SSD9, il volume `NOSAI-SSD`, il layout canonico, la policy SQLite e il provisioning PC-Phone.

### Runtime

- Aggiunto il riduttore di contesto orientato alla VRAM.
- Aggiunto RecoveryController adattivo.
- Aggiunte modalità runtime del watchdog.
- Aggiunto watchdog hardware con gestione termica e I/O opzionale.
- Aggiunto storage discovery del volume NosAi tramite label Windows, indipendente dalla lettera di unità.
- Aggiunta validazione non distruttiva del volume: NTFS, accessibilità e spazio minimo.
- Aggiunto layout storage canonico sul volume dedicato.
- Aggiunta policy SQLite centralizzata con WAL, `synchronous=FULL`, busy timeout, cache, limite WAL e incremental vacuum.
- Aggiornato `NosAiSqliteLogger` per utilizzare la policy centralizzata.
- Aggiunto provisioning ADB della phone Guard AI con verifica device autorizzato e gestione dell'APK locale.

### Validazione

- Il deployment SSD e il percorso PC-Phone restano soggetti a validazione fisica sul PC e sullo smartphone reali.
- Nessuna prestazione dichiarata del Crucial X6 viene assunta come garantita: throughput e latenza devono essere misurati.
- Nessun APK, handshake o wire protocol non presente viene dichiarato produttivo senza test di interoperabilità.
