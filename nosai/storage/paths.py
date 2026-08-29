from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class NosAiStoragePaths:
    """Canonical paths rooted at the dedicated NosAi volume."""

    root: Path

    @property
    def project(self) -> Path:
        return self.root / "NosAi"

    @property
    def app(self) -> Path:
        return self.project / "app"

    @property
    def runtime(self) -> Path:
        return self.project / "runtime"

    @property
    def models(self) -> Path:
        return self.project / "models"

    @property
    def data(self) -> Path:
        return self.project / "data"

    @property
    def db(self) -> Path:
        return self.data / "db"

    @property
    def state(self) -> Path:
        return self.data / "state"

    @property
    def evidence(self) -> Path:
        return self.data / "evidence"

    @property
    def exports(self) -> Path:
        return self.data / "exports"

    @property
    def cache(self) -> Path:
        return self.project / "cache"

    @property
    def logs(self) -> Path:
        return self.project / "logs"

    @property
    def temp(self) -> Path:
        return self.project / "temp"

    @property
    def backups(self) -> Path:
        return self.project / "backups"

    @property
    def config(self) -> Path:
        return self.project / "config"

    @property
    def tools(self) -> Path:
        return self.project / "tools"

    def ensure_layout(self) -> None:
        for path in (
            self.app,
            self.runtime,
            self.models,
            self.db,
            self.state,
            self.evidence,
            self.exports,
            self.cache,
            self.logs,
            self.temp,
            self.backups,
            self.config,
            self.tools,
        ):
            path.mkdir(parents=True, exist_ok=True)
