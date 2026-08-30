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


# Seven fractional digits and a literal Z, matching the C# side. Both languages
# serialise the same contract, so the representation is pinned here rather than
# left to each one's default formatter.
def _to_wire_timestamp(moment: datetime) -> str:
    utc = moment.astimezone(timezone.utc)
    return utc.strftime("%Y-%m-%dT%H:%M:%S.") + "{:06d}0Z".format(utc.microsecond)


@dataclass(frozen=True)
class ClassifiedValue(Generic[T]):
    value: T | None
    source: DataSource
    observed_at: datetime
    warning: str | None = None
    failure_reason: str | None = None
    has_observed_value: bool = True
    """Whether a value was actually observed.

    Kept separate from ``value`` so an observed ``None`` stays distinguishable
    from a reading that never happened. ``ClassifiedValue<T>`` in C# carries the
    same flag; without it the two sides of the contract disagree.
    """

    @property
    def has_value(self) -> bool:
        return self.has_observed_value and self.source is not DataSource.UNKNOWN

    @staticmethod
    def live(value: T, warning: str | None = None) -> "ClassifiedValue[T]":
        return ClassifiedValue(value, DataSource.LIVE, datetime.now(timezone.utc), warning, None)

    @staticmethod
    def derived(value: T, warning: str | None = None) -> "ClassifiedValue[T]":
        return ClassifiedValue(value, DataSource.DERIVED, datetime.now(timezone.utc), warning, None)

    @staticmethod
    def cached(value: T, observed_at: datetime, warning: str | None = None) -> "ClassifiedValue[T]":
        return ClassifiedValue(value, DataSource.CACHED, observed_at, warning, None)

    @staticmethod
    def simulated(value: T, warning: str | None = None) -> "ClassifiedValue[T]":
        """Explicit home for simulated data.

        Without it the only convenient constructor is ``live``, which is exactly
        how simulated values end up labelled as real.
        """
        return ClassifiedValue(value, DataSource.SIMULATED, datetime.now(timezone.utc), warning, None)

    @staticmethod
    def unknown(reason: str, warning: str | None = None) -> "ClassifiedValue[Any]":
        return ClassifiedValue(
            None, DataSource.UNKNOWN, datetime.now(timezone.utc), warning, reason, has_observed_value=False
        )

    def to_wire(self) -> dict[str, Any]:
        return {
            "value": self.value if self.has_value else None,
            "source": self.source.value,
            "observedAtUtc": _to_wire_timestamp(self.observed_at),
            "hasObservedValue": self.has_observed_value,
            "warning": self.warning,
            "failureReason": self.failure_reason,
        }
