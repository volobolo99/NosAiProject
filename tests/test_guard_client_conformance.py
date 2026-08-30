"""Conformance of the reference Guard AI client against the canonical channel.

ADR-0006 makes GuardAiNetworkChannel the only canonical PC-phone channel and
requires the phone application to implement it exactly. Before these tests the
contract had only ever been exercised by the runtime's own suite, which builds
frames inline in the same codebase and therefore could only agree with itself.

The integration tests here drive the real runtime process over a real socket, so
they fail if either side drifts. They skip when the runtime has not been built,
because a missing build is not a protocol failure.
"""
from __future__ import annotations

import socket
import subprocess
import threading
from pathlib import Path

import pytest

from nosai.network.wire_protocol import HEADER, MAX_PAYLOAD, Frame, decode
from nosai.phone.guard_client import (
    GuardAiClient,
    GuardProtocolError,
    generate_client_key,
    public_key_pem,
)

REPO_ROOT = Path(__file__).resolve().parent.parent
RUNTIME_DLL = REPO_ROOT / "src" / "NosAi.Runtime" / "bin" / "Release" / "net8.0-windows" / "NosAi.Runtime.dll"
STARTUP_TIMEOUT_S = 30.0


# --------------------------------------------------------------------------
# Wire format — no runtime needed
# --------------------------------------------------------------------------

def test_frame_bytes_match_the_csharp_header_layout():
    # Golden bytes for WireHeader.WriteTo: MAGIC "NOSA", VERSION 1, TYPE, then
    # PAYLOAD_LEN as uint16 big-endian and SEQ as uint32 big-endian. A silent
    # endianness or field-order change on either side breaks the phone client,
    # so the layout is pinned to literal bytes rather than to a round-trip.
    frame = Frame(message_type=0x11, sequence=0x01020304, payload=b"hi")
    assert frame.encode() == b"NOSA" + b"\x01" + b"\x11" + b"\x00\x02" + b"\x01\x02\x03\x04" + b"hi"


def test_header_is_twelve_bytes():
    assert HEADER.size == 12


def test_max_payload_matches_the_uint16_length_field():
    # Regression: MAX_PAYLOAD was 64 * 1024, one byte more than a uint16 can
    # express, so a 65536-byte payload passed the guard and then died inside
    # struct.pack with an opaque struct.error.
    assert MAX_PAYLOAD == 0xFFFF
    assert len(Frame(0x11, 1, b"x" * MAX_PAYLOAD).encode()) == HEADER.size + MAX_PAYLOAD
    with pytest.raises(ValueError):
        Frame(0x11, 1, b"x" * (MAX_PAYLOAD + 1)).encode()


def test_decode_rejects_a_foreign_magic():
    good = Frame(0x06, 1, b"").encode()
    with pytest.raises(ValueError):
        decode(b"XXXX" + good[4:])


# --------------------------------------------------------------------------
# Integration — real runtime process, real socket
# --------------------------------------------------------------------------

def _free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def _pump(stream, sink: list[str], marker: threading.Event) -> None:
    for line in stream:
        sink.append(line)
        if "runtime is listening" in line:
            marker.set()
    marker.set()  # the process ended; unblock the waiter so it can report why


def _wait_until_listening(process: subprocess.Popen, timeout: float) -> list[str]:
    """Wait for the runtime's own "listening" log line.

    Deliberately NOT a TCP connect probe. GuardAiNetworkChannel serves a single
    phone at a time, so a probe connection is accepted as *the* session and the
    real client that follows is aborted — which made this suite pass alone and
    fail when the full run shifted the timing.
    """
    lines: list[str] = []
    listening = threading.Event()
    pump = threading.Thread(target=_pump, args=(process.stdout, lines, listening), daemon=True)
    pump.start()

    if not listening.wait(timeout):
        raise AssertionError(f"runtime did not report listening within {timeout}s:\n{''.join(lines)}")
    if process.poll() is not None:
        raise AssertionError(f"runtime exited with code {process.returncode}:\n{''.join(lines)}")
    return lines


@pytest.fixture(scope="module")
def trusted_key():
    return generate_client_key()


@pytest.fixture(scope="module")
def guard_runtime(trusted_key, tmp_path_factory):
    """The real runtime, trusting exactly one public key, on a private port."""
    if not RUNTIME_DLL.is_file():
        pytest.skip(f"runtime not built: {RUNTIME_DLL}")

    pem_path = tmp_path_factory.mktemp("guard") / "guard_public.pem"
    pem_path.write_bytes(public_key_pem(trusted_key))
    port = _free_port()

    process = subprocess.Popen(
        [
            "dotnet", str(RUNTIME_DLL),
            "--guard-port", str(port),
            "--no-dashboard",
            "--guard-public-key-path", str(pem_path),
        ],
        cwd=str(REPO_ROOT),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    try:
        _wait_until_listening(process, STARTUP_TIMEOUT_S)
        yield port
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)


def test_reference_client_completes_the_canonical_handshake(guard_runtime, trusted_key):
    with GuardAiClient("127.0.0.1", guard_runtime, trusted_key) as client:
        session = client.open_session()

        assert session.authenticated
        assert "auth=rsa2048-sha256" in session.capabilities
        assert "heartbeat=2000" in session.capabilities
        # Gate 1 forbids execution, and the phone must be able to read that from
        # the capabilities rather than assuming it.
        assert "execution=disabled" in session.capabilities

        snapshot = session.telemetry
        assert snapshot["contractVersion"] == "gate1.snapshot.v1"
        # The channel must classify what it sends: an authenticated phone still
        # gets UNKNOWN gameplay, never a fabricated value.
        gameplay = snapshot["client"]["gameplayBaseline"]
        assert gameplay["source"] == "UNKNOWN"
        assert gameplay["value"] is None


def test_heartbeat_is_acknowledged_and_returns_fresh_telemetry(guard_runtime, trusted_key):
    with GuardAiClient("127.0.0.1", guard_runtime, trusted_key) as client:
        client.open_session()
        snapshot = client.heartbeat()
        assert snapshot["contractVersion"] == "gate1.snapshot.v1"
        # A second heartbeat proves the sequence guards stay aligned across
        # several exchanges, not just the first one.
        assert client.heartbeat()["contractVersion"] == "gate1.snapshot.v1"


def test_an_untrusted_key_is_refused(guard_runtime):
    # Negative test on the security boundary: a well-formed RSA-2048 signature
    # from a key the runtime does not trust must fail closed.
    intruder = generate_client_key()
    with GuardAiClient("127.0.0.1", guard_runtime, intruder) as client:
        with pytest.raises(GuardProtocolError) as raised:
            client.open_session()
    assert raised.value.reason == "authentication_refused"


def test_a_trusted_key_still_works_after_a_refused_session(guard_runtime, trusted_key):
    # A rejected intruder must not poison the channel for the legitimate phone:
    # the challenge is single-use, so the next session needs a fresh one.
    with GuardAiClient("127.0.0.1", guard_runtime, trusted_key) as client:
        assert client.open_session().authenticated
