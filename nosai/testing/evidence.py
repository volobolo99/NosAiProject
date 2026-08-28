"""Standardized, append-safe evidence bundle for NosAi test runs.

The bundle is deliberately filesystem based so every PC/smartphone run can
produce portable evidence without requiring a network service. It records
provenance, environment, test outcomes, metrics, events, AI decisions and
artifacts. A run cannot be considered PASS merely because some tests passed.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Mapping
from uuid import uuid4

SCHEMA_VERSION = "1.0"
VALID_STATUSES = {"NOT_RUN", "RUNNING", "PASS", "FAIL", "PARTIAL"}


@dataclass(frozen=True)
class TestResult:
    name: str
    status: str
    duration_ms: float | None = None
    message: str | None = None
    details: Mapping[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if self.status not in VALID_STATUSES:
            raise ValueError(f"invalid test status: {self.status}")


class TestEvidenceBundle:
    """Collect and persist all evidence belonging to one test run."""

    def __init__(
        self,
        root: str | Path,
        *,
        run_id: str | None = None,
        project_version: str = "1.0 Beta",
        git_commit: str | None = None,
        platform: str | None = None,
        device: Mapping[str, Any] | None = None,
    ) -> None:
        self.run_id = run_id or f"RUN-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}-{uuid4().hex[:8]}"
        self.path = Path(root) / self.run_id
        self.path.mkdir(parents=True, exist_ok=False)
        self.tests: list[TestResult] = []
        self.metrics: dict[str, Any] = {}
        self.environment: dict[str, Any] = {}
        self.events: list[dict[str, Any]] = []
        self.ai_decisions: list[dict[str, Any]] = []
        self.errors: list[dict[str, Any]] = []
        self.artifacts: list[dict[str, Any]] = []
        self.manifest = {
            "schema_version": SCHEMA_VERSION,
            "run_id": self.run_id,
            "started_at": datetime.now(timezone.utc).isoformat(),
            "project_version": project_version,
            "git_commit": git_commit,
            "platform": platform,
            "device": dict(device or {}),
            "status": "RUNNING",
        }
        self._write_json("manifest.json", self.manifest)

    def add_test(self, result: TestResult) -> None:
        self.tests.append(result)

    def set_environment(self, values: Mapping[str, Any]) -> None:
        self.environment.update(values)

    def set_metrics(self, values: Mapping[str, Any]) -> None:
        self.metrics.update(values)

    def add_event(self, event: Mapping[str, Any]) -> None:
        self.events.append(dict(event))

    def add_ai_decision(self, decision: Mapping[str, Any]) -> None:
        self.ai_decisions.append(dict(decision))

    def add_error(self, error: Mapping[str, Any]) -> None:
        self.errors.append(dict(error))

    def add_artifact(self, source: str | Path, *, kind: str = "artifact") -> Path:
        source_path = Path(source)
        if not source_path.is_file():
            raise FileNotFoundError(source_path)
        target_dir = self.path / "artifacts"
        target_dir.mkdir(exist_ok=True)
        target = target_dir / source_path.name
        target.write_bytes(source_path.read_bytes())
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        self.artifacts.append({"name": target.name, "kind": kind, "sha256": digest, "size": target.stat().st_size})
        return target

    def finalize(self, *, required_gates: Mapping[str, str] | None = None) -> str:
        statuses = [result.status for result in self.tests]
        gates = dict(required_gates or {})
        all_pass = bool(statuses) and all(status == "PASS" for status in statuses)
        gates_pass = all(value == "PASS" for value in gates.values())
        if any(status in {"FAIL", "PARTIAL", "NOT_RUN"} for status in statuses):
            final = "FAIL"
        elif all_pass and gates_pass:
            final = "PASS"
        else:
            final = "FAIL"
        self.manifest.update({
            "finished_at": datetime.now(timezone.utc).isoformat(),
            "status": final,
            "test_count": len(self.tests),
            "required_gates": gates,
        })
        self._write_json("environment.json", self.environment)
        self._write_json("tests.json", [asdict(item) for item in self.tests])
        self._write_json("metrics.json", self.metrics)
        self._write_jsonl("events.jsonl", self.events)
        self._write_jsonl("ai_decisions.jsonl", self.ai_decisions)
        self._write_jsonl("errors.jsonl", self.errors)
        self._write_json("artifacts.json", self.artifacts)
        self._write_json("manifest.json", self.manifest)
        return final

    def _write_json(self, name: str, value: Any) -> None:
        (self.path / name).write_text(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")

    def _write_jsonl(self, name: str, values: list[Mapping[str, Any]]) -> None:
        with (self.path / name).open("w", encoding="utf-8") as handle:
            for value in values:
                handle.write(json.dumps(value, ensure_ascii=False, sort_keys=True) + "\n")
