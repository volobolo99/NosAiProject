"""The Python tooling and the C# runtime must agree on where things live.

Every value here exists in two places by necessity — one side is C#, the other
Python — so each is pinned to the C# source it mirrors. Drift in any of them is
silent at build time and only shows up as a phone that never connects.
"""
from __future__ import annotations

import re
from pathlib import Path

from nosai.phone.adb import GUARD_PORT, PACKAGE_NAME, Gate1Defaults

REPO_ROOT = Path(__file__).resolve().parent.parent
GATE1_OPTIONS = REPO_ROOT / "src" / "NosAi.Runtime" / "Configuration" / "Gate1HostOptions.cs"
DISCOVERY_PROTOCOL = REPO_ROOT / "src" / "NosAi.Protocol" / "DiscoveryProtocol.cs"
APP_CSPROJ = REPO_ROOT / "src" / "NosAi.GuardAi.App" / "NosAi.GuardAi.App.csproj"
CONNECTION_SERVICE = REPO_ROOT / "src" / "NosAi.GuardAi.App" / "GuardConnectionService.cs"


def test_trusted_key_path_matches_where_the_runtime_looks():
    # Pairing writes the key here and the runtime loads it without a flag. If the
    # two disagree the runtime starts with no trusted key and refuses every
    # session, while the pairing step reports success.
    source = GATE1_OPTIONS.read_text(encoding="utf-8")
    match = re.search(r'DefaultTrustedKeyPath\s*=\s*"([^"]+)"', source)
    assert match, "Gate1HostOptions.DefaultTrustedKeyPath not found"
    assert Gate1Defaults.TRUSTED_KEY_PATH == match.group(1)


def test_runtime_public_path_matches_the_identity_companion():
    source = (REPO_ROOT / "src" / "NosAi.Runtime" / "Gate1" / "RuntimeIdentity.cs").read_text(encoding="utf-8")
    public = re.search(r'const string DefaultPublicPath\s*=\s*"([^"]+)"', source)
    private = re.search(r'const string DefaultPath\s*=\s*"([^"]+)"', source)
    assert public and private, "RuntimeIdentity default paths not found"
    assert Gate1Defaults.RUNTIME_PUBLIC_KEY_PATH == public.group(1)
    assert Gate1Defaults.RUNTIME_IDENTITY_PATH == private.group(1)


def test_discovery_port_matches_the_protocol():
    source = DISCOVERY_PROTOCOL.read_text(encoding="utf-8")
    match = re.search(r"public const int Port\s*=\s*(\d+)", source)
    assert match, "DiscoveryProtocol.Port not found"
    assert Gate1Defaults.DISCOVERY_PORT == int(match.group(1))


def test_discovery_does_not_share_the_guard_port():
    # One is an unauthenticated UDP announcement, the other the authenticated
    # session. Sharing a number would invite treating a discovery reply as if it
    # carried the authority of a session.
    assert Gate1Defaults.DISCOVERY_PORT != GUARD_PORT


def test_the_app_dials_the_canonical_guard_port():
    source = CONNECTION_SERVICE.read_text(encoding="utf-8")
    match = re.search(r"const int GuardPort\s*=\s*(\d+)", source)
    assert match, "GuardPort not found in GuardConnectionService"
    assert int(match.group(1)) == GUARD_PORT


def test_package_name_still_matches_the_application_id():
    source = APP_CSPROJ.read_text(encoding="utf-8")
    match = re.search(r"<ApplicationId>([^<]+)</ApplicationId>", source)
    assert match, "<ApplicationId> not found"
    assert PACKAGE_NAME == match.group(1).strip()


def test_the_app_offers_both_transports_and_remembers_the_choice():
    # The operator picks USB or Wi-Fi and nothing else; that choice has to survive
    # a relaunch, or it is not a preference but a prompt.
    preference = (REPO_ROOT / "src" / "NosAi.GuardAi.App" / "TransportPreference.cs").read_text(encoding="utf-8")
    assert "Usb" in preference and "WiFi" in preference
    assert "Preferences.Default.Set" in preference
    assert "Preferences.Default.Get" in preference


def test_the_app_screen_exposes_no_key_or_address_controls():
    # The requirement is that the operator manages nothing: no key, no pairing, no
    # address. Only the transport choice and connect/disconnect.
    xaml = (REPO_ROOT / "src" / "NosAi.GuardAi.App" / "MainPage.xaml").read_text(encoding="utf-8")
    lowered = xaml.lower()
    assert "publickey" not in lowered, "the key must not be shown or handled on screen"
    assert "entry" not in lowered, "no free-text field: the address is discovered, not typed"
    assert "onconnectclicked" in lowered
    assert "ondisconnectclicked" in lowered
