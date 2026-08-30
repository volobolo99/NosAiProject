"""ADB deployment of the Guard AI application.

No device is needed: the adb executable is replaced by a recorder, so the tests
pin the commands that get issued. That is where the bugs were — the flow looked
plausible and issued the wrong ones.
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path

import pytest

from nosai.phone import adb as adb_module
from nosai.phone.adb import (
    GUARD_PORT,
    PACKAGE_NAME,
    Adb,
    AdbError,
    deploy,
    resolve_adb,
)

REPO_ROOT = Path(__file__).resolve().parent.parent
APP_CSPROJ = REPO_ROOT / "src" / "NosAi.GuardAi.App" / "NosAi.GuardAi.App.csproj"
GATE1_OPTIONS = REPO_ROOT / "src" / "NosAi.Runtime" / "Configuration" / "Gate1HostOptions.cs"


# --------------------------------------------------------------------------
# The constants must track the authoritative C# sources, not a copy of them
# --------------------------------------------------------------------------

def test_guard_port_matches_the_runtime_default():
    # Drift is silent: the tunnel would open on a port the runtime is not serving
    # and the phone would simply never connect.
    source = GATE1_OPTIONS.read_text(encoding="utf-8")
    match = re.search(r"DefaultGuardPort\s*=\s*(\d+)", source)
    assert match, "Gate1HostOptions.DefaultGuardPort not found"
    assert GUARD_PORT == int(match.group(1))


def test_package_name_matches_the_application_id():
    source = APP_CSPROJ.read_text(encoding="utf-8")
    match = re.search(r"<ApplicationId>([^<]+)</ApplicationId>", source)
    assert match, "<ApplicationId> not found"
    assert PACKAGE_NAME == match.group(1).strip()


# --------------------------------------------------------------------------
# Command construction
# --------------------------------------------------------------------------

class RecordingAdb:
    """Stands in for the adb executable and records every invocation."""

    def __init__(self, responses: dict[str, str] | None = None):
        self.calls: list[list[str]] = []
        self.responses = responses or {}

    def __call__(self, argv, **kwargs):
        args = [str(a) for a in argv[1:]]
        self.calls.append(args)
        key = " ".join(args)
        stdout = ""
        for pattern, response in self.responses.items():
            if key.startswith(pattern):
                stdout = response
                break
        return subprocess.CompletedProcess(argv, 0, stdout=stdout, stderr="")

    def issued(self, *fragment: str) -> bool:
        return any(list(fragment) == call[: len(fragment)] or fragment[0] in call for call in self.calls)

    def flat(self) -> str:
        return " | ".join(" ".join(call) for call in self.calls)


@pytest.fixture
def fake_apk(tmp_path) -> Path:
    apk = tmp_path / f"{PACKAGE_NAME}-Signed.apk"
    apk.write_bytes(b"not a real apk")
    return apk


@pytest.fixture
def fake_adb_binary(tmp_path) -> Path:
    binary = tmp_path / "adb.exe"
    binary.write_bytes(b"")
    return binary


def _install_recorder(monkeypatch, responses: dict[str, str]) -> RecordingAdb:
    recorder = RecordingAdb(responses)
    monkeypatch.setattr(subprocess, "run", recorder)
    return recorder


def test_deploy_opens_a_reverse_tunnel_never_a_forward(monkeypatch, fake_apk, fake_adb_binary):
    # The bug this pins: `adb forward` makes the PC reach the phone, but the
    # runtime is the listener and the phone dials it. The tunnel must be reverse
    # or the app can never connect, however long anyone waits.
    recorder = _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": f"package:{PACKAGE_NAME}\n",
    })

    result = deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert result.serial == "R58M12345"
    assert result.reversed_port == GUARD_PORT
    assert ["-s", "R58M12345", "reverse", f"tcp:{GUARD_PORT}", f"tcp:{GUARD_PORT}"] in recorder.calls
    assert "forward" not in recorder.flat()


def test_deploy_skips_install_when_the_package_is_already_present(monkeypatch, fake_apk, fake_adb_binary):
    recorder = _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": f"package:{PACKAGE_NAME}\n",
    })

    result = deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert result.installed is False
    assert "install" not in recorder.flat()


def test_deploy_installs_when_the_package_is_absent(monkeypatch, fake_apk, fake_adb_binary):
    recorder = _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": "",
        "-s R58M12345 install": "Success\n",
    })

    result = deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert result.installed is True
    assert any(call[:4] == ["-s", "R58M12345", "install", "-r"] for call in recorder.calls)


def test_a_similarly_named_package_does_not_count_as_installed(monkeypatch, fake_apk, fake_adb_binary):
    # `pm list packages com.nosai.guardai` also matches com.nosai.guardai.debug;
    # a substring check would skip the install and leave the phone without the app.
    recorder = _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": f"package:{PACKAGE_NAME}.debug\n",
        "-s R58M12345 install": "Success\n",
    })

    result = deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert result.installed is True


def test_push_runtime_pin_never_uses_run_as(monkeypatch, tmp_path, fake_adb_binary):
    """The pin must reach a release build, which `run-as` cannot.

    This check previously asserted the opposite: it required `run-as`, so it
    passed against a recorder and then failed on the first real handset with
    "run-as: package not debuggable". The release APK is not debuggable and never
    will be, so the pin goes to the app's external files directory instead and the
    app adopts it from there.
    """
    pem = tmp_path / "runtime_public.pem"
    pem.write_text("-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----\n", encoding="utf-8")
    inbox = f"/sdcard/Android/data/{PACKAGE_NAME}/files"
    recorder = _install_recorder(monkeypatch, {
        "-s R58M12345 push": "1 file pushed, 0 skipped.",
        "-s R58M12345 shell head": "-----BEGIN PUBLIC KEY-----",
    })

    Adb(fake_adb_binary).push_runtime_pin("R58M12345", pem)

    joined = " | ".join(" ".join(call) for call in recorder.calls)
    assert "run-as" not in joined, "run-as does not work on a release build"
    assert ["-s", "R58M12345", "push", str(pem), f"{inbox}/runtime_public.pem"] in recorder.calls
    # push will not create the directory, and it does not exist until the app has
    # used external storage.
    assert ["-s", "R58M12345", "shell", "mkdir", "-p", inbox] in recorder.calls


def test_push_runtime_pin_fails_closed_when_the_copy_did_not_land(monkeypatch, tmp_path, fake_adb_binary):
    # A pin that silently failed to arrive leaves the phone unable to verify the
    # runtime, and the failure would surface much later as "runtime not
    # recognised" — pointing the operator at the wrong problem.
    pem = tmp_path / "runtime_public.pem"
    pem.write_text("-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----\n", encoding="utf-8")
    _install_recorder(monkeypatch, {"-s R58M12345 push": "adb: error: failed to copy"})

    with pytest.raises(AdbError) as raised:
        Adb(fake_adb_binary).push_runtime_pin("R58M12345", pem)

    assert raised.value.reason == "runtime_pin_push_failed"


def test_push_runtime_pin_verifies_what_landed(monkeypatch, tmp_path, fake_adb_binary):
    # The push can report success while the file is empty or truncated, so the
    # pin is read back before pairing claims to have worked.
    pem = tmp_path / "runtime_public.pem"
    pem.write_text("-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----\n", encoding="utf-8")
    _install_recorder(monkeypatch, {
        "-s R58M12345 push": "1 file pushed, 0 skipped.",
        "-s R58M12345 shell head": "",
    })

    with pytest.raises(AdbError) as raised:
        Adb(fake_adb_binary).push_runtime_pin("R58M12345", pem)

    assert raised.value.reason == "runtime_pin_unreadable"


def test_unauthorized_device_is_reported_as_such(monkeypatch, fake_apk, fake_adb_binary):
    # The most common real failure. Reporting it as "no device" sends the operator
    # hunting for a cable problem instead of tapping the prompt on the phone.
    _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tunauthorized\n",
    })

    with pytest.raises(AdbError) as raised:
        deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert raised.value.reason == "no_authorized_device"
    assert "unauthorized" in (raised.value.detail or "")


def test_a_failed_install_fails_closed(monkeypatch, fake_apk, fake_adb_binary):
    # No tunnel must be opened for an app that is not there: a reverse plus a
    # missing app looks like a connectivity fault rather than a failed install.
    recorder = _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": "",
        "-s R58M12345 install": "Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]\n",
    })

    with pytest.raises(AdbError) as raised:
        deploy(apk=fake_apk, adb_path=fake_adb_binary)

    assert raised.value.reason == "install_failed"
    assert "reverse" not in recorder.flat()


def test_a_missing_apk_is_refused_before_any_device_work(monkeypatch, tmp_path, fake_adb_binary):
    _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nR58M12345\tdevice\n",
        "-s R58M12345 shell pm list packages": "",
    })

    with pytest.raises(AdbError) as raised:
        deploy(apk=tmp_path / "absent.apk", adb_path=fake_adb_binary)

    assert raised.value.reason == "apk_not_found"


# --------------------------------------------------------------------------
# Locating adb
# --------------------------------------------------------------------------

def test_an_explicit_adb_that_does_not_exist_is_an_error(tmp_path):
    with pytest.raises(AdbError) as raised:
        resolve_adb(explicit=tmp_path / "nope.exe")
    assert raised.value.reason == "adb_not_found"


def test_the_isolated_volume_wins_over_the_sdk(tmp_path, monkeypatch):
    # The project provisions from its own volume rather than whatever happens to be
    # installed on the machine, so that copy must take precedence.
    isolated = tmp_path / "volume"
    (isolated / "tools" / "adb").mkdir(parents=True)
    isolated_adb = isolated / "tools" / "adb" / "adb.exe"
    isolated_adb.write_bytes(b"")

    sdk = tmp_path / "sdk"
    (sdk / "platform-tools").mkdir(parents=True)
    (sdk / "platform-tools" / "adb.exe").write_bytes(b"")
    (sdk / "platform-tools" / "adb").write_bytes(b"")
    monkeypatch.setenv("ANDROID_HOME", str(sdk))

    assert resolve_adb(isolated_root=isolated) == isolated_adb


def test_the_sdk_is_used_when_there_is_no_isolated_copy(tmp_path, monkeypatch):
    sdk = tmp_path / "sdk"
    (sdk / "platform-tools").mkdir(parents=True)
    name = "adb.exe" if __import__("os").name == "nt" else "adb"
    expected = sdk / "platform-tools" / name
    expected.write_bytes(b"")
    monkeypatch.setenv("ANDROID_HOME", str(sdk))

    assert resolve_adb(isolated_root=tmp_path / "absent") == expected


def test_no_adb_anywhere_is_a_structured_failure(tmp_path, monkeypatch):
    monkeypatch.delenv("ANDROID_HOME", raising=False)
    monkeypatch.delenv("ANDROID_SDK_ROOT", raising=False)
    monkeypatch.setattr(adb_module.shutil, "which", lambda _: None)

    with pytest.raises(AdbError) as raised:
        resolve_adb(isolated_root=tmp_path / "absent")
    assert raised.value.reason == "adb_not_found"


def test_devices_reports_states_rather_than_filtering_them(monkeypatch, fake_adb_binary):
    _install_recorder(monkeypatch, {
        "devices": "List of devices attached\nAAA\tdevice\nBBB\tunauthorized\nCCC\toffline\n",
    })
    states = {d.serial: d.state for d in Adb(fake_adb_binary).devices()}
    assert states == {"AAA": "device", "BBB": "unauthorized", "CCC": "offline"}
