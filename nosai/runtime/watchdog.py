"""Independent runtime watchdog for bounded autonomous execution."""
from dataclasses import dataclass
from time import monotonic
from typing import Callable


@dataclass(frozen=True)
class WatchdogPolicy:
    max_runtime_ms: int = 30_000
    max_consecutive_failures: int = 3
    max_actions: int = 32


class RuntimeWatchdog:
    """Model-independent kill switch; it can only reduce execution, never grant it."""
    def __init__(self, policy: WatchdogPolicy | None = None,
                 on_trip: Callable[[str], None] | None = None) -> None:
        self.policy = policy or WatchdogPolicy()
        self.on_trip = on_trip
        self.started_at = monotonic()
        self.actions = 0
        self.consecutive_failures = 0
        self.tripped = False
        self.reason = ""

    def before_action(self) -> bool:
        if self.tripped:
            return False
        if self.actions >= self.policy.max_actions:
            return self.trip("action_budget_exhausted")
        if self.elapsed_ms() > self.policy.max_runtime_ms:
            return self.trip("runtime_budget_exhausted")
        return True

    def after_action(self, success: bool) -> None:
        self.actions += 1
        if success:
            self.consecutive_failures = 0
        else:
            self.consecutive_failures += 1
            if self.consecutive_failures >= self.policy.max_consecutive_failures:
                self.trip("consecutive_failure_limit")

    def elapsed_ms(self) -> float:
        return (monotonic() - self.started_at) * 1000.0

    def trip(self, reason: str) -> bool:
        self.tripped = True
        self.reason = reason
        if self.on_trip is not None:
            self.on_trip(reason)
        return False

    def reset(self) -> None:
        self.started_at = monotonic()
        self.actions = 0
        self.consecutive_failures = 0
        self.tripped = False
        self.reason = ""
