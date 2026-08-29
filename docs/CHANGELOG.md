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
- Integrate nella documentazione le ottimizzazioni di memoria, throttling adattivo, delta encoding e requisiti di benchmark della specifica definitiva di performance.

### Runtime

- Aggiunto il riduttore di contesto orientato alla VRAM.
- Aggiunto RecoveryController adattivo.
- Aggiunte modalità runtime del watchdog.
- Aggiunto watchdog hardware con gestione termica e I/O opzionale.
- Aggiunto `AdaptiveThrottler` con `AdaptiveLimits`, `ResourcePlan` e modalità `NORMAL`, `COOLING`, `DEGRADED` e `STOPPED`.
- Aggiunto storage discovery del volume NosAi tramite label Windows, indipendente dalla lettera di unità.
- Aggiunta validazione non distruttiva del volume: NTFS, accessibilità e spazio minimo.
- Aggiunto layout storage canonico sul volume dedicato.
- Aggiunta policy SQLite centralizzata con WAL, `synchronous=FULL`, busy timeout, cache, limite WAL e incremental vacuum.
- Aggiornato `NosAiSqliteLogger` per utilizzare la policy centralizzata.
- Aggiunto provisioning ADB della phone Guard AI con verifica device autorizzato e gestione dell'APK locale.
- Integrato `NosAiCryptoAuthManager` per challenge monouso da 32 byte e verifica RSA-2048/SHA-256/PKCS#1 v1.5 della firma Guard AI.
- Consolidato il framing binario PC↔Phone a 12 byte e il controllo della sequenza.
- Aggiunto delta encoding deterministico del WorldState al livello di framing, separato da autenticazione e cifratura.
- Aggiunto `NosAiOnboardingEngine` per provisioning ADB isolato, forwarding TCP 6100 e costruzione del `SESSION_HELLO`.

### Test e validazione

- Aggiunti test per autenticazione RSA, consumo monouso della challenge e fail-closed su firma non valida.
- Aggiunti test per framing `SESSION_HELLO` e blocco del provisioning in assenza dell'ADB isolato.
- Aggiunti test per `AdaptiveThrottler` e delta encoding del WorldState.
- Il deployment SSD e il percorso PC-Phone restano soggetti a validazione fisica sul PC e sullo smartphone reali.
- Nessuna prestazione dichiarata del Crucial X6 viene assunta come garantita: throughput e latenza devono essere misurati.
- AES-GCM-256, heartbeat temporizzato, timeout fail-closed da 2000 ms, APK reale e interoperabilità con smartphone fisico non sono dichiarati produttivi finché non vengono implementati e validati.
- Le ottimizzazioni C#/.NET 8 basate su `ArrayPool`, `Memory`, `Span` e caricamento modelli on-demand restano da integrare e benchmarkare nel percorso nativo.
