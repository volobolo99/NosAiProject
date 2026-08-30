"""ADB mechanics for deploying and linking the Guard AI phone application.

Single implementation shared by the onboarding and provisioning flows, which
previously each carried their own copy and had drifted apart.

The one thing this module exists to get right is the direction of the tunnel.
Under ADR-0006 the **runtime is the listener**: it binds TCP/17471 on the PC and
the phone connects to it. So the device needs `adb reverse`, which makes the
phone's own localhost:17471 reach the PC. The previous code used `adb forward`,
which is the opposite — it makes the *PC* reach a port on the phone — and would
never have connected no matter how long anyone waited for a device.
"""
from __future__ import annotations

import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

#: Application id of the Guard AI app. Must match <ApplicationId> in
#: src/NosAi.GuardAi.App/NosAi.GuardAi.App.csproj; pinned by a test.
PACKAGE_NAME = "com.nosai.guardai"

#: Canonical Guard channel port. Must match Gate1HostOptions.DefaultGuardPort;
#: pinned by a test.
GUARD_PORT = 17471

#: Release output of the MAUI project.
APK_NAME = f"{PACKAGE_NAME}-Signed.apk"


class Gate1Defaults:
    """Paths and ports the runtime uses when nothing is passed on its command line.

    Mirrors constants in src/NosAi.Runtime/Configuration/Gate1HostOptions.cs and is
    pinned to them by tests, so pairing writes where the runtime actually looks.
    """

    #: Gate1HostOptions.DefaultTrustedKeyPath
    TRUSTED_KEY_PATH = "data/guard_public_key.pem"

    #: DiscoveryProtocol.Port
    DISCOVERY_PORT = 17472


BUILT_APK_RELATIVE = Path("src/NosAi.GuardAi.App/bin/Release/net8.0-android") / APK_NAME


class AdbError(RuntimeError):
    """An ADB step failed. `reason` is a stable identifier, not prose."""

    def __init__(self, reason: str, detail: str | None = None):
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason
        self.detail = detail


@dataclass(frozen=True)
class AdbDevice:
    serial: str
    state: str

    @property
    def is_ready(self) -> bool:
        return self.state == "device"


def resolve_adb(explicit: str | Path | None = None, isolated_root: str | Path | None = None) -> Path:
    """Locate an adb executable.

    The isolated copy on the dedicated volume wins when present: the project
    deliberately provisions from its own volume rather than whatever happens to be
    installed on the machine. The Android SDK and PATH are fallbacks so the flow is
    usable on a development box.
    """
    if explicit is not None:
        candidate = Path(explicit)
        if not candidate.is_file():
            raise AdbError("adb_not_found", str(candidate))
        return candidate

    candidates: list[Path] = []
    if isolated_root is not None:
        candidates.append(Path(isolated_root) / "tools" / "adb" / "adb.exe")

    sdk = os.getenv("ANDROID_HOME") or os.getenv("ANDROID_SDK_ROOT")
    if sdk:
        candidates.append(Path(sdk) / "platform-tools" / ("adb.exe" if os.name == "nt" else "adb"))

    for candidate in candidates:
        if candidate.is_file():
            return candidate

    on_path = shutil.which("adb")
    if on_path:
        return Path(on_path)

    raise AdbError(
        "adb_not_found",
        "provide an isolated adb, set ANDROID_HOME, or put adb on PATH",
    )


class Adb:
    """A thin, testable wrapper around the adb executable."""

    def __init__(self, adb_path: str | Path, timeout: float = 15.0):
        self._adb = Path(adb_path)
        self._timeout = timeout

    @property
    def path(self) -> Path:
        return self._adb

    def run(self, *args: str, check: bool = True, timeout: float | None = None) -> subprocess.CompletedProcess[str]:
        try:
            return subprocess.run(
                [str(self._adb), *args],
                check=check,
                capture_output=True,
                text=True,
                timeout=timeout or self._timeout,
            )
        except subprocess.TimeoutExpired as exc:
            raise AdbError("adb_timeout", " ".join(args)) from exc
        except subprocess.CalledProcessError as exc:
            raise AdbError("adb_failed", (exc.stderr or exc.stdout or "").strip() or " ".join(args)) from exc
        except OSError as exc:
            raise AdbError("adb_unusable", exc.strerror or str(exc)) from exc

    def devices(self) -> list[AdbDevice]:
        """Every attached device with its state.

        States other than ``device`` are returned rather than filtered out: an
        ``unauthorized`` phone is the most common real failure, and reporting it as
        "no device" sends the operator looking for a cable problem that isn't there.
        """
        output = self.run("devices").stdout
        found: list[AdbDevice] = []
        for line in output.splitlines()[1:]:
            parts = line.split()
            if len(parts) >= 2:
                found.append(AdbDevice(serial=parts[0], state=parts[1]))
        return found

    def ready_device(self) -> AdbDevice | None:
        return next((device for device in self.devices() if device.is_ready), None)

    def is_installed(self, serial: str, package: str = PACKAGE_NAME) -> bool:
        result = self.run("-s", serial, "shell", "pm", "list", "packages", package, check=False)
        return any(line.strip() == f"package:{package}" for line in result.stdout.splitlines())

    def install(self, serial: str, apk: str | Path, timeout: float = 180.0) -> None:
        apk_path = Path(apk)
        if not apk_path.is_file():
            raise AdbError("apk_not_found", str(apk_path))
        # -r reinstalls over an existing copy, -g grants manifest permissions so the
        # operator is not left tapping dialogs on a phone they may not be holding.
        result = self.run("-s", serial, "install", "-r", "-g", str(apk_path), check=False, timeout=timeout)
        combined = f"{result.stdout}\n{result.stderr}"
        if "Success" not in combined:
            raise AdbError("install_failed", combined.strip()[:400] or f"exit {result.returncode}")

    def reverse(self, serial: str, port: int = GUARD_PORT) -> None:
        """Make the phone's localhost:<port> reach the PC's listener on the same port.

        `reverse`, never `forward`: the runtime listens on the PC and the phone dials
        it. With `forward` the tunnel points the other way and the app cannot connect.
        """
        self.run("-s", serial, "reverse", f"tcp:{port}", f"tcp:{port}")

    def reverse_list(self, serial: str) -> str:
        return self.run("-s", serial, "reverse", "--list", check=False).stdout.strip()

    def remove_reverse(self, serial: str, port: int = GUARD_PORT) -> None:
        self.run("-s", serial, "reverse", "--remove", f"tcp:{port}", check=False)

    def launch(self, serial: str, package: str = PACKAGE_NAME) -> None:
        self.run("-s", serial, "shell", "monkey", "-p", package, "-c", "android.intent.category.LAUNCHER", "1", check=False)


@dataclass(frozen=True)
class DeploymentResult:
    serial: str
    installed: bool
    reversed_port: int
    apk: Path


def deploy(
    apk: str | Path,
    adb_path: str | Path | None = None,
    isolated_root: str | Path | None = None,
    port: int = GUARD_PORT,
    reinstall: bool = False,
    launch: bool = True,
) -> DeploymentResult:
    """Install the Guard AI app on an authorized device and open the reverse tunnel.

    Fails closed: no authorized device, no APK, or a failed install raises rather
    than reporting a partial success the operator would then have to debug on the
    phone.
    """
    adb = Adb(resolve_adb(adb_path, isolated_root))
    adb.run("start-server", check=False)

    device = adb.ready_device()
    if device is None:
        states = ", ".join(f"{d.serial}={d.state}" for d in adb.devices()) or "none attached"
        raise AdbError("no_authorized_device", states)

    installed = False
    if reinstall or not adb.is_installed(device.serial):
        adb.install(device.serial, apk)
        installed = True

    adb.reverse(device.serial, port)
    if launch:
        adb.launch(device.serial)

    return DeploymentResult(
        serial=device.serial,
        installed=installed,
        reversed_port=port,
        apk=Path(apk),
    )
