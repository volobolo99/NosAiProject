"""Test del contratto ORCH-001 (docs/contracts/orchestration_v1.json).

Ogni test verifica una postcondizione dichiarata nel contratto. I test sono il
criterio di accettazione dell'implementazione: se falliscono, il modulo non e'
implementato, qualunque cosa dica il modello che lo ha scritto.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from nosai.orchestration.ledger import CostLedger, TaskRecord
from nosai.orchestration.messages import AgentMessage, validate_message
from nosai.orchestration.routing import CostClass, ModelId, ModelRoute, TaskKind, escalate, route_task

ROOT = Path(__file__).resolve().parents[1]

# Ordine esplicito: CostClass e' un'enumerazione di stringhe e il confronto
# lessicografico sarebbe sbagliato ("medium" precede "max" in ordine alfabetico).
RANGO_COSTO = {
    CostClass.ZERO: 0,
    CostClass.LOW: 1,
    CostClass.MEDIUM: 2,
    CostClass.MAX: 3,
}


def record_di_prova(task_id: str = "T-1", calls: int = 1, costo: float = 0.0) -> TaskRecord:
    return TaskRecord(
        task_id=task_id,
        model="qwen2.5-coder:7b",
        calls=calls,
        estimated_cost_usd=costo,
        status="completed",
        file="docs/x.md",
        words=100,
    )


def messaggio_di_prova() -> AgentMessage:
    return AgentMessage(
        task_id="T-1",
        objective="Verificare il contratto",
        input="contratto e scheletro",
        expected_output="modulo implementato",
        files=["nosai/orchestration/routing.py"],
        dependencies=[],
        risks=[],
        required_tests=["tests/test_orchestration.py"],
        status="pending",
        model="claude",
        summary="prova",
        confidence=0.5,
    )


# ---------------------------------------------------------------------- routing


@pytest.mark.parametrize("kind", list(TaskKind))
def test_route_task_copre_tutti_i_kind(kind):
    rotta = route_task(kind)
    assert isinstance(rotta, ModelRoute)
    assert isinstance(rotta.model, ModelId)
    assert rotta.cost_class in RANGO_COSTO


@pytest.mark.parametrize("kind", list(TaskKind))
def test_route_task_deterministica(kind):
    assert route_task(kind) == route_task(kind)


@pytest.mark.parametrize("kind", list(TaskKind))
def test_escalate_non_abbassa_il_costo(kind):
    base = route_task(kind)
    if base.model is ModelId.CLAUDE:
        pytest.skip("sopra Claude non esiste escalation: coperto da un test dedicato")
    salita = escalate(kind, "il modello base non ha risolto il task")
    assert RANGO_COSTO[salita.cost_class] >= RANGO_COSTO[base.cost_class]


POLITICA_ATTESA = {
    TaskKind.DOCUMENTATION: ModelId.LOCAL_QWEN25_CODER_7B,
    TaskKind.SKELETON: ModelId.LOCAL_QWEN25_CODER_7B,
    TaskKind.SIMPLE_CODE: ModelId.DEEPSEEK_V4_FLASH,
    TaskKind.REPETITIVE_TEST: ModelId.DEEPSEEK_V4_FLASH,
    TaskKind.INITIAL_DEBUG: ModelId.DEEPSEEK_V4_FLASH,
    TaskKind.LOG_ANALYSIS: ModelId.DEEPSEEK_V4_FLASH,
    TaskKind.COMPLEX_CODE: ModelId.QWEN3_CODER_30B,
    TaskKind.MODULE_INTEGRATION: ModelId.QWEN3_CODER_30B,
    TaskKind.HARD_DEBUG: ModelId.QWEN3_CODER_30B,
    TaskKind.STRUCTURAL_REFACTOR: ModelId.QWEN3_CODER_30B,
    TaskKind.VISION: ModelId.GEMINI_25_FLASH_LITE,
    TaskKind.FAST_CLASSIFICATION: ModelId.GEMINI_25_FLASH_LITE,
    TaskKind.ARCHITECTURE: ModelId.CLAUDE,
    TaskKind.CRITICAL_REVIEW: ModelId.CLAUDE,
}


@pytest.mark.parametrize("kind,atteso", sorted(POLITICA_ATTESA.items(), key=lambda x: x[0].value))
def test_routing_rispetta_la_politica(kind, atteso):
    """La tabella deve applicare COST_POLICY.md, non le preferenze del modello
    che l'ha scritta: ogni tipo di lavoro va al modello previsto."""
    assert route_task(kind).model is atteso


@pytest.mark.parametrize("kind", list(TaskKind))
def test_classi_di_costo_coerenti_col_listino(kind):
    """La classe di costo non e' una seconda verita': deriva dal listino
    verificato in scripts/model_prices.json."""
    listino = json.loads((ROOT / "scripts" / "model_prices.json").read_text(encoding="utf-8"))
    rotta = route_task(kind)
    voce = listino["models"][rotta.model.value]
    assert rotta.cost_class.value == voce["cost_class"]


def test_escalate_da_claude_solleva():
    kind_claude = [k for k in TaskKind if route_task(k).model is ModelId.CLAUDE]
    assert kind_claude, "almeno un tipo di task deve essere instradato su Claude"
    for kind in kind_claude:
        with pytest.raises(ValueError):
            escalate(kind, "tentativo di salire sopra Claude")


# ----------------------------------------------------------------------- ledger


def test_append_e_read_all(tmp_path):
    ledger = CostLedger(tmp_path / "ledger.jsonl")
    record = record_di_prova()
    ledger.append(record)
    assert ledger.read_all() == [record]


def test_append_non_riscrive_le_righe_esistenti(tmp_path):
    ledger = CostLedger(tmp_path / "ledger.jsonl")
    ledger.append(record_di_prova("T-1"))
    ledger.append(record_di_prova("T-2"))
    assert [r.task_id for r in ledger.read_all()] == ["T-1", "T-2"]


def test_read_all_su_file_assente(tmp_path):
    assert CostLedger(tmp_path / "mai_creato.jsonl").read_all() == []


def test_read_all_salta_riga_malformata(tmp_path):
    percorso = tmp_path / "ledger.jsonl"
    valida = json.dumps(
        {
            "task_id": "T-1",
            "model": "qwen2.5-coder:7b",
            "calls": 1,
            "estimated_cost_usd": 0.0,
            "status": "completed",
            "file": "docs/x.md",
            "words": 100,
        }
    )
    altra = json.dumps(
        {
            "task_id": "T-2",
            "model": "qwen2.5-coder:7b",
            "calls": 1,
            "estimated_cost_usd": 0.0,
            "status": "completed",
            "file": "docs/y.md",
            "words": 200,
        }
    )
    percorso.write_text(valida + "\nquesta riga non e' JSON\n" + altra + "\n", encoding="utf-8")
    letti = CostLedger(percorso).read_all()
    assert [r.task_id for r in letti] == ["T-1", "T-2"]


def test_read_all_salta_riga_con_campi_errati(tmp_path):
    """Una riga JSON valida ma che non e' un TaskRecord non deve interrompere la
    lettura: e' malformata quanto una riga non JSON."""
    percorso = tmp_path / "ledger.jsonl"
    percorso.write_text(
        json.dumps({"task_id": "T-1", "campo_ignoto": True})
        + "\n"
        + json.dumps(
            {
                "task_id": "T-2",
                "model": "qwen2.5-coder:7b",
                "calls": 1,
                "estimated_cost_usd": 0.0,
                "status": "completed",
                "file": "docs/y.md",
                "words": 200,
            }
        )
        + "\n",
        encoding="utf-8",
    )
    assert [r.task_id for r in CostLedger(percorso).read_all()] == ["T-2"]


def test_append_crea_la_directory_padre(tmp_path):
    """Il contratto prevede che la directory padre venga creata se assente."""
    ledger = CostLedger(tmp_path / "sotto" / "cartella" / "ledger.jsonl")
    ledger.append(record_di_prova())
    assert ledger.read_all() == [record_di_prova()]


def test_append_conserva_i_caratteri_accentati(tmp_path):
    """Il registro contiene percorsi e descrizioni in italiano: l'encoding del
    file non puo' dipendere dalle impostazioni del sistema operativo."""
    percorso = tmp_path / "ledger.jsonl"
    ledger = CostLedger(percorso)
    record = TaskRecord(
        task_id="T-àèì",
        model="qwen2.5-coder:7b",
        calls=1,
        estimated_cost_usd=0.0,
        status="completed",
        file="docs/perché.md",
        words=1,
    )
    ledger.append(record)
    assert ledger.read_all() == [record]
    assert "perché" in percorso.read_text(encoding="utf-8")


def test_append_rifiuta_record_non_valido(tmp_path):
    ledger = CostLedger(tmp_path / "ledger.jsonl")
    with pytest.raises(ValueError):
        ledger.append(record_di_prova(calls=0))
    with pytest.raises(ValueError):
        ledger.append(record_di_prova(costo=-1.0))


def test_riga_conforme_allo_schema(tmp_path):
    schema = json.loads((ROOT / "schemas" / "task_record.schema.json").read_text(encoding="utf-8"))
    validatore = Draft202012Validator(schema)
    percorso = tmp_path / "ledger.jsonl"
    CostLedger(percorso).append(record_di_prova())
    righe = [r for r in percorso.read_text(encoding="utf-8").splitlines() if r.strip()]
    assert len(righe) == 1
    validatore.validate(json.loads(righe[0]))


def test_summary_by_model(tmp_path):
    ledger = CostLedger(tmp_path / "ledger.jsonl")
    ledger.append(record_di_prova("T-1", calls=1, costo=0.0))
    ledger.append(record_di_prova("T-2", calls=2, costo=0.1))
    riepilogo = ledger.summary_by_model()
    assert set(riepilogo) == {"qwen2.5-coder:7b"}
    voce = riepilogo["qwen2.5-coder:7b"]
    assert voce["calls"] == 3
    assert voce["tasks"] == 2
    assert voce["estimated_cost_usd"] == pytest.approx(0.1)


def test_total_estimated_cost(tmp_path):
    ledger = CostLedger(tmp_path / "ledger.jsonl")
    ledger.append(record_di_prova("T-1", costo=0.25))
    ledger.append(record_di_prova("T-2", costo=0.5))
    assert ledger.total_estimated_cost() == pytest.approx(0.75)


# --------------------------------------------------------------------- messaggi


def test_validate_message_accetta_messaggio_conforme():
    assert validate_message(messaggio_di_prova().to_dict()) == []


def test_validate_message_rifiuta_campo_mancante():
    payload = messaggio_di_prova().to_dict()
    del payload["task_id"]
    errori = validate_message(payload)
    assert errori
    assert all(isinstance(e, str) for e in errori)


def test_validate_message_non_solleva_su_payload_assurdo():
    assert validate_message({}) != []
    assert validate_message({"task_id": 42}) != []


def test_validate_message_rifiuta_stato_non_ammesso():
    payload = messaggio_di_prova().to_dict()
    payload["status"] = "quasi_finito"
    assert validate_message(payload) != []


def test_validate_message_rifiuta_confidence_fuori_scala():
    payload = messaggio_di_prova().to_dict()
    payload["confidence"] = 1.5
    assert validate_message(payload) != []


def test_to_dict_e_from_dict_sono_inversi():
    messaggio = messaggio_di_prova()
    assert AgentMessage.from_dict(messaggio.to_dict()) == messaggio


def test_from_dict_su_payload_non_conforme_solleva():
    payload = messaggio_di_prova().to_dict()
    del payload["objective"]
    with pytest.raises(ValueError):
        AgentMessage.from_dict(payload)


def test_messaggio_conforme_allo_schema_del_repo():
    schema = json.loads((ROOT / "schemas" / "agent_message.schema.json").read_text(encoding="utf-8"))
    Draft202012Validator(schema).validate(messaggio_di_prova().to_dict())
