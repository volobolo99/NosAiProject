"""Validatore esplicito per il risultato del modello locale.

Schema di riferimento: schemas/local_result.schema.json. Stesso stile di
messages.py::validate_message: nessuna dipendenza da jsonschema a runtime,
cosi' il risultato puo' essere controllato anche dove lo schema non e'
raggiungibile. La coerenza fra le due descrizioni e' verificata dai test.
"""
from __future__ import annotations

CAMPI_OBBLIGATORI_TOP = ("status", "file", "purpose", "requirements_satisfied", "warnings", "missing_items", "checks", "confidence")
CAMPI_STRINGA_TOP = ("status", "file", "purpose")
CAMPI_ELENCO_STRINGHE_TOP = ("requirements_satisfied", "warnings", "missing_items")
STATI_AMMESSI = ("completed", "needs_revision", "blocked")
CHECKS_OBBLIGATORI = ("format_valid", "references_valid", "placeholders_resolved", "requirements_covered", "contradictions_found")


def validate_local_result(payload: dict) -> list[str]:
    """Elenca i difetti di un risultato locale, in ordine deterministico.

    Postcondizione: lista vuota quando il risultato e' conforme.
    Postcondizione: non solleva mai eccezioni, qualunque cosa riceva.
    """
    if not isinstance(payload, dict):
        return ["il risultato deve essere un dizionario"]

    difetti: list[str] = []

    for nome in CAMPI_OBBLIGATORI_TOP:
        if nome not in payload:
            difetti.append("campo obbligatorio assente: " + nome)

    for nome in sorted(set(payload) - set(CAMPI_OBBLIGATORI_TOP)):
        difetti.append("campo non previsto: " + nome)

    for nome in CAMPI_STRINGA_TOP:
        if nome in payload and not isinstance(payload[nome], str):
            difetti.append(f"il campo {nome} deve essere una stringa")

    for nome in CAMPI_ELENCO_STRINGHE_TOP:
        if nome in payload:
            valore = payload[nome]
            if not isinstance(valore, list) or not all(isinstance(v, str) for v in valore):
                difetti.append(f"il campo {nome} deve essere un elenco di stringhe")

    stato = payload.get("status")
    if isinstance(stato, str) and stato not in STATI_AMMESSI:
        difetti.append(f"stato non ammesso: {stato}. Ammessi: " + ", ".join(STATI_AMMESSI))

    if "checks" in payload:
        checks = payload["checks"]
        if not isinstance(checks, dict):
            difetti.append("il campo checks deve essere un oggetto")
        else:
            for chiave in CHECKS_OBBLIGATORI:
                if chiave not in checks:
                    difetti.append("checks: campo obbligatorio assente: " + chiave)

            for chiave in sorted(set(checks) - set(CHECKS_OBBLIGATORI)):
                difetti.append("checks: campo non previsto: " + chiave)

            for chiave in CHECKS_OBBLIGATORI:
                if chiave in checks and not isinstance(checks[chiave], bool):
                    difetti.append(f"checks: il campo {chiave} deve essere booleano")

    if "confidence" in payload:
        c = payload["confidence"]
        if isinstance(c, bool) or not isinstance(c, (int, float)):
            difetti.append("il campo confidence deve essere un numero")
        elif not (0.0 <= float(c) <= 1.0):
            difetti.append(f"confidence fuori scala: {c}. Ammesso da 0 a 1")

    return difetti
