from __future__ import annotations

import json
import uuid
from dataclasses import asdict, dataclass
from enum import Enum
from pathlib import Path
from typing import Any


class LearningStatus(str, Enum):
    REJECTED = "rejected"
    VALIDATED = "validated"


@dataclass(frozen=True)
class ValidationResult:
    candidate_id: str
    status: LearningStatus
    reason: str


class LearningFactory:
    def __init__(self, root: Path | str):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self.candidates_path = self.root / "candidates.jsonl"

    def record_candidate(self, topic: str, payload: dict[str, Any], source: str, evidence_count: int):
        if not topic.strip() or not source.strip():
            raise ValueError("topic and source are required")
        candidate = {
            "candidate_id": uuid.uuid4().hex,
            "topic": topic,
            "payload": payload,
            "source": source,
            "evidence_count": int(evidence_count),
            "status": "candidate",
        }
        with self.candidates_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(candidate, ensure_ascii=False, sort_keys=True) + "\n")
        return type("Candidate", (), candidate)()

    def _find(self, candidate_id: str) -> dict[str, Any]:
        if not self.candidates_path.is_file():
            raise KeyError(candidate_id)
        for line in self.candidates_path.read_text(encoding="utf-8").splitlines():
            item = json.loads(line)
            if item["candidate_id"] == candidate_id:
                return item
        raise KeyError(candidate_id)

    def validate_candidate(self, candidate_id: str) -> ValidationResult:
        candidate = self._find(candidate_id)
        if candidate["evidence_count"] < 1:
            return ValidationResult(candidate_id, LearningStatus.REJECTED, "observed evidence is required")
        candidate["status"] = LearningStatus.VALIDATED.value
        lines = [json.loads(line) for line in self.candidates_path.read_text(encoding="utf-8").splitlines()]
        lines = [candidate if item["candidate_id"] == candidate_id else item for item in lines]
        self.candidates_path.write_text(
            "".join(json.dumps(item, ensure_ascii=False, sort_keys=True) + "\n" for item in lines),
            encoding="utf-8",
        )
        return ValidationResult(candidate_id, LearningStatus.VALIDATED, "evidence threshold satisfied")

    def export_offline_skill(self, candidate_id: str) -> dict[str, Any]:
        candidate = self._find(candidate_id)
        if candidate["status"] != LearningStatus.VALIDATED.value:
            raise ValueError("candidate must be validated before offline export")
        skill = {
            "schema_version": "mcp.offline.skill.v1",
            "topic": candidate["topic"],
            "payload": candidate["payload"],
            "provenance": {"source": candidate["source"], "evidence_count": candidate["evidence_count"]},
        }
        target = self.root / "skills" / (candidate_id + ".json")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(skill, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
        return skill


