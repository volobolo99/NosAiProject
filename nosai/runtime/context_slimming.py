from __future__ import annotations

from dataclasses import dataclass
import hashlib
import re
from typing import Any, Mapping, Sequence

@dataclass(frozen=True)
class ExceptionSignature:
    signature: str
    message: str
    exception_type: str = "unknown"
    source: str = "unknown"
    attempt: int | None = None
    def as_dict(self) -> dict[str, Any]:
        d = {"signature": self.signature, "msg": self.message, "exception_type": self.exception_type, "source": self.source}
        if self.attempt is not None: d["attempt"] = self.attempt
        return d

class VRAMContextSlimmer:
    def __init__(self, max_errors: int = 3, max_message_chars: int = 240) -> None:
        if max_errors < 1 or max_message_chars < 32: raise ValueError("invalid context slimming limits")
        self.max_errors, self.max_message_chars = max_errors, max_message_chars
    @staticmethod
    def _normalize(error: str) -> str:
        value = re.sub(r"0x[0-9a-fA-F]+", "0xADDR", str(error))
        return re.sub(r"line\s+\d+", "line N", value, flags=re.IGNORECASE)
    def _signature(self, error: str) -> str:
        return hashlib.sha256(self._normalize(error).encode("utf-8", "replace")).hexdigest()[:16]
    def compress_error(self, error: Mapping[str, Any] | str, attempt: int | None = None) -> ExceptionSignature:
        if isinstance(error, Mapping):
            raw = str(error.get("errore", error.get("error", error.get("message", "unknown error"))))
            exc_type = str(error.get("exception_type", error.get("type", "unknown")))
            source = str(error.get("source", "unknown"))
        else: raw, exc_type, source = str(error), "unknown", "unknown"
        lines = [x.strip() for x in raw.splitlines() if x.strip()]
        return ExceptionSignature(self._signature(raw), (lines[-1] if lines else "unknown error")[:self.max_message_chars], exc_type, source, attempt)
    def comprimi_storico(self, funzione: str, storico_errori: Sequence[Mapping[str, Any] | str]) -> list[dict[str, Any]]:
        del funzione
        recent = list(storico_errori[-self.max_errors:])
        start = len(storico_errori) - len(recent)
        return [self.compress_error(e, start + i + 1).as_dict() for i, e in enumerate(recent)]
    def compress(self, storico_errori: Sequence[Mapping[str, Any] | str]) -> list[dict[str, Any]]:
        return self.comprimi_storico("runtime", storico_errori)
