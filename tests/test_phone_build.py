"""The APK build guard: a stale APK must not be installed silently.

Wire version 3 refuses an older peer at the header, so an APK left over from
version 2 installs cleanly and then cannot connect. These tests pin the guard
that catches it on the PC instead.
"""
from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

from nosai.phone import deploy as deploy_cli
from nosai.phone.build import (
    APP_PROJECT,
    IGNORED_DIRECTORIES,
    SOURCE_SUFFIXES,
    WIRE_CONTRACT_PROJECTS,
    BuildError,
    build_apk,
    build_command_hint,
    check_freshness,
    find_android_sdk,
    find_java_sdk,
    newest_wire_source,
)

REPO_ROOT = Path(__file__).resolve().parent.parent


# --------------------------------------------------------------------------
# The paths the guard depends on must keep existing
# --------------------------------------------------------------------------

def test_the_watched_projects_exist_in_this_repository():
    # A renamed project would make the freshness check silently vacuous: it
    # would find no sources, report "nothing to compare against", and never call
    # an APK stale again.
    for project in WIRE_CONTRACT_PROJECTS:
        assert (REPO_ROOT / project).is_dir(), project


def test_the_app_project_exists():
    assert (REPO_ROOT / APP_PROJECT).is_file()


def test_the_real_repository_has_wire_sources_to_compare_against():
    newest = newest_wire_source(REPO_ROOT)
    assert newest is not None
    source, _ = newest
    assert source.suffix in SOURCE_SUFFIXES


# --------------------------------------------------------------------------
# Freshness
# --------------------------------------------------------------------------

def _fake_repo(tmp_path: Path, apk_mtime: float, source_mtime: float) -> tuple[Path, Path]:
    protocol = tmp_path / "src" / "NosAi.Protocol"
    protocol.mkdir(parents=True)
    source = protocol / "WireProtocol.cs"
    source.write_text("// wire", encoding="utf-8")
    import os

    os.utime(source, (source_mtime, source_mtime))

    apk = tmp_path / "app.apk"
    apk.write_bytes(b"apk")
    os.utime(apk, (apk_mtime, apk_mtime))
    return apk, source


def test_an_apk_older_than_the_protocol_is_stale(tmp_path):
    apk, source = _fake_repo(tmp_path, apk_mtime=1000, source_mtime=2000)
    freshness = check_freshness(apk, tmp_path)
    assert freshness.stale
    assert freshness.apk_exists
    assert freshness.newest_source == source
    assert "newer than the APK" in freshness.detail


def test_an_apk_newer_than_the_protocol_is_current(tmp_path):
    apk, _ = _fake_repo(tmp_path, apk_mtime=3000, source_mtime=2000)
    freshness = check_freshness(apk, tmp_path)
    assert not freshness.stale


def test_a_missing_apk_is_stale_and_says_so(tmp_path):
    freshness = check_freshness(tmp_path / "absent.apk", tmp_path)
    assert freshness.stale
    assert not freshness.apk_exists
    assert "not built yet" in freshness.detail


def test_build_output_does_not_make_the_apk_stale_against_itself(tmp_path):
    # The APK lives under bin/. Counting bin/ and obj/ as sources would make
    # every APK permanently stale and the guard would cry wolf until ignored.
    import os

    protocol = tmp_path / "src" / "NosAi.Protocol"
    (protocol / "bin" / "Release").mkdir(parents=True)
    (protocol / "obj").mkdir(parents=True)
    source = protocol / "WireProtocol.cs"
    source.write_text("// wire", encoding="utf-8")
    os.utime(source, (1000, 1000))

    for noise in (protocol / "bin" / "Release" / "Stale.cs", protocol / "obj" / "Generated.cs"):
        noise.write_text("// generated", encoding="utf-8")
        os.utime(noise, (9000, 9000))

    apk = tmp_path / "app.apk"
    apk.write_bytes(b"apk")
    os.utime(apk, (2000, 2000))

    assert not check_freshness(apk, tmp_path).stale
    assert "bin" in IGNORED_DIRECTORIES and "obj" in IGNORED_DIRECTORIES


def test_an_unrelated_file_does_not_force_a_rebuild(tmp_path):
    import os

    protocol = tmp_path / "src" / "NosAi.Protocol"
    protocol.mkdir(parents=True)
    (protocol / "WireProtocol.cs").write_text("// wire", encoding="utf-8")
    os.utime(protocol / "WireProtocol.cs", (1000, 1000))
    note = protocol / "NOTES.md"
    note.write_text("nota", encoding="utf-8")
    os.utime(note, (9000, 9000))

    apk = tmp_path / "app.apk"
    apk.write_bytes(b"apk")
    os.utime(apk, (2000, 2000))

    assert not check_freshness(apk, tmp_path).stale


def test_no_sources_reports_the_absence_rather_than_implying_freshness(tmp_path):
    apk = tmp_path / "app.apk"
    apk.write_bytes(b"apk")
    freshness = check_freshness(apk, tmp_path)
    assert not freshness.stale
    assert freshness.newest_source is None
    assert "no wire-contract sources" in freshness.detail


# --------------------------------------------------------------------------
# Toolchain discovery
# --------------------------------------------------------------------------

def test_the_environment_wins_for_the_android_sdk(tmp_path, monkeypatch):
    sdk = tmp_path / "sdk"
    (sdk / "platform-tools").mkdir(parents=True)
    monkeypatch.setenv("ANDROID_HOME", str(sdk))
    assert find_android_sdk() == sdk


def test_an_sdk_without_platform_tools_is_refused(tmp_path):
    empty = tmp_path / "not-an-sdk"
    empty.mkdir()
    with pytest.raises(BuildError) as raised:
        find_android_sdk(empty)
    assert raised.value.reason == "android_sdk_invalid"


def test_the_environment_wins_for_the_jdk(tmp_path, monkeypatch):
    jdk = tmp_path / "jdk"
    (jdk / "bin").mkdir(parents=True)
    monkeypatch.setenv("JAVA_HOME", str(jdk))
    assert find_java_sdk() == jdk


def test_a_jdk_without_bin_is_refused(tmp_path):
    empty = tmp_path / "not-a-jdk"
    empty.mkdir()
    with pytest.raises(BuildError) as raised:
        find_java_sdk(empty)
    assert raised.value.reason == "java_sdk_invalid"


def test_the_command_hint_names_the_project_and_the_framework():
    hint = build_command_hint(REPO_ROOT)
    assert "NosAi.GuardAi.App.csproj" in hint
    assert "net8.0-android" in hint
    assert "-c Release" in hint


# --------------------------------------------------------------------------
# Building
# --------------------------------------------------------------------------

def _toolchain(tmp_path, monkeypatch):
    sdk = tmp_path / "sdk"
    (sdk / "platform-tools").mkdir(parents=True)
    jdk = tmp_path / "jdk"
    (jdk / "bin").mkdir(parents=True)
    monkeypatch.setenv("ANDROID_HOME", str(sdk))
    monkeypatch.setenv("JAVA_HOME", str(jdk))


def test_a_failed_build_names_the_compiler_errors(tmp_path, monkeypatch):
    _toolchain(tmp_path, monkeypatch)

    def failing(*_args, **_kwargs):
        return subprocess.CompletedProcess(
            args=[], returncode=1,
            stdout="Foo.cs(3,5): error CS1002: ; expected\nirrelevant line\n", stderr="",
        )

    monkeypatch.setattr(subprocess, "run", failing)
    with pytest.raises(BuildError) as raised:
        build_apk(REPO_ROOT)
    assert raised.value.reason == "build_failed"
    assert "CS1002" in (raised.value.detail or "")


def test_a_build_that_produces_no_apk_fails_closed(tmp_path, monkeypatch):
    # The dangerous case: dotnet returns 0 but the artifact is not there. Passing
    # a nonexistent path back to the installer would surface much later.
    _toolchain(tmp_path, monkeypatch)
    monkeypatch.setattr(
        subprocess, "run",
        lambda *_a, **_k: subprocess.CompletedProcess(args=[], returncode=0, stdout="", stderr=""),
    )

    project = tmp_path / APP_PROJECT
    project.parent.mkdir(parents=True)
    project.write_text("<Project/>", encoding="utf-8")

    with pytest.raises(BuildError) as raised:
        build_apk(tmp_path)
    assert raised.value.reason == "apk_missing_after_build"


def test_a_missing_project_is_refused_before_touching_the_toolchain(tmp_path):
    with pytest.raises(BuildError) as raised:
        build_apk(tmp_path)
    assert raised.value.reason == "app_project_not_found"


# --------------------------------------------------------------------------
# The deploy CLI
# --------------------------------------------------------------------------

def test_deploy_refuses_a_stale_apk_when_told_not_to_build(tmp_path, monkeypatch, capsys):
    apk, _ = _fake_repo(tmp_path, apk_mtime=1000, source_mtime=2000)
    monkeypatch.setattr(deploy_cli, "REPO_ROOT", tmp_path)

    # No --apk: the operator did not name this artifact, so installing something
    # that cannot connect would be the tool's mistake, not theirs.
    monkeypatch.setattr(deploy_cli, "BUILT_APK_RELATIVE", apk.relative_to(tmp_path))

    assert deploy_cli.main(["--no-build"]) == 2
    err = capsys.readouterr().err
    assert "APK refused" in err
    assert "refuses an older peer" in err


def test_deploy_reports_the_wire_version_it_expects(tmp_path, monkeypatch, capsys):
    from nosai.network.wire_protocol import VERSION

    apk, _ = _fake_repo(tmp_path, apk_mtime=3000, source_mtime=2000)
    monkeypatch.setattr(deploy_cli, "REPO_ROOT", tmp_path)
    monkeypatch.setattr(deploy_cli, "BUILT_APK_RELATIVE", apk.relative_to(tmp_path))
    monkeypatch.setattr(
        deploy_cli, "deploy",
        lambda **_kwargs: (_ for _ in ()).throw(deploy_cli.AdbError("no_authorized_device", "none attached")),
    )

    deploy_cli.main([])
    out = capsys.readouterr().out
    assert f"version {VERSION}" in out


def test_an_explicitly_named_stale_apk_is_installed_but_flagged(tmp_path, monkeypatch, capsys):
    # The operator named the artifact. Overriding that would be worse than
    # warning; being silent about it would be worse still.
    apk, _ = _fake_repo(tmp_path, apk_mtime=1000, source_mtime=2000)
    monkeypatch.setattr(deploy_cli, "REPO_ROOT", tmp_path)
    monkeypatch.setattr(
        deploy_cli, "deploy",
        lambda **_kwargs: (_ for _ in ()).throw(deploy_cli.AdbError("no_authorized_device", "none attached")),
    )

    assert deploy_cli.main(["--apk", str(apk)]) == 1
    captured = capsys.readouterr()
    assert "Warning" in captured.err
    assert "newer than the APK" in captured.err
