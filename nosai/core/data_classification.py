"""Explicit provenance for externally visible observations."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Generic, TypeVar

T = TypeVar("T")


class DataSource(str, Enum):
    LIVE = "LIVE"
    DERIVED = "DERIVED"
    CACHED = "CACHED"
    SIMULATED = "SIMULATED"
    UNKNOWN = "UNKNOWN"


@dataclass(frozen=True)
class ClassifiedValue(Generic[T]):
    value: T | None
    source: DataSource
    observed_at: datetime
    warning: str | None = None
    failure_reason: str | None = None

    @staticmethod
    def live(value: T, warning: str | None = None) -> "ClassifiedValue[T]":
        return ClassifiedValue(value, DataSource.LIVE, datetime.now(timezone.utc), warning, None)

    @staticmethod
    def unknown(reason: str, warning: str | None = None) -> "ClassifiedValue[Any]":
        return ClassifiedValue(None, DataSource.UNKNOWN, datetime.now(timezone.utc), warning, reason)

    def to_wire(self) -> dict[str, Any]:
        return {
            "value": None if self.source is DataSource.UNKNOWN else self.value,
            "source": self.source.value,
            "observedAtUtc": self.observed_at.isoformat(),
            "warning": self.warning,
            "failureReason": self.failure_reason,
        }
