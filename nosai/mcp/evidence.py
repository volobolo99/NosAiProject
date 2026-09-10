from __future__ import annotations

import hashlib
import hmac
import json
import os
import re
import secrets
import threading
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Mapping

_DIGEST_RE = re.compile(r"^sha256:[0-9a-f]{64}$")
_FORBIDDEN_KEYS = {
    "api_key",
    "apikey",
    "credential",
    "password",
    "secret",
    "token",
    "access_token",
    "refresh_token",
    "authorization",
    "private_key",
}
_PASS_STATUSES = {"ok", "pass", "passed", "success", "verified"}
_DEFAULT_TTL_S = 3600


class EvidenceAuthority:
    """Local authority for signed, time-bounded promotion evidence.

    Evidence contains digests and status metadata only. It never stores provider
    credentials or raw gameplay/client data.
    """

    def __init__(self, root: Path | str, default_ttl_s: int = _DEFAULT_TTL_S) -> None:
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self.default_ttl_s = max(1, int(default_ttl_s))
        self._key_path = self.root / "signing.key"
        self._records_path = self.root / "records.json"
        self._lock = threading.RLock()
        self._key = self._load_or_create_key()
        self._records = self._load_records()

    def record(
        self,
        candidate_digest: str,
        test_version: str,
        dataset_digest: str,
        executor_id: str,
        environment_digest: str,
        artifact_digest: str,
        result: Mapping[str, Any],
        signer_id: str,
        *,
        evidence_kind: str = "generic",
        observed_at: str | None = None,
        ttl_s: int | None = None,
    ) -> dict[str, Any]:
        """Create and persist one signed evidence record."""
        for name, value in (
            ("candidate_digest", candidate_digest),
            ("dataset_digest", dataset_digest),
            ("environment_digest", environment_digest),
            ("artifact_digest", artifact_digest),
        ):
            self._validate_digest(name, value)
        if not str(test_version).strip():
            raise ValueError("test_version is required")
        if not str(executor_id).strip() or not str(signer_id).strip():
            raise ValueError("executor_id and signer_id are required")
        if not str(evidence_kind).strip():
            raise ValueError("evidence_kind is required")
        safe_result = self._safe_result(result)
        observed = self._parse_time(observed_at) if observed_at else datetime.now(timezone.utc)
        expires = observed + timedelta(seconds=max(1, int(ttl_s or self.default_ttl_s)))
        payload: dict[str, Any] = {
            "evidence_id": uuid.uuid4().hex,
            "evidence_kind": str(evidence_kind).strip(),
            "candidate_digest": candidate_digest,
            "test_version": str(test_version).strip(),
            "dataset_digest": dataset_digest,
            "executor_id": str(executor_id).strip(),
            "environment_digest": environment_digest,
            "artifact_digest": artifact_digest,
            "result": safe_result,
            "observed_at": observed.isoformat(),
            "expires_at": expires.isoformat(),
            "signer_id": str(signer_id).strip(),
        }
        payload["signature"] = self._sign(payload)
        with self._lock:
            self._records[payload["evidence_id"]] = payload
            self._persist()
            return dict(payload)

    def get(self, evidence_id: str) -> dict[str, Any]:
        with self._lock:
            record = self._records.get(str(evidence_id))
            if record is None:
                raise KeyError("unknown evidence_id")
            return dict(record)

    def list(self) -> list[dict[str, Any]]:
        with self._lock:
            return [dict(self._records[key]) for key in sorted(self._records)]

    def verify(
        self,
        record: Mapping[str, Any],
        *,
        expected_candidate_digest: str | None = None,
        expected_kind: str | None = None,
        now: datetime | None = None,
    ) -> tuple[bool, str]:
        """Verify integrity, freshness and semantic status without mutation."""
        required = (
            "evidence_id",
            "evidence_kind",
            "candidate_digest",
            "test_version",
            "dataset_digest",
            "executor_id",
            "environment_digest",
            "artifact_digest",
            "result",
            "observed_at",
            "expires_at",
            "signer_id",
            "signature",
        )
        if any(name not in record for name in required):
            return False, "incomplete evidence record"
        try:
            self._validate_digest("candidate_digest", str(record["candidate_digest"]))
            self._validate_digest("dataset_digest", str(record["dataset_digest"]))
            self._validate_digest("environment_digest", str(record["environment_digest"]))
            self._validate_digest("artifact_digest", str(record["artifact_digest"]))
            observed = self._parse_time(str(record["observed_at"]))
            expires = self._parse_time(str(record["expires_at"]))
        except (TypeError, ValueError):
            return False, "invalid evidence fields"
        if expected_candidate_digest and record["candidate_digest"] != expected_candidate_digest:
            return False, "candidate digest mismatch"
        if expected_kind and record["evidence_kind"] != expected_kind:
            return False, "evidence kind mismatch"
        current = now or datetime.now(timezone.utc)
        if expires <= current or observed > current + timedelta(seconds=5):
            return False, "stale evidence"
        expected_signature = self._sign(record)
        supplied_signature = str(record.get("signature", ""))
        if not hmac.compare_digest(expected_signature, supplied_signature):
            return False, "invalid evidence signature"
        result = record.get("result")
        if not isinstance(result, Mapping) or str(result.get("status", "")).lower() not in _PASS_STATUSES:
            return False, "evidence result is not passing"
        return True, "verified"

    @staticmethod
    def _validate_digest(name: str, value: str) -> None:
        if not _DIGEST_RE.fullmatch(str(value)):
            raise ValueError(f"{name} must be a sha256:<64 hex> digest")

    @classmethod
    def _safe_result(cls, value: Mapping[str, Any]) -> dict[str, Any]:
        if not isinstance(value, Mapping):
            raise TypeError("result must be a JSON object")
        encoded = json.dumps(value, ensure_ascii=False, sort_keys=True)
        if len(encoded) > 16_384:
            raise ValueError("evidence result is too large")
        def visit(node: Any) -> None:
            if isinstance(node, Mapping):
                lowered = {str(key).lower().strip() for key in node}
                forbidden = {
                    key
                    for key in lowered
                    if key in _FORBIDDEN_KEYS or key.endswith("_token") or key.endswith("_secret")
                }
                if forbidden:
                    raise ValueError("evidence result cannot contain secret-like fields")
                for child in node.values():
                    visit(child)
            elif isinstance(node, (list, tuple)):
                for child in node:
                    visit(child)

        visit(value)
        json.loads(encoded)
        return json.loads(encoded)

    def _sign(self, record: Mapping[str, Any]) -> str:
        unsigned = {key: value for key, value in record.items() if key != "signature"}
        canonical = json.dumps(unsigned, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        return hmac.new(self._key, canonical, hashlib.sha256).hexdigest()

    def _load_or_create_key(self) -> bytes:
        if self._key_path.is_file():
            key = self._key_path.read_bytes()
            if len(key) < 32:
                raise ValueError("evidence signing key is too short")
            return key
        key = secrets.token_bytes(32)
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
        descriptor = os.open(self._key_path, flags, 0o600)
        try:
            os.write(descriptor, key)
        finally:
            os.close(descriptor)
        return key

    def _load_records(self) -> dict[str, dict[str, Any]]:
        if not self._records_path.is_file():
            return {}
        payload = json.loads(self._records_path.read_text(encoding="utf-8"))
        if not isinstance(payload, dict) or not isinstance(payload.get("records", {}), dict):
            raise ValueError("evidence store must be a JSON object")
        return {str(key): dict(value) for key, value in payload["records"].items()}

    def _persist(self) -> None:
        payload = {
            "schema_version": "mcp.evidence.v1",
            "records": self._records,
        }
        temporary = self._records_path.with_name(self._records_path.name + ".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temporary, self._records_path)

    @staticmethod
    def _parse_time(value: str) -> datetime:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc)

