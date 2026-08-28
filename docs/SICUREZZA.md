# NosAi — Sicurezza

## Principi

La sicurezza deve essere esplicita, osservabile e verificabile. Nessun componente deve trasformare automaticamente dati non attendibili in esecuzione non controllata.

## Confini

- I provider decisionali non eseguono direttamente.
- Percezione, Simulazione e Ranking non eseguono direttamente.
- Executor/Game Adapter è il confine tecnico di esecuzione.
- Le azioni protette seguono il percorso Guard/Trust/Safety configurato.
- Un risultato non verificato non è considerato successo.
- EventBus e subscriber non costituiscono un canale alternativo di esecuzione.

## Recovery e Watchdog

Recovery e Watchdog sono controller runtime attivi. Possono cambiare strategia, modalità e budget secondo policy e telemetria. Quando un cambiamento comporta un'azione protetta, l'azione deve comunque attraversare i relativi controlli di autorizzazione.

## Rete

Le comunicazioni LAN devono usare messaggi tipizzati, autenticazione, controllo di sequenza e protezione da replay prima del bring-up produttivo.

## Segreti

Credenziali, chiavi e token non devono essere memorizzati nel repository. Devono essere forniti tramite configurazione sicura dell'ambiente.

## Integrazione live

L'accesso a client di gioco e hardware reale deve rimanere dietro gate espliciti di validazione e rilascio.
