"""Let a pytest test publish what it actually observed.

pytest runs out of process from the operator's test page, so the only channel
back is standard output: pytest captures it and writes it into the JUnit report,
where the console reads it again. A test emits one marker line per observation.

The classification travels with the value, as everywhere else in this project.
A value the test genuinely read is ``LIVE``; a value produced by a fixture or a
simulation is ``SIMULATED``; something the test could not determine is
``UNKNOWN`` with a reason. ``UNKNOWN`` is never zero, false or empty.

    from nosai.testing.evidence import live, unknown

    def test_wal_is_configured():
        policy = default_sqlite_policy()
        live("journal_mode", policy.journal_mode)
        assert policy.journal_mode == "WAL"
"""
from __future__ import annotations

import json
from typing import Any, Optional

#: Matches the constant on the C# side. Deliberately unlikely to occur by accident.
MARKER = "##nosai-evidence##"

#: The classifications the runtime recognises. Anything else is read back as
#: UNKNOWN rather than trusted, so a typo cannot promote a guess to a fact.
LIVE = "Live"
DERIVED = "Derived"
CACHED = "Cached"
SIMULATED = "Simulated"
UNKNOWN = "Unknown"


def emit(key: str, value: Any, source: str = LIVE, note: Optional[str] = None) -> None:
    """Publish one observation to the operator's test page."""
    if isinstance(value, bool):
        rendered = "true" if value else "false"
    elif value is None:
        rendered = "null"
    else:
        rendered = str(value)

    payload = {"key": key, "value": rendered, "source": source, "note": note}
    print(f"{MARKER} {json.dumps(payload, ensure_ascii=False)}")


def live(key: str, value: Any, note: Optional[str] = None) -> None:
    """A value the test genuinely observed."""
    emit(key, value, LIVE, note)


def derived(key: str, value: Any, note: Optional[str] = None) -> None:
    """A value computed from observed values."""
    emit(key, value, DERIVED, note)


def simulated(key: str, value: Any, note: Optional[str] = None) -> None:
    """A value from a fixture or a simulation, never to be read as live."""
    emit(key, value, SIMULATED, note)


def unknown(key: str, reason: str) -> None:
    """Something the test could not determine, with why."""
    emit(key, "UNKNOWN", UNKNOWN, reason)
