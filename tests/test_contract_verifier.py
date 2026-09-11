import json
from pathlib import Path

from scripts.verify_mcp_contracts import verify


def _write_fixture(root: Path, *, resolved: bool = True) -> None:
    (root / "contracts").mkdir(parents=True)
    (root / "docs").mkdir()
    (root / "schemas").mkdir()
    (root / "src").mkdir()
    (root / "tests").mkdir()
    (root / "src" / "canonical.py").write_text("def run():\n    return True\n", encoding="utf-8")
    (root / "tests" / "test_canonical.py").write_text("def test_run():\n    assert True\n", encoding="utf-8")
    contract = {
        "cid": "C-TEST",
        "title": "fixture",
        "target_file": "src/canonical.py",
        "test_file": "tests/test_canonical.py",
        "signature": "def run() -> bool",
        "status": "DRAFT",
    }
    if resolved:
        contract.update({"signature_status": "RESOLVED", "signature_resolved_at": "2026-09-10"})
    else:
        contract.update({"signature_status": "UNRESOLVED", "implementation_gate": "decide first"})
    (root / "contracts" / "ledger.json").write_text(
        json.dumps({"gates": [{"contracts": [contract]}]}), encoding="utf-8"
    )
    (root / "docs" / "CONTRACT_MAP.md").write_text("| C-TEST | fixture |\n", encoding="utf-8")
    (root / "schemas" / "fixture.schema.json").write_text("{}\n", encoding="utf-8")


def test_verify_accepts_complete_contract_fixture(tmp_path):
    _write_fixture(tmp_path)
    report = verify(tmp_path)
    assert report["ok"] is True
    assert report["contract_count"] == 1


def test_verify_rejects_resolved_placeholder_and_missing_link(tmp_path):
    _write_fixture(tmp_path)
    ledger = json.loads((tmp_path / "contracts" / "ledger.json").read_text())
    contract = ledger["gates"][0]["contracts"][0]
    contract["signature"] = "da definire"
    contract["target_file"] = "src/missing.py"
    (tmp_path / "contracts" / "ledger.json").write_text(json.dumps(ledger), encoding="utf-8")
    (tmp_path / "docs" / "CONTRACT_MAP.md").write_text("", encoding="utf-8")
    report = verify(tmp_path)
    assert report["ok"] is False
    assert any("placeholder" in item for item in report["errors"])
    assert any("does not exist" in item for item in report["errors"])
    assert any("missing from docs" in item for item in report["errors"])


def _messaggio_valido(task_id: str) -> dict:
    return {
        "task_id": task_id, "objective": "o", "input": "i", "expected_output": "e",
        "files": [], "dependencies": [], "risks": [], "required_tests": [],
        "status": "pending", "model": "claude", "summary": "s", "confidence": 0.5,
    }


def test_verify_accepts_conformant_ai_task_packets(tmp_path):
    _write_fixture(tmp_path)
    (tmp_path / "docs" / "mcp").mkdir()
    (tmp_path / "docs" / "mcp" / "AI_TASK_PACKETS.json").write_text(
        json.dumps([_messaggio_valido("T-1")]), encoding="utf-8"
    )
    report = verify(tmp_path)
    assert report["ok"] is True


def test_verify_rejects_malformed_ai_task_packet(tmp_path):
    _write_fixture(tmp_path)
    (tmp_path / "docs" / "mcp").mkdir()
    pacchetto = _messaggio_valido("T-2")
    pacchetto["status"] = "quasi_finito"
    (tmp_path / "docs" / "mcp" / "AI_TASK_PACKETS.json").write_text(
        json.dumps([pacchetto]), encoding="utf-8"
    )
    report = verify(tmp_path)
    assert report["ok"] is False
    assert any("T-2" in item and "stato non ammesso" in item for item in report["errors"])

