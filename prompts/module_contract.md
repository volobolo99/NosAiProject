# Prompt: contratto di modulo

## Quando si usa
Il contratto di modulo viene formulato nella prima fase del protocollo di produzione, prima di creare qualsiasi modulo. Claude, nel ruolo di orchestratore, lo formula.

## Campi del contratto
- **Nomi dei file bersaglio**: Elenco dei file che il modulo deve generare.
- **Firme delle funzioni e delle struct**: Definizione delle funzioni e strutture dati che il modulo deve implementare.
- **Allineamento byte e dimensioni dei buffer**: Specifiche per i moduli nativi riguardanti l'allineamento byte e le dimensioni dei buffer.
- **Precondizioni e postcondizioni di ogni funzione**: Condizioni necessarie e risultati attesi per ogni funzione.
- **Vincoli di memoria**: Limiti di memoria che il modulo deve rispettare.
- **Eccezioni previste**: Elenco delle eccezioni che il modulo deve gestire.

## Divieti
- **Nessuna API non verificata**: Il contratto non può contenere API non verificate.
- **Nessun campo vago o non deciso**: Tutti i campi del contratto devono essere specificati e definiti.
- **Nessun campo implementazioni**: Il contratto contiene solo firme e vincoli, senza implementazioni.

## Passo successivo
Dopo aver formulato il contratto, lo stato del contratto viene registrato in `contracts/ledger.json` con lo stato `DRAFT`. Il modello locale genera lo scheletro del modulo utilizzando il tool `local_generate_skeleton`.
