from pathlib import Path

from nosai.storage.paths import NosAiStoragePaths


def test_canonical_layout(tmp_path: Path) -> None:
    paths = NosAiStoragePaths(tmp_path)
    paths.ensure_layout()

    assert paths.project == tmp_path / "NosAi"
    assert paths.models == tmp_path / "NosAi" / "models"
    assert paths.db == tmp_path / "NosAi" / "data" / "db"
    assert paths.logs == tmp_path / "NosAi" / "logs"
    assert paths.config == tmp_path / "NosAi" / "config"

    for directory in (
        paths.app,
        paths.runtime,
        paths.models,
        paths.db,
        paths.state,
        paths.evidence,
        paths.exports,
        paths.cache,
        paths.logs,
        paths.temp,
        paths.backups,
        paths.config,
        paths.tools,
    ):
        assert directory.is_dir()
