"""Criterio di accettazione del contratto C-316, contracts/mcp-chief-watchdog-023.json.

docs/mcp/CHIEF_RUNBOOK.md descrive il ciclo del watchdog come una chiamata
manuale: nessun processo lo ripete nel tempo, quindi non esiste uno storico
dei tick di salute (R-208). Qui il Chief e' sempre un fittizio: run_health_tick
non deve mai chiamare un metodo che muta stato, e il modo piu' diretto per
provarlo e' far sollevare quei metodi se vengono chiamati.
"""
from __future__ import annotations

import json

from nosai.mcp.chief_watchdog import read_recent_ticks, run_health_tick


class _ChiefFinto:
    authority = {"direct_execution": False, "health_monitoring": True}

    def __init__(self, status="healthy", failed_checks=None, recommendations=None, provider_health=None):
        self._status = status
        self._failed_checks = failed_checks if failed_checks is not None else []
        self._recommendations = recommendations if recommendations is not None else ["continue periodic health checks"]
        self._provider_health = provider_health

    def health_check(self):
        checks = {}
        if self._provider_health is not None:
            checks["provider_health"] = self._provider_health
        return {
            "schema_version": "mcp.chief.health.v1",
            "status": self._status,
            "checks": checks,
            "failed_checks": self._failed_checks,
            "recommendations": self._recommendations,
        }

    def promote_binding(self, *args, **kwargs):
        raise AssertionError("run_health_tick non deve chiamare promote_binding")

    def rollback_binding(self, *args, **kwargs):
        raise AssertionError("run_health_tick non deve chiamare rollback_binding")

    def propose_improvement(self, *args, **kwargs):
        raise AssertionError("run_health_tick non deve chiamare propose_improvement")

    def audit_proposal(self, *args, **kwargs):
        raise AssertionError("run_health_tick non deve chiamare audit_proposal")


def test_run_health_tick_scrive_un_record_completo(tmp_path):
    log_path = tmp_path / "sub" / "chief_watchdog.jsonl"
    chief = _ChiefFinto(provider_health={"ok": True, "enabled": ["groq-free"]})

    record = run_health_tick(chief, log_path, now="2026-09-12T10:00:00+00:00")

    assert record["schema_version"] == "mcp.chief.watchdog.v1"
    assert record["ticked_at"] == "2026-09-12T10:00:00+00:00"
    assert record["status"] == "healthy"
    assert record["failed_checks"] == []
    assert record["recommendations"] == ["continue periodic health checks"]
    assert record["provider_health"] == {"ok": True, "enabled": ["groq-free"]}
    scritto = json.loads(log_path.read_text(encoding="utf-8").splitlines()[0])
    assert scritto == record


def test_due_chiamate_appendono_due_righe_distinte(tmp_path):
    log_path = tmp_path / "chief_watchdog.jsonl"
    chief_sano = _ChiefFinto(status="healthy")
    chief_degradato = _ChiefFinto(status="degraded", failed_checks=["binding_store"])

    run_health_tick(chief_sano, log_path, now="2026-09-12T10:00:00+00:00")
    run_health_tick(chief_degradato, log_path, now="2026-09-12T10:05:00+00:00")

    righe = [json.loads(r) for r in log_path.read_text(encoding="utf-8").splitlines()]
    assert len(righe) == 2
    assert righe[0]["status"] == "healthy"
    assert righe[1]["status"] == "degraded"
    assert righe[1]["failed_checks"] == ["binding_store"]


def test_read_recent_ticks_su_file_assente_non_solleva(tmp_path):
    assert read_recent_ticks(tmp_path / "mai_scritto.jsonl") == []


def test_read_recent_ticks_rispetta_limite_e_ordine_cronologico(tmp_path):
    log_path = tmp_path / "chief_watchdog.jsonl"
    for indice in range(5):
        run_health_tick(_ChiefFinto(), log_path, now=f"2026-09-12T10:0{indice}:00+00:00")

    ultimi_due = read_recent_ticks(log_path, limit=2)

    assert [tick["ticked_at"] for tick in ultimi_due] == [
        "2026-09-12T10:03:00+00:00",
        "2026-09-12T10:04:00+00:00",
    ]


def test_tick_riuscito_anche_senza_autorita_esecutiva(tmp_path):
    chief = _ChiefFinto()
    assert chief.authority["direct_execution"] is False

    record = run_health_tick(chief, tmp_path / "chief_watchdog.jsonl")

    assert record["status"] == "healthy"


def test_non_chiama_alcun_metodo_mutante(tmp_path):
    chief = _ChiefFinto()

    run_health_tick(chief, tmp_path / "chief_watchdog.jsonl")
