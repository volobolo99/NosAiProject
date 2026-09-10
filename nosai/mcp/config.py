from __future__ import annotations

import json
from pathlib import Path
from typing import Any


DEFAULT_CONFIG: dict[str, Any] = {
    "schema_version": "mcp.config.v1",
    "network_enabled": False,
    "audit_path": "data/mcp/audit.jsonl",
    "learning_path": "data/mcp/learning",
    "secret_path": "data/mcp/secrets.enc.json",
    "role_bindings_path": "data/mcp/role_bindings.json",
    "providers": [
        {"provider_id": "ollama-local", "model_id": "qwen2.5-coder:7b", "tier": 0, "network_required": False},
        {"provider_id": "groq-free", "model_id": "qwen/qwen3-32b", "tier": 1, "network_required": True},
        {"provider_id": "openrouter-free", "model_id": "openrouter/free", "tier": 2, "network_required": True},
    ],
}


def load_config(path: Path | str | None = None) -> dict[str, Any]:
    target = Path(path) if path else Path("config/mcp.default.json")
    if not target.is_file():
        return json.loads(json.dumps(DEFAULT_CONFIG))
    loaded = json.loads(target.read_text(encoding="utf-8"))
    if not isinstance(loaded, dict):
        raise ValueError("MCP config must be a JSON object")
    merged = json.loads(json.dumps(DEFAULT_CONFIG))
    merged.update(loaded)
    merged["network_enabled"] = bool(merged.get("network_enabled", False))
    return merged


