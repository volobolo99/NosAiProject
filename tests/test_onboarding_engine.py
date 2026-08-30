import pytest

from nosai.network.wire_protocol import HEADER, TYPE_SESSION_HELLO, decode
from nosai.phone.adb import GUARD_PORT
from nosai.phone.onboarding_engine import PORT, NosAiOnboardingEngine, NosAiOnboardingError


def test_session_hello_is_the_canonical_empty_frame():
    # This test previously asserted the frame carried a payload, pinning a
    # SESSION_HELLO that embedded a client-supplied "challenge". That inverted the
    # authentication model of ADR-0006: the runtime generates the 32-byte
    # single-use nonce and the phone signs it, so a phone choosing its own
    # challenge could present a precomputed signature. The canonical hello is
    # empty and the runtime answers with CAPABILITIES and its own AUTH_CHALLENGE.
    frame = NosAiOnboardingEngine.build_session_hello()

    assert len(frame) == HEADER.size
    decoded = decode(frame)
    assert decoded.message_type == TYPE_SESSION_HELLO
    assert decoded.sequence == 1
    assert decoded.payload == b""


def test_onboarding_uses_the_canonical_guard_port():
    # It used to be 6100, which matched nothing on either side of the link.
    assert PORT == GUARD_PORT


def test_missing_adb_fails_closed(tmp_path):
    engine = NosAiOnboardingEngine(tmp_path)
    with pytest.raises(NosAiOnboardingError):
        engine.provision()


def test_missing_apk_fails_closed_when_a_device_is_ready(tmp_path, monkeypatch):
    # A setup error must be visible. Reporting "no device" for a missing APK would
    # send the operator to check the cable.
    adb_binary = tmp_path / "tools" / "adb" / "adb.exe"
    adb_binary.parent.mkdir(parents=True)
    adb_binary.write_bytes(b"")

    import subprocess

    def fake_run(argv, **kwargs):
        args = [str(a) for a in argv[1:]]
        stdout = "List of devices attached\nR58M12345\tdevice\n" if args[:1] == ["devices"] else ""
        return subprocess.CompletedProcess(argv, 0, stdout=stdout, stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)

    engine = NosAiOnboardingEngine(tmp_path)
    with pytest.raises(NosAiOnboardingError, match="APK"):
        engine.provision()


def test_no_device_returns_false_rather_than_raising(tmp_path, monkeypatch):
    # An unplugged phone is an ordinary state of the world, not a setup error.
    adb_binary = tmp_path / "tools" / "adb" / "adb.exe"
    adb_binary.parent.mkdir(parents=True)
    adb_binary.write_bytes(b"")

    import subprocess

    def fake_run(argv, **kwargs):
        args = [str(a) for a in argv[1:]]
        stdout = "List of devices attached\n" if args[:1] == ["devices"] else ""
        return subprocess.CompletedProcess(argv, 0, stdout=stdout, stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)

    assert NosAiOnboardingEngine(tmp_path).provision() is False
