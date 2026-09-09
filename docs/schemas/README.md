# Schemi di comunicazione

## Schemi disponibili

- [schemas/agent_message.schema.json](schemas/agent_message.schema.json): Descrive il messaggio strutturato con task_id, obiettivo, input, output atteso, file coinvolti, dipendenze, rischi, test richiesti, stato, modello, riepilogo e confidence.
- [schemas/local_result.schema.json](schemas/local_result.schema.json): Descrive lo stato, file, scopo, requisiti soddisfatti, avvisi, elementi mancanti, controlli e confidence.
- [schemas/task_record.schema.json](schemas/task_record.schema.json): Descrive task, modello, numero di chiamate, costo stimato, stato, file e parole prodotte.

## Come si valida

Per validare un messaggio contro uno schema, utilizzare la libreria Python `jsonschema`, versione 4.26.0, presente nell'ambiente. Esempio di codice:

```python
from jsonschema import validate, Draft202012Validator

# Carica lo schema
with open('schemas/agent_message.schema.json') as f:
    schema = json.load(f)

# Carica il messaggio
with open('path/to/agent_message.json') as f:
    message = json.load(f)

# Valida il messaggio
validator = Draft202012Validator(schema)
errors = list(validator.iter_errors(message))

if errors:
    print("Errore di validazione:", errors)
else:
    print("Messaggio valido.")
```

## Regola di modifica

Una modifica di schema che rompe la compatibilità con i messaggi esistenti richiede una decisione architetturale registrata in [docs/adr/](docs/adr/).
