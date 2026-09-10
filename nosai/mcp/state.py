from __future__ import annotations

import hashlib
import json
import os
import shutil
import sqlite3
import threading
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Iterable, Mapping


class ConcurrentUpdateError(RuntimeError):
    """Raised when a revision or fencing token is stale."""


class McpStateStore:
    """Small SQLite authority shared by MCP, dashboard and worker processes."""

    SCHEMA_VERSION = "mcp.state.v1"

    def __init__(self, path: Path | str) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()
        self._initialize()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=30, isolation_level=None)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA busy_timeout=30000")
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA journal_mode=WAL")
        return connection

    def _initialize(self) -> None:
        with self._lock, self._connect() as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS bindings (
                    employee_id TEXT PRIMARY KEY,
                    primary_model TEXT NOT NULL,
                    fallback_models_json TEXT NOT NULL,
                    state TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    proposal_id TEXT,
                    updated_at TEXT NOT NULL,
                    candidate_digest TEXT NOT NULL,
                    author_id TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS proposals (
                    proposal_id TEXT PRIMARY KEY,
                    payload_json TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS idempotency (
                    idempotency_key TEXT PRIMARY KEY,
                    result_json TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS leases (
                    task_id TEXT PRIMARY KEY,
                    worker_id TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    fencing_token INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS observations (
                    name TEXT PRIMARY KEY,
                    status TEXT NOT NULL,
                    details_json TEXT NOT NULL,
                    observed_at TEXT NOT NULL,
                    expires_at TEXT
                );
                """
            )
            connection.execute(
                "INSERT OR IGNORE INTO metadata(key, value) VALUES ('schema_version', ?)",
                (self.SCHEMA_VERSION,),
            )
            connection.execute("INSERT OR IGNORE INTO metadata(key, value) VALUES ('state_revision', '0')")
            connection.execute("INSERT OR IGNORE INTO metadata(key, value) VALUES ('fencing_counter', '0')")

    def snapshot(self) -> dict[str, Any]:
        with self._lock, self._connect() as connection:
            bindings = [self._binding_from_row(row) for row in connection.execute("SELECT * FROM bindings ORDER BY employee_id")]
            proposals = [
                json.loads(row["payload_json"])
                for row in connection.execute("SELECT payload_json FROM proposals ORDER BY proposal_id")
            ]
            leases = [
                {
                    "task_id": row["task_id"],
                    "worker_id": row["worker_id"],
                    "expires_at": row["expires_at"],
                    "fencing_token": int(row["fencing_token"]),
                }
                for row in connection.execute("SELECT * FROM leases ORDER BY task_id")
            ]
            observations = [
                {
                    "name": row["name"],
                    "status": row["status"],
                    "details": json.loads(row["details_json"]),
                    "observed_at": row["observed_at"],
                    "expires_at": row["expires_at"],
                }
                for row in connection.execute("SELECT * FROM observations ORDER BY name")
            ]
            return {
                "schema_version": self.SCHEMA_VERSION,
                "revision": self._revision(connection),
                "bindings": bindings,
                "proposals": proposals,
                "leases": leases,
                "observations": observations,
            }

    def get_binding(self, employee_id: str) -> dict[str, Any]:
        with self._lock, self._connect() as connection:
            row = connection.execute("SELECT * FROM bindings WHERE employee_id = ?", (employee_id,)).fetchone()
            if row is None:
                raise KeyError("unknown employee_id")
            return self._binding_from_row(row)

    def list_bindings(self) -> list[dict[str, Any]]:
        return self.snapshot()["bindings"]

    def get_proposal(self, proposal_id: str) -> dict[str, Any]:
        with self._lock, self._connect() as connection:
            row = connection.execute("SELECT payload_json FROM proposals WHERE proposal_id = ?", (proposal_id,)).fetchone()
            if row is None:
                raise KeyError("unknown binding proposal")
            return json.loads(row["payload_json"])

    def list_proposals(self) -> list[dict[str, Any]]:
        return self.snapshot()["proposals"]

    def ensure_default_bindings(self, employees: Iterable[Any]) -> int:
        inserted = 0
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            for employee in employees:
                employee_id = str(employee.employee_id)
                existing = connection.execute(
                    "SELECT 1 FROM bindings WHERE employee_id = ?", (employee_id,)
                ).fetchone()
                if existing is not None:
                    continue
                primary = str(employee.primary_model)
                fallbacks = [str(model) for model in employee.fallback_models]
                digest = str(getattr(employee, "candidate_digest", ""))
                if not digest:
                    digest = "sha256:" + hashlib.sha256(
                        json.dumps(
                            {"primary_model": primary, "fallback_models": fallbacks},
                            sort_keys=True,
                            separators=(",", ":"),
                        ).encode("utf-8")
                    ).hexdigest()
                connection.execute(
                    """
                    INSERT INTO bindings(
                        employee_id, primary_model, fallback_models_json, state, version,
                        proposal_id, updated_at, candidate_digest, author_id
                    ) VALUES (?, ?, ?, 'active', 1, NULL, 'default', ?, 'default')
                    """,
                    (employee_id, primary, json.dumps(fallbacks), digest),
                )
                inserted += 1
            if inserted:
                self._bump_revision(connection)
            connection.commit()
        return inserted

    def stage_binding(self, proposal: Mapping[str, Any], expected_revision: int | None = None) -> dict[str, Any]:
        required = ("proposal_id", "employee_id", "primary_model", "fallback_models", "candidate_digest", "author_id")
        if any(name not in proposal for name in required):
            raise ValueError("binding proposal is incomplete")
        payload = dict(proposal)
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            self._check_revision(connection, expected_revision)
            existing = connection.execute(
                "SELECT payload_json FROM proposals WHERE proposal_id = ?", (str(payload["proposal_id"]),)
            ).fetchone()
            if existing is not None:
                current = json.loads(existing["payload_json"])
                if current != payload:
                    raise ValueError("binding proposal id already exists with different payload")
                payload["revision"] = self._revision(connection)
                connection.commit()
                return payload
            connection.execute(
                "INSERT INTO proposals(proposal_id, payload_json) VALUES (?, ?)",
                (str(payload["proposal_id"]), self._json(payload)),
            )
            payload["revision"] = self._bump_revision(connection)
            connection.execute(
                "UPDATE proposals SET payload_json = ? WHERE proposal_id = ?",
                (self._json({key: value for key, value in payload.items() if key != "revision"}), str(payload["proposal_id"])),
            )
            connection.commit()
            return payload

    def promote_binding(
        self,
        proposal_id: str,
        evidence_ids: Mapping[str, str],
        expected_revision: int | None,
        idempotency_key: str,
    ) -> dict[str, Any]:
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            previous_result = self._idempotent_result(connection, idempotency_key)
            if previous_result is not None:
                connection.commit()
                return previous_result
            self._check_revision(connection, expected_revision)
            row = connection.execute(
                "SELECT payload_json FROM proposals WHERE proposal_id = ?", (str(proposal_id),)
            ).fetchone()
            if row is None:
                raise KeyError("unknown binding proposal")
            proposal = json.loads(row["payload_json"])
            employee_id = str(proposal["employee_id"])
            old_row = connection.execute("SELECT * FROM bindings WHERE employee_id = ?", (employee_id,)).fetchone()
            if old_row is None:
                raise KeyError("unknown employee_id")
            previous = self._binding_from_row(old_row)
            active = {
                "employee_id": employee_id,
                "primary_model": str(proposal["primary_model"]),
                "fallback_models": list(proposal["fallback_models"]),
                "state": "active",
                "version": int(previous["version"]) + 1,
                "proposal_id": str(proposal_id),
                "updated_at": self._now(),
                "candidate_digest": str(proposal["candidate_digest"]),
                "author_id": str(proposal.get("author_id", "")),
            }
            connection.execute(
                """
                UPDATE bindings SET primary_model=?, fallback_models_json=?, state=?,
                    version=?, proposal_id=?, updated_at=?, candidate_digest=?, author_id=?
                WHERE employee_id=?
                """,
                (
                    active["primary_model"],
                    self._json(active["fallback_models"]),
                    active["state"],
                    active["version"],
                    active["proposal_id"],
                    active["updated_at"],
                    active["candidate_digest"],
                    active["author_id"],
                    employee_id,
                ),
            )
            revision = self._bump_revision(connection)
            updated_proposal = dict(proposal)
            updated_proposal.update(
                {
                    "state": "promoted",
                    "promoted_at": active["updated_at"],
                    "previous": previous,
                    "evidence_ids": dict(evidence_ids),
                }
            )
            connection.execute(
                "UPDATE proposals SET payload_json=? WHERE proposal_id=?",
                (self._json(updated_proposal), str(proposal_id)),
            )
            result = dict(active, revision=revision)
            self._store_idempotent(connection, idempotency_key, result)
            connection.commit()
            return result

    def rollback_binding(
        self,
        employee_id: str,
        expected_revision: int | None,
        idempotency_key: str,
    ) -> dict[str, Any]:
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            previous_result = self._idempotent_result(connection, idempotency_key)
            if previous_result is not None:
                connection.commit()
                return previous_result
            self._check_revision(connection, expected_revision)
            current_row = connection.execute("SELECT * FROM bindings WHERE employee_id=?", (employee_id,)).fetchone()
            if current_row is None:
                raise KeyError("unknown employee_id")
            current = self._binding_from_row(current_row)
            proposal_id = current.get("proposal_id")
            proposal_row = connection.execute(
                "SELECT payload_json FROM proposals WHERE proposal_id=?", (proposal_id or "",)
            ).fetchone()
            previous = json.loads(proposal_row["payload_json"]).get("previous") if proposal_row else None
            if not previous:
                raise ValueError("no previous binding is available for rollback")
            restored = {
                "employee_id": employee_id,
                "primary_model": previous["primary_model"],
                "fallback_models": list(previous.get("fallback_models", [])),
                "state": "active",
                "version": int(current["version"]) + 1,
                "proposal_id": None,
                "updated_at": self._now(),
                "candidate_digest": previous.get("candidate_digest", ""),
                "author_id": previous.get("author_id", ""),
            }
            connection.execute(
                """
                UPDATE bindings SET primary_model=?, fallback_models_json=?, state=?,
                    version=?, proposal_id=NULL, updated_at=?, candidate_digest=?, author_id=?
                WHERE employee_id=?
                """,
                (
                    restored["primary_model"],
                    self._json(restored["fallback_models"]),
                    restored["state"],
                    restored["version"],
                    restored["updated_at"],
                    restored["candidate_digest"],
                    restored["author_id"],
                    employee_id,
                ),
            )
            revision = self._bump_revision(connection)
            if proposal_row:
                updated = json.loads(proposal_row["payload_json"])
                updated.update({"state": "rolled_back", "rolled_back_at": restored["updated_at"]})
                connection.execute(
                    "UPDATE proposals SET payload_json=? WHERE proposal_id=?",
                    (self._json(updated), proposal_id),
                )
            result = dict(restored, revision=revision)
            self._store_idempotent(connection, idempotency_key, result)
            connection.commit()
            return result

    def migrate_json(self, legacy_path: Path | str) -> bool:
        source = Path(legacy_path)
        with self._lock, self._connect() as connection:
            if connection.execute("SELECT 1 FROM bindings LIMIT 1").fetchone() is not None:
                return False
            if not source.is_file():
                return False
            backup = source.with_name(source.name + ".bak." + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"))
            if not backup.exists():
                shutil.copy2(source, backup)
            payload = json.loads(source.read_text(encoding="utf-8"))
            if not isinstance(payload, dict):
                raise ValueError("legacy binding store must be a JSON object")
            connection.execute("BEGIN IMMEDIATE")
            for employee_id, raw in payload.get("bindings", {}).items():
                primary = str(raw["primary_model"])
                fallbacks = [str(item) for item in raw.get("fallback_models", [])]
                digest = str(raw.get("candidate_digest", ""))
                if not digest:
                    digest = "sha256:" + hashlib.sha256(
                        json.dumps(
                            {"primary_model": primary, "fallback_models": fallbacks},
                            sort_keys=True,
                            separators=(",", ":"),
                        ).encode("utf-8")
                    ).hexdigest()
                connection.execute(
                    """
                    INSERT OR IGNORE INTO bindings(
                        employee_id, primary_model, fallback_models_json, state, version,
                        proposal_id, updated_at, candidate_digest, author_id
                    ) VALUES (?, ?, ?, 'active', ?, ?, ?, ?, ?)
                    """,
                    (
                        str(employee_id),
                        primary,
                        self._json(fallbacks),
                        int(raw.get("version", 1)),
                        raw.get("proposal_id"),
                        str(raw.get("updated_at", "")),
                        digest,
                        str(raw.get("author_id", "legacy")),
                    ),
                )
            for proposal_id, proposal in payload.get("proposals", {}).items():
                connection.execute(
                    "INSERT OR IGNORE INTO proposals(proposal_id, payload_json) VALUES (?, ?)",
                    (str(proposal_id), self._json(proposal)),
                )
            connection.execute(
                "INSERT OR REPLACE INTO metadata(key, value) VALUES ('legacy_source_digest', ?)",
                ("sha256:" + hashlib.sha256(source.read_bytes()).hexdigest(),),
            )
            connection.execute(
                "INSERT OR REPLACE INTO metadata(key, value) VALUES ('legacy_backup', ?)",
                (str(backup),),
            )
            self._bump_revision(connection)
            connection.commit()
            return True

    def acquire_lease(
        self,
        task_id: str,
        worker_id: str,
        ttl_s: int,
        *,
        now: str | None = None,
    ) -> dict[str, Any]:
        if not str(task_id).strip() or not str(worker_id).strip() or int(ttl_s) < 1:
            raise ValueError("task_id, worker_id and positive ttl_s are required")
        current = self._parse_time(now) if now else datetime.now(timezone.utc)
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM leases WHERE task_id=?", (task_id,)).fetchone()
            if row is not None and self._parse_time(row["expires_at"]) >= current:
                raise ConcurrentUpdateError("active lease is held by another worker")
            counter = int(connection.execute("SELECT value FROM metadata WHERE key='fencing_counter'").fetchone()["value"]) + 1
            connection.execute("UPDATE metadata SET value=? WHERE key='fencing_counter'", (str(counter),))
            expires = current + timedelta(seconds=int(ttl_s))
            lease = {
                "task_id": task_id,
                "worker_id": worker_id,
                "expires_at": expires.isoformat(),
                "fencing_token": counter,
            }
            connection.execute(
                "INSERT OR REPLACE INTO leases(task_id, worker_id, expires_at, fencing_token) VALUES (?, ?, ?, ?)",
                (task_id, worker_id, lease["expires_at"], counter),
            )
            self._bump_revision(connection)
            connection.commit()
            return lease

    def release_lease(self, task_id: str, worker_id: str, fencing_token: int) -> None:
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM leases WHERE task_id=?", (task_id,)).fetchone()
            if row is None or row["worker_id"] != worker_id or int(row["fencing_token"]) != int(fencing_token):
                raise PermissionError("lease fencing token is invalid")
            connection.execute("DELETE FROM leases WHERE task_id=?", (task_id,))
            self._bump_revision(connection)
            connection.commit()

    def observe(
        self,
        name: str,
        status: str,
        details: Mapping[str, Any] | None = None,
        *,
        observed_at: str | None = None,
        ttl_s: int | None = None,
    ) -> dict[str, Any]:
        if not str(name).strip() or not str(status).strip():
            raise ValueError("observation name and status are required")
        observed = self._parse_time(observed_at) if observed_at else datetime.now(timezone.utc)
        expires = observed + timedelta(seconds=max(1, int(ttl_s))) if ttl_s is not None else None
        safe_details = dict(details or {})
        payload = {
            "name": str(name),
            "status": str(status),
            "details": safe_details,
            "observed_at": observed.isoformat(),
            "expires_at": expires.isoformat() if expires else None,
        }
        with self._lock, self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            connection.execute(
                """
                INSERT OR REPLACE INTO observations(name, status, details_json, observed_at, expires_at)
                VALUES (?, ?, ?, ?, ?)
                """,
                (payload["name"], payload["status"], self._json(safe_details), payload["observed_at"], payload["expires_at"]),
            )
            self._bump_revision(connection)
            connection.commit()
        return payload

    def observation_snapshot(self, *, now: str | None = None) -> list[dict[str, Any]]:
        current = self._parse_time(now) if now else datetime.now(timezone.utc)
        result = []
        for observation in self.snapshot()["observations"]:
            expires = observation["expires_at"]
            if expires and self._parse_time(expires) <= current:
                observation = dict(observation, status="UNKNOWN")
            result.append(observation)
        return result

    def _check_revision(self, connection: sqlite3.Connection, expected_revision: int | None) -> None:
        if expected_revision is not None and self._revision(connection) != int(expected_revision):
            raise ConcurrentUpdateError(
                f"stale state revision: expected {expected_revision}, current {self._revision(connection)}"
            )

    def _revision(self, connection: sqlite3.Connection) -> int:
        return int(connection.execute("SELECT value FROM metadata WHERE key='state_revision'").fetchone()["value"])

    def _bump_revision(self, connection: sqlite3.Connection) -> int:
        revision = self._revision(connection) + 1
        connection.execute("UPDATE metadata SET value=? WHERE key='state_revision'", (str(revision),))
        return revision

    @staticmethod
    def _binding_from_row(row: sqlite3.Row) -> dict[str, Any]:
        return {
            "employee_id": row["employee_id"],
            "primary_model": row["primary_model"],
            "fallback_models": json.loads(row["fallback_models_json"]),
            "state": row["state"],
            "version": int(row["version"]),
            "proposal_id": row["proposal_id"],
            "updated_at": row["updated_at"],
            "candidate_digest": row["candidate_digest"],
            "author_id": row["author_id"],
        }

    @staticmethod
    def _idempotent_result(connection: sqlite3.Connection, key: str) -> dict[str, Any] | None:
        row = connection.execute("SELECT result_json FROM idempotency WHERE idempotency_key=?", (key,)).fetchone()
        return json.loads(row["result_json"]) if row else None

    @staticmethod
    def _store_idempotent(connection: sqlite3.Connection, key: str, result: Mapping[str, Any]) -> None:
        connection.execute(
            "INSERT INTO idempotency(idempotency_key, result_json) VALUES (?, ?)",
            (key, json.dumps(dict(result), ensure_ascii=False, sort_keys=True)),
        )

    @staticmethod
    def _json(value: Any) -> str:
        return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()

    @staticmethod
    def _parse_time(value: str) -> datetime:
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc)
