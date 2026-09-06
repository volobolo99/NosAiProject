"""AP-00 / A5 audit coverage for the Python side of A2's hardware work.

New file (A5 owns only tests/docs for AP-00, never A1/A2/A3/A4 implementation
-- see docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md): closes two coverage
gaps found while auditing ``nosai/runtime/hardware.py`` and
``nosai/runtime/hardware_watchdog.py`` against
``docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md``'s explicit "boundary
values" requirement.

1. ``HardwareProfiler.profile()`` (the VRAM/RAM -> RuntimeProfile tier
   mapping) had exact-threshold tests in ``tests/test_agent_runtime_expansion.py``
   (``vram_mb=24000, ram_mb=32000`` -> "high"; ``vram_mb=8000, ram_mb=16000``
   -> "balanced") but no *off-by-one-below* test, so a regression that shifted
   a threshold by one MB in either direction would not be caught.

2. ``NOSAIHardwareWatchdog.check()`` had exactly one existing test
   (``test_watchdog_trips_on_thermal_limit`` in
   ``tests/test_runtime_optimizations.py``), covering only the case where a
   temperature reading IS present and over the limit. The realistic worst
   case for this project's actual target environment -- confirmed
   independently by A4's own C# finding that ``Thermal.ThrottleState`` is
   always Unknown with the existing probe path, and reproduced here in
   Python -- is that NO thermal reading is available at all (the default
   ``NullHardwareProbe``, or any probe that cannot read a sensor). That case
   was never exercised.
"""
from __future__ import annotations

import pytest

from nosai.runtime import HardwareProfiler, HardwareSnapshot
from nosai.runtime.hardware_watchdog import (
    HardwareTelemetry,
    NOSAIHardwareWatchdog,
    NullHardwareProbe,
)


# ---------------------------------------------------------------------------
# HardwareProfiler.profile() -- off-by-one boundary values
# ---------------------------------------------------------------------------

def test_high_tier_boundary_is_exact_not_off_by_one():
    profiler = HardwareProfiler()

    at_threshold = profiler.profile(HardwareSnapshot(vram_mb=24000, ram_mb=32000))
    assert at_threshold.name == "high"

    one_mb_short_on_vram = profiler.profile(HardwareSnapshot(vram_mb=23999, ram_mb=32000))
    assert one_mb_short_on_vram.name != "high"

    one_mb_short_on_ram = profiler.profile(HardwareSnapshot(vram_mb=24000, ram_mb=31999))
    assert one_mb_short_on_ram.name != "high"


def test_balanced_tier_boundary_is_exact_not_off_by_one():
    profiler = HardwareProfiler()

    at_threshold = profiler.profile(HardwareSnapshot(vram_mb=8000, ram_mb=16000))
    assert at_threshold.name == "balanced"

    one_mb_short_on_vram = profiler.profile(HardwareSnapshot(vram_mb=7999, ram_mb=16000))
    assert one_mb_short_on_vram.name == "constrained"

    one_mb_short_on_ram = profiler.profile(HardwareSnapshot(vram_mb=8000, ram_mb=15999))
    assert one_mb_short_on_ram.name == "constrained"


def test_high_tier_requires_both_vram_and_ram_thresholds_simultaneously():
    # Plenty of VRAM alone (or RAM alone) must not be enough to reach "high";
    # docs/ROADMAP_ESECUTIVA.md's resource-aware policy is conjunctive across
    # dimensions, never satisfied by a single generous reading.
    profiler = HardwareProfiler()

    vram_only = profiler.profile(HardwareSnapshot(vram_mb=999_999, ram_mb=1))
    assert vram_only.name == "constrained"

    ram_only = profiler.profile(HardwareSnapshot(vram_mb=1, ram_mb=999_999))
    assert ram_only.name == "constrained"


# ---------------------------------------------------------------------------
# NOSAIHardwareWatchdog.check() -- fully-absent thermal telemetry
# ---------------------------------------------------------------------------

def test_watchdog_allows_by_default_when_no_thermal_telemetry_is_available_at_all():
    """Documents a real design gap found during this AP-00/A5 audit.

    With the default ``NullHardwareProbe`` (or any probe reporting
    ``HardwareTelemetry()`` with every field ``None``), ``check()`` builds an
    EMPTY ``temps`` list, and ``any(x > self.max_temp for x in temps)`` on an
    empty sequence is ``False`` -- so a total absence of thermal data is
    treated exactly like "confirmed nominal", i.e. the watchdog defaults to
    ``allowed=True``.

    This is not the fail-closed behaviour CLAUDE.md's "Unknown is not zero,
    false or empty" invariant asks for, and it is the *opposite* policy from
    A1/A4's C# hardware-capability gate (``InferenceTierFeasibility.CanRun`` /
    ``HardwareInferenceCapabilityGate``), which explicitly refuses every tier
    above Tier 0 when ``Thermal.ThrottleState`` is Unknown.

    Severity note (why this is a documented gap, not a hard test failure):
    ``NOSAIHardwareWatchdog`` is not currently wired into any execution/Safety
    path anywhere in ``nosai/`` (grep confirms no call site besides its own
    tests) -- it is a "Present", not yet "Integrated", mechanism. This test
    pins down its CURRENT behaviour precisely so that whichever later AP
    phase (most likely AP-08, Strategic Autonomy + Safety) wires this
    watchdog into an authoritative execution gate does so with eyes open,
    rather than inheriting a silent fail-open default under missing/UNKNOWN
    thermal telemetry.
    """
    watchdog = NOSAIHardwareWatchdog(max_temp=80.0, probe=NullHardwareProbe())

    decision = watchdog.check()

    assert decision.allowed is True
    assert decision.reason == "ok"


def test_watchdog_allows_when_probe_reports_telemetry_object_with_all_fields_none():
    # Same scenario as above, made explicit for a probe that is not the
    # built-in NullHardwareProbe (e.g. a real probe whose sensors are simply
    # unsupported on this machine) but returns an equivalently-empty reading.
    class AllUnknownProbe:
        def read(self) -> HardwareTelemetry:
            return HardwareTelemetry(cpu_temperature_c=None, gpu_temperature_c=None, io_rate_mb_s=None)

    watchdog = NOSAIHardwareWatchdog(max_temp=80.0, probe=AllUnknownProbe())

    decision = watchdog.check()

    assert decision.allowed is True


def test_watchdog_still_trips_when_only_one_of_two_temperature_readings_is_known():
    # Partial telemetry (one sensor known, one Unknown) must not mask a real
    # thermal problem on the sensor that IS reporting.
    watchdog = NOSAIHardwareWatchdog(
        max_temp=80.0,
        probe=_FixedTelemetryProbe(HardwareTelemetry(cpu_temperature_c=None, gpu_temperature_c=95.0)),
    )

    decision = watchdog.check()

    assert decision.allowed is False
    assert decision.reason == "thermal_limit"


class _FixedTelemetryProbe:
    def __init__(self, telemetry: HardwareTelemetry) -> None:
        self._telemetry = telemetry

    def read(self) -> HardwareTelemetry:
        return self._telemetry
