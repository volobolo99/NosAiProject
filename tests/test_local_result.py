import json
from pathlib import Path

from jsonschema import Draft202012Validator

from nosai.orchestration.local_result import validate_local_result

ROOT = Path(__file__).resolve().parents[1]


def risultato_di_prova() -> dict:
    return {
        "status": "completed",
        "file": "docs/example.md",
        "purpose": "prova",
        "requirements_satisfied": ["r1"],
        "warnings": [],
        "missing_items": [],
        "checks": {
            "format_valid": True,
            "references_valid": True,
            "placeholders_resolved": True,
            "requirements_covered": True,
            "contradictions_found": False,
        },
        "confidence": 0.9,
    }


def test_risultato_conforme_non_ha_difetti():
    assert validate_local_result(risultato_di_prova()) == []


def test_non_dict_e_un_difetto():
    assert validate_local_result([]) != []
    assert validate_local_result(None) != []


def test_campo_obbligatorio_assente():
    payload = risultato_di_prova()
    del payload["purpose"]
    assert any("purpose" in d for d in validate_local_result(payload))


def test_campo_non_previsto():
    payload = risultato_di_prova()
    payload["extra"] = "no"
    assert any("extra" in d for d in validate_local_result(payload))


def test_status_non_ammesso():
    payload = risultato_di_prova()
    payload["status"] = "quasi_finito"
    assert validate_local_result(payload) != []


def test_checks_deve_essere_oggetto():
    payload = risultato_di_prova()
    payload["checks"] = "no"
    assert any("checks" in d for d in validate_local_result(payload))


def test_checks_campo_mancante():
    payload = risultato_di_prova()
    del payload["checks"]["contradictions_found"]
    assert any("contradictions_found" in d for d in validate_local_result(payload))


def test_checks_valore_non_booleano():
    payload = risultato_di_prova()
    payload["checks"]["format_valid"] = "si"
    assert any("format_valid" in d for d in validate_local_result(payload))


def test_confidence_fuori_scala():
    payload = risultato_di_prova()
    payload["confidence"] = 1.5
    assert validate_local_result(payload) != []


def test_confidence_booleano_e_rifiutato():
    payload = risultato_di_prova()
    payload["confidence"] = True
    assert validate_local_result(payload) != []


def test_risultato_conforme_allo_schema_del_repo():
    schema = json.loads((ROOT / "schemas" / "local_result.schema.json").read_text(encoding="utf-8"))
    Draft202012Validator(schema).validate(risultato_di_prova())
