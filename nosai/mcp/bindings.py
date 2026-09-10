from __future__ import annotations

import hashlib
import json
import os
import threading
import uuid
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from .evidence import EvidenceAuthority
from .roles import DEFAULT_EMPLOYEE_ROLES, EmployeeRole
from .state import McpStateStore


def binding_candidate_digest(primary_model: str, fallback_models: Iterable[str]) -> str:
    canonical = {
        "primary_model": str(primary_model).strip(),
        "fallback_models": list(dict.fromkeys(str(model).strip() for model in fallback_models if str(model).strip())),
    }
    encoded = json.dumps(canonical, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return "sha256:" + hashlib.sha256(encoded).hexdigest()


@dataclass(frozen=True)
class RoleBinding:
    employee_id: str
    primary_model: str
    fallback_models: tuple[str, ...]
    state: str = "active"
    version: int = 1
    proposal_id: str | None = None
    updated_at: str = ""
    candidate_digest: str = ""
    author_id: str = ""


class RoleBindingRegistry:
    """SQLite-backed binding registry with signed-evidence promotion and rollback."""

    REQUIRED_EVIDENCE = ("tests", "shadow", "audit")

    def __init__(
        self,
        path: Path | str,
        employees: Iterable[EmployeeRole] = DEFAULT_EMPLOYEE_ROLES,
        evidence_authority: EvidenceAuthority | None = None,
        state_store: McpStateStore | None = None,
    ):
        self.path = Path(path)
        self._lock = threading.RLock()
        self._employees = {employee.employee_id: employee for employee in employees}
        self.evidence = evidence_authority or EvidenceAuthority(self.path.parent / "evidence")
        state_path = self.path.with_suffix(".sqlite3")
        self.state = state_store or McpStateStore(state_path)
        self.state.migrate_json(self.path)
        self.state.ensure_default_bindings(self._employees.values())

    def get(self, employee_id: str) -> dict:
        with self._lock:
            self._require_employee(employee_id)
            return self.state.get_binding(employee_id)

    def list(self) -> list[dict]:
        with self._lock:
            return self.state.list_bindings()

    def proposals(self) -> list[dict]:
        with self._lock:
            return self.state.list_proposals()

    def get_proposal(self, proposal_id: str) -> dict:
        with self._lock:
            return self.state.get_proposal(proposal_id)

    def propose(
        self,
        employee_id: str,
        primary_model: str,
        fallback_models: Iterable[str],
        *,
        author_id: str = "operator",
    ) -> dict:
        with self._lock:
            self._require_employee(employee_id)
            primary = str(primary_model).strip()
            fallbacks = tuple(dict.fromkeys(str(model).strip() for model in fallback_models if str(model).strip()))
            if not primary:
                raise ValueError("primary_model is required")
            if primary in fallbacks:
                raise ValueError("primary_model must not appear in fallback_models")
            if not str(author_id).strip():
                raise ValueError("author_id is required")
            proposal = {
                "proposal_id": uuid.uuid4().hex,
                "employee_id": employee_id,
                "primary_model": primary,
                "fallback_models": list(fallbacks),
                "candidate_digest": binding_candidate_digest(primary, fallbacks),
                "author_id": str(author_id).strip(),
                "state": "shadow",
                "created_at": self._now(),
            }
            staged = self.state.stage_binding(proposal, expected_revision=self.state.snapshot()["revision"])
            self._sync_legacy_json()
            return {key: value for key, value in staged.items() if key != "revision"}

    def promote(self, proposal_id: str, evidence_ids: dict[str, str], confirmation: str) -> dict:
        with self._lock:
            if confirmation != "operator":
                raise PermissionError("binding promotion requires explicit operator confirmation")
            if not isinstance(evidence_ids, dict):
                raise TypeError("promotion requires evidence_ids mapping")
            proposal = self.state.get_proposal(str(proposal_id))
            missing = [kind for kind in self.REQUIRED_EVIDENCE if not str(evidence_ids.get(kind, "")).strip()]
            if missing:
                raise ValueError("promotion evidence missing: " + ", ".join(missing))
            executors: set[str] = set()
            for kind in self.REQUIRED_EVIDENCE:
                evidence_id = str(evidence_ids[kind])
                record = self.evidence.get(evidence_id)
                valid, reason = self.evidence.verify(
                    record,
                    expected_candidate_digest=str(proposal["candidate_digest"]),
                    expected_kind=kind,
                )
                if not valid:
                    raise ValueError(f"{kind} evidence rejected: {reason}")
                executor = str(record["executor_id"])
                if executor == str(proposal.get("author_id", "")):
                    raise PermissionError("candidate author cannot evaluate its own binding")
                if executor in executors:
                    raise ValueError("promotion evidence executors must be independent")
                executors.add(executor)
            fingerprint = hashlib.sha256(
                json.dumps(evidence_ids, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
            ).hexdigest()
            result = self.state.promote_binding(
                str(proposal_id),
                evidence_ids,
                expected_revision=self.state.snapshot()["revision"],
                idempotency_key=f"promote:{proposal_id}:{fingerprint}",
            )
            self._sync_legacy_json()
            return result

    def rollback(self, employee_id: str, confirmation: str) -> dict:
        with self._lock:
            if confirmation != "operator":
                raise PermissionError("binding rollback requires explicit operator confirmation")
            self._require_employee(employee_id)
            current = self.state.get_binding(employee_id)
            result = self.state.rollback_binding(
                employee_id,
                expected_revision=self.state.snapshot()["revision"],
                idempotency_key=f"rollback:{employee_id}:{current['version']}",
            )
            self._sync_legacy_json()
            return result

    def _sync_legacy_json(self) -> None:
        """Keep the old JSON path as an export, never as the concurrency authority."""
        snapshot = self.state.snapshot()
        payload = {
            "schema_version": "mcp.role_bindings.v2",
            "state_revision": snapshot["revision"],
            "bindings": {item["employee_id"]: item for item in snapshot["bindings"]},
            "proposals": {item["proposal_id"]: item for item in snapshot["proposals"]},
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_name(self.path.name + ".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temporary, self.path)

    def _require_employee(self, employee_id: str) -> None:
        if employee_id not in self._employees:
            raise KeyError("unknown employee_id")

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()
