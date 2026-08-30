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
from pathlib import Path

from nosai.phone.adb import (
    BUILT_APK_RELATIVE,
    GUARD_PORT,
    PACKAGE_NAME,
    AdbError,
    deploy,
)

REPO_ROOT = Path(__file__).resolve().parent.parent.parent


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Deploy Guard AI to an attached Android device")
    parser.add_argument("--apk", default=None, help=f"APK to install (default: {BUILT_APK_RELATIVE})")
    parser.add_argument("--adb", default=None, help="Path to an adb executable")
    parser.add_argument("--isolated-root", default=None, help="Dedicated volume holding tools/adb/adb.exe")
    parser.add_argument("--port", type=int, default=GUARD_PORT, help=f"Guard channel port (default {GUARD_PORT})")
    parser.add_argument("--reinstall", action="store_true", help="Reinstall even if already present")
    parser.add_argument("--no-launch", action="store_true", help="Do not start the app after deploying")
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
    print()
    print("In the app, connect to 127.0.0.1 on that port.")
    print("Enroll the device public key on the PC first: the runtime refuses untrusted keys.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
