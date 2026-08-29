import sqlite3

from nosai.storage.sqlite_policy import SqlitePolicy, configure_connection


def test_default_sqlite_policy_configures_wal_and_full(tmp_path):
    db = tmp_path / "nosai.db"
    conn = sqlite3.connect(db)
    try:
        configure_connection(conn)
        assert conn.execute("PRAGMA journal_mode").fetchone()[0].upper() == "WAL"
        assert conn.execute("PRAGMA synchronous").fetchone()[0] == 2
        assert conn.execute("PRAGMA busy_timeout").fetchone()[0] == SqlitePolicy().busy_timeout_ms
    finally:
        conn.close()


def test_sqlite_policy_is_configurable(tmp_path):
    db = tmp_path / "nosai.db"
    policy = SqlitePolicy(busy_timeout_ms=2500, cache_size_kib=32768)
    conn = sqlite3.connect(db)
    try:
        configure_connection(conn, policy)
        assert conn.execute("PRAGMA busy_timeout").fetchone()[0] == 2500
        assert conn.execute("PRAGMA cache_size").fetchone()[0] == -32768
    finally:
        conn.close()
