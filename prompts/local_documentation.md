# Prompt: incarico documentale locale

## Struttura dell'incarico
Lo strumento accetta un incarico documentale in formato JSON, che può essere un oggetto singolo o una lista di oggetti.

## Campi obbligatori
- `file`: percorso di destinazione relativo alla radice del repository
- `purpose`: scopo del documento in una riga
- `requirements`: elenco dei requisiti che il documento deve soddisfare
- `facts`: elenco dei fatti verificati, unica fonte ammessa per il modello
- `required_sections`: intestazioni obbligatorie confrontate alla lettera
- `format`: markdown, json oppure python
- `max_words`: limite indicativo di lunghezza

## Controlli applicati
- Formato corretto e, per gli schemi, validità secondo JSON Schema draft 2020-12
- Presenza di tutte le sezioni obbligatorie
- Assenza dei segnaposto residui, cioè i termini `TODO`, `TBD`, `FIXME`, `XXX` scritti fuori dai backtick
- Presenza di tutti i link Markdown relativi che puntano a file esistenti
- Assenza di intestazioni duplicate
- Per gli scheletri Python, sintassi valida e assenza di logica implementata

## Esito
- `completed`: tutti i controlli passano
- `needs_revision`: lo strumento rilancia l'incarico al modello con l'elenco dei difetti, fino a tre tentativi
- `blocked`: il file non viene scritto su disco
