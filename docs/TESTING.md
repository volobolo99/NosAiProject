# NosAi — Strategia di test

## Obiettivo

I test devono dimostrare che il percorso critico sia deterministico, che i contratti siano rispettati e che il runtime possa essere verificato senza un client di gioco reale.

## Livelli

### Test unitari

Verificano singole funzioni e componenti: WorldState, ranking, simulazione, policy, Trust, Recovery, Watchdog, EventBus e contratti.

### Test di integrazione

Verificano le comunicazioni tra componenti e il ciclo osservazione → decisione → autorizzazione → esecuzione simulata → verifica → recupero.

### Test di replay

Devono ricostruire un'esecuzione a partire da eventi e WorldState senza generare I/O live.

### Test hardware

Verificano soglie termiche, gestione I/O, Cooling e cambi di modalità con telemetria simulata.

## Criteri

Un cambiamento è accettabile quando i test esistenti rimangono verdi, i nuovi comportamenti hanno test dedicati e nessuna integrazione non validata viene presentata come produttiva.

## Comandi

I comandi effettivi devono essere mantenuti aggiornati nel progetto e nella configurazione di sviluppo. I test non devono dipendere dalla presenza di NosTale in esecuzione.
