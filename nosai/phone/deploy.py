"""Install Guard AI on an attached phone and open the canonical tunnel.

    python -m nosai.phone.deploy

Run the PC runtime first, with this device's public key enrolled:

    dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll \
        --guard-public-key-path <device.pem>

The app then connects to 127.0.0.1:17471 on the phone, which `adb reverse` carries
to the runtime on the PC. No Wi-Fi setup and no LAN address are needed over USB.
"""
from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

from nosai.phone.adb import (
    BUILT_APK_RELATIVE,
    GUARD_PORT,
    PACKAGE_NAME,
    Adb,
    AdbError,
    Gate1Defaults,
    deploy,
    resolve_adb,
)
from nosai.phone.enroll import EnrollmentError, collect, ensure_runtime_public_pem

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

#: The app publishes its key at startup; the first log line can lag the launch.
ENROLL_ATTEMPTS = 5
ENROLL_RETRY_DELAY_S = 1.5


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Deploy Guard AI to an attached Android device")
    parser.add_argument("--apk", default=None, help=f"APK to install (default: {BUILT_APK_RELATIVE})")
    parser.add_argument("--adb", default=None, help="Path to an adb executable")
    parser.add_argument("--isolated-root", default=None, help="Dedicated volume holding tools/adb/adb.exe")
    parser.add_argument("--port", type=int, default=GUARD_PORT, help=f"Guard channel port (default {GUARD_PORT})")
    parser.add_argument("--reinstall", action="store_true", help="Reinstall even if already present")
    parser.add_argument("--no-launch", action="store_true", help="Do not start the app after deploying")
    parser.add_argument("--no-enroll", action="store_true", help="Skip collecting the device key")
    parser.add_argument(
        "--key-out",
        default=Gate1Defaults.TRUSTED_KEY_PATH,
        help=f"Where to write the device public key (default: {Gate1Defaults.TRUSTED_KEY_PATH})",
    )
    args = parser.parse_args(argv)

    apk = Path(args.apk) if args.apk else REPO_ROOT / BUILT_APK_RELATIVE
    if not apk.is_file():
        print(f"APK not found: {apk}", file=sys.stderr)
        print(
            "Build it with:\n"
            "  dotnet build src/NosAi.GuardAi.App/NosAi.GuardAi.App.csproj -c Release -f net8.0-android",
            file=sys.stderr,
        )
        return 2

    try:
        result = deploy(
            apk=apk,
            adb_path=args.adb,
            isolated_root=args.isolated_root,
            port=args.port,
            reinstall=args.reinstall,
            launch=not args.no_launch,
        )
    except AdbError as exc:
        print(f"Deployment failed: {exc.reason}", file=sys.stderr)
        if exc.detail:
            print(f"  {exc.detail}", file=sys.stderr)
        if exc.reason == "no_authorized_device":
            print(
                "  Attach the phone over USB, enable developer options and USB debugging,\n"
                "  then accept the debugging prompt on the device.",
                file=sys.stderr,
            )
        return 1

    print(f"Device:   {result.serial}")
    print(f"Package:  {PACKAGE_NAME} ({'installed' if result.installed else 'already present'})")
    print(f"Tunnel:   phone 127.0.0.1:{result.reversed_port} -> PC {result.reversed_port} (adb reverse)")

    if args.no_enroll:
        print("Pairing: skipped (--no-enroll)")
        return 0

    # Pairing is part of deploying, not a separate chore for the operator. The app
    # publishes its public key at startup, so it has to have been launched first.
    if args.no_launch:
        print("Pairing: skipped (the app must be running to publish its key)")
        return 0

    key_path = Path(args.key_out)
    for attempt in range(ENROLL_ATTEMPTS):
        try:
            pem = collect(adb_path=args.adb, isolated_root=args.isolated_root)
            break
        except EnrollmentError as exc:
            # The app was launched a moment ago; its first log line may not have
            # landed yet. Only the "not there yet" case is worth retrying.
            if exc.reason == "public_key_not_in_log" and attempt < ENROLL_ATTEMPTS - 1:
                time.sleep(ENROLL_RETRY_DELAY_S)
                continue
            print(f"Pairing failed: {exc.reason}", file=sys.stderr)
            if exc.detail:
                print(f"  {exc.detail}", file=sys.stderr)
            return 1

    key_path.parent.mkdir(parents=True, exist_ok=True)
    key_path.write_text(pem, encoding="utf-8")
    print(f"Pairing:  device key written to {key_path}")

    try:
        runtime_pem = ensure_runtime_public_pem(REPO_ROOT)
        Adb(resolve_adb(args.adb, args.isolated_root)).push_runtime_pin(result.serial, runtime_pem)
    except (EnrollmentError, AdbError) as exc:
        reason = getattr(exc, "reason", "failed")
        print(f"Pairing failed: {reason}", file=sys.stderr)
        if getattr(exc, "detail", None):
            print(f"  {exc.detail}", file=sys.stderr)
        if reason == "runtime_identity_missing":
            print(
                "  Start the runtime once so it writes data/runtime_identity.pem, then re-run deploy.",
                file=sys.stderr,
            )
        return 1

    print(f"Pairing:  runtime pin pushed ({runtime_pem})")
    print()
    print("Start the runtime; it picks that key up on its own:")
    print("  dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll")
    print()
    print("Then press Connetti in the app. USB works over this tunnel; for Wi-Fi,")
    print("put the phone on the same network and choose Wi-Fi - the PC is found by discovery.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
