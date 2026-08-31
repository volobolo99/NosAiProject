"""Build the Guard AI APK, so what gets installed speaks the current wire version.

The hazard this module exists for is specific and was hit for real: the release
APK sitting in `bin/Release/net8.0-android/` outlives the protocol. Wire version 3
(ADR-0009) refuses a version 2 peer at the header, by design, so installing a
stale APK produces a phone that pairs, launches, and then cannot connect — with
`unsupported_version` as the only clue, on the runtime side, where nobody is
looking.

Two guards, and it is worth being precise about what each one is worth:

- **Freshness** compares the APK's mtime with the sources that decide the wire
  contract. That answers "was this APK built after the protocol changed", which
  is the actual question. It is not proof of the version inside the APK: the
  packaged assemblies live in a compressed blob, and reading the constant back
  out would mean an LZ4 dependency for a check the handshake already performs.
- **The handshake** is the decisive check and always was. A mismatched APK is
  refused at the header, fail-closed. This module exists to turn that refusal
  from a puzzling field failure into something caught on the PC beforehand.
"""
from __future__ import annotations

import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

#: Projects whose sources decide what the phone speaks on the wire. A change in
#: any of them makes an existing APK stale.
WIRE_CONTRACT_PROJECTS = (
    Path("src/NosAi.Protocol"),
    Path("src/NosAi.GuardClient"),
    Path("src/NosAi.GuardAi.App"),
)

#: Extensions that actually affect the built output. Excluding everything else
#: keeps a stray note or a log from forcing a rebuild.
SOURCE_SUFFIXES = frozenset({".cs", ".csproj", ".xaml", ".props", ".targets"})

#: Directories never worth scanning: they hold build output, so including them
#: would make the APK look stale against itself.
IGNORED_DIRECTORIES = frozenset({"bin", "obj"})

APP_PROJECT = Path("src/NosAi.GuardAi.App/NosAi.GuardAi.App.csproj")
ANDROID_FRAMEWORK = "net8.0-android"


class BuildError(RuntimeError):
    """A build step failed. `reason` is a stable identifier, not prose."""

    def __init__(self, reason: str, detail: str | None = None):
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason
        self.detail = detail


@dataclass(frozen=True)
class Freshness:
    """Whether an APK predates the sources that decide the wire contract."""

    stale: bool
    apk_exists: bool
    newest_source: Path | None
    detail: str


def _candidate_dirs(*relative: str) -> list[Path]:
    home = Path.home()
    program_files = os.getenv("ProgramFiles")
    local_app_data = os.getenv("LOCALAPPDATA")
    roots = [home]
    if local_app_data:
        roots.append(Path(local_app_data))
    if program_files:
        roots.append(Path(program_files))
    return [root.joinpath(*relative) for root in roots]


def find_android_sdk(explicit: str | Path | None = None) -> Path:
    """Locate the Android SDK.

    The environment wins, because that is what a CI runner sets. The fallbacks
    exist so the flow works on a development box without exporting anything.
    """
    if explicit is not None:
        candidate = Path(explicit)
        if not (candidate / "platform-tools").is_dir():
            raise BuildError("android_sdk_invalid", str(candidate))
        return candidate

    for name in ("ANDROID_HOME", "ANDROID_SDK_ROOT"):
        value = os.getenv(name)
        if value and (Path(value) / "platform-tools").is_dir():
            return Path(value)

    for candidate in [Path.home() / "android-sdk", *_candidate_dirs("Android", "Sdk")]:
        if (candidate / "platform-tools").is_dir():
            return candidate

    raise BuildError(
        "android_sdk_not_found",
        "set ANDROID_HOME, or install the SDK under ~/android-sdk",
    )


def find_java_sdk(explicit: str | Path | None = None) -> Path:
    """Locate a JDK. The Android build needs one and does not find it itself."""
    if explicit is not None:
        candidate = Path(explicit)
        if not (candidate / "bin").is_dir():
            raise BuildError("java_sdk_invalid", str(candidate))
        return candidate

    value = os.getenv("JAVA_HOME")
    if value and (Path(value) / "bin").is_dir():
        return Path(value)

    candidates = [Path.home() / "jdk"]
    program_files = os.getenv("ProgramFiles")
    if program_files:
        for parent in (Path(program_files) / "Microsoft", Path(program_files) / "Android" / "jdk"):
            if parent.is_dir():
                candidates.extend(sorted(parent.glob("*jdk*")))
                candidates.extend(sorted(parent.glob("*")))

    for candidate in candidates:
        if (candidate / "bin").is_dir():
            return candidate

    raise BuildError("java_sdk_not_found", "set JAVA_HOME, or install a JDK under ~/jdk")


def newest_wire_source(repo_root: str | Path) -> tuple[Path, float] | None:
    """The most recently modified source that decides the wire contract."""
    root = Path(repo_root)
    newest: tuple[Path, float] | None = None

    for project in WIRE_CONTRACT_PROJECTS:
        base = root / project
        if not base.is_dir():
            continue
        for path in base.rglob("*"):
            if not path.is_file() or path.suffix not in SOURCE_SUFFIXES:
                continue
            if IGNORED_DIRECTORIES.intersection(part.lower() for part in path.relative_to(base).parts):
                continue
            stamp = path.stat().st_mtime
            if newest is None or stamp > newest[1]:
                newest = (path, stamp)

    return newest


def check_freshness(apk: str | Path, repo_root: str | Path) -> Freshness:
    """Whether the APK was built after the last change to the wire contract."""
    apk_path = Path(apk)
    if not apk_path.is_file():
        return Freshness(True, False, None, f"APK not built yet: {apk_path}")

    newest = newest_wire_source(repo_root)
    if newest is None:
        # Nothing to compare against; say so rather than implying the APK is fine.
        return Freshness(False, True, None, "no wire-contract sources found to compare against")

    source, stamp = newest
    if stamp > apk_path.stat().st_mtime:
        try:
            named = source.relative_to(Path(repo_root))
        except ValueError:
            named = source
        return Freshness(True, True, source, f"{named} is newer than the APK")

    return Freshness(False, True, source, "APK is newer than every wire-contract source")


def build_apk(
    repo_root: str | Path,
    android_sdk: str | Path | None = None,
    java_sdk: str | Path | None = None,
    timeout: float = 900.0,
) -> Path:
    """Build the release APK and return its path.

    Raises `BuildError` rather than returning a path that may not exist: a caller
    that installed a stale APK because the build quietly failed is exactly the
    failure this module was written to remove.
    """
    root = Path(repo_root)
    project = root / APP_PROJECT
    if not project.is_file():
        raise BuildError("app_project_not_found", str(project))

    sdk = find_android_sdk(android_sdk)
    jdk = find_java_sdk(java_sdk)

    command = [
        "dotnet", "build", str(project),
        "-c", "Release",
        "-f", ANDROID_FRAMEWORK,
        "--nologo",
        f"-p:AndroidSdkDirectory={sdk}",
        f"-p:JavaSdkDirectory={jdk}",
    ]

    environment = dict(os.environ, ANDROID_HOME=str(sdk), JAVA_HOME=str(jdk))
    try:
        result = subprocess.run(
            command, cwd=str(root), capture_output=True, text=True, timeout=timeout, env=environment
        )
    except subprocess.TimeoutExpired as exc:
        raise BuildError("build_timeout", f"{timeout:.0f}s") from exc
    except OSError as exc:
        raise BuildError("dotnet_unusable", exc.strerror or str(exc)) from exc

    if result.returncode != 0:
        combined = f"{result.stdout}\n{result.stderr}"
        errors = [line.strip() for line in combined.splitlines() if ": error " in line]
        raise BuildError("build_failed", "\n  ".join(errors[:5]) or f"exit {result.returncode}")

    from nosai.phone.adb import BUILT_APK_RELATIVE

    apk = root / BUILT_APK_RELATIVE
    if not apk.is_file():
        raise BuildError("apk_missing_after_build", str(apk))
    return apk


def build_command_hint(repo_root: str | Path = ".") -> str:
    """The exact command to run by hand, for an operator or a report."""
    try:
        sdk: str = str(find_android_sdk())
    except BuildError:
        sdk = "<android-sdk>"
    try:
        jdk: str = str(find_java_sdk())
    except BuildError:
        jdk = "<jdk>"
    return (
        f"dotnet build {APP_PROJECT.as_posix()} -c Release -f {ANDROID_FRAMEWORK} "
        f'-p:AndroidSdkDirectory="{sdk}" -p:JavaSdkDirectory="{jdk}"'
    )


__all__ = [
    "ANDROID_FRAMEWORK",
    "APP_PROJECT",
    "BuildError",
    "Freshness",
    "IGNORED_DIRECTORIES",
    "SOURCE_SUFFIXES",
    "WIRE_CONTRACT_PROJECTS",
    "build_apk",
    "build_command_hint",
    "check_freshness",
    "find_android_sdk",
    "find_java_sdk",
    "newest_wire_source",
]
