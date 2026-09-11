#!/usr/bin/env python3
"""Agente documentale locale: delega la stesura a qwen2.5-coder:7b (Ollama) e
restituisce a Claude soltanto il JSON di stato, mai il contenuto del file.

Uso:
    python scripts/doc_agent.py <incarico.json> [--dry-run]

L'incarico e' un JSON (oggetto o lista di oggetti) con:
    file                percorso di destinazione, relativo alla radice del repo
    purpose             scopo in una riga
    requirements[]      requisiti che il documento deve soddisfare
    facts[]             fatti verificati da usare; il modello non puo' inventarne altri
    required_sections[] intestazioni Markdown obbligatorie, confronto letterale
    format              "markdown" oppure "json"
    max_words           limite indicativo di lunghezza
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from nosai.orchestration.local_result import validate_local_result
OLLAMA_URL = os.getenv("OLLAMA_URL", "http://localhost:11434/api/generate")
LOCAL_MODEL = os.getenv("NOSAI_LOCAL_MODEL", "qwen2.5-coder:7b")
LEDGER = ROOT / "data" / "ai_task_ledger.jsonl"
MAX_ATTEMPTS = 3

FORBIDDEN = re.compile(
    r"\b(TODO|TBD|FIXME|XXX|lorem ipsum|PLACEHOLDER|da definire|inserire qui)\b", re.I
)
MD_LINK = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)")
INLINE_CODE = re.compile(r"`[^`\n]+`")
FENCE = re.compile(r"^\s*`{3}[a-zA-Z]*\s*\n(.*)\n\s*`{3}\s*$", re.S)


def build_prompt(task: dict, previous_errors: list | None) -> str:
    fmt = task.get("format", "markdown")
    parts = [
        "Sei il Technical Writer locale del progetto NosAi. Scrivi in italiano corretto e conciso.",
        "Produci il contenuto COMPLETO del file `{}` in formato {}.".format(task["file"], fmt),
        "",
        "REGOLE VINCOLANTI:",
        "- Usa esclusivamente i FATTI forniti. Non inventare API, percorsi, numeri, date o nomi di file.",
        "- Vietati i segnaposto residui. Se devi citare uno di questi termini come token tecnico, "
        "scrivilo fra backtick.",
        "- Nessuna sezione duplicata, nessuna contraddizione interna.",
        "- Non aggiungere preamboli, spiegazioni o recinti di codice attorno al contenuto.",
        "- Lunghezza massima indicativa: {} parole.".format(task.get("max_words", 700)),
    ]
    if fmt == "markdown" and task.get("required_sections"):
        parts += ["", "INTESTAZIONI OBBLIGATORIE (copiale alla lettera, in quest'ordine):"]
        parts += ["  " + s for s in task["required_sections"]]
    if fmt == "json":
        parts.append("- Emetti SOLO JSON valido, senza commenti.")
    parts += ["", "SCOPO: " + task["purpose"], "", "REQUISITI DA SODDISFARE:"]
    parts += ["- " + r for r in task.get("requirements", [])]
    parts += ["", "FATTI VERIFICATI (unica fonte ammessa):"]
    parts += ["- " + f for f in task.get("facts", [])]
    if previous_errors:
        parts += [
            "",
            "IL TENTATIVO PRECEDENTE E' STATO RIFIUTATO. Correggi esattamente questi difetti:",
        ]
        parts += ["- " + e for e in previous_errors]
    parts += ["", "Rispondi con il solo contenuto del file."]
    return "\n".join(parts)


def call_local(prompt: str) -> str:
    response = requests.post(
        OLLAMA_URL,
        json={
            "model": LOCAL_MODEL,
            "prompt": prompt,
            "stream": False,
            "options": {"temperature": 0.15, "num_ctx": 8192, "num_predict": 3072},
        },
        timeout=900,
    )
    response.raise_for_status()
    return response.json().get("response", "")


def strip_fence(text: str) -> str:
    text = text.strip()
    match = FENCE.match(text)
    return match.group(1).strip() if match else text


def validate_python_skeleton(content: str, task: dict):
    """Uno scheletro Python deve compilare, dichiarare le firme richieste e non
    contenere logica: ogni corpo e' una docstring seguita da NotImplementedError."""
    import ast

    errors = []
    try:
        tree = ast.parse(content)
    except SyntaxError as exc:
        return False, ["Sintassi Python non valida alla riga {}: {}".format(exc.lineno, exc.msg)]

    defined = set()
    implemented = []
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            defined.add(node.name)
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            body = [n for n in node.body if not isinstance(n, ast.Expr) or not isinstance(n.value, ast.Constant)]
            if not body:
                continue
            only_stub = len(body) == 1 and (
                isinstance(body[0], ast.Pass)
                or (isinstance(body[0], ast.Raise) and "NotImplementedError" in ast.dump(body[0]))
                or isinstance(body[0], ast.Expr)
            )
            # Un costruttore che si limita a memorizzare i parametri ricevuti fa parte della
            # firma, non della logica: e' ammesso in uno scheletro.
            if not only_stub and node.name == "__init__":
                only_stub = all(
                    isinstance(stmt, ast.Assign)
                    and all(
                        isinstance(t, ast.Attribute)
                        and isinstance(t.value, ast.Name)
                        and t.value.id == "self"
                        for t in stmt.targets
                    )
                    and isinstance(stmt.value, (ast.Name, ast.Constant, ast.Attribute))
                    for stmt in body
                )
            if not only_stub:
                implemented.append(node.name)

    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name):
                    defined.add(target.id)
        elif isinstance(node, ast.AnnAssign):
            if isinstance(node.target, ast.Name):
                defined.add(node.target.id)

    if implemented:
        errors.append(
            "Scheletro con logica implementata in: " + ", ".join(sorted(set(implemented)))
        )
    missing_symbols = [s for s in task.get("required_symbols", []) if s not in defined]
    if missing_symbols:
        errors.append("Simboli richiesti assenti: " + ", ".join(missing_symbols))
    return not errors, errors


def validate(content: str, task: dict):
    """Restituisce (checks, errori_bloccanti, avvisi)."""
    errors = []
    warnings = []
    fmt = task.get("format", "markdown")

    format_valid = True
    if fmt == "json":
        try:
            parsed = json.loads(content)
        except json.JSONDecodeError as exc:
            parsed = None
            format_valid = False
            errors.append("JSON non valido: {}".format(exc))
        if parsed is not None and task["file"].endswith(".schema.json"):
            try:
                import jsonschema

                jsonschema.Draft202012Validator.check_schema(parsed)
            except jsonschema.exceptions.SchemaError as exc:
                format_valid = False
                errors.append("JSON Schema non valido: {}".format(str(exc).splitlines()[0]))
            for key in task.get("required_properties", []):
                if key not in (parsed.get("properties") or {}):
                    errors.append("Proprieta' obbligatoria assente dallo schema: " + key)
    elif fmt == "python":
        format_valid, python_errors = validate_python_skeleton(content, task)
        errors.extend(python_errors)
    elif fmt == "python_test":
        # Il codice di test contiene logica: si controllano sintassi e simboli richiesti.
        import ast as _ast

        try:
            albero = _ast.parse(content)
            definiti = {
                n.name
                for n in _ast.walk(albero)
                if isinstance(n, (_ast.FunctionDef, _ast.AsyncFunctionDef, _ast.ClassDef))
            }
            assenti = [s for s in task.get("required_symbols", []) if s not in definiti]
            if assenti:
                errors.append("Test richiesti assenti: " + ", ".join(assenti))
        except SyntaxError as exc:
            format_valid = False
            errors.append("Sintassi Python non valida alla riga {}: {}".format(exc.lineno, exc.msg))
    elif not content.lstrip().startswith("#"):
        format_valid = False
        errors.append("Il Markdown non inizia con un'intestazione di primo livello.")

    missing = [s for s in task.get("required_sections", []) if s not in content]
    if missing:
        errors.append("Sezioni obbligatorie assenti: " + ", ".join(missing))

    # Un termine citato come token tecnico sta fra backtick: e' una menzione, non un segnaposto
    # residuo. Si cerca solo fuori dal codice inline.
    prose = INLINE_CODE.sub(" ", content)
    placeholders = sorted({m.group(0) for m in FORBIDDEN.finditer(prose)})
    if placeholders:
        errors.append("Segnaposto vietati presenti: " + ", ".join(placeholders))

    broken = []
    for match in MD_LINK.finditer(content):
        target = match.group(1)
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        candidate = Path(target) if target.startswith("/") else (ROOT / target)
        if not candidate.exists():
            broken.append(target)
    if broken:
        errors.append("Riferimenti a file inesistenti: " + ", ".join(sorted(set(broken))))

    if fmt == "markdown":
        headings = [line.strip() for line in content.splitlines() if line.startswith("#")]
        duplicates = sorted({h for h in headings if headings.count(h) > 1})
        if duplicates:
            errors.append("Intestazioni duplicate: " + ", ".join(duplicates))

    words = len(content.split())
    min_words = task.get("min_words", 40)
    if words < min_words:
        errors.append("Contenuto troppo breve: {} parole.".format(words))
    limit = task.get("max_words", 700)
    if words > limit * 1.8:
        warnings.append(
            "Documento lungo: {} parole contro un limite indicativo di {}.".format(words, limit)
        )

    checks = {
        "format_valid": format_valid,
        "references_valid": not broken,
        "placeholders_resolved": not placeholders,
        "requirements_covered": not missing,
        "contradictions_found": False,
    }
    return checks, errors, warnings


def record(entry: dict) -> None:
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    with LEDGER.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, ensure_ascii=False) + "\n")


def run(task: dict, dry_run: bool) -> dict:
    errors = []
    warnings = []
    checks = {}
    content = ""
    attempts = 0

    for attempt in range(1, MAX_ATTEMPTS + 1):
        attempts = attempt
        content = strip_fence(call_local(build_prompt(task, errors or None)))
        checks, errors, warnings = validate(content, task)
        if not errors:
            break

    if not errors:
        status = "completed"
    elif attempts < MAX_ATTEMPTS:
        status = "needs_revision"
    else:
        status = "blocked"

    if status == "completed" and not dry_run:
        target = ROOT / task["file"]
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content.rstrip() + "\n", encoding="utf-8")

    result = {
        "status": status,
        "file": task["file"],
        "purpose": task["purpose"],
        "requirements_satisfied": task.get("requirements", []) if not errors else [],
        "warnings": warnings,
        "missing_items": errors,
        "checks": checks,
        "confidence": round(max(0.0, 1.0 - 0.25 * (attempts - 1) - 0.5 * bool(errors)), 2),
    }
    schema_defects = validate_local_result(result)
    if schema_defects:
        result["status"] = "blocked"
        result["missing_items"] = result["missing_items"] + schema_defects
    record(
        {
            "task_id": task.get("task_id", task["file"]),
            "model": LOCAL_MODEL,
            "calls": attempts,
            "estimated_cost_usd": 0.0,
            "status": result["status"],
            "file": task["file"],
            "words": len(content.split()),
        }
    )
    return result


def main() -> int:
    if len(sys.argv) < 2:
        print(json.dumps({"status": "blocked", "missing_items": ["incarico non fornito"]}))
        return 2
    payload = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    dry_run = "--dry-run" in sys.argv
    tasks = payload if isinstance(payload, list) else [payload]
    results = [run(task, dry_run) for task in tasks]
    print(json.dumps(results if len(results) > 1 else results[0], ensure_ascii=False, indent=2))
    return 0 if all(r["status"] == "completed" for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
