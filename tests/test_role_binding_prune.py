"""Criterio di accettazione del contratto C-314, contracts/binding-registry-prune-020.json.

Il caso centrale e' un residuo osservato il 2026-09-12: il contratto
role-unification-012 aveva fuso employee.product_manager ed
employee.game_ai_architect in employee.product_architect, ma le due righe gia'
scritte nel registro sono rimaste. ensure_default_bindings inserisce e basta.
Risultato: mcp_role_catalog elencava due dipendenti che propose e choose
rifiutavano con KeyError('unknown employee_id').

Le due salvaguardie contano quanto la potatura. Senza quella sulla tabella
vuota, costruire un registro con un sottoinsieme dei ruoli cancellerebbe tutto,
e al riavvio migrate_json reimporterebbe il vecchio JSON legacy.
"""
from __future__ import annotations

import json
import sqlite3

from nosai.mcp.bindings import RoleBindingRegistry
from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES

FANTASMA = "employee.game_ai_architect"


def _inserisci_orfano(sqlite_path, employee_id: str = FANTASMA) -> None:
    """Scrive a mano una riga di un ruolo che il codice non definisce piu'."""
    connection = sqlite3.connect(sqlite_path)
    connection.execute(
        """
        INSERT INTO bindings(
            employee_id, primary_model, fallback_models_json, state, version,
            proposal_id, updated_at, candidate_digest, author_id
        ) VALUES (?, 'claude', '["gpt-oss-120b"]', 'active', 1, NULL, 'default', ?, 'default')
        """,
        (employee_id, "sha256:" + "a" * 64),
    )
    connection.commit()
    connection.close()


def _employee_ids(sqlite_path) -> set[str]:
    connection = sqlite3.connect(sqlite_path)
    righe = {row[0] for row in connection.execute("SELECT employee_id FROM bindings")}
    connection.close()
    return righe


def test_la_riga_orfana_sparisce_alla_costruzione(tmp_path):
    percorso = tmp_path / "bindings.json"
    RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)
    _inserisci_orfano(percorso.with_suffix(".sqlite3"))
    assert FANTASMA in _employee_ids(percorso.with_suffix(".sqlite3"))

    registry = RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)

    assert registry.pruned_employee_ids == [FANTASMA]
    assert FANTASMA not in _employee_ids(percorso.with_suffix(".sqlite3"))


def test_i_ruoli_vivi_sopravvivono_alla_potatura(tmp_path):
    percorso = tmp_path / "bindings.json"
    RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)
    _inserisci_orfano(percorso.with_suffix(".sqlite3"))

    registry = RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)

    vivi = {employee.employee_id for employee in DEFAULT_EMPLOYEE_ROLES}
    assert _employee_ids(percorso.with_suffix(".sqlite3")) == vivi
    atteso = next(e for e in DEFAULT_EMPLOYEE_ROLES if e.employee_id == "employee.perception")
    ottenuto = registry.get("employee.perception")
    assert ottenuto["primary_model"] == atteso.primary_model
    assert ottenuto["fallback_models"] == list(atteso.fallback_models)


def test_l_export_json_non_conserva_l_orfano(tmp_path):
    percorso = tmp_path / "bindings.json"
    RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)
    _inserisci_orfano(percorso.with_suffix(".sqlite3"))

    RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)

    esportato = json.loads(percorso.read_text(encoding="utf-8"))
    assert FANTASMA not in esportato["bindings"]
    assert len(esportato["bindings"]) == len(DEFAULT_EMPLOYEE_ROLES)


def test_un_insieme_di_noti_vuoto_non_cancella_niente(tmp_path):
    percorso = tmp_path / "bindings.json"
    registry = RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)
    prima = _employee_ids(percorso.with_suffix(".sqlite3"))

    assert registry.state.prune_unknown_bindings([]) == []
    assert registry.state.prune_unknown_bindings(["", "   "]) == []
    assert _employee_ids(percorso.with_suffix(".sqlite3")) == prima


def test_una_potatura_che_svuoterebbe_la_tabella_non_cancella_niente(tmp_path):
    """Con zero righe migrate_json reimporterebbe il JSON legacy al riavvio."""
    percorso = tmp_path / "bindings.json"
    registry = RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)
    prima = _employee_ids(percorso.with_suffix(".sqlite3"))

    assert registry.state.prune_unknown_bindings(["employee.che.non.esiste"]) == []
    assert _employee_ids(percorso.with_suffix(".sqlite3")) == prima


def test_senza_orfani_non_pota_e_non_riscrive(tmp_path):
    percorso = tmp_path / "bindings.json"
    registry = RoleBindingRegistry(percorso, DEFAULT_EMPLOYEE_ROLES)

    assert registry.pruned_employee_ids == []
    assert registry.state.prune_unknown_bindings(
        [employee.employee_id for employee in DEFAULT_EMPLOYEE_ROLES]
    ) == []
    assert not percorso.exists()
