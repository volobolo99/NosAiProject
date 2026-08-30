"""PC-side Guard AI onboarding over an isolated ADB installation."""
from __future__ import annotations

from pathlib import Path

from nosai.network.wire_protocol import Frame, TYPE_SESSION_HELLO
from nosai.phone.adb import APK_NAME, GUARD_PORT, PACKAGE_NAME, Adb, AdbError, resolve_adb

#: Kept for callers that imported it. The canonical Guard channel port, not the
#: 6100 this module used to invent, which matched nothing on either side.
PORT = GUARD_PORT


class NosAiOnboardingError(RuntimeError):
    pass


class NosAiOnboardingEngine:
    """Provisions Guard AI from the dedicated volume, never from the network."""

    def __init__(self, root_path: str | Path):
        self.root_path = Path(root_path)
        self.adb_path = self.root_path / "tools" / "adb" / "adb.exe"
        self.apk_path = self.root_path / "runtime" / APK_NAME

    def _adb(self) -> Adb:
        try:
            return Adb(resolve_adb(explicit=self.adb_path))
        except AdbError as exc:
            raise NosAiOnboardingError(f"ADB isolato assente: {self.adb_path}") from exc

    def provision(self) -> bool:
        """Provision only an authorized device; never download external components.

        Returns False when no authorized device is attached, and raises when the
        environment is unusable — a missing ADB or APK is a setup error the operator
        must see, not a device that happens not to be plugged in.
        """
        adb = self._adb()
        adb.run("start-server", check=False)

        device = adb.ready_device()
        if device is None:
            return False

        if not self.apk_path.is_file():
            raise NosAiOnboardingError(f"APK Guard AI assente: {self.apk_path}")

        try:
            if not adb.is_installed(device.serial):
                adb.install(device.serial, self.apk_path)
            # reverse, not forward: the runtime listens on the PC and the phone dials
            # it, so the tunnel has to carry the phone's localhost to this machine.
            adb.reverse(device.serial, PORT)
            adb.launch(device.serial, PACKAGE_NAME)
        except AdbError as exc:
            raise NosAiOnboardingError(f"provisioning fallito: {exc}") from exc

        return True

    @staticmethod
    def build_session_hello() -> bytes:
        """The canonical first frame: SESSION_HELLO, sequence 1, empty payload.

        It used to carry a JSON body with a client-supplied ``challenge``. That
        inverted the authentication model of ADR-0006, in which the **runtime**
        generates the 32-byte single-use nonce and the phone signs it. A phone that
        chose its own challenge could present a precomputed signature, so the field
        was not merely redundant. The runtime ignores this payload and answers with
        CAPABILITIES and its own AUTH_CHALLENGE.
        """
        return Frame(TYPE_SESSION_HELLO, 1, b"").encode()
