"""Collect the phone's Guard AI public key over ADB and write it for the runtime.

    python -m nosai.phone.enroll --out data/guard_public_key.pem

The app writes its public key to the device log at startup. Only the public half
is published: it is the part the PC is meant to hold, and it grants nothing on its
own — the runtime can verify a signature with it but never produce one.

Then start the runtime with the collected key:

    dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll \
        --guard-public-key-path data/guard_public_key.pem
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

from nosai.phone.adb import PACKAGE_NAME, Adb, AdbError, Gate1Defaults, resolve_adb

LOG_TAG = "NosAiGuardKey"
BEGIN_MARKER = "BEGIN_NOSAI_GUARD_PUBLIC_KEY"
END_MARKER = "END_NOSAI_GUARD_PUBLIC_KEY"

PEM_HEADER = "-----BEGIN PUBLIC KEY-----"
PEM_FOOTER = "-----END PUBLIC KEY-----"


class EnrollmentError(RuntimeError):
    def __init__(self, reason: str, detail: str | None = None):
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason
        self.detail = detail


def extract_public_key(logcat: str) -> str:
    """Reassemble the PEM from tagged log lines.

    The most recent block wins: relaunching the app after a reinstall emits a new
    key, and enrolling a stale one would fail authentication with no visible
    reason on either side.
    """
    blocks: list[list[str]] = []
    current: list[str] | None = None

    for raw in logcat.splitlines():
        line = raw.strip()
        if not line:
            continue
        # logcat prefixes each line; keep only what follows the tag.
        if LOG_TAG in line:
            line = line.split(LOG_TAG, 1)[1].lstrip(" :")
        if line == BEGIN_MARKER:
            current = []
            continue
        if line == END_MARKER:
            if current:
                blocks.append(current)
            current = None
            continue
        if current is not None:
            current.append(line)

    if not blocks:
        raise EnrollmentError(
            "public_key_not_in_log",
            "start the Guard AI app on the phone, then run this again",
        )

    body = [line for line in blocks[-1] if line]
    pem = "\n".join(body) + "\n"
    if PEM_HEADER not in pem or PEM_FOOTER not in pem:
        raise EnrollmentError("malformed_public_key", pem[:120])
    return pem


def collect(adb_path: str | Path | None = None, isolated_root: str | Path | None = None) -> str:
    adb = Adb(resolve_adb(adb_path, isolated_root))
    device = adb.ready_device()
    if device is None:
        states = ", ".join(f"{d.serial}={d.state}" for d in adb.devices()) or "none attached"
        raise EnrollmentError("no_authorized_device", states)

    # -d dumps and exits rather than streaming. The app logs the key at startup, so
    # the buffer holds it as long as the app was launched recently.
    result = adb.run("-s", device.serial, "logcat", "-d", "-s", LOG_TAG, check=False, timeout=30.0)
    return extract_public_key(result.stdout)


def ensure_runtime_public_pem(repo_root: str | Path | None = None) -> Path:
    """Return the runtime public key, exporting it from the private identity if needed.

    Pairing must pin this on the phone. The runtime writes it on first start; if
    only the private file exists (an identity created before the companion was
    added), the public half is derived here rather than requiring a restart.
    The private file is never copied to the phone.
    """
    root = Path(repo_root) if repo_root is not None else Path.cwd()
    public = root / Gate1Defaults.RUNTIME_PUBLIC_KEY_PATH
    private = root / Gate1Defaults.RUNTIME_IDENTITY_PATH
    if public.is_file():
        text = public.read_text(encoding="utf-8")
        if "BEGIN PUBLIC KEY" in text:
            return public

    if not private.is_file():
        raise EnrollmentError(
            "runtime_identity_missing",
            "start the runtime once so it writes data/runtime_identity.pem, then pair again",
        )

    try:
        key = serialization.load_pem_private_key(private.read_bytes(), password=None)
    except ValueError as exc:
        raise EnrollmentError("runtime_identity_unreadable", str(private)) from exc
    if not isinstance(key, rsa.RSAPrivateKey) or key.key_size != 2048:
        raise EnrollmentError("runtime_identity_unreadable", "not RSA-2048")

    pem = key.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    public.parent.mkdir(parents=True, exist_ok=True)
    public.write_bytes(pem)
    return public


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Enroll the phone's Guard AI public key on this PC")
    parser.add_argument("--out", default="data/guard_public_key.pem", help="Where to write the PEM")
    parser.add_argument("--adb", default=None, help="Path to an adb executable")
    parser.add_argument("--isolated-root", default=None, help="Dedicated volume holding tools/adb/adb.exe")
    args = parser.parse_args(argv)

    try:
        pem = collect(adb_path=args.adb, isolated_root=args.isolated_root)
    except (EnrollmentError, AdbError) as exc:
        reason = getattr(exc, "reason", "failed")
        print(f"Enrollment failed: {reason}", file=sys.stderr)
        if getattr(exc, "detail", None):
            print(f"  {exc.detail}", file=sys.stderr)
        if reason == "public_key_not_in_log":
            print(f"  The app must have been started at least once: {PACKAGE_NAME}", file=sys.stderr)
        return 1

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(pem, encoding="utf-8")

    print(f"Public key written to {out}")

    try:
        runtime_pem = ensure_runtime_public_pem()
        adb = Adb(resolve_adb(args.adb, args.isolated_root))
        device = adb.ready_device()
        if device is None:
            raise EnrollmentError("no_authorized_device", "none attached")
        adb.push_runtime_pin(device.serial, runtime_pem)
    except (EnrollmentError, AdbError) as exc:
        reason = getattr(exc, "reason", "failed")
        print(f"Runtime pin failed: {reason}", file=sys.stderr)
        if getattr(exc, "detail", None):
            print(f"  {exc.detail}", file=sys.stderr)
        if reason == "runtime_identity_missing":
            print(
                "  Start the runtime once so it writes data/runtime_identity.pem, then run this again.",
                file=sys.stderr,
            )
        return 1

    print(f"Runtime pin pushed ({runtime_pem})")
    print()
    print("Start the runtime trusting it:")
    print(f"  dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --guard-public-key-path {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
