# Piano di test

## Documento canonico
Il documento canonico per la strategia di test è `docs/TESTING.md`.

## Cosa esiste oggi
Il repository contiene test C# e Python; i conteggi correnti sono generati in `docs/REPOSITORY_INVENTORY_2026-09-10.json`. Gli script di build, test e validazione sono presenti in `scripts/` nelle versioni PowerShell e bash.

## Livelli di verifica
- **Present**: Codice presente con build e test verdi.
- **Verified**: Codice verificato con l'evidenza definita dalla fase di roadmap applicabile, ad esempio l'esecuzione contro un client reale.

## Regola di avanzamento
Un test fallito blocca il passaggio all'obiettivo successivo. Dopo la correzione, tutti i test pertinenti vanno ripetuti con esito positivo. Il baseline dei test cambia in base al sistema operativo, confrontando i nomi dei test falliti, non i totali.
