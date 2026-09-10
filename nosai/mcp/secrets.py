from __future__ import annotations

import base64
import hashlib
import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from cryptography.fernet import Fernet, InvalidToken

from .contracts import SecretMetadata


class SecretStore:
    """Encrypted local provider-key store; values are never returned by APIs."""

    def __init__(self, path: Path | str, master_key: bytes | None = None):
        self.path = Path(path)
        raw = master_key or os.environ.get("NOSAI_MCP_MASTER_KEY", "").encode()
        if not raw:
            raise RuntimeError("NOSAI_MCP_MASTER_KEY is required; refusing plaintext secret storage")
        if len(raw) != 44:
            raw = base64.urlsafe_b64encode(hashlib.sha256(raw).digest())
        self._fernet = Fernet(raw)

    def _load(self) -> dict[str, Any]:
        if not self.path.is_file():
            return {}
        try:
            token = self.path.read_bytes()
            return json.loads(self._fernet.decrypt(token).decode("utf-8"))
        except (InvalidToken, ValueError, json.JSONDecodeError) as exc:
            raise RuntimeError("encrypted MCP secret store is invalid or unreadable") from exc

    def _save(self, data: dict[str, Any]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        token = self._fernet.encrypt(json.dumps(data, ensure_ascii=False, sort_keys=True).encode("utf-8"))
        self.path.write_bytes(token)

    def upsert(self, provider_id: str, secret: str) -> SecretMetadata:
        if not secret or "\n" in secret:
            raise ValueError("secret must be a non-empty single-line value")
        data = self._load()
        now = datetime.now(timezone.utc).isoformat()
        fingerprint = hashlib.sha256(secret.encode()).hexdigest()[:12]
        data[provider_id] = {"value": secret, "updated_at": now, "fingerprint": fingerprint}
        self._save(data)
        return SecretMetadata(provider_id, True, now, fingerprint)

    def delete(self, provider_id: str) -> bool:
        data = self._load()
        existed = provider_id in data
        data.pop(provider_id, None)
        self._save(data)
        return existed

    def metadata(self) -> list[SecretMetadata]:
        return [
            SecretMetadata(provider_id, True, value.get("updated_at"), value.get("fingerprint"))
            for provider_id, value in sorted(self._load().items())
        ]

    def resolve_for_internal_call(self, provider_id: str) -> str | None:
        """Internal-only resolution; callers must not serialize or log the result."""
        item = self._load().get(provider_id)
        return item.get("value") if item else None


