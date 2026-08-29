from __future__ import annotations

import os
import shutil
from dataclasses import dataclass
from pathlib import Path

from .paths import NosAiStoragePaths


DEFAULT_LABEL = "NOSAI-SSD"
DEFAULT_MIN_FREE_GB = 20


@dataclass(frozen=True)
class NosAiVolume:
    """Validated storage volume used by the NosAi runtime."""

    mount_point: Path
    label: str
    min_free_gb: int = DEFAULT_MIN_FREE_GB

    @property
    def paths(self) -> NosAiStoragePaths:
        return NosAiStoragePaths(self.mount_point)

    def validate(self) -> None:
        if not self.mount_point.exists() or not self.mount_point.is_dir():
            raise RuntimeError(f"NosAi volume is unavailable: {self.mount_point}")
        if self.label.upper() != DEFAULT_LABEL:
            raise RuntimeError(
                f"Unexpected NosAi volume label: {self.label!r}; expected {DEFAULT_LABEL!r}"
            )
        if not os.access(self.mount_point, os.R_OK | os.W_OK):
            raise RuntimeError(f"NosAi volume is not readable/writable: {self.mount_point}")
        free_gb = shutil.disk_usage(self.mount_point).free / (1024**3)
        if free_gb < self.min_free_gb:
            raise RuntimeError(
                f"Insufficient free space: {free_gb:.1f} GiB < {self.min_free_gb} GiB"
            )

    def prepare(self) -> NosAiStoragePaths:
        self.validate()
        paths = self.paths
        paths.ensure_layout()
        return paths


def _windows_volume_label(path: Path) -> str | None:
    """Read a Windows volume label without requiring third-party packages."""
    if os.name != "nt":
        return None
    import ctypes

    root = str(path).rstrip("\\/") + "\\"
    volume_name = ctypes.create_unicode_buffer(261)
    fs_name = ctypes.create_unicode_buffer(261)
    serial = ctypes.c_uint32()
    max_component = ctypes.c_uint32()
    flags = ctypes.c_uint32()
    ok = ctypes.windll.kernel32.GetVolumeInformationW(
        ctypes.c_wchar_p(root),
        volume_name,
        len(volume_name),
        ctypes.byref(serial),
        ctypes.byref(max_component),
        ctypes.byref(flags),
        fs_name,
        len(fs_name),
    )
    return volume_name.value if ok else None


def find_nosai_volume(label: str = DEFAULT_LABEL) -> NosAiVolume:
    """Find a local Windows volume by label; never formats or modifies it."""
    if os.name != "nt":
        raise RuntimeError("External-volume discovery currently targets Windows.")

    for letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
        mount = Path(f"{letter}:\\")
        if not mount.exists():
            continue
        detected = _windows_volume_label(mount)
        if detected and detected.upper() == label.upper():
            return NosAiVolume(mount_point=mount, label=detected)

    raise RuntimeError(f"Dedicated NosAi volume {label!r} was not found.")
