"""PC-side Guard AI onboarding over an isolated ADB installation."""
from __future__ import annotations

import json
import subprocess
import time
from pathlib import Path

from nosai.network.wire_protocol import Frame, TYPE_SESSION_HELLO

PORT = 6100
PACKAGE_NAME = "com.nosai.guard"


class NosAiOnboardingError(RuntimeError):
    pass


class NosAiOnboardingEngine:
    def __init__(self, root_path: str | Path):
        self.root_path = Path(root_path)
        self.adb_path = self.root_path / "tools" / "adb" / "adb.exe"
        self.apk_path = self.root_path / "runtime" / "GuardAi.apk"

    def _adb(self, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
        if not self.adb_path.is_file():
            raise NosAiOnboardingError(f"ADB isolato assente: {self.adb_path}")
        return subprocess.run(
            [str(self.adb_path), *args],
            check=check,
            capture_output=True,
            text=True,
            timeout=15,
        )

    def _authorized_device_present(self) -> bool:
        result = self._adb("devices")
        return any(line.endswith("\tdevice") for line in result.stdout.splitlines())

    def provision(self) -> bool:
        """Provision only an authorized device; never download external components."""
        self._adb("start-server")
        if not self._authorized_device_present():
            return False
        if not self.apk_path.is_file():
            raise NosAiOnboardingError(f"APK Guard AI assente: {self.apk_path}")
        installed = self._adb("shell", "pm", "path", PACKAGE_NAME, check=False)
        if installed.returncode != 0:
            self._adb("install", "-r", "-g", str(self.apk_path))
        self._adb("forward", f"tcp:{PORT}", f"tcp:{PORT}")
        self._adb("shell", "monkey", "-p", PACKAGE_NAME, "1")
        time.sleep(2)
        return True

    @staticmethod
    def build_session_hello(challenge_hex: str) -> bytes:
        payload = json.dumps(
            {"type": "SESSION_HELLO", "version": "1.0-Beta", "challenge": challenge_hex},
            separators=(",", ":"),
        ).encode("utf-8")
        return Frame(TYPE_SESSION_HELLO, 1, payload).encode()
