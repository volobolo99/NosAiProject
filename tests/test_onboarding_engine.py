from nosai.phone.onboarding_engine import NosAiOnboardingEngine
from nosai.network.wire_protocol import HEADER


def test_session_hello_uses_12_byte_frame():
    frame = NosAiOnboardingEngine.build_session_hello("ab" * 32)
    assert len(frame) > HEADER.size
    assert len(frame[:HEADER.size]) == 12


def test_missing_adb_fails_closed(tmp_path):
    engine = NosAiOnboardingEngine(tmp_path)
    try:
        engine.provision()
    except RuntimeError:
        pass
    else:
        raise AssertionError("missing isolated ADB must block provisioning")
