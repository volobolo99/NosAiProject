# NosAi — Recovery e Watchdog

## RecoveryController

RecoveryController gestisce il recupero dopo un fallimento di verifica o di runtime.

Strategie previste:

- `retry`
- `replan`
- `degraded_replan`
- `cooling`

Il contesto degli errori viene ridotto con `VRAMContextSlimmer`, che normalizza le parti variabili delle eccezioni e mantiene uno storico limitato.

Recovery può cambiare strategia, modalità runtime e budget operativo secondo policy e condizioni osservate. Una nuova strategia deve essere rivalutata nel normale ciclo operativo.

## Watchdog runtime

Il watchdog gestisce:

- `NORMAL`
- `DEGRADED`
- `RECOVERY`
- `COOLING`
- `STOPPED`

Può modificare budget di runtime/azioni e modalità operative in risposta a fallimenti e condizioni del sistema.

## Watchdog hardware

Il watchdog hardware supporta monitoraggio di temperatura CPU/GPU e, quando disponibile, frequenza I/O. La soglia termica predefinita è 80 °C.

Quando viene rilevato un rischio termico o hardware, il runtime può entrare in Cooling o in modalità degradata e successivamente riprendere secondo policy.

## Ciclo

```text
fallimento
   ↓
raccolta contesto
   ↓
context slimming
   ↓
analisi recovery
   ├─ retry
   ├─ replan
   ├─ degraded
   └─ cooling
   ↓
nuova valutazione
   ↓
nuovo ciclo runtime
```

## Principio

Recovery e Watchdog non sono più limitati a ridurre o bloccare l'esecuzione. Sono controller adattivi. Le azioni che richiedono autorizzazione continuano a rispettare i confini Guard/Trust/Safety configurati.
