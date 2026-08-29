# NosAi — Strategia di test

## 1. Obiettivo

I test devono dimostrare che il percorso critico sia deterministico, che i contratti siano rispettati e che ogni obiettivo significativo raggiunto dal progetto funzioni completamente al livello di sviluppo dichiarato.

## 2. Regola di avanzamento

Un obiettivo significativo non è considerato completato quando il codice è soltanto presente. È completato quando le implementazioni coinvolte sono integrate, testate e funzionanti.

**Un test fallito blocca il passaggio all'obiettivo successivo.** Dopo la correzione, tutti i test pertinenti devono essere ripetuti con esito positivo.

## 3. Primo gate operativo

Il primo percorso reale da validare è:

`NosAi PC ↔ client NosTale ↔ rete ↔ Guard AI smartphone`

con acquisizione dei dati di base del client e del PC e con dashboard funzionante al 100% per il livello raggiunto.

Devono essere verificati almeno:

- avvio di NosAi sul PC;
- acquisizione dei dati di base del PC;
- collegamento al client NosTale tramite integrazione consentita e documentata;
- lettura e validazione dei dati di base necessari;
- avvio di Guard AI sullo smartphone;
- autenticazione e collegamento PC ↔ smartphone;
- ricezione dei primi dati di base;
- integrità, provenienza e freschezza dei dati;
- gestione di errore, disconnessione e riconnessione;
- dashboard coerente con lo stato reale del sistema.

## 4. Livelli di test

### Test unitari

Verificano singole funzioni e componenti: WorldState, ranking, simulazione, policy, Trust, Recovery, Watchdog, EventBus e contratti.

### Test di integrazione

Verificano le comunicazioni tra componenti e il ciclo osservazione → decisione → autorizzazione → esecuzione simulata → verifica → recupero.

### Test PC

Verificano il runtime reale sul PC, l'acquisizione dei dati hardware, il collegamento al client e le integrazioni che coinvolgono il PC.

### Test smartphone

Verificano avvio, stato, collegamento, autenticazione, ricezione dati e gestione degli errori di Guard AI.

### Test PC ↔ smartphone

Verificano l'intero percorso di comunicazione, inclusi autenticazione, sequenza, heartbeat, stato, dati, disconnessione e riconnessione.

### Test dashboard

Verificano che ogni funzione prevista al livello corrente sia realmente operativa e che dati, stati e controlli mostrati corrispondano al runtime reale.

La dashboard deve avanzare **insieme al resto del progetto**: una funzione non deve essere esposta come operativa se il corrispondente comportamento del runtime non è stato implementato e validato.

### Test di replay

Devono ricostruire un'esecuzione a partire da eventi e WorldState senza generare I/O live.

### Test hardware

Verificano soglie termiche, gestione I/O, Cooling e cambi di modalità con telemetria simulata e, quando richiesto dal gate, con hardware reale.

### Test end-to-end

Verificano il percorso completo dei componenti coinvolti nell'obiettivo, compresi PC, smartphone, client e dashboard quando pertinenti.

## 5. Criteri di superamento

Un gate è superato solo quando:

1. il codice interessato è integrato;
2. i test automatici pertinenti sono positivi;
3. i test manuali richiesti sono positivi;
4. le integrazioni coinvolte funzionano realmente;
5. PC e smartphone sono testati quando pertinenti;
6. la dashboard è completa e funzionante al 100% per il livello corrente;
7. non esistono regressioni bloccanti;
8. la documentazione descrive esattamente il comportamento osservato.

## 6. Test senza client reale

La maggior parte dei test unitari e delle integrazioni interne deve poter essere eseguita senza NosTale reale. Tuttavia, **il primo gate operativo richiede esplicitamente la validazione reale del collegamento al client**, oltre ai test PC, smartphone e PC ↔ smartphone pertinenti.

## 7. Registrazione dei risultati

Ogni gate significativo deve registrare almeno:

- versione del codice testato;
- ambiente di test;
- componenti coinvolti;
- test eseguiti;
- risultato;
- errori rilevati;
- correzioni applicate;
- nuova esecuzione dei test;
- decisione di superamento o blocco del gate.

## 8. Prestazioni

I valori prestazionali delle specifiche sono obiettivi fino alla loro misurazione. Non devono essere dichiarati raggiunti sulla sola base dell'esistenza del codice.

## 9. Regola finale

**Non si prosegue con il successivo obiettivo significativo finché il precedente non è completamente implementato, testato e verificato.**
