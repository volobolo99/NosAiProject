"""Enrollment of the phone's public key over ADB.

The parsing is what matters: a stale or truncated key enrolls silently and then
fails authentication with nothing on either side saying why.
"""
from __future__ import annotations

import pytest
from cryptography.hazmat.primitives import serialization

from nosai.phone.enroll import (
    BEGIN_MARKER,
    END_MARKER,
    LOG_TAG,
    EnrollmentError,
    extract_public_key,
)
from nosai.phone.guard_client import generate_client_key, public_key_pem


def _logcat_block(pem: bytes, prefix: str = f"08-30 19:00:00.000  1234  1234 I {LOG_TAG}: ") -> str:
    lines = [prefix + BEGIN_MARKER]
    lines += [prefix + line for line in pem.decode().splitlines()]
    lines.append(prefix + END_MARKER)
    return "\n".join(lines) + "\n"


def test_key_survives_the_logcat_round_trip():
    key = generate_client_key()
    pem = public_key_pem(key)

    extracted = extract_public_key(_logcat_block(pem))

    # The point is not string equality but that the PEM still loads and is the
    # same key: enrolling anything else means the handshake fails.
    reloaded = serialization.load_pem_public_key(extracted.encode())
    assert reloaded.public_numbers() == key.public_key().public_numbers()


def test_the_most_recent_key_wins():
    # A reinstall makes the app emit a new identity. Enrolling the earlier one
    # would be refused by the runtime with no visible cause.
    old = public_key_pem(generate_client_key())
    new_key = generate_client_key()
    new = public_key_pem(new_key)

    extracted = extract_public_key(_logcat_block(old) + _logcat_block(new))

    reloaded = serialization.load_pem_public_key(extracted.encode())
    assert reloaded.public_numbers() == new_key.public_key().public_numbers()


def test_an_unterminated_block_is_ignored():
    # A log buffer that wrapped mid-key must not yield a truncated PEM.
    key = generate_client_key()
    complete = _logcat_block(public_key_pem(key))
    truncated = f"I {LOG_TAG}: {BEGIN_MARKER}\nI {LOG_TAG}: -----BEGIN PUBLIC KEY-----\n"

    extracted = extract_public_key(complete + truncated)

    reloaded = serialization.load_pem_public_key(extracted.encode())
    assert reloaded.public_numbers() == key.public_key().public_numbers()


def test_no_key_in_the_log_is_a_structured_failure():
    with pytest.raises(EnrollmentError) as raised:
        extract_public_key("08-30 19:00:00.000  1234  1234 I SomethingElse: hello\n")
    assert raised.value.reason == "public_key_not_in_log"


def test_a_block_without_pem_delimiters_is_refused():
    noise = f"I {LOG_TAG}: {BEGIN_MARKER}\nI {LOG_TAG}: not-a-key\nI {LOG_TAG}: {END_MARKER}\n"
    with pytest.raises(EnrollmentError) as raised:
        extract_public_key(noise)
    assert raised.value.reason == "malformed_public_key"


def test_the_extracted_key_is_accepted_by_the_client_contract():
    # End to end on the PC side: what enrollment writes must be loadable as the
    # trusted key the runtime is started with.
    key = generate_client_key()
    extracted = extract_public_key(_logcat_block(public_key_pem(key)))
    assert extracted.startswith("-----BEGIN PUBLIC KEY-----")
    assert extracted.rstrip().endswith("-----END PUBLIC KEY-----")
    loaded = serialization.load_pem_public_key(extracted.encode())
    assert loaded.key_size == 2048
