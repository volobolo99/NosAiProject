from datetime import datetime, timedelta, timezone

import pytest

from nosai.mcp.evidence import EvidenceAuthority


def _record(authority, *, kind="tests", executor="worker.tests", observed_at=None):
    return authority.record(
        "sha256:" + "a" * 64,
        "test-v1",
        "sha256:" + "b" * 64,
        executor,
        "sha256:" + "c" * 64,
        "sha256:" + "d" * 64,
        {"status": "pass"},
        "signer." + kind,
        evidence_kind=kind,
        observed_at=observed_at,
    )


def test_signed_evidence_round_trip_and_tamper_detection(tmp_path):
    authority = EvidenceAuthority(tmp_path / "evidence")
    record = _record(authority)
    assert authority.verify(record)[0] is True
    tampered = dict(record)
    tampered["result"] = {"status": "fail"}
    assert authority.verify(tampered) == (False, "invalid evidence signature")


def test_stale_evidence_is_rejected(tmp_path):
    authority = EvidenceAuthority(tmp_path / "evidence")
    old = (datetime.now(timezone.utc) - timedelta(hours=2)).isoformat()
    record = authority.record(
        "sha256:" + "a" * 64,
        "test-v1",
        "sha256:" + "b" * 64,
        "worker.tests",
        "sha256:" + "c" * 64,
        "sha256:" + "d" * 64,
        {"status": "pass"},
        "signer.tests",
        evidence_kind="tests",
        observed_at=old,
        ttl_s=60,
    )
    assert authority.verify(record)[0] is False
    assert authority.verify(record)[1] == "stale evidence"


def test_candidate_digest_mismatch_is_rejected(tmp_path):
    authority = EvidenceAuthority(tmp_path / "evidence")
    record = _record(authority)
    assert authority.verify(record, expected_candidate_digest="sha256:" + "e" * 64) == (
        False,
        "candidate digest mismatch",
    )


def test_secret_like_result_fields_are_rejected(tmp_path):
    authority = EvidenceAuthority(tmp_path / "evidence")
    with pytest.raises(ValueError, match="secret-like"):
        authority.record(
            "sha256:" + "a" * 64,
            "test-v1",
            "sha256:" + "b" * 64,
            "worker.tests",
            "sha256:" + "c" * 64,
            "sha256:" + "d" * 64,
            {"status": "pass", "token": "never"},
            "signer.tests",
            evidence_kind="tests",
        )
    with pytest.raises(ValueError, match="secret-like"):
        authority.record(
            "sha256:" + "a" * 64,
            "test-v1",
            "sha256:" + "b" * 64,
            "worker.tests",
            "sha256:" + "c" * 64,
            "sha256:" + "d" * 64,
            {"status": "pass", "details": [{"credential": "never"}]},
            "signer.tests",
            evidence_kind="tests",
        )
    with pytest.raises(ValueError, match="secret-like"):
        authority.record(
            "sha256:" + "a" * 64,
            "test-v1",
            "sha256:" + "b" * 64,
            "worker.tests",
            "sha256:" + "c" * 64,
            "sha256:" + "d" * 64,
            {"status": "pass", "access_token": "never"},
            "signer.tests",
            evidence_kind="tests",
        )

