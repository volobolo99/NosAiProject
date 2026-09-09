"""Messaggio strutturato scambiato fra gli agenti del progetto.

Contratto: docs/contracts/orchestration_v1.json (ORCH-001).
Schema di riferimento: schemas/agent_message.schema.json. La validazione qui e'
esplicita e senza accesso al disco, cosi' che un messaggio possa essere
controllato anche dove lo schema non e' raggiungibile; la coerenza fra le due
descrizioni e' verificata da tests/test_orchestration.py.
"""
from __future__ import annotations

from dataclasses import dataclass, fields

STATI_AMMESSI = ("pending", "in_progress", "done", "blocked")
MODELLI_AMMESSI = (
    "claude",
    "qwen2.5-coder:7b",
    "deepseek-v4-flash",
    "qwen3-coder-30b",
    "gemini-2.5-flash-lite",
)

_CAMPI_TESTO = ("task_id", "objective", "input", "expected_output", "summary", "model", "status")
_CAMPI_ELENCO = ("files", "dependencies", "risks", "required_tests")


@dataclass
class AgentMessage:
    """Un incarico o un esito, nella forma che ogni agente sa leggere."""

    task_id: str
    objective: str
    input: str
    expected_output: str
    files: list[str]
    dependencies: list[str]
    risks: list[str]
    required_tests: list[str]
    status: str
    model: str
    summary: str
    confidence: float

    def to_dict(self) -> dict:
        """Restituisce il messaggio come dizionario conforme allo schema.

        Postcondizione: from_dict applicato al risultato restituisce un
        messaggio uguale a questo.
        """
        return {campo.name: getattr(self, campo.name) for campo in fields(self)}

    @classmethod
    def from_dict(cls, payload: dict) -> AgentMessage:
        """Costruisce un messaggio a partire da un dizionario.

        Precondizione: payload e' gia' deserializzato.
        Postcondizione: su payload non conforme solleva ValueError con l'elenco
        dei difetti.
        """
        difetti = validate_message(payload)
        if difetti:
            raise ValueError("messaggio non conforme: " + "; ".join(difetti))
        return cls(**{campo.name: payload[campo.name] for campo in fields(cls)})


def validate_message(payload: dict) -> list[str]:
    """Elenca i difetti di un messaggio, in ordine deterministico.

    Postcondizione: lista vuota quando il messaggio e' conforme.
    Postcondizione: non solleva mai eccezioni, qualunque cosa riceva.
    """
    if not isinstance(payload, dict):
        return ["il messaggio deve essere un dizionario"]

    difetti: list[str] = []
    attesi = [campo.name for campo in fields(AgentMessage)]

    for nome in attesi:
        if nome not in payload:
            difetti.append("campo obbligatorio assente: " + nome)

    for nome in sorted(set(payload) - set(attesi)):
        difetti.append("campo non previsto: " + nome)

    for nome in _CAMPI_TESTO:
        if nome in payload and not isinstance(payload[nome], str):
            difetti.append("il campo {} deve essere una stringa".format(nome))

    for nome in _CAMPI_ELENCO:
        if nome in payload:
            valore = payload[nome]
            if not isinstance(valore, list) or not all(isinstance(v, str) for v in valore):
                difetti.append("il campo {} deve essere un elenco di stringhe".format(nome))

    stato = payload.get("status")
    if isinstance(stato, str) and stato not in STATI_AMMESSI:
        difetti.append("stato non ammesso: {}. Ammessi: {}".format(stato, ", ".join(STATI_AMMESSI)))

    modello = payload.get("model")
    if isinstance(modello, str) and modello not in MODELLI_AMMESSI:
        difetti.append(
            "modello non ammesso: {}. Ammessi: {}".format(modello, ", ".join(MODELLI_AMMESSI))
        )

    if "confidence" in payload:
        confidenza = payload["confidence"]
        if isinstance(confidenza, bool) or not isinstance(confidenza, (int, float)):
            difetti.append("il campo confidence deve essere un numero")
        elif not 0.0 <= float(confidenza) <= 1.0:
            difetti.append("confidence fuori scala: {}. Ammesso da 0 a 1".format(confidenza))

    return difetti
