from __future__ import annotations

import sqlite3
from dataclasses import dataclass


@dataclass(frozen=True)
class SqlitePolicy:
    """Centralized SQLite policy for the dedicated NosAi volume."""

    journal_mode: str = "WAL"
    synchronous: str = "FULL"
    busy_timeout_ms: int = 5000
    cache_size_kib: int = 65536
    journal_size_limit: int = 64 * 1024 * 1024
    auto_vacuum: str = "INCREMENTAL"


def configure_connection(conn: sqlite3.Connection, policy: SqlitePolicy | None = None) -> None:
    policy = policy or SqlitePolicy()
    conn.execute("PRAGMA foreign_keys=ON")
    conn.execute(f"PRAGMA busy_timeout={int(policy.busy_timeout_ms)}")
    mode = conn.execute(f"PRAGMA journal_mode={policy.journal_mode}").fetchone()[0]
    if str(mode).upper() != policy.journal_mode.upper():
        raise RuntimeError(f"SQLite journal mode mismatch: {mode!r}")
    conn.execute(f"PRAGMA synchronous={policy.synchronous}")
    conn.execute(f"PRAGMA cache_size={-abs(int(policy.cache_size_kib))}")
    conn.execute(f"PRAGMA journal_size_limit={int(policy.journal_size_limit)}")
    # auto_vacuum is a database-file property and cannot safely be changed
    # on an existing database through every connection. Do not issue the PRAGMA
    # here; it is applied when creating a new database file.


def initialize_database_file(conn: sqlite3.Connection) -> None:
    """Apply file-level settings before schema creation on a new database."""
    conn.execute("PRAGMA auto_vacuum=INCREMENTAL")


def checkpoint(conn: sqlite3.Connection, mode: str = "TRUNCATE") -> tuple[int, int, int]:
    """Perform a controlled WAL checkpoint and return SQLite's checkpoint tuple."""
    allowed = {"PASSIVE", "FULL", "RESTART", "TRUNCATE"}
    normalized = mode.upper()
    if normalized not in allowed:
        raise ValueError(f"Unsupported checkpoint mode: {mode!r}")
    row = conn.execute(f"PRAGMA wal_checkpoint({normalized})").fetchone()
    return tuple(int(x) for x in row)  # type: ignore[return-value]
