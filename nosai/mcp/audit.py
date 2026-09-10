from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


_SECRET_PATTERN = re.compile(r"(?i)(api[_-]?key|token|secret|password)(\s*[:=]\s*)[^,\s}]+")


def redact(value: Any) -> Any:
    if isinstance(value, str):
        return _SECRET_PATTERN.sub(r"\1\2[REDACTED]", value)
    if isinstance(value, dict):
        return {str(key): ("[REDACTED]" if re.search(r"(?i)(key|token|secret|password)", str(key)) else redact(item)) for key, item in value.items()}
    if isinstance(value, list):
        return [redact(item) for item in value]
    return value


class AuditLog:
    def __init__(self, path: Path | str):
        self.path = Path(path)

    def append(self, event: str, payload: dict[str, Any] | None = None) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        record = {"ts": datetime.now(timezone.utc).isoformat(), "event": event, "payload": redact(payload or {})}
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")


