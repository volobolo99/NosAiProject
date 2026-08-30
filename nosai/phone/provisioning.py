from __future__ import annotations

import logging
import subprocess
import time
from pathlib import Path

from nosai.phone.adb import APK_NAME, GUARD_PORT, PACKAGE_NAME

logger = logging.getLogger("NosAi.Provisioning")


class GuardProvisioningManager:
    """Guided USB/ADB provisioning for phone Guard AI."""

    def __init__(self, root_path: str | Path) -> None:
        self.root_path = Path(root_path).resolve()
        self.adb_path = self.root_path / "tools" / "adb" / "adb.exe"
        self.apk_path = self.root_path / "runtime" / APK_NAME
        self.package_name = PACKAGE_NAME

    def _execute_adb(self, args: list[str], timeout: float = 10.0) -> str | None:
        if not self.adb_path.is_file():
            logger.error("ADB executable not found: %s", self.adb_path)
            return None
        try:
            result = subprocess.run(
                [str(self.adb_path), *args], stdout=subprocess.PIPE,
                stderr=subprocess.PIPE, text=True, timeout=timeout, check=True,
            )
            return result.stdout.strip()
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError) as exc:
            logger.warning("ADB command failed: %s (%s)", args, exc)
            return None

    def wait_for_phone(self, timeout: float = 120.0) -> str | None:
        logger.info("Waiting for an authorized Android device via USB...")
        self._execute_adb(["start-server"])
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            output = self._execute_adb(["devices"])
            if output:
                for line in output.splitlines()[1:]:
                    parts = line.split()
                    if len(parts) != 2:
                        continue
                    device_id, state = parts
                    if state == "device":
                        return device_id
                    if state == "unauthorized":
                        logger.warning("Android device detected but ADB authorization is pending.")
            time.sleep(2.0)
        return None

    def run_onboarding_flow(self, timeout: float = 120.0) -> bool:
        device_id = self.wait_for_phone(timeout)
        if not device_id:
            return False
        installed = self._execute_adb(
            ["-s", device_id, "shell", "pm", "list", "packages", self.package_name]
        )
        if not (installed and self.package_name in installed):
            if not self.apk_path.is_file():
                logger.error("Guard AI APK not found: %s", self.apk_path)
                return False
            result = self._execute_adb(
                ["-s", device_id, "install", "-r", "-g", str(self.apk_path)], timeout=60.0
            )
            if not (result and "Success" in result):
                return False

        # Without this the app had nothing to reach: the runtime listens on the PC,
        # so the phone's own localhost:GUARD_PORT has to be carried back here.
        # `reverse`, not `forward`, which points the tunnel the other way.
        if self._execute_adb(
            ["-s", device_id, "reverse", f"tcp:{GUARD_PORT}", f"tcp:{GUARD_PORT}"]
        ) is None:
            logger.error("Could not open the reverse tunnel on port %s", GUARD_PORT)
            return False

        return self._execute_adb(
            ["-s", device_id, "shell", "monkey", "-p", self.package_name, "1"]
        ) is not None
