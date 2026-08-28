"""Local test runner that always emits a Test Evidence Bundle."""
from __future__ import annotations

import os
from pathlib import Path
import platform
import subprocess
import sys
from typing import Sequence

from .evidence import TestEvidenceBundle, TestResult


def run_pytest_with_evidence(
    args: Sequence[str] = (),
    *,
    evidence_root: str | Path = ".nosai/test-center/runs",
    pc_status: str = "NOT_RUN",
    smartphone_status: str = "NOT_RUN",
) -> int:
    """Run pytest and persist its result; physical gates default to blocked."""
    commit = os.environ.get("GITHUB_SHA") or os.environ.get("NOSAI_GIT_COMMIT")
    bundle = TestEvidenceBundle(
        evidence_root,
        git_commit=commit,
        platform=platform.platform(),
        device={"machine": platform.machine(), "python": sys.version.split()[0]},
    )
    command = [sys.executable, "-m", "pytest", *args]
    completed = subprocess.run(command, text=True, capture_output=True, check=False)
    output = (completed.stdout + "\n" + completed.stderr).strip()
    bundle.add_event({"type": "pytest_completed", "returncode": completed.returncode, "output": output})
    bundle.add_test(TestResult(
        name="pytest",
        status="PASS" if completed.returncode == 0 else "FAIL",
        message=output[-8000:],
    ))
    final = bundle.finalize(required_gates={"pc": pc_status, "smartphone": smartphone_status})
    print(f"NosAi Test Evidence: {bundle.path} status={final}")
    return 0 if final == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(run_pytest_with_evidence(sys.argv[1:]))
