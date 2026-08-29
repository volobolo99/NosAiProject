from __future__ import annotations

import sqlite3
from dataclasses import dataclass
from pathlib import Path

from nosai.storage.sqlite_policy import SqlitePolicy, configure_connection


@dataclass(frozen=True)
class HuntingSessionLog:
    timestamp: int
    map_id: int
    duration_seconds: int
    mobs_killed: int
    gold_earned: int
    exp_gained: float


@dataclass(frozen=True)
class TrajectoryPoint:
    timestamp: int
    session_id: int
    coord_x: int
    coord_y: int


class NosAiSqliteLogger:
    """Persistenza locale delle sessioni e delle traiettorie."""

    def __init__(self, db_path: str = "data/nosai_analytics.db", policy: SqlitePolicy | None = None) -> None:
        self.db_path = Path(db_path).resolve()
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.policy = policy or SqlitePolicy()
        self._initialize_database()

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.db_path, timeout=self.policy.busy_timeout_ms / 1000.0)
        configure_connection(conn, self.policy)
        return conn

    def _initialize_database(self) -> None:
        with self._connect() as conn:
            conn.execute("""CREATE TABLE IF NOT EXISTS hunting_sessions (
                session_id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                map_id INTEGER NOT NULL,
                duration_seconds INTEGER NOT NULL,
                mobs_killed INTEGER NOT NULL,
                gold_earned INTEGER NOT NULL,
                exp_gained REAL NOT NULL
            )""")
            conn.execute("""CREATE TABLE IF NOT EXISTS trajectories (
                point_id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                session_id INTEGER NOT NULL,
                coord_x INTEGER NOT NULL,
                coord_y INTEGER NOT NULL,
                FOREIGN KEY(session_id) REFERENCES hunting_sessions(session_id)
            )""")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_trajectories_session ON trajectories(session_id)")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_sessions_time ON hunting_sessions(timestamp)")

    def log_session(self, log: HuntingSessionLog) -> int:
        with self._connect() as conn:
            cur = conn.execute(
                "INSERT INTO hunting_sessions(timestamp,map_id,duration_seconds,mobs_killed,gold_earned,exp_gained) VALUES(?,?,?,?,?,?)",
                (log.timestamp, log.map_id, log.duration_seconds, log.mobs_killed, log.gold_earned, log.exp_gained),
            )
            return int(cur.lastrowid)

    def log_trajectory_batch(self, points: list[TrajectoryPoint]) -> None:
        if not points:
            return
        with self._connect() as conn:
            conn.executemany(
                "INSERT INTO trajectories(timestamp,session_id,coord_x,coord_y) VALUES(?,?,?,?)",
                [(p.timestamp, p.session_id, p.coord_x, p.coord_y) for p in points],
            )
