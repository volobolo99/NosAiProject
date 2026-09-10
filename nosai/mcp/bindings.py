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
    """Persistent binding registry with signed-evidence promotion and rollback."""

    REQUIRED_EVIDENCE = ("tests", "shadow", "audit")

    def __init__(
        self,
        path: Path | str,
        employees: Iterable[EmployeeRole] = DEFAULT_EMPLOYEE_ROLES,
        evidence_authority: EvidenceAuthority | None = None,
    ):
        self.path = Path(path)
        self._lock = threading.RLock()
        self._employees = {employee.employee_id: employee for employee in employees}
        self.evidence = evidence_authority or EvidenceAuthority(self.path.parent / "evidence")
        self._bindings: dict[str, RoleBinding] = {}
        self._proposals: dict[str, dict] = {}
        self._load()

    def _load(self) -> None:
        with self._lock:
            if self.path.is_file():
                payload = json.loads(self.path.read_text(encoding="utf-8"))
                if not isinstance(payload, dict):
                    raise ValueError("role binding store must be a JSON object")
                for employee_id, raw in payload.get("bindings", {}).items():
                    if employee_id in self._employees:
                        primary = str(raw["primary_model"])
                        fallbacks = tuple(str(model) for model in raw.get("fallback_models", ()))
                        self._bindings[employee_id] = RoleBinding(
                            employee_id=employee_id,
                            primary_model=primary,
                            fallback_models=fallbacks,
                            state="active",
                            version=int(raw.get("version", 1)),
                            proposal_id=raw.get("proposal_id"),
                            updated_at=str(raw.get("updated_at", "")),
                            candidate_digest=str(raw.get("candidate_digest", binding_candidate_digest(primary, fallbacks))),
                            author_id=str(raw.get("author_id", "")),
                        )
                loaded_proposals = payload.get("proposals", {})
                if not isinstance(loaded_proposals, dict):
                    raise ValueError("role binding proposals must be a JSON object")
                self._proposals = {str(key): dict(value) for key, value in loaded_proposals.items()}
            for employee in self._employees.values():
                self._bindings.setdefault(
                    employee.employee_id,
                    RoleBinding(
                        employee_id=employee.employee_id,
                        primary_model=employee.primary_model,
                        fallback_models=employee.fallback_models,
                        updated_at="default",
                        candidate_digest=binding_candidate_digest(employee.primary_model, employee.fallback_models),
                        author_id="default",
                    ),
                )

    def _persist(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "schema_version": "mcp.role_bindings.v2",
            "bindings": {key: asdict(value) for key, value in self._bindings.items()},
            "proposals": self._proposals,
        }
        temporary = self.path.with_name(self.path.name + ".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temporary, self.path)

    def get(self, employee_id: str) -> dict:
        with self._lock:
            self._require_employee(employee_id)
            return asdict(self._bindings[employee_id])

    def list(self) -> list[dict]:
        with self._lock:
            return [asdict(self._bindings[key]) for key in sorted(self._bindings)]

    def proposals(self) -> list[dict]:
        with self._lock:
            return [dict(self._proposals[key]) for key in sorted(self._proposals)]

    def get_proposal(self, proposal_id: str) -> dict:
        with self._lock:
            proposal = self._proposals.get(str(proposal_id))
            if proposal is None:
                raise KeyError("unknown binding proposal")
            return dict(proposal)

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
            proposal_id = uuid.uuid4().hex
            proposal = {
                "proposal_id": proposal_id,
                "employee_id": employee_id,
                "primary_model": primary,
                "fallback_models": list(fallbacks),
                "candidate_digest": binding_candidate_digest(primary, fallbacks),
                "author_id": str(author_id).strip(),
                "state": "shadow",
                "created_at": self._now(),
            }
            self._proposals[proposal_id] = proposal
            self._persist()
            return dict(proposal)

    def promote(self, proposal_id: str, evidence_ids: dict[str, str], confirmation: str) -> dict:
        with self._lock:
            if confirmation != "operator":
                raise PermissionError("binding promotion requires explicit operator confirmation")
            if not isinstance(evidence_ids, dict):
                raise TypeError("promotion requires evidence_ids mapping")
            proposal = self._proposals.get(str(proposal_id))
            if proposal is None:
                raise KeyError("unknown binding proposal")
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
            employee_id = str(proposal["employee_id"])
            previous = self._bindings[employee_id]
            active = RoleBinding(
                employee_id=employee_id,
                primary_model=str(proposal["primary_model"]),
                fallback_models=tuple(proposal["fallback_models"]),
                state="active",
                version=previous.version + 1,
                proposal_id=str(proposal_id),
                updated_at=self._now(),
                candidate_digest=str(proposal["candidate_digest"]),
                author_id=str(proposal.get("author_id", "")),
            )
            self._bindings[employee_id] = active
            proposal["state"] = "promoted"
            proposal["promoted_at"] = active.updated_at
            proposal["previous"] = asdict(previous)
            proposal["evidence_ids"] = dict(evidence_ids)
            self._persist()
            return asdict(active)

    def rollback(self, employee_id: str, confirmation: str) -> dict:
        with self._lock:
            if confirmation != "operator":
                raise PermissionError("binding rollback requires explicit operator confirmation")
            self._require_employee(employee_id)
            current = self._bindings[employee_id]
            proposal_id = current.proposal_id
            proposal = self._proposals.get(proposal_id or "")
            previous = proposal.get("previous") if proposal else None
            if not previous:
                raise ValueError("no previous binding is available for rollback")
            restored = RoleBinding(
                employee_id=employee_id,
                primary_model=previous["primary_model"],
                fallback_models=tuple(previous.get("fallback_models", ())),
                state="active",
                version=current.version + 1,
                updated_at=self._now(),
                candidate_digest=str(previous.get("candidate_digest", binding_candidate_digest(previous["primary_model"], previous.get("fallback_models", ())))),
                author_id=str(previous.get("author_id", "")),
            )
            self._bindings[employee_id] = restored
            proposal["state"] = "rolled_back"
            proposal["rolled_back_at"] = restored.updated_at
            self._persist()
            return asdict(restored)

    def _require_employee(self, employee_id: str) -> None:
        if employee_id not in self._employees:
            raise KeyError("unknown employee_id")

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()
