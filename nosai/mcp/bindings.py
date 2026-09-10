from __future__ import annotations

import json
import os
import threading
import uuid
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from .roles import DEFAULT_EMPLOYEE_ROLES, EmployeeRole


@dataclass(frozen=True)
class RoleBinding:
    employee_id: str
    primary_model: str
    fallback_models: tuple[str, ...]
    state: str = "active"
    version: int = 1
    proposal_id: str | None = None
    updated_at: str = ""


class RoleBindingRegistry:
    """Persistent, transactional model bindings with staged promotion."""

    REQUIRED_CHECKS = ("tests_passed", "shadow_passed", "audit_approved")

    def __init__(self, path: Path | str, employees: Iterable[EmployeeRole] = DEFAULT_EMPLOYEE_ROLES):
        self.path = Path(path)
        self._lock = threading.RLock()
        self._employees = {employee.employee_id: employee for employee in employees}
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
                        self._bindings[employee_id] = RoleBinding(
                            employee_id=employee_id,
                            primary_model=str(raw["primary_model"]),
                            fallback_models=tuple(raw.get("fallback_models", ())),
                            state="active",
                            version=int(raw.get("version", 1)),
                            updated_at=str(raw.get("updated_at", "")),
                        )
                self._proposals = dict(payload.get("proposals", {}))
            for employee in self._employees.values():
                self._bindings.setdefault(
                    employee.employee_id,
                    RoleBinding(
                        employee_id=employee.employee_id,
                        primary_model=employee.primary_model,
                        fallback_models=employee.fallback_models,
                        updated_at="default",
                    ),
                )

    def _persist(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "schema_version": "mcp.role_bindings.v1",
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
            return [dict(value) for value in self._proposals.values()]

    def propose(self, employee_id: str, primary_model: str, fallback_models: Iterable[str]) -> dict:
        with self._lock:
            self._require_employee(employee_id)
            primary = str(primary_model).strip()
            fallbacks = tuple(dict.fromkeys(str(model).strip() for model in fallback_models if str(model).strip()))
            if not primary:
                raise ValueError("primary_model is required")
            if primary in fallbacks:
                raise ValueError("primary_model must not appear in fallback_models")
            proposal_id = uuid.uuid4().hex
            proposal = {
                "proposal_id": proposal_id,
                "employee_id": employee_id,
                "primary_model": primary,
                "fallback_models": list(fallbacks),
                "state": "shadow",
                "created_at": self._now(),
            }
            self._proposals[proposal_id] = proposal
            self._persist()
            return dict(proposal)

    def promote(self, proposal_id: str, checks: dict[str, bool], confirmation: str) -> dict:
        with self._lock:
            if confirmation != "operator":
                raise PermissionError("binding promotion requires explicit operator confirmation")
            proposal = self._proposals.get(proposal_id)
            if proposal is None:
                raise KeyError("unknown binding proposal")
            missing = [name for name in self.REQUIRED_CHECKS if checks.get(name) is not True]
            if missing:
                raise ValueError("promotion checks missing or failed: " + ", ".join(missing))
            employee_id = proposal["employee_id"]
            previous = self._bindings[employee_id]
            active = RoleBinding(
                employee_id=employee_id,
                primary_model=proposal["primary_model"],
                fallback_models=tuple(proposal["fallback_models"]),
                state="active",
                version=previous.version + 1,
                proposal_id=proposal_id,
                updated_at=self._now(),
            )
            self._bindings[employee_id] = active
            proposal["state"] = "promoted"
            proposal["promoted_at"] = active.updated_at
            proposal["previous"] = asdict(previous)
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
            )
            self._bindings[employee_id] = restored
            if proposal:
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
