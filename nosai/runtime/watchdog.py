"""Adaptive runtime watchdog with recovery-oriented operating modes."""
from __future__ import annotations
from dataclasses import dataclass
from enum import Enum
from time import monotonic
from typing import Callable


class WatchdogMode(str, Enum):
    NORMAL = "NORMAL"
    DEGRADED = "DEGRADED"
    COOLING = "COOLING"
    RECOVERY = "RECOVERY"
    STOPPED = "STOPPED"


@dataclass(frozen=True)
class WatchdogPolicy:
    max_runtime_ms: int = 30_000
    max_consecutive_failures: int = 3
    max_actions: int = 32
    degraded_runtime_factor: float = 0.5
    degraded_action_factor: float = 0.5


class RuntimeWatchdog:
    """Runtime controller that can stop, recover, or change operating mode."""
    def __init__(self, policy: WatchdogPolicy | None = None,
                 on_trip: Callable[[str], None] | None = None,
                 on_mode_change: Callable[[WatchdogMode, str], None] | None = None) -> None:
        self.policy = policy or WatchdogPolicy()
        self.on_trip = on_trip
        self.on_mode_change = on_mode_change
        self.started_at = monotonic()
        self.actions = 0
        self.consecutive_failures = 0
        self.tripped = False
        self.reason = ""
        self.mode = WatchdogMode.NORMAL

    @property
    def effective_runtime_ms(self) -> float:
        return self.policy.max_runtime_ms * (self.policy.degraded_runtime_factor if self.mode == WatchdogMode.DEGRADED else 1.0)

    @property
    def effective_actions(self) -> int:
        return max(1, int(self.policy.max_actions * (self.policy.degraded_action_factor if self.mode == WatchdogMode.DEGRADED else 1.0)))

    def before_action(self) -> bool:
        if self.tripped or self.mode == WatchdogMode.STOPPED or self.mode == WatchdogMode.COOLING:
            return False
        if self.actions >= self.effective_actions:
            return self.trip("action_budget_exhausted")
        if self.elapsed_ms() > self.effective_runtime_ms:
            return self.trip("runtime_budget_exhausted")
        return True

    def after_action(self, success: bool) -> None:
        self.actions += 1
        if success:
            self.consecutive_failures = 0
            if self.mode == WatchdogMode.RECOVERY:
                self.set_mode(WatchdogMode.NORMAL, "recovery_action_succeeded")
        else:
            self.consecutive_failures += 1
            if self.consecutive_failures >= self.policy.max_consecutive_failures:
                self.set_mode(WatchdogMode.RECOVERY, "consecutive_failure_recovery")
                self.trip("consecutive_failure_limit")

    def set_mode(self, mode: WatchdogMode, reason: str = "") -> None:
        self.mode = WatchdogMode(mode)
        if self.mode in (WatchdogMode.COOLING, WatchdogMode.STOPPED):
            self.tripped = True
        elif self.mode in (WatchdogMode.NORMAL, WatchdogMode.DEGRADED, WatchdogMode.RECOVERY):
            self.tripped = False
        if self.on_mode_change is not None:
            self.on_mode_change(self.mode, reason)

    def enter_recovery(self) -> None:
        self.set_mode(WatchdogMode.RECOVERY, "recovery_requested")

    def enter_cooling(self) -> None:
        self.set_mode(WatchdogMode.COOLING, "hardware_cooling")

    def resume(self, mode: WatchdogMode = WatchdogMode.NORMAL) -> None:
        self.tripped = False
        self.reason = ""
        self.set_mode(mode, "resume")

    def elapsed_ms(self) -> float:
        return (monotonic() - self.started_at) * 1000.0

    def trip(self, reason: str) -> bool:
        self.tripped = True
        self.reason = reason
        self.mode = WatchdogMode.STOPPED
        if self.on_trip is not None:
            self.on_trip(reason)
        return False

    def reset(self) -> None:
        self.started_at = monotonic()
        self.actions = 0
        self.consecutive_failures = 0
        self.tripped = False
        self.reason = ""
        self.mode = WatchdogMode.NORMAL
