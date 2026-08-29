# NosAiProject

Implementazione sorgente di **NosAi**, runtime di intelligenza artificiale per NosTale.

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane **1.0 Beta** finché il creatore non richiede esplicitamente un cambiamento.

## Deployment PC

Il runtime PC è progettato per essere installato sul **Crucial X6 CT2000X6SSD9 da 2 TB**, collegato via USB-C/USB 3.2. Il volume dedicato usa l'etichetta `NOSAI-SSD` e una root `NosAi\`; Windows rimane sul disco interno.

Il sistema individua il volume tramite etichetta, non tramite una lettera fissa, e valida filesystem NTFS, accessibilità e spazio disponibile. Il bootstrap Windows è non distruttivo e non formatta il dispositivo.

Codice, runtime locale, modelli, SQLite/WAL, memoria persistente, evidence, log, cache, configurazioni e artefatti NosAi sono destinati al volume dedicato. Driver e dipendenze realmente globali di Windows restano gestiti dal sistema operativo.

La policy SQLite è centralizzata: WAL, `synchronous=FULL` per la persistenza critica, busy timeout, cache, limite WAL e incremental vacuum.

## Stato del progetto

Il repository è la sorgente di sviluppo ufficiale. Il repository legacy `volobolo99/NosAi` è utilizzato esclusivamente come riferimento: il codice viene analizzato e reimplementato selettivamente, senza copia indiscriminata.

Il runtime realizza un ciclo autonomo controllato: osservazione → orchestrazione → autorizzazione → esecuzione → verifica → nuova osservazione → recupero e ripianificazione adattivi.

Sono presenti EventBus tipizzato e bounded, WorldState versionato, riduzione del contesto per VRAM, RecoveryController adattivo, circuit breaker, watchdog hardware/runtime, nucleo di cifratura per sessioni effimere, logger SQLite per sessioni/traiettorie e controller Miniland tramite adapter.

È presente inoltre la fondazione di deployment su SSD dedicato e provisioning ADB della phone Guard AI (`com.nosai.guard`). Il wire protocol PC-Phone completo e il fail-closed 1000/2000 ms della specifica restano da integrare e validare.

## Documentazione

- `docs/METADATI_PROGETTO.md` — metadati ufficiali.
- `docs/REGOLE_PROGETTO.md` — regole e vincoli del progetto.
- `docs/ARCHITETTURA.md` — architettura e comunicazioni, incluso storage SSD e PC-Phone.
- `docs/STATO_IMPLEMENTAZIONE.md` — registro dell'implementazione e validazione.
- `docs/EXTERNAL_SSD_DEPLOYMENT.md` — specifica del deployment Crucial X6.
- `docs/ROADMAP.md` — roadmap e traguardi.
- `docs/REQUISITI.md` — requisiti funzionali e non funzionali.
- `docs/CONTRIBUTING.md` — regole per contribuire.
- `docs/TESTING.md` — strategia e procedure di test.
- `docs/SICUREZZA.md` — modello di sicurezza.
- `docs/OSSERVABILITA.md` — EventBus, trace, audit e replay.
- `docs/RECOVERY_WATCHDOG.md` — recupero adattivo e controllo hardware/runtime.
- `docs/PERCEZIONE.md` — pipeline di percezione e stato di implementazione.
- `docs/RETE_LAN.md` — comunicazione locale/LAN.
- `docs/LLM_PROVIDER.md` — provider decisionali e instradamento.
- `docs/CONTRATTI.md` — contratti tra componenti.
- `docs/CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md` — trasporto sicuro e chiavi effimere.
- `docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md` — persistenza e fondazioni Shared Memory.
- `docs/GLOSSARIO.md` — terminologia ufficiale.
- `docs/CHANGELOG.md` — storico delle modifiche.

## Principi

1. Sicurezza e autorizzazione esplicita.
2. Percorso critico deterministico e verificabile.
3. WorldState come fonte canonica dello stato corrente.
4. Separazione tra decisione ed esecuzione.
5. Recupero adattivo e verifica a ciclo chiuso.
6. Osservabilità e provenienza dei dati.
7. Persistenza separata dallo stato operativo canonico.
8. Adapter espliciti per le integrazioni esterne.
9. Testabilità senza client di gioco reale.
10. Integrazioni live dietro traguardi espliciti.
11. Storage dedicato validato prima dell'avvio del runtime PC.
12. Nessuna fase successiva senza esito positivo dei test PC/Smartphone pertinenti.
