from pathlib import Path

import pytest

from nosai.testing.evidence import TestEvidenceBundle, TestResult


def test_bundle_records_full_run_and_passes_only_when_all_gates_pass(tmp_path: Path) -> None:
    bundle = TestEvidenceBundle(tmp_path, git_commit="abc123", platform="PC", device={"os": "test"})
    bundle.set_environment({"python": "3.x"})
    bundle.set_metrics({"latency_ms": 12.5})
    bundle.add_test(TestResult("unit", "PASS", duration_ms=2.0))
    bundle.add_ai_decision({"action": "observe", "confidence": 0.91})
    bundle.add_event({"type": "test_started"})

    assert bundle.finalize(required_gates={"pc": "PASS", "smartphone": "PASS"}) == "PASS"
    assert (bundle.path / "manifest.json").is_file()
    assert (bundle.path / "tests.json").is_file()
    assert (bundle.path / "metrics.json").is_file()
    assert (bundle.path / "ai_decisions.jsonl").is_file()


def test_missing_or_failed_gate_blocks_pass(tmp_path: Path) -> None:
    bundle = TestEvidenceBundle(tmp_path)
    bundle.add_test(TestResult("unit", "PASS"))
    assert bundle.finalize(required_gates={"pc": "PASS", "smartphone": "NOT_RUN"}) == "FAIL"


def test_not_run_test_blocks_pass(tmp_path: Path) -> None:
    bundle = TestEvidenceBundle(tmp_path)
    bundle.add_test(TestResult("smartphone", "NOT_RUN"))
    assert bundle.finalize(required_gates={"pc": "PASS", "smartphone": "PASS"}) == "FAIL"


def test_invalid_status_rejected() -> None:
    with pytest.raises(ValueError):
        TestResult("bad", "GREEN")
