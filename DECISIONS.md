# Registro delle decisioni

## Dove stanno le decisioni
Le decisioni architetturali vivono come ADR numerati in `docs/adr/`.

## Quando serve un ADR
Serve un ADR per:
- Cambiare un confine fra livelli
- Cambiare il percorso dei dati di gioco
- Cambiare il modello di sicurezza o di custodia delle chiavi
- Adottare o rimuovere un livello cognitivo
- Cambiare la fonte di osservazione del gioco

## Regola di precedenza
Un ADR successivo prevale su uno precedente solo dove lo dichiara esplicitamente. Quando due documenti sono in disaccordo, il lavoro si ferma e il conflitto si risolve con un ADR, come stabilito da `docs/SOURCE_OF_TRUTH.md`. La ricerca datata in `docs/research/` non prevale mai su un ADR o su una specifica canonica.
