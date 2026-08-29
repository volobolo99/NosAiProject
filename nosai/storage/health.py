from __future__ import annotations

import os
import shutil
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class StorageHealth:
    available: bool
    writable: bool
    free_bytes: int
    total_bytes: int
    error: str | None = None


def check_storage(path: Path) -> StorageHealth:
    try:
        usage = shutil.disk_usage(path)
        writable = os.access(path, os.W_OK)
        return StorageHealth(
            available=path.exists(),
            writable=writable,
            free_bytes=usage.free,
            total_bytes=usage.total,
            error=None if writable else "volume is not writable",
        )
    except OSError as exc:
        return StorageHealth(
            available=False,
            writable=False,
            free_bytes=0,
            total_bytes=0,
            error=str(exc),
        )
